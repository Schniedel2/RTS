param([Parameter(Mandatory)][string]$InputDirectory, [Parameter(Mandatory)][string]$OutputFile)
$ErrorActionPreference = 'Stop'
$runs = @()
foreach ($mode in @('host','one','shared','split','stress')) {
    $directory = Join-Path $InputDirectory $mode
    $resultFile = Join-Path $directory 'result.json'
    if (!(Test-Path $resultFile)) { continue }
    $result = Get-Content $resultFile -Raw | ConvertFrom-Json -AsHashtable
    $hostReport = Get-Content (Join-Path $directory 'host-metrics.json') -Raw | ConvertFrom-Json -AsHashtable
    $bots = @()
    foreach ($file in @(Get-ChildItem $directory -Filter '*-metrics.json' | Where-Object Name -ne 'host-metrics.json')) {
        $bots += @{ name=$file.BaseName; report=(Get-Content $file.FullName -Raw | ConvertFrom-Json -AsHashtable) }
    }
    $runs += @{ mode=$mode; result=$result; host=$hostReport; bots=$bots }
}
if (!$runs.Count) { throw 'No completed benchmark results found.' }
$cpu = Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors
$report = @{ schemaVersion=1; measuredAt=[DateTime]::UtcNow.ToString('o'); rawDirectory=(Resolve-Path $InputDirectory).Path; cpu=$cpu; runs=$runs }
$target = [System.IO.Path]::GetFullPath($OutputFile)
New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
$report | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $target -Encoding utf8
Write-Output "Saved: $target"
