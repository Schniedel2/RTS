param(
    [ValidateSet('host','one','shared','split','stress')][string[]]$Modes = @('host','one','shared','split','stress'),
    [int]$Seconds = 75, [int]$StressSeconds = 300, [switch]$SkipBuild,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($Seconds -lt 60 -or $StressSeconds -lt 180) { throw 'Use at least 60 seconds for comparison and 180 for stress.' }
if (!$SkipBuild) { dotnet build (Join-Path $repository 'tests/GridNavigationChecks/GridNavigationChecks.csproj'); if ($LASTEXITCODE -ne 0) { throw 'Build failed' } }
if (!$OutputDirectory) { $OutputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('RTS-ai-comparison-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Write-Output "Results: $OutputDirectory"
$dll = Join-Path $repository 'tests/GridNavigationChecks/bin/Debug/net9.0/GridNavigationChecks.dll'
foreach ($mode in $Modes) {
    $directory = Join-Path $OutputDirectory $mode
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $duration = if ($mode -eq 'stress') { $StressSeconds } else { $Seconds }
    $peer = Start-Process dotnet -ArgumentList @(('"' + $dll + '"'), '--ai-benchmark', $mode, ('"' + $directory + '"'), $duration) -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'host.out') -RedirectStandardError (Join-Path $directory 'host.err') -PassThru
    try {
        # Keep each blocking wait short so the caller can inspect progress.
        while (!$peer.WaitForExit(10000)) { if (([DateTime]::UtcNow - $peer.StartTime.ToUniversalTime()).TotalSeconds -gt $duration + 60) { throw "$mode timed out" } }
        $errors = Get-Content (Join-Path $directory 'host.err') -Raw
        if ($peer.ExitCode -ne 0 -or $errors) { throw "$mode failed: $errors" }
        $result = Get-Content (Join-Path $directory 'result.json') -Raw | ConvertFrom-Json -AsHashtable
        $botReports = @(Get-ChildItem $directory -Filter '*-metrics.json' | Where-Object Name -ne 'host-metrics.json')
        if ($mode -ne 'host' -and !$botReports.Count) { throw 'No bot metrics' }
        foreach ($report in $botReports) {
            $data = Get-Content $report.FullName -Raw | ConvertFrom-Json -AsHashtable
            if ($report.Name -notlike 'LateObserver*' -and $data.metrics.MaximumControllers -lt $(if ($mode -in @('shared','stress')) {2} else {1})) { throw 'Missing active controllers' }
        }
        if ($mode -eq 'stress') {
            $bot = Get-Content (Join-Path $directory 'BenchBot0-metrics.json') -Raw | ConvertFrom-Json -AsHashtable
            $observer = Get-Content (Join-Path $directory 'LateObserver-metrics.json') -Raw | ConvertFrom-Json -AsHashtable
            if ($bot.metrics.Snapshots -lt 2 -or $bot.metrics.MatchStarts -ne 2 -or
                $observer.metrics.Snapshots -ne 1 -or $observer.metrics.MatchStarts -ne 2 -or
                $observer.metrics.MaximumControllers -ne 0 -or $result.rejections -lt 1) { throw 'Incomplete replica/reconnect/rejection evidence' }
        }
        if (Get-Content (Join-Path $directory 'bot-logs.txt') | Select-String '^ERROR ') { throw 'Bot process reported an error' }
        Write-Output "$mode passed: maxUnits=$($result.maxUnits), requests=$((@($result.requests.Values) | Measure-Object -Sum).Sum)"
    }
    finally { if (!$peer.HasExited) { Stop-Process -Id $peer.Id -Force }; $peer.Dispose() }
}
