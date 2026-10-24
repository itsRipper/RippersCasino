#Requires -Version 7.4
param([Parameter(Mandatory)][string]$SptRoot,[string]$ModSource='',[ValidateRange(1024,65535)][int]$Port=6978)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$ModSource){$ModSource=Join-Path $root 'server\bin\Release\net10.0'}
$run=Join-Path $root ('test-runtime\public-release-qa\'+[Guid]::NewGuid().ToString('N'))
$fixture=Join-Path $run 'backend'
$runtime=Join-Path $SptRoot 'SPT_Runtime'
New-Item -ItemType Directory -Force -Path $fixture,"$fixture\SPT_Data","$fixture\user\profiles","$fixture\user\mods" | Out-Null
Get-ChildItem -LiteralPath $runtime -File | Where-Object {$_.Extension -eq '.dll' -or $_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json' -or $_.Name -eq 'sptLogger.json'} | Copy-Item -Destination $fixture
Copy-Item -LiteralPath "$runtime\SPT_Data\configs" -Destination "$fixture\SPT_Data\configs" -Recurse
foreach($folder in 'database','images','wwwroot'){New-Item -ItemType Junction -Path "$fixture\SPT_Data\$folder" -Target "$runtime\SPT_Data\$folder" | Out-Null}
Copy-Item -LiteralPath "$runtime\SPT_Data\checks.dat" -Destination "$fixture\SPT_Data"
$http=Get-Content -LiteralPath "$fixture\SPT_Data\configs\http.json" -Raw | ConvertFrom-Json
$http.ip='127.0.0.1';$http.backendIp='127.0.0.1';$http.port=$Port;$http.backendPort=$Port
$http | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath "$fixture\SPT_Data\configs\http.json"
$mod="$fixture\user\mods\RippersCasino"
New-Item -ItemType Directory -Path $mod | Out-Null
foreach($file in 'RippersCasino.Server.dll','RippersCasino.Core.dll','HoldemPoker.Evaluator.dll','HoldemPoker.Cards.dll','ripperscasino.defaults.json'){Copy-Item -LiteralPath "$ModSource\$file" -Destination $mod}
# No player profiles are copied. Register a new disposable account after starting this backend.
Write-Output $run
