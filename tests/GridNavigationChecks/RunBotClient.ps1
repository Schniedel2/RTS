param([switch]$SkipBuild, [switch]$Reconnect)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$SkipBuild) { dotnet build (Join-Path $repository 'tests/GridNavigationChecks/GridNavigationChecks.csproj'); if ($LASTEXITCODE -ne 0) { throw 'Build failed' } }
$runDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('RTS-bot-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$hostPeer = $null; $botPeer = $null
try {
    $harness = Join-Path $repository 'tests/GridNavigationChecks/bin/Debug/net9.0/GridNavigationChecks.dll'
    $bot = Join-Path $repository 'bin/Debug/net9.0/RTS.dll'
    $hostPeer = Start-Process dotnet -ArgumentList @(('"' + $harness + '"'), '--bot-host', ('"' + $runDirectory + '"'), $(if ($Reconnect) {'reconnect'} else {'normal'})) -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runDirectory 'host.out') -RedirectStandardError (Join-Path $runDirectory 'host.err') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (!(Test-Path (Join-Path $runDirectory 'port.txt'))) {
        if ($hostPeer.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw ('Host failed: ' + (Get-Content (Join-Path $runDirectory 'host.err') -Raw)) }
        Start-Sleep -Milliseconds 100
    }
    $port = [int](Get-Content (Join-Path $runDirectory 'port.txt'))
    $config = Join-Path $runDirectory 'bot.json'
    @{schemaVersion=1; serverAddress='127.0.0.1'; port=$port; displayName='Production Bot'; proposedProfileId='balanced-assault'; maximumArmies=1; reconnect=[bool]$Reconnect} | ConvertTo-Json | Set-Content $config -Encoding utf8
    $botPeer = Start-Process dotnet -ArgumentList @(('"' + $bot + '"'), '--bot-client', ('"' + $config + '"')) -WorkingDirectory $runDirectory -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runDirectory 'bot.out') -RedirectStandardError (Join-Path $runDirectory 'bot.err') -PassThru
    if (!$hostPeer.WaitForExit(50000)) { throw 'Host timed out' }
    if ($Reconnect) { Stop-Process -Id $botPeer.Id -Force; $botPeer.WaitForExit() }
    elseif (!$botPeer.WaitForExit(10000)) { throw 'Bot did not stop after disconnect' }
    foreach ($role in @('host','bot')) { $errors = Get-Content (Join-Path $runDirectory "$role.err") -Raw; if ($errors) { throw "$role failed: $errors" } }
    if ($hostPeer.ExitCode -ne 0 -or (!$Reconnect -and $botPeer.ExitCode -ne 2)) { throw "Unexpected exit codes: host=$($hostPeer.ExitCode), bot=$($botPeer.ExitCode)" }
    $result = Get-Content (Join-Path $runDirectory 'host.json') -Raw | ConvertFrom-Json
    if (!$result.assigned -or (!$Reconnect -and $result.health.LastFallbackReason) -or $result.health.HeartbeatAgeSeconds -gt 3) { throw 'Bot controller inactive' }
    Write-Output "Production bot passed: $runDirectory"
    Write-Output ($result | ConvertTo-Json -Depth 5)
}
finally {
    foreach ($peer in @($hostPeer,$botPeer)) { if ($null -ne $peer -and !$peer.HasExited) { Stop-Process -Id $peer.Id -Force } }
}
