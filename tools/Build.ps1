#Requires -Version 7.0
param([Parameter(Mandatory)][string]$SptRoot,[switch]$SkipTests)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=([xml](Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+(?:-rc\.\d+)?$'){throw 'Invalid casino package version'}
foreach($project in 'client/RippersCasino.Client.csproj','server/RippersCasino.Server.csproj'){
 $declared=([xml](Get-Content -Raw -LiteralPath (Join-Path $root $project))).Project.PropertyGroup.Version
 if($declared -ne $version){throw "Product version mismatch: $project"}
}
$packageName="RippersCasino-$version"
$env:MSBuildEnableWorkloadResolver='false'
function Run-Dotnet([string[]]$Arguments){& dotnet @Arguments;if($LASTEXITCODE -ne 0){throw "dotnet failed ($LASTEXITCODE)"}}
Run-Dotnet -Arguments @('build',"$root/RippersCasino.slnx",'-c','Release',"-p:SptRoot=$SptRoot",'-p:UseSharedCompilation=false','--nologo')
if(!$SkipTests){
 Run-Dotnet -Arguments @('run','--project',"$root\tests\RippersCasino.Tests.csproj",'-c','Release','-p:UseSharedCompilation=false')
 Run-Dotnet -Arguments @('run','--project',"$root\transaction-tests\RippersCasino.TransactionTests.csproj",'-c','Release',"-p:SptRoot=$SptRoot",'-p:UseSharedCompilation=false','--',"$SptRoot\SPT_Runtime")
}
$package=Join-Path $root (Join-Path 'artifacts' $packageName)
if(Test-Path -LiteralPath $package){
 $resolved=[IO.Path]::GetFullPath($package)
 if([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))){throw 'Invalid package cleanup target'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
$files=@(Get-Content -Raw -LiteralPath "$PSScriptRoot/runtime-files.json" | ConvertFrom-Json)
foreach($file in $files){
 if($file -notmatch '^(SPT_Runtime/user/mods/RippersCasino/|BepInEx/plugins/RippersCasino/)' -or $file -match '(^|/)\.\.(/|$)'){throw 'Invalid runtime file path'}
 $target=Join-Path $package $file
 $relative=$file -replace '^SPT_Runtime/user/mods/RippersCasino/',''
 $source=if($file -like 'BepInEx/plugins/RippersCasino/assets/*'){Join-Path $root ('assets/'+[IO.Path]::GetFileName($file))}
 elseif($file.EndsWith('RippersCasino.Client.dll')){Join-Path $root 'client/bin/Release/net472/RippersCasino.Client.dll'}
 else{Join-Path $root ('server/bin/Release/net10.0/'+$relative)}
 New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
 if($relative -eq 'NOTICE.txt'){
  $sections=foreach($notice in 'LICENSE','licenses/HoldemPoker-LICENSE.txt','licenses/HoldemPoker-NOTICE.txt','licenses/Assets-NOTICE.md'){
   "===== $notice =====`n`n"+[IO.File]::ReadAllText((Join-Path $root $notice)).TrimEnd()
  }
  [IO.File]::WriteAllText($target,($sections -join "`n`n")+"`n",[Text.UTF8Encoding]::new($false))
 }else{Copy-Item -LiteralPath $source -Destination $target}
}
$actual=@(Get-ChildItem -LiteralPath $package -Recurse -File -Force | ForEach-Object {[IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/')})
if(@(Compare-Object ($files | Sort-Object) ($actual | Sort-Object) -CaseSensitive).Count -ne 0){throw 'Packaged files differ from the reviewed runtime manifest'}
$top=@(Get-ChildItem -LiteralPath $package -Force | Select-Object -ExpandProperty Name)
if(@(Compare-Object @('BepInEx','SPT_Runtime') ($top | Sort-Object) -CaseSensitive).Count -ne 0){throw 'Package must contain exactly BepInEx and SPT_Runtime'}
$files | Sort-Object | ForEach-Object {"$((Get-FileHash -LiteralPath (Join-Path $package $_)).Hash.ToLowerInvariant())  $_"} | Set-Content -LiteralPath "$package.files.sha256" -Encoding utf8NoBOM
$zip=Join-Path $root "artifacts/$packageName.zip"
Compress-Archive -Path "$package/BepInEx","$package/SPT_Runtime" -DestinationPath $zip -Force
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $entries=@($archive.Entries | Where-Object {!$_.FullName.EndsWith('/')} | ForEach-Object {$_.FullName.Replace('\','/')})
 if(@(Compare-Object ($files | Sort-Object) ($entries | Sort-Object) -CaseSensitive).Count -ne 0){throw 'ZIP entries differ from the reviewed runtime manifest'}
}finally{$archive.Dispose()}
"$((Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant())  $packageName.zip" | Set-Content -LiteralPath (Join-Path $root 'artifacts/SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Output "Package: $zip"
Write-Output "Release checksum: $root/artifacts/SHA256SUMS.txt"
Write-Output "Maintainer file checksums: $package.files.sha256"
