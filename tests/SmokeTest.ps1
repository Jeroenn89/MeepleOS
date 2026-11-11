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

try {
    $null = Invoke-Mcp 1 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'meepleos-smoke'; version = '1.0' } }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $tools = Invoke-Mcp 2 'tools/list' @{}
    $names = @($tools.tools | ForEach-Object { $_.name })
    foreach ($name in @('add_boardgame', 'get_boardgames', 'add_player', 'get_players')) {
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
    Write-Output 'MCP-smoketest geslaagd: vier tools, opslag, dubbele speler en validatie.'
}
finally {
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(3000)) { $process.Kill() }
    $process.Dispose()
    foreach ($suffix in @('', '-wal', '-shm')) { Remove-Item -LiteralPath "$database$suffix" -ErrorAction SilentlyContinue }
}
