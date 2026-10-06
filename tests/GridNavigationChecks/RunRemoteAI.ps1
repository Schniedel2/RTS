param([ValidateSet("normal", "disconnect", "stall")][string]$Mode = "normal", [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $PSScriptRoot 'GridNavigationChecks.csproj'
if (!$SkipBuild) {
    & dotnet build $project --no-restore -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Harness build failed' }
}
$runDirectory = Join-Path $env:TEMP ('RTS-remote-ai-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$dll = Join-Path $PSScriptRoot 'bin/Debug/net9.0/GridNavigationChecks.dll'
$hostPeer = $null; $clientPeer = $null
try {
    $hostPeer = Start-Process dotnet -ArgumentList @(('"' + $dll + '"'), '--remote-ai-peer', 'host', ('"' + $runDirectory + '"'), $Mode) -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runDirectory 'host.out') -RedirectStandardError (Join-Path $runDirectory 'host.err') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (!(Test-Path (Join-Path $runDirectory 'port.txt'))) {
        if ($hostPeer.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Host did not become ready' }
        Start-Sleep -Milliseconds 100
    }
    $clientPeer = Start-Process dotnet -ArgumentList @(('"' + $dll + '"'), '--remote-ai-peer', 'client', ('"' + $runDirectory + '"'), $Mode) -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runDirectory 'client.out') -RedirectStandardError (Join-Path $runDirectory 'client.err') -PassThru
    # Both real processes have bounded 38/40 second lifetimes; preserve their final diagnostics.
    if (!$clientPeer.WaitForExit(55000)) { throw 'Client process timed out' }
    if (!$hostPeer.WaitForExit(10000)) { throw 'Host process timed out' }
    foreach ($role in @('host', 'client')) {
        $errorText = Get-Content (Join-Path $runDirectory "$role.err") -Raw
        if ($errorText) { throw "$role failed: $errorText" }
    }
    if ($hostPeer.ExitCode -ne 0 -or $clientPeer.ExitCode -ne 0) { throw 'Peer exited unsuccessfully' }
    $hostResult = Get-Content (Join-Path $runDirectory 'host.json') -Raw | ConvertFrom-Json -AsHashtable
    $clientResult = Get-Content (Join-Path $runDirectory 'client.json') -Raw | ConvertFrom-Json -AsHashtable
    if (!$hostResult.assigned -or ($Mode -eq 'normal' -and $clientResult.controllers.Count -ne 1)) { throw 'Missing assigned remote controller' }
    foreach ($request in @('BuildRequest', 'TrainUnitRequest', 'HarvestRequest', 'GotoRequest', 'AttackTargetRequest')) {
        if ($hostResult.requests.$request -lt 1) { throw "No $request arrived at host" }
    }
    if ($hostResult.exploredGain -le 0) { throw 'No exploration progress' }
    if ($hostResult.maxCargo -le 0 -or $hostResult.enemyDamage -le 0) { throw 'No authoritative harvest/combat progress' }
    if ($hostResult.units.reaktor -lt 1 -or $hostResult.units.'squad-leader' -lt 1) { throw 'No construction/production progress' }
    if ($Mode -eq 'normal' -and ($clientResult.feedbacks.Completed -lt 1 -or $clientResult.feedbacks.InProgress -lt 1 -or $clientResult.rejectedProbe -ne 'Rejected')) { throw 'Missing execution or rejection feedback' }
    if ($Mode -ne 'normal') {
        if (!$hostResult.fallbackSeen -or !$hostResult.profilePreserved -or !$hostResult.constructionFinished -or $hostResult.hostRequests -lt 1 -or $hostResult.remoteRequests -lt 1) { throw 'Handoff did not preserve construction or resume host decisions' }
        if (!$clientResult.interrupted -or $clientResult.controllers.Count -ne 0 -or $clientResult.staleProbe -ne 'Rejected') { throw 'Old controller still runs or stale token accepted' }
        if ($hostResult.requests.ResearchRequest -ne 1) { throw 'Paid research duplicated across handoff' }
        if ($Mode -eq 'disconnect' -and (!$clientResult.rejoined -or $clientResult.Status -ne 4)) { throw 'Reconnect did not synchronize' }
        if ($Mode -eq 'stall' -and $hostResult.fallbackReason -notmatch 'heartbeat') { throw 'Stalled controller was not detected' }
    }
    Write-Output "Remote AI two-process $Mode checks passed. Diagnostics: $runDirectory"
    $hostResult | ConvertTo-Json -Depth 5
    $clientResult | Select-Object Status, rejectedProbe, feedbacks | ConvertTo-Json -Depth 5
} finally {
    foreach ($peer in @($clientPeer, $hostPeer)) { if ($null -ne $peer -and !$peer.HasExited) { Stop-Process -Id $peer.Id } }
}
