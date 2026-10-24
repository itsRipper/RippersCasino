#Requires -Version 7.0
param([Parameter(Mandatory)][string]$SptRoot,[string]$PackageRoot)
$ErrorActionPreference='Stop'
$SptRoot=[IO.Path]::GetFullPath($SptRoot)
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=([xml](Get-Content -Raw -LiteralPath (Join-Path $project 'Directory.Build.props'))).Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+(?:-rc\.\d+)?$'){throw 'Invalid casino package version'}
$package=if($PackageRoot){[IO.Path]::GetFullPath($PackageRoot)}else{Join-Path $project "artifacts/RippersCasino-$version"}
if(!(Test-Path -LiteralPath "$SptRoot\EscapeFromTarkov.exe")){throw 'The SPT root must contain EscapeFromTarkov.exe'}
if([Reflection.AssemblyName]::GetAssemblyName("$SptRoot\BepInEx\plugins\spt\spt-common.dll").Version.ToString() -ne '4.1.6.0'){throw 'RippersCasino requires SPT 4.1.6'}
if(Get-Process -Name EscapeFromTarkov,SPT.Server -ErrorAction SilentlyContinue){throw 'Close Tarkov and the SPT server before installing'}
$backendDll=Join-Path $SptRoot 'SPT_Runtime\SPT.Server.dll'
if(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($backendDll,[StringComparison]::OrdinalIgnoreCase)}){throw 'Close the SPT backend before installing'}
$runtimeManifest=Join-Path $PSScriptRoot 'runtime-files.json'
if(!(Test-Path -LiteralPath $runtimeManifest)){throw 'Missing source runtime manifest. Use the complete source checkout.'}
$files=@(Get-Content -Raw -LiteralPath $runtimeManifest | ConvertFrom-Json)
if($files.Count -eq 0 -or @($files | Sort-Object -Unique).Count -ne $files.Count){throw 'Invalid runtime file manifest'}
foreach($file in $files){
 $resolved=[IO.Path]::GetFullPath((Join-Path $package $file))
 if(!$resolved.StartsWith($package+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $file -notmatch '^(SPT_Runtime/user/mods/RippersCasino/|BepInEx/plugins/RippersCasino/)' -or $file -match '(^|/)\.\.(/|$)'){throw 'Invalid runtime file path'}
}

foreach($file in $files){if(!(Test-Path -LiteralPath (Join-Path $package $file))){throw "Incomplete package: $file"}}
$actual=@(Get-ChildItem -LiteralPath $package -Recurse -File -Force | ForEach-Object {[IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/')})
if(@(Compare-Object ($files | Sort-Object) ($actual | Sort-Object) -CaseSensitive).Count -ne 0){throw 'Package contains missing or unexpected files'}
$top=@(Get-ChildItem -LiteralPath $package -Force | Select-Object -ExpandProperty Name)
if(@(Compare-Object @('BepInEx','SPT_Runtime') ($top | Sort-Object) -CaseSensitive).Count -ne 0){throw 'Package must contain exactly BepInEx and SPT_Runtime'}
$manifest="$package.files.sha256"
if(!(Test-Path -LiteralPath $manifest)){throw 'Missing external file checksums. Build the complete package first.'}
$covered=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($line in Get-Content -LiteralPath $manifest){
 $parts=$line -split '  ',2
 if($parts.Length -ne 2 -or $parts[0] -notmatch '^[a-f0-9]{64}$'){throw 'Invalid package checksum manifest'}
 $resolved=[IO.Path]::GetFullPath((Join-Path $package $parts[1]))
 if(!$resolved.StartsWith($package+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !$covered.Add($resolved)){throw 'Invalid or duplicate package checksum path'}
 if((Get-FileHash -LiteralPath $resolved).Hash.ToLowerInvariant() -ne $parts[0]){throw "Package checksum mismatch: $($parts[1])"}
}
if($covered.Count -ne $files.Count){throw 'Package checksums differ from the runtime manifest'}
foreach($file in $files){
 if(!$covered.Contains([IO.Path]::GetFullPath((Join-Path $package $file)))){throw "Package checksum missing: $file"}
}
# Installation never edits player data or recovery records, regardless of configuration.
$profileRoot=Join-Path $SptRoot 'SPT_Runtime/user/profiles'
if(Test-Path -LiteralPath $profileRoot){foreach($profile in Get-ChildItem -LiteralPath $profileRoot -File -Filter '*.json'){
 $document=Get-Content -Raw -LiteralPath $profile.FullName | ConvertFrom-Json -AsHashtable
 $pmc=$document.characters.pmc
 $wallet=$pmc.CasinoWallet
 if($null -ne $wallet -and $wallet.Version -ne 4){throw 'Incompatible casino wallet. Installation stopped without changing saves. Back up your profiles and casino recovery files; use the matching older mod or an explicitly reviewed conversion tool. Do not delete the wallet.'}
}}
$installedConfig=Join-Path $SptRoot 'SPT_Runtime/user/mods/RippersCasino/ripperscasino.json'
if(Test-Path -LiteralPath $installedConfig){
 $existing=Get-Content -Raw -LiteralPath $installedConfig | ConvertFrom-Json -AsHashtable
 if($existing.economyVersion -ne 4){throw 'Incompatible casino configuration. Installation stopped without changing saves. Back up the old configuration, then replace only ripperscasino.json with this release default after confirming wallet v4 compatibility. Never remove profiles, table-data or retained requests.'}
}
$tableRoot=Join-Path $SptRoot 'SPT_Runtime/user/mods/RippersCasino/table-data'
foreach($name in 'tables.json','pending.json'){
 $saved=Join-Path $tableRoot $name
 if(Test-Path -LiteralPath $saved){
  $table=Get-Content -Raw -LiteralPath $saved | ConvertFrom-Json -AsHashtable
  $schema=if($name -eq 'pending.json'){$table.State.Schema}else{$table.Schema}
  $state=if($name -eq 'pending.json'){$table.State}else{$table}
  if(@($state.Tables | Where-Object {$null -ne $_ -and (!$_.Solo -or $_.Seats.Count -gt 1)}).Count -gt 0){throw 'Invalid saved casino table. Installation stopped; reservations and journals were preserved.'}
  if($schema -ne 2){throw 'Incompatible casino table save. Installation stopped; keep table-data and use its matching mod version for recovery.'}
 }
}
$pendingRoot=Join-Path $SptRoot 'BepInEx/config/RippersCasino'
if(Test-Path -LiteralPath $pendingRoot){foreach($saved in Get-ChildItem -LiteralPath $pendingRoot -File -Filter '*pending-*.json'){
 $request=Get-Content -Raw -LiteralPath $saved.FullName | ConvertFrom-Json -AsHashtable
 if($request.economyVersion -ne 4){throw 'Incompatible retained casino request. Installation stopped; recover it with the matching mod before upgrading. The request was preserved.'}
}}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$backup=Join-Path $SptRoot "SPT_Runtime/user/backups/RippersCasino/install-$stamp"
New-Item -ItemType Directory -Force -Path $backup | Out-Null
foreach($file in $files){
 $source=Join-Path $package $file;$target=Join-Path $SptRoot $file
 if(Test-Path -LiteralPath $target){$saved=Join-Path "$backup\previous-version" $file;New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($saved)) | Out-Null;Copy-Item -LiteralPath $target -Destination $saved}
 New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
 Copy-Item -LiteralPath $source -Destination $target -Force
 Write-Output "Installed $file"
}

if(!(Test-Path -LiteralPath $installedConfig)){
 $defaults=Join-Path $SptRoot 'SPT_Runtime/user/mods/RippersCasino/ripperscasino.defaults.json'
 [IO.File]::Copy($defaults,$installedConfig,$false)
}
Write-Output 'Casino installation complete. Restart SPT and open RippersCasino through the Intelligence Center laptop. Requires Generator 1 and Intelligence Center 1.'
