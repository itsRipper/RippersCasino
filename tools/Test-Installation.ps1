#Requires -Version 7.0
param([Parameter(Mandatory)][string]$SptRoot)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=([xml](Get-Content -Raw -LiteralPath "$project/Directory.Build.props")).Project.PropertyGroup.Version
$package=Join-Path $project "artifacts/RippersCasino-$version"
if(!(Test-Path -LiteralPath "$package/BepInEx/plugins/RippersCasino/RippersCasino.Client.dll")){throw 'Build the complete package first.'}
$run=Join-Path $project ('test-runtime/install-preservation-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$script:assertions=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "FAIL $Name"};$script:assertions++;Write-Output "PASS $Name"}
function Hashes([string]$Directory){
 $result=@{}
 if(Test-Path -LiteralPath $Directory){foreach($file in Get-ChildItem -LiteralPath $Directory -Recurse -File){$result[[IO.Path]::GetRelativePath($Directory,$file.FullName)]=(Get-FileHash -LiteralPath $file.FullName).Hash}}
 return $result
}
function Same([hashtable]$Before,[hashtable]$After){
 if($Before.Count -ne $After.Count){return $false}
 foreach($key in $Before.Keys){if($Before[$key] -ne $After[$key]){return $false}}
 return $true
}
function Put([string]$Path,[object]$Value){
 New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($Path)) | Out-Null
 [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 50),[Text.UTF8Encoding]::new($false))
}
function New-Fixture([string]$Name,[bool]$WithState=$true){
 $target=Join-Path $run $Name
 New-Item -ItemType Directory -Path $target | Out-Null
 [IO.File]::WriteAllBytes((Join-Path $target 'EscapeFromTarkov.exe'),[byte[]]@())
 foreach($file in 'BepInEx/plugins/spt/spt-common.dll'){
  $dest=Join-Path $target $file
  New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($dest)) | Out-Null
  Copy-Item -LiteralPath (Join-Path $SptRoot $file) -Destination $dest
 }
 $script:profile=Join-Path $target 'SPT_Runtime/user/profiles/synthetic.json'
 $script:config=Join-Path $target 'SPT_Runtime/user/mods/RippersCasino/ripperscasino.json'
 if(!$WithState){return $target}
 Put $profile @{characters=@{pmc=@{Info=@{Nickname='InstallerFixture';Level=17};Inventory=@{items=@(@{_id='unrelated';_tpl='synthetic';upd=@{StackObjectsCount=123}})};CasinoWallet=@{Version=4;Chips=7654;RemainderRub=321;NextSequence=9;LastRequestId='saved-receipt';Last=@{Id='saved-receipt';Fingerprint='unchanged'};CasinoTableReceipt=@{Escrow=25;Liability=40;CommitId='saved-commit'}}}}}
 Put (Join-Path $target 'SPT_Runtime/user/mods/RippersCasino/table-data/tables.json') @{Schema=2;Revision=3;Tables=@();Actions=@()}
 Put (Join-Path $target 'SPT_Runtime/user/mods/RippersCasino/table-data/pending.json') @{CommitId='pending-commit';State=@{Schema=2;Revision=4};Wallets=@(@{PlayerId='synthetic';Chips=-25;Held=25})}
 Put (Join-Path $target 'SPT_Runtime/user/mods/RippersCasino/table-data/receipts/player/action.json') @{Fingerprint='archived-replay-proof'}
 $key=[Guid]::NewGuid().ToString('N').Substring(0,24)
 Put (Join-Path $target "BepInEx/config/RippersCasino/pending-$key.json") @{economyVersion=4;operation='redeem';transactionId=[Guid]::NewGuid().ToString('N');sequence=9;amount=5;exchangeVersion=3}
 Put (Join-Path $target "BepInEx/config/RippersCasino/table-pending-$key.json") @{economyVersion=4;operation='leave';requestId=[Guid]::NewGuid().ToString('N')}
 Put (Join-Path $target 'BepInEx/config/RippersCasino/preferences.json') @{volume=0.4}
 return $target
}
function Protected([string]$Target){
 $all=Hashes $Target;$result=@{}
 foreach($key in $all.Keys){if($key -match '(^|[\\/])(profiles|table-data|config)[\\/]'){$result[$key]=$all[$key]}}
 return $result
}
function Install([string]$Target,[string]$From=$package,[bool]$Success=$true){
 $log=Join-Path $run ('install-'+[Guid]::NewGuid().ToString('N')+'.log')
 & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'Install.ps1') -SptRoot $Target -PackageRoot $From *> $log
 $ok=$LASTEXITCODE -eq 0
 Check ($ok -eq $Success) "installer expected result ($([IO.Path]::GetFileName($Target)))"
 if($ok -ne $Success){Get-Content -LiteralPath $log -Tail 10}
}
$zip=Join-Path $project "artifacts/RippersCasino-$version.zip"
$runtime=@(Get-Content -Raw -LiteralPath "$PSScriptRoot/runtime-files.json" | ConvertFrom-Json)
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $entries=@($archive.Entries | Where-Object {!$_.FullName.EndsWith('/')} | ForEach-Object {$_.FullName.Replace('\','/')})
 Check (@(Compare-Object ($runtime | Sort-Object) ($entries | Sort-Object) -CaseSensitive).Count -eq 0) 'ZIP contains exactly the reviewed runtime files and notices'
 Check (@(Compare-Object @('BepInEx','SPT_Runtime') @($entries | ForEach-Object {($_ -split '/')[0]} | Sort-Object -Unique) -CaseSensitive).Count -eq 0) 'ZIP contains exactly two installation directories'
 Check (@($entries | Where-Object {$_ -notmatch '/'}).Count -eq 0) 'ZIP contains no root-level files'
}finally{$archive.Dispose()}
$checksum=Get-Content -LiteralPath (Join-Path $project 'artifacts/SHA256SUMS.txt')
Check ($checksum -ceq "$((Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant())  RippersCasino-$version.zip") 'external release checksum matches final ZIP'
$target=New-Fixture 'direct-extract-clean' -WithState $false
$before=Hashes $target
Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
foreach($file in $before.Keys){Check ((Get-FileHash -LiteralPath (Join-Path $target $file)).Hash -eq $before[$file]) "direct extraction preserves external dependency: $file"}
$runtime=@(Get-Content -Raw -LiteralPath "$PSScriptRoot/runtime-files.json" | ConvertFrom-Json)
foreach($file in $runtime){Check ((Get-FileHash -LiteralPath (Join-Path $package $file)).Hash -eq (Get-FileHash -LiteralPath (Join-Path $target $file)).Hash) "direct-extract runtime file hash: $file"}
foreach($name in 'README.md','INSTALL.md','LICENSE','Audio-NOTICE.txt','runtime-files.json','SHA256SUMS.txt'){Check (!(Test-Path -LiteralPath (Join-Path $target $name))) "direct extraction leaves no root file: $name"}
$notice=Get-Content -Raw -LiteralPath (Join-Path $target 'SPT_Runtime/user/mods/RippersCasino/NOTICE.txt')
foreach($source in 'LICENSE','licenses/HoldemPoker-LICENSE.txt','licenses/HoldemPoker-NOTICE.txt','licenses/Assets-NOTICE.md'){
 Check ($notice.Contains([IO.File]::ReadAllText((Join-Path $project $source)).TrimEnd())) "complete installed licence/notice: $source"
}
Check (!(Test-Path -LiteralPath (Join-Path $target 'SPT_Runtime/user/mods/RippersCasino/licenses'))) 'notices require no installed subdirectory'
Check (!(Test-Path -LiteralPath $config)) 'direct-extract package leaves live configuration creation to first server load'
Check (!(Test-Path -LiteralPath (Join-Path $target 'Install.ps1'))) 'player archive requires no installation script'
$target=New-Fixture 'direct-extract-upgrade'
Put $config @{economyVersion=4;enabled=$false;customSetting='preserve'}
$before=Protected $target;$configHash=(Get-FileHash -LiteralPath $config).Hash
Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
Check (Same $before (Protected $target)) 'direct-extract upgrade preserves all wallets, journals, receipts, requests and preferences'
Check ((Get-FileHash -LiteralPath $config).Hash -eq $configHash) 'direct-extract upgrade preserves custom live configuration byte-for-byte'
$target=New-Fixture 'missing-config'
$before=Protected $target
Install $target
Check (Same $before (Protected $target)) 'missing configuration preserves wallets, receipts, wagers, archive and preferences byte-for-byte'
Check (Test-Path -LiteralPath $config) 'missing configuration installs current defaults'
$runtime=@(Get-Content -Raw -LiteralPath "$PSScriptRoot/runtime-files.json" | ConvertFrom-Json)
foreach($file in $runtime){Check ((Get-FileHash -LiteralPath (Join-Path $package $file)).Hash -eq (Get-FileHash -LiteralPath (Join-Path $target $file)).Hash) "runtime file hash: $file"}
$custom=Get-Content -Raw -LiteralPath $config | ConvertFrom-Json -AsHashtable
$custom['enabled']=$false;Put $config $custom
$configHash=(Get-FileHash -LiteralPath $config).Hash
Install $target
Check (Same $before (Protected $target)) 'repeat installation preserves all protected state'
Check ((Get-FileHash -LiteralPath $config).Hash -eq $configHash) 'custom compatible configuration preserved exactly'
foreach($kind in 'old-config','missing-config-old-wallet','missing-wallet-version','old-table','old-pending','malformed-profile','invalid-table-mode'){
 $target=New-Fixture $kind
 if($kind -eq 'old-config'){Put $config @{economyVersion=3}}
 if($kind -in @('missing-config-old-wallet','missing-wallet-version')){
  $doc=Get-Content -Raw -LiteralPath $profile | ConvertFrom-Json -AsHashtable
  if($kind -eq 'missing-wallet-version'){[void]$doc.characters.pmc.CasinoWallet.Remove('Version')}else{$doc.characters.pmc.CasinoWallet.Version=3}
  Put $profile $doc
 }
 if($kind -eq 'old-table'){Put (Join-Path $target 'SPT_Runtime/user/mods/RippersCasino/table-data/tables.json') @{Schema=99}}
 if($kind -eq 'old-pending'){
  $pending=Get-ChildItem -LiteralPath (Join-Path $target 'BepInEx/config/RippersCasino') -Filter 'pending-*.json' | Select-Object -First 1
  Put $pending.FullName @{economyVersion=3}
 }
 if($kind -eq 'invalid-table-mode'){Put (Join-Path $target 'SPT_Runtime/user/mods/RippersCasino/table-data/tables.json') @{Schema=2;Tables=@(@{Solo=$false;Seats=@()})}}
 if($kind -eq 'malformed-profile'){[IO.File]::WriteAllText($profile,'{invalid')}
 $before=Hashes $target
 Install $target -Success $false
 Check (Same $before (Hashes $target)) "incompatible $kind fails before changing any installed file"
}
foreach($kind in 'missing-checksums','uncovered-runtime','tampered-runtime','unexpected-root-file','missing-runtime'){
 $broken=Join-Path $run $kind
 Copy-Item -LiteralPath $package -Destination $broken -Recurse
 Copy-Item -LiteralPath "$package.files.sha256" -Destination "$broken.files.sha256"
 if($kind -eq 'missing-checksums'){Remove-Item -LiteralPath "$broken.files.sha256"}
 if($kind -eq 'uncovered-runtime'){
  $lines=Get-Content -LiteralPath "$broken.files.sha256"
  $lines | Where-Object {$_ -notmatch 'RippersCasino.Client.dll$'} | Set-Content -LiteralPath "$broken.files.sha256"
 }
 if($kind -eq 'unexpected-root-file'){[IO.File]::WriteAllText("$broken/README.md",'unexpected')}
 if($kind -eq 'missing-runtime'){Remove-Item -LiteralPath "$broken/SPT_Runtime/user/mods/RippersCasino/NOTICE.txt"}
 if($kind -eq 'tampered-runtime'){[IO.File]::AppendAllText("$broken/BepInEx/plugins/RippersCasino/RippersCasino.Client.dll",'tamper')}
 $target=New-Fixture ('target-'+$kind);$before=Hashes $target
 Install $target -From $broken -Success $false
 Check (Same $before (Hashes $target)) "$kind fails before installed files change"
}
Write-Output "PASS: $script:assertions installation assertions. Disposable evidence: $run"
