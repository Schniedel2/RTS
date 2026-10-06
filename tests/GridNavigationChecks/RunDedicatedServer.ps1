param([int]$Seconds=300,[string]$OutputDirectory='',[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$repository=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if($Seconds -lt 180){throw 'Use at least 180 seconds.'}
if(!$SkipBuild){dotnet build (Join-Path $repository 'tests/GridNavigationChecks/GridNavigationChecks.csproj');if($LASTEXITCODE -ne 0){throw 'Build failed'}}
if(!$OutputDirectory){$OutputDirectory=Join-Path $env:TEMP ('RTS-dedicated-' + [guid]::NewGuid().ToString('N'))}
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
Write-Output "Results: $OutputDirectory"
$dll=Join-Path $repository 'tests/GridNavigationChecks/bin/Debug/net9.0/GridNavigationChecks.dll'
& dotnet $dll --dedicated-check $OutputDirectory $Seconds
if($LASTEXITCODE -ne 0){throw 'Dedicated server scenario failed'}
