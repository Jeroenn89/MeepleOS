param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $root "src/MeepleOS.ServerLayer/bin/$Configuration/net10.0/MeepleOS.ServerLayer.dll"
$database = Join-Path ([IO.Path]::GetTempPath()) ("meepleos-smoke-{0}.db" -f [guid]::NewGuid())

$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.Arguments = '"' + $dll + '"'
$start.WorkingDirectory = $root
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['MEEPLEOS_DB_PATH'] = $database
$process = [Diagnostics.Process]::Start($start)

function Invoke-Mcp($id, $method, $parameters) {
    $request = @{ jsonrpc = '2.0'; id = $id; method = $method; params = $parameters } | ConvertTo-Json -Depth 10 -Compress
    $process.StandardInput.WriteLine($request)
    $line = $process.StandardOutput.ReadLine()
    if (-not $line) { throw "Geen MCP-antwoord op $method. $($process.StandardError.ReadToEnd())" }
    $response = $line | ConvertFrom-Json
    if ($response.error) { throw "MCP-fout bij ${method}: $line" }
    return $response.result
}

function Get-ToolData($result) {
    if ($result.isError) { throw "Tool gaf een fout: $($result | ConvertTo-Json -Depth 10)" }
    if ($result.structuredContent) {
        if ($result.structuredContent -is [array]) { return $result.structuredContent }
        if ($null -ne $result.structuredContent.value) {
            return $result.structuredContent.value
        }
        return $result.structuredContent
    }
    $data = ($result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text | ConvertFrom-Json
    if ($data -is [array]) { return $data }
    if ($null -ne $data.value) { return $data.value }
    return $data
}

try {
    $null = Invoke-Mcp 1 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'meepleos-smoke'; version = '1.0' } }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $tools = Invoke-Mcp 2 'tools/list' @{}
    $names = @($tools.tools | ForEach-Object { $_.name })
    foreach ($name in @('add_boardgame', 'get_boardgames', 'add_player', 'get_players', 'record_game_session', 'get_game_sessions',
        'get_player_statistics', 'get_boardgame_statistics', 'get_leaderboard', 'get_play_statistics',
        'update_boardgame', 'delete_boardgame', 'update_player', 'delete_player', 'update_game_session', 'delete_game_session',
        'add_game_expansion', 'get_game_expansions', 'update_game_expansion', 'delete_game_expansion')) {
        if ($name -notin $names) { throw "Ontbrekende tool: $name" }
    }
    foreach ($name in @('delete_boardgame', 'delete_player', 'delete_game_session', 'delete_game_expansion')) {
        $tool = $tools.tools | Where-Object { $_.name -eq $name }
        if ($tool.annotations.destructiveHint -ne $true) { throw "Destructieve annotatie ontbreekt bij $name" }
    }
    $recordSessionTool = $tools.tools | Where-Object { $_.name -eq 'record_game_session' }
    if (-not $recordSessionTool.inputSchema.properties.expansionIds -or
        $recordSessionTool.description -notmatch 'uitbreiding') {
        throw 'Optionele uitbreidingen ontbreken bij record_game_session'
    }
    $updateSessionTool = $tools.tools | Where-Object { $_.name -eq 'update_game_session' }
    foreach ($field in @('expansionIds', 'expansionIdsToAdd', 'expansionIdsToRemove')) {
        if ($updateSessionTool.description -notmatch $field) {
            throw "Uitbreidingsveld $field ontbreekt in update_game_session beschrijving"
        }
    }

    $empty = Get-ToolData (Invoke-Mcp 25 'tools/call' @{ name = 'get_play_statistics'; arguments = @{} })
    if ($empty.totalPlays -ne 0 -or @($empty.playsByPeriod).Count -ne 0 -or @($empty.mostPlayedBoardgames).Count -ne 0) {
        throw 'Lege database levert onjuiste speelstatistieken'
    }

    $game = Invoke-Mcp 3 'tools/call' @{ name = 'add_boardgame'; arguments = @{ name = ' Azul '; minPlayers = 2; maxPlayers = 4; estimatedMinutes = 45 } }
    if ($game.isError) { throw 'add_boardgame mislukt' }
    $unlimited = Get-ToolData (Invoke-Mcp 83 'tools/call' @{ name = 'add_boardgame'; arguments = @{
        name = 'Open spel'; minPlayers = 1; maxPlayers = $null; estimatedMinutes = 30
    } })
    if ($null -ne $unlimited.maxPlayers) { throw 'Bordspel zonder maximum kreeg een vaste grens' }
    $unknownMaximum = Invoke-Mcp 84 'tools/call' @{ name = 'add_boardgame'; arguments = @{
        name = 'Onbekend maximum'; minPlayers = 1; estimatedMinutes = 30
    } }
    if (-not $unknownMaximum.isError) { throw 'Ontbrekend maximum werd als onbeperkt aanvaard' }
    $games = Invoke-Mcp 4 'tools/call' @{ name = 'get_boardgames'; arguments = @{} }
    if ($games.isError -or ($games | ConvertTo-Json -Depth 10) -notmatch 'Azul') { throw 'get_boardgames gaf Azul niet terug' }
    $openGame = @(Get-ToolData $games) | Where-Object { $_.id -eq $unlimited.id }
    if (@($openGame).Count -ne 1 -or $null -ne $openGame.maxPlayers) { throw 'get_boardgames gaf null niet correct terug' }

    $player = Invoke-Mcp 5 'tools/call' @{ name = 'add_player'; arguments = @{ name = ' Alex ' } }
    if ($player.isError) { throw 'add_player mislukt' }
    $duplicate = Invoke-Mcp 6 'tools/call' @{ name = 'add_player'; arguments = @{ name = 'alex' } }
    if ($duplicate.isError) { throw 'Dubbele speler gaf een fout' }
    $players = Invoke-Mcp 7 'tools/call' @{ name = 'get_players'; arguments = @{} }
    $playerJson = $players | ConvertTo-Json -Depth 10
    if ($players.isError -or $playerJson -notmatch 'Alex' -or $playerJson -match '"name":\s*"alex"') { throw 'Dubbele speler werd niet goed afgehandeld' }

    $invalid = Invoke-Mcp 8 'tools/call' @{ name = 'add_boardgame'; arguments = @{ name = 'Fout'; minPlayers = 4; maxPlayers = 2; estimatedMinutes = 30 } }
    if (-not $invalid.isError) { throw 'Ongeldig spelersaantal werd geaccepteerd' }
    $invalidMinimum = Invoke-Mcp 85 'tools/call' @{ name = 'add_boardgame'; arguments = @{
        name = 'Fout minimum'; minPlayers = 0; maxPlayers = $null; estimatedMinutes = 30
    } }
    if (-not $invalidMinimum.isError) { throw 'Minimum nul werd geaccepteerd' }

    $azul = Get-ToolData $game
    $alex = Get-ToolData $player
    $sam = Get-ToolData (Invoke-Mcp 9 'tools/call' @{ name = 'add_player'; arguments = @{ name = 'Sam' } })
    $pat = Get-ToolData (Invoke-Mcp 10 'tools/call' @{ name = 'add_player'; arguments = @{ name = 'Pat' } })
    $lee = Get-ToolData (Invoke-Mcp 26 'tools/call' @{ name = 'add_player'; arguments = @{ name = 'Lee' } })
    $carcassonne = Get-ToolData (Invoke-Mcp 11 'tools/call' @{ name = 'add_boardgame'; arguments = @{ name = 'Carcassonne'; minPlayers = 2; maxPlayers = 5; estimatedMinutes = 45 } })
    $eldorado = Get-ToolData (Invoke-Mcp 12 'tools/call' @{ name = 'add_boardgame'; arguments = @{ name = 'El Dorado'; minPlayers = 2; maxPlayers = 4; estimatedMinutes = 60 } })

    $scored = Get-ToolData (Invoke-Mcp 13 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $carcassonne.id; playedAt = '2026-11-10'; notes = 'Gedeelde overwinning'
        players = @(@{ playerId = $alex.id; score = 85; position = 1 }, @{ playerId = $sam.id; score = 85; position = 1 })
        winnerPlayerIds = @($alex.id, $sam.id)
    } })
    if ($scored.players.Count -ne 2 -or @($scored.players | Where-Object { $_.isWinner -and $_.score -eq 85 }).Count -ne 2) {
        throw 'Scores of gedeelde overwinning zijn niet opgeslagen'
    }

    $unscored = Get-ToolData (Invoke-Mcp 14 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $eldorado.id; playedAt = '2026-11-11'
        players = @(@{ playerId = $pat.id }, @{ playerId = $alex.id })
        winnerPlayerIds = @($pat.id)
    } })
    if (@($unscored.players | Where-Object { $null -ne $_.score -or $null -ne $_.position }).Count -ne 0 -or
        @($unscored.players | Where-Object { $_.isWinner }).Count -ne 1) {
        throw 'Potje zonder scores of winnaar is niet goed opgeslagen'
    }

    $history = Get-ToolData (Invoke-Mcp 15 'tools/call' @{ name = 'get_game_sessions'; arguments = @{} })
    if (@($history).Count -ne 2) { throw 'Speelgeschiedenis bevat niet beide potjes' }
    $filtered = Get-ToolData (Invoke-Mcp 16 'tools/call' @{ name = 'get_game_sessions'; arguments = @{
        boardGameId = $carcassonne.id; playerId = $alex.id; from = '2026-11-10'; to = '2026-11-10'
    } })
    if (@($filtered).Count -ne 1 -or $filtered[0].id -ne $scored.id) { throw 'Filters voor bordspel, speler of datum werken niet' }
    $byPlayer = Get-ToolData (Invoke-Mcp 20 'tools/call' @{ name = 'get_game_sessions'; arguments = @{ playerId = $pat.id } })
    if (@($byPlayer).Count -ne 1 -or $byPlayer[0].id -ne $unscored.id) { throw 'Spelerfilter werkt niet' }
    $byDate = Get-ToolData (Invoke-Mcp 21 'tools/call' @{ name = 'get_game_sessions'; arguments = @{ from = '2026-11-11'; to = '2026-11-11' } })
    if (@($byDate).Count -ne 1 -or $byDate[0].id -ne $unscored.id) { throw 'Datumfilter werkt niet' }
    $byBoardgame = Get-ToolData (Invoke-Mcp 22 'tools/call' @{ name = 'get_game_sessions'; arguments = @{ boardGameId = $eldorado.id } })
    if (@($byBoardgame).Count -ne 1 -or $byBoardgame[0].id -ne $unscored.id) { throw 'Bordspelfilter werkt niet' }

    $badWinner = Invoke-Mcp 17 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $azul.id; playedAt = '2026-11-11'; players = @(@{ playerId = $alex.id }); winnerPlayerIds = @($pat.id)
    } }
    if (-not $badWinner.isError) { throw 'Winnaar buiten de deelnemers werd geaccepteerd' }
    $duplicatePlayer = Invoke-Mcp 18 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $azul.id; playedAt = '2026-11-11'; players = @(@{ playerId = $alex.id }, @{ playerId = $alex.id }); winnerPlayerIds = @($alex.id)
    } }
    if (-not $duplicatePlayer.isError) { throw 'Dubbele deelnemer werd geaccepteerd' }
    $unknownPlayer = Invoke-Mcp 19 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $azul.id; playedAt = '2026-11-11'; players = @(@{ playerId = 999999 }); winnerPlayerIds = @(999999)
    } }
    if (-not $unknownPlayer.isError) { throw 'Onbekende deelnemer werd geaccepteerd' }
    $unknownBoardgame = Invoke-Mcp 23 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = 999999; playedAt = '2026-11-11'; players = @(@{ playerId = $alex.id }); winnerPlayerIds = @($alex.id)
    } }
    if (-not $unknownBoardgame.isError) { throw 'Onbekend bordspel werd geaccepteerd' }
    $badPeriod = Invoke-Mcp 24 'tools/call' @{ name = 'get_game_sessions'; arguments = @{ from = '2026-11-11'; to = '2026-11-10' } }
    if (-not $badPeriod.isError) { throw 'Omgekeerde datumperiode werd geaccepteerd' }

    $playerStats = @(Get-ToolData (Invoke-Mcp 27 'tools/call' @{ name = 'get_player_statistics'; arguments = @{} }))
    $alexStats = $playerStats | Where-Object { $_.playerId -eq $alex.id }
    $samStats = $playerStats | Where-Object { $_.playerId -eq $sam.id }
    $leeStats = $playerStats | Where-Object { $_.playerId -eq $lee.id }
    if ($alexStats.plays -ne 2 -or $alexStats.wins -ne 1 -or $alexStats.winPercentage -ne 50 -or
        $samStats.plays -ne 1 -or $samStats.wins -ne 1 -or $samStats.winPercentage -ne 100 -or
        $leeStats.plays -ne 0 -or $leeStats.wins -ne 0 -or $leeStats.winPercentage -ne 0) {
        throw 'Spelerstatistieken of gedeelde overwinning zijn onjuist'
    }
    $samOnly = @(Get-ToolData (Invoke-Mcp 28 'tools/call' @{ name = 'get_player_statistics'; arguments = @{
        playerId = $sam.id; boardGameId = $eldorado.id
    } }))
    if ($samOnly.Count -ne 1 -or $samOnly[0].plays -ne 0 -or $samOnly[0].wins -ne 0) {
        throw 'Speler- en bordspelfilter leveren geen nulresultaat'
    }
    $gameStats = @(Get-ToolData (Invoke-Mcp 29 'tools/call' @{ name = 'get_boardgame_statistics'; arguments = @{} }))
    $carcassonneStats = $gameStats | Where-Object { $_.boardGameId -eq $carcassonne.id }
    $eldoradoStats = $gameStats | Where-Object { $_.boardGameId -eq $eldorado.id }
    if ($carcassonneStats.plays -ne 1 -or $carcassonneStats.uniquePlayers -ne 2 -or
        @($carcassonneStats.topWinners).Count -ne 2 -or $eldoradoStats.plays -ne 1 -or
        $eldoradoStats.uniquePlayers -ne 2 -or $eldoradoStats.topWinners[0].playerId -ne $pat.id) {
        throw 'Bordspelstatistieken zijn onjuist'
    }
    $leaderboard = @(Get-ToolData (Invoke-Mcp 30 'tools/call' @{ name = 'get_leaderboard'; arguments = @{ sortBy = 'winPercentage' } }))
    if ($leaderboard[0].rank -ne 1 -or $leaderboard[0].winPercentage -ne 100 -or
        $leaderboard[-1].playerId -ne $lee.id) { throw 'Ranglijst is onjuist' }
    $byMonth = Get-ToolData (Invoke-Mcp 31 'tools/call' @{ name = 'get_play_statistics'; arguments = @{
        year = 2026; month = 11; groupBy = 'week'
    } })
    if ($byMonth.totalPlays -ne 2 -or @($byMonth.playsByPeriod).Count -ne 1 -or
        $byMonth.playsByPeriod[0].period -ne '2026-W46' -or $byMonth.playsByPeriod[0].plays -ne 2) {
        throw 'Maandfilter of weekgroepering is onjuist'
    }
    $byWeek = Get-ToolData (Invoke-Mcp 32 'tools/call' @{ name = 'get_play_statistics'; arguments = @{
        year = 2026; week = 46; groupBy = 'year'
    } })
    if ($byWeek.totalPlays -ne 2 -or $byWeek.playsByPeriod[0].period -ne '2026') {
        throw 'ISO-weekfilter of jaargroepering is onjuist'
    }
    $byRange = @(Get-ToolData (Invoke-Mcp 33 'tools/call' @{ name = 'get_player_statistics'; arguments = @{
        playerId = $alex.id; from = '2026-11-11'; to = '2026-11-11'
    } }))
    if ($byRange.Count -ne 1 -or $byRange[0].plays -ne 1 -or $byRange[0].wins -ne 0) {
        throw 'Aangepaste datumperiode of speler zonder overwinning is onjuist'
    }
    $noSessions = Get-ToolData (Invoke-Mcp 34 'tools/call' @{ name = 'get_play_statistics'; arguments = @{
        year = 2025; groupBy = 'month'
    } })
    if ($noSessions.totalPlays -ne 0 -or @($noSessions.playsByPeriod).Count -ne 0) {
        throw 'Periode zonder potjes levert onjuiste waarden'
    }
    $badStatisticsPeriod = Invoke-Mcp 35 'tools/call' @{ name = 'get_play_statistics'; arguments = @{
        year = 2026; month = 11; week = 46
    } }
    if (-not $badStatisticsPeriod.isError) { throw 'Conflicterende periode werd geaccepteerd' }

    $updatedGame = Get-ToolData (Invoke-Mcp 36 'tools/call' @{ name = 'update_boardgame'; arguments = @{
        id = $azul.id; changes = @{ name = 'Azul vernieuwd'; estimatedMinutes = 50 }
    } })
    if ($updatedGame.name -ne 'Azul vernieuwd' -or $updatedGame.minPlayers -ne 2 -or $updatedGame.maxPlayers -ne 4 -or
        $updatedGame.estimatedMinutes -ne 50) { throw 'Bordspel gedeeltelijk wijzigen mislukt' }
    $badGameUpdate = Invoke-Mcp 37 'tools/call' @{ name = 'update_boardgame'; arguments = @{ id = $azul.id; changes = @{ minPlayers = 5 } } }
    if (-not $badGameUpdate.isError) { throw 'Ongeldig spelersaantal bij wijzigen geaccepteerd' }
    $emptyGameName = Invoke-Mcp 56 'tools/call' @{ name = 'update_boardgame'; arguments = @{ id = $azul.id; changes = @{ name = ' ' } } }
    if (-not $emptyGameName.isError) { throw 'Lege bordspelnaam bij wijzigen geaccepteerd' }
    $missingGame = Invoke-Mcp 57 'tools/call' @{ name = 'update_boardgame'; arguments = @{ id = 999999; changes = @{ name = 'Onbekend' } } }
    if (-not $missingGame.isError) { throw 'Niet-bestaand bordspel wijzigen gaf geen fout' }
    $clearedMaximum = Get-ToolData (Invoke-Mcp 86 'tools/call' @{ name = 'update_boardgame'; arguments = @{
        id = $azul.id; changes = @{ maxPlayers = $null }
    } })
    if ($null -ne $clearedMaximum.maxPlayers -or $clearedMaximum.name -ne 'Azul vernieuwd' -or
        $clearedMaximum.minPlayers -ne 2 -or $clearedMaximum.estimatedMinutes -ne 50) {
        throw 'Maximum wissen wijzigde andere bordspelgegevens'
    }
    $fixedMaximum = Get-ToolData (Invoke-Mcp 87 'tools/call' @{ name = 'update_boardgame'; arguments = @{
        id = $unlimited.id; changes = @{ maxPlayers = 6 }
    } })
    if ($fixedMaximum.maxPlayers -ne 6 -or $fixedMaximum.minPlayers -ne 1 -or
        $fixedMaximum.estimatedMinutes -ne 30) { throw 'Onbeperkt maximum naar vaste waarde wijzigen mislukt' }

    $updatedPlayer = Get-ToolData (Invoke-Mcp 38 'tools/call' @{ name = 'update_player'; arguments = @{ id = $alex.id; name = 'Alexandra' } })
    if ($updatedPlayer.name -ne 'Alexandra' -or $updatedPlayer.id -ne $alex.id) { throw 'Speler wijzigen mislukt' }
    $duplicateUpdate = Invoke-Mcp 39 'tools/call' @{ name = 'update_player'; arguments = @{ id = $sam.id; name = 'alexandra' } }
    if (-not $duplicateUpdate.isError) { throw 'Dubbele spelersnaam bij wijzigen geaccepteerd' }
    $emptyPlayerName = Invoke-Mcp 58 'tools/call' @{ name = 'update_player'; arguments = @{ id = $sam.id; name = ' ' } }
    if (-not $emptyPlayerName.isError) { throw 'Lege spelersnaam bij wijzigen geaccepteerd' }
    $missingPlayer = Invoke-Mcp 59 'tools/call' @{ name = 'update_player'; arguments = @{ id = 999999; name = 'Onbekend' } }
    if (-not $missingPlayer.isError) { throw 'Niet-bestaande speler wijzigen gaf geen fout' }
    $history = @(Get-ToolData (Invoke-Mcp 40 'tools/call' @{ name = 'get_game_sessions'; arguments = @{ playerId = $alex.id } }))
    if ($history.Count -ne 2 -or $history[0].players.playerName -notcontains 'Alexandra') { throw 'Naamwijziging ontbreekt in speelgeschiedenis' }

    $edited = Get-ToolData (Invoke-Mcp 41 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $scored.id; changes = @{ notes = $null; winnerPlayerIds = @($sam.id);
            playerChanges = @(@{ playerId = $alex.id; score = $null; position = $null }) }
    } })
    $editedAlex = $edited.players | Where-Object { $_.playerId -eq $alex.id }
    if ($null -ne $edited.notes -or $null -ne $editedAlex.score -or $null -ne $editedAlex.position -or $editedAlex.isWinner -or
        @($edited.players | Where-Object { $_.isWinner }).Count -ne 1) { throw 'Optionele velden of winnaar wijzigen mislukt' }
    $badEdit = Invoke-Mcp 42 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $scored.id; changes = @{ playedAt = '2025-01-01'; winnerPlayerIds = @($pat.id) }
    } }
    if (-not $badEdit.isError) { throw 'Ongeldige winnaar bij wijzigen geaccepteerd' }
    $unchanged = @(Get-ToolData (Invoke-Mcp 43 'tools/call' @{ name = 'get_game_sessions'; arguments = @{ from = '2026-11-10'; to = '2026-11-10' } }))
    if ($unchanged.Count -ne 1 -or $unchanged[0].id -ne $scored.id) { throw 'Ongeldige wijziging heeft gegevens aangepast' }

    $replaced = Get-ToolData (Invoke-Mcp 44 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $scored.id; changes = @{ boardGameId = $azul.id; playedAt = '2025-01-01';
            players = @(@{ playerId = $sam.id }, @{ playerId = $lee.id }); winnerPlayerIds = @($lee.id) }
    } })
    if ($replaced.boardGameId -ne $azul.id -or $replaced.playedAt -ne '2025-01-01' -or
        @($replaced.players).Count -ne 2 -or @($replaced.players | Where-Object { $_.isWinner -and $_.playerId -eq $lee.id }).Count -ne 1) {
        throw 'Deelnemers, winnaar, bordspel of datum vervangen mislukt'
    }
    $leaderboardAfterEdit = @(Get-ToolData (Invoke-Mcp 45 'tools/call' @{ name = 'get_leaderboard'; arguments = @{} }))
    $leeRank = $leaderboardAfterEdit | Where-Object { $_.playerId -eq $lee.id }
    if ($leeRank.wins -ne 1) { throw 'Winnaarwijziging ontbreekt in ranglijst' }
    $oldMonth = Get-ToolData (Invoke-Mcp 46 'tools/call' @{ name = 'get_play_statistics'; arguments = @{ year = 2026; month = 11 } })
    if ($oldMonth.totalPlays -ne 1) { throw 'Datumwijziging ontbreekt in periodecijfers' }

    foreach ($blocked in @(@{ name = 'delete_boardgame'; id = $azul.id }, @{ name = 'delete_player'; id = $lee.id })) {
        $result = Invoke-Mcp 47 'tools/call' @{ name = $blocked.name; arguments = @{ id = $blocked.id } }
        if (-not $result.isError) { throw "Verwijdering met gekoppelde geschiedenis toegestaan: $($blocked.name)" }
    }
    $badParticipants = Invoke-Mcp 48 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $scored.id; changes = @{ players = @(@{ playerId = $sam.id }, @{ playerId = $sam.id }); winnerPlayerIds = @($sam.id) }
    } }
    if (-not $badParticipants.isError) { throw 'Dubbele deelnemers bij wijzigen geaccepteerd' }
    $badUnknown = Invoke-Mcp 49 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $scored.id; changes = @{ players = @(@{ playerId = 999999 }); winnerPlayerIds = @(999999) }
    } }
    if (-not $badUnknown.isError) { throw 'Onbekende deelnemer bij wijzigen geaccepteerd' }
    $badPosition = Invoke-Mcp 60 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $scored.id; changes = @{ playerChanges = @(@{ playerId = $sam.id; position = 0 }) }
    } }
    if (-not $badPosition.isError) { throw 'Ongeldige eindpositie bij wijzigen geaccepteerd' }

    $null = Get-ToolData (Invoke-Mcp 50 'tools/call' @{ name = 'delete_game_session'; arguments = @{ id = $scored.id } })
    $remaining = @(Get-ToolData (Invoke-Mcp 51 'tools/call' @{ name = 'get_game_sessions'; arguments = @{} }))
    if ($remaining.Count -ne 1 -or $remaining[0].id -ne $unscored.id) { throw 'Potje verwijderen heeft andere potjes geraakt' }
    $afterDelete = Get-ToolData (Invoke-Mcp 52 'tools/call' @{ name = 'get_play_statistics'; arguments = @{} })
    if ($afterDelete.totalPlays -ne 1) { throw 'Verwijderd potje telt nog mee in statistieken' }
    $null = Get-ToolData (Invoke-Mcp 53 'tools/call' @{ name = 'delete_boardgame'; arguments = @{ id = $azul.id } })
    $null = Get-ToolData (Invoke-Mcp 54 'tools/call' @{ name = 'delete_player'; arguments = @{ id = $lee.id } })
    $missing = Invoke-Mcp 55 'tools/call' @{ name = 'delete_game_session'; arguments = @{ id = 999999 } }
    if (-not $missing.isError) { throw 'Niet-bestaand potje verwijderen gaf geen fout' }

    if (@($unscored.expansions).Count -ne 0) { throw 'Bestaand potje heeft geen lege uitbreidingenlijst' }
    $firstExpansion = Get-ToolData (Invoke-Mcp 61 'tools/call' @{ name = 'add_game_expansion'; arguments = @{
        boardGameId = $eldorado.id; name = 'Helden'; description = 'Eerste uitbreiding'
    } })
    $secondExpansion = Get-ToolData (Invoke-Mcp 62 'tools/call' @{ name = 'add_game_expansion'; arguments = @{
        boardGameId = $eldorado.id; name = 'Grot'
    } })
    $otherExpansion = Get-ToolData (Invoke-Mcp 63 'tools/call' @{ name = 'add_game_expansion'; arguments = @{
        boardGameId = $carcassonne.id; name = 'Herbergen'
    } })
    $foundExpansions = @(Get-ToolData (Invoke-Mcp 64 'tools/call' @{ name = 'get_game_expansions'; arguments = @{
        boardGameId = $eldorado.id
    } }))
    if ($foundExpansions.Count -ne 2 -or $foundExpansions[0].boardGameName -ne 'El Dorado') {
        throw 'Meerdere uitbreidingen of bordspelnaam ontbreken'
    }
    $duplicateExpansion = Invoke-Mcp 65 'tools/call' @{ name = 'add_game_expansion'; arguments = @{
        boardGameId = $eldorado.id; name = 'helden'
    } }
    if (-not $duplicateExpansion.isError) { throw 'Dubbele uitbreiding met andere hoofdletters geaccepteerd' }
    $updatedExpansion = Get-ToolData (Invoke-Mcp 66 'tools/call' @{ name = 'update_game_expansion'; arguments = @{
        id = $firstExpansion.id; changes = @{ description = $null }
    } })
    if ($null -ne $updatedExpansion.description -or $updatedExpansion.name -ne 'Helden') {
        throw 'Beschrijving wissen wijzigde andere velden'
    }
    $renamedExpansion = Get-ToolData (Invoke-Mcp 79 'tools/call' @{ name = 'update_game_expansion'; arguments = @{
        id = $secondExpansion.id; changes = @{ name = 'De Grot' }
    } })
    if ($renamedExpansion.name -ne 'De Grot' -or $renamedExpansion.boardGameId -ne $eldorado.id) {
        throw 'Uitbreidingsnaam wijzigen of basisspel behouden mislukt'
    }

    $withoutExpansion = Get-ToolData (Invoke-Mcp 67 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $eldorado.id; playedAt = '2026-11-12';
        players = @(@{ playerId = $alex.id }, @{ playerId = $pat.id }); winnerPlayerIds = @($alex.id)
    } })
    if (@($withoutExpansion.expansions).Count -ne 0) { throw 'Potje zonder uitbreidingen kreeg koppelingen' }
    $withExpansion = Get-ToolData (Invoke-Mcp 68 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $eldorado.id; playedAt = '2026-11-12';
        players = @(@{ playerId = $alex.id }, @{ playerId = $pat.id }); winnerPlayerIds = @($pat.id);
        expansionIds = @($firstExpansion.id, $secondExpansion.id)
    } })
    if (@($withExpansion.expansions).Count -ne 2) { throw 'Potje met twee uitbreidingen is niet opgeslagen' }
    $loadedSession = @(Get-ToolData (Invoke-Mcp 80 'tools/call' @{ name = 'get_game_sessions'; arguments = @{
        from = '2026-11-12'; to = '2026-11-12'
    } })) | Where-Object { $_.id -eq $withExpansion.id }
    if (@($loadedSession).Count -ne 1 -or @($loadedSession.expansions).Count -ne 2) {
        throw 'get_game_sessions toont gebruikte uitbreidingen niet'
    }
    $wrongGameExpansion = Invoke-Mcp 69 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $eldorado.id; playedAt = '2026-11-12';
        players = @(@{ playerId = $alex.id }); winnerPlayerIds = @($alex.id);
        expansionIds = @($otherExpansion.id)
    } }
    if (-not $wrongGameExpansion.isError) { throw 'Uitbreiding van ander basisspel geaccepteerd' }
    $invalidUpdate = Invoke-Mcp 81 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $withExpansion.id; changes = @{ notes = 'Mag niet blijven'; expansionIds = @($otherExpansion.id) }
    } }
    if (-not $invalidUpdate.isError) { throw 'Uitbreiding van ander basisspel bij wijzigen geaccepteerd' }
    $afterInvalidUpdate = @(Get-ToolData (Invoke-Mcp 82 'tools/call' @{ name = 'get_game_sessions'; arguments = @{
        from = '2026-11-12'; to = '2026-11-12'
    } })) | Where-Object { $_.id -eq $withExpansion.id }
    if ($null -ne $afterInvalidUpdate.notes -or @($afterInvalidUpdate.expansions).Count -ne 2) {
        throw 'Ongeldige potjewijziging was niet atomair'
    }
    $duplicateLink = Invoke-Mcp 70 'tools/call' @{ name = 'record_game_session'; arguments = @{
        boardGameId = $eldorado.id; playedAt = '2026-11-12';
        players = @(@{ playerId = $alex.id }); winnerPlayerIds = @($alex.id);
        expansionIds = @($firstExpansion.id, $firstExpansion.id)
    } }
    if (-not $duplicateLink.isError) { throw 'Dubbele uitbreiding bij potje geaccepteerd' }

    $removedExpansion = Get-ToolData (Invoke-Mcp 71 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $withExpansion.id; changes = @{ expansionIdsToRemove = @($secondExpansion.id) }
    } })
    if (@($removedExpansion.expansions).Count -ne 1) { throw 'Uitbreiding verwijderen van potje mislukt' }
    $addedExpansion = Get-ToolData (Invoke-Mcp 72 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $withExpansion.id; changes = @{ expansionIdsToAdd = @($secondExpansion.id) }
    } })
    if (@($addedExpansion.expansions).Count -ne 2) { throw 'Uitbreiding toevoegen aan potje mislukt' }
    $blockedExpansionDelete = Invoke-Mcp 73 'tools/call' @{ name = 'delete_game_expansion'; arguments = @{
        id = $firstExpansion.id
    } }
    if (-not $blockedExpansionDelete.isError) { throw 'Gebruikte uitbreiding kon worden verwijderd' }
    $clearedExpansion = Get-ToolData (Invoke-Mcp 74 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $withExpansion.id; changes = @{ expansionIds = @() }
    } })
    if (@($clearedExpansion.expansions).Count -ne 0) { throw 'Uitbreidingenlijst leegmaken mislukt' }
    $restoredExpansion = Get-ToolData (Invoke-Mcp 75 'tools/call' @{ name = 'update_game_session'; arguments = @{
        id = $withExpansion.id; changes = @{ expansionIds = @($firstExpansion.id) }
    } })
    if (@($restoredExpansion.expansions).Count -ne 1) { throw 'Uitbreidingenlijst vervangen mislukt' }
    $null = Get-ToolData (Invoke-Mcp 76 'tools/call' @{ name = 'delete_game_session'; arguments = @{
        id = $withExpansion.id
    } })
    $stillExists = @(Get-ToolData (Invoke-Mcp 77 'tools/call' @{ name = 'get_game_expansions'; arguments = @{
        boardGameId = $eldorado.id
    } }))
    if ($stillExists.Count -ne 2) { throw 'Potje verwijderen heeft uitbreidingen verwijderd' }
    $null = Get-ToolData (Invoke-Mcp 78 'tools/call' @{ name = 'delete_game_expansion'; arguments = @{
        id = $firstExpansion.id
    } })
    Write-Output 'MCP-smoketest geslaagd: twintig tools, uitbreidingen, CRUD, statistieken, filters en validatie.'
}
finally {
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(3000)) { $process.Kill() }
    $process.Dispose()
    foreach ($suffix in @('', '-wal', '-shm')) { Remove-Item -LiteralPath "$database$suffix" -ErrorAction SilentlyContinue }
}
