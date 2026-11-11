$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $root 'src/MeepleOS.ServerLayer/bin/Debug/net10.0/MeepleOS.ServerLayer.dll'
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
    if ($result.structuredContent) { return $result.structuredContent }
    return ($result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text | ConvertFrom-Json
}

try {
    $null = Invoke-Mcp 1 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'meepleos-smoke'; version = '1.0' } }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $tools = Invoke-Mcp 2 'tools/list' @{}
    $names = @($tools.tools | ForEach-Object { $_.name })
    foreach ($name in @('add_boardgame', 'get_boardgames', 'add_player', 'get_players', 'record_game_session', 'get_game_sessions')) {
        if ($name -notin $names) { throw "Ontbrekende tool: $name" }
    }

    $game = Invoke-Mcp 3 'tools/call' @{ name = 'add_boardgame'; arguments = @{ name = ' Azul '; minPlayers = 2; maxPlayers = 4; estimatedMinutes = 45 } }
    if ($game.isError) { throw 'add_boardgame mislukt' }
    $games = Invoke-Mcp 4 'tools/call' @{ name = 'get_boardgames'; arguments = @{} }
    if ($games.isError -or ($games | ConvertTo-Json -Depth 10) -notmatch 'Azul') { throw 'get_boardgames gaf Azul niet terug' }

    $player = Invoke-Mcp 5 'tools/call' @{ name = 'add_player'; arguments = @{ name = ' Alex ' } }
    if ($player.isError) { throw 'add_player mislukt' }
    $duplicate = Invoke-Mcp 6 'tools/call' @{ name = 'add_player'; arguments = @{ name = 'alex' } }
    if ($duplicate.isError) { throw 'Dubbele speler gaf een fout' }
    $players = Invoke-Mcp 7 'tools/call' @{ name = 'get_players'; arguments = @{} }
    $playerJson = $players | ConvertTo-Json -Depth 10
    if ($players.isError -or $playerJson -notmatch 'Alex' -or $playerJson -match '"name":\s*"alex"') { throw 'Dubbele speler werd niet goed afgehandeld' }

    $invalid = Invoke-Mcp 8 'tools/call' @{ name = 'add_boardgame'; arguments = @{ name = 'Fout'; minPlayers = 4; maxPlayers = 2; estimatedMinutes = 30 } }
    if (-not $invalid.isError) { throw 'Ongeldig spelersaantal werd geaccepteerd' }

    $azul = Get-ToolData $game
    $alex = Get-ToolData $player
    $sam = Get-ToolData (Invoke-Mcp 9 'tools/call' @{ name = 'add_player'; arguments = @{ name = 'Sam' } })
    $pat = Get-ToolData (Invoke-Mcp 10 'tools/call' @{ name = 'add_player'; arguments = @{ name = 'Pat' } })
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

    Write-Output 'MCP-smoketest geslaagd: zes tools, potjes met en zonder scores, gedeelde winst, filters en validatie.'
}
finally {
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(3000)) { $process.Kill() }
    $process.Dispose()
    foreach ($suffix in @('', '-wal', '-shm')) { Remove-Item -LiteralPath "$database$suffix" -ErrorAction SilentlyContinue }
}
