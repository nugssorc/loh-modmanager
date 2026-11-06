# 외부 모드 설치(압축·폴더를 끌어다 놓기): 풀기 → 종류 판별 → 고른 갈래만 설치 + 설치 기록 → 기록으로 정확히 빼기.
# 압축 안의 설치 스크립트(.bat/.cmd/.ps1/.py/.exe)는 실행하지 않는다. 파일만 읽고 옮긴다. 계획: docs/설치기_계획.md
$script:MMProxyNames=@('version.dll','hid.dll','winhttp.dll','winmm.dll','dwmapi.dll','dinput8.dll','d3d9.dll','d3d11.dll','dxgi.dll','dsound.dll','xinput1_3.dll','xinput1_4.dll','opengl32.dll','iphlpapi.dll','msimg32.dll','wininet.dll')
# Loader entries: a proxy DLL that starts a .NET loader (Unity Doorstop for BepInEx and others, MelonLoader) has to keep its own name
# and run first, so the manager never takes one over or installs one. Told apart by what the file itself says, not by a mod's name.
function Get-MMLoaderEntry([string]$Path){
    try{
        $item=Get-Item -LiteralPath $Path -ErrorAction Stop;if($item.Length -gt 32MB){return ''}
        $b=[IO.File]::ReadAllBytes($Path);$texts=@([Text.Encoding]::ASCII.GetString($b),[Text.Encoding]::Unicode.GetString($b),$(if($b.Length -gt 1){[Text.Encoding]::Unicode.GetString($b,1,$b.Length-1)}else{''}))
        if(@($texts | Where-Object {$_ -match '(?i)MelonLoader'}).Count){return 'MelonLoader'}
        # the HeroesVow common loader (빠른거래 · 삼혼비급탭 …): its plugins find it as 'version.dll' (GetModuleHandle), so under another
        # name they register nowhere and stop without a word (11-03 디시 79360). Only the loader reads MODs\NativePlugins — the
        # manager's own entry names its two exports to forward them, never that folder
        if(@($texts | Where-Object {$_ -match 'NativePlugins'}).Count){return 'HeroesVow'}
        if(@($texts | Where-Object {$_ -match '(?i)doorstop'}).Count){return $(if(Test-Path -LiteralPath (Join-Path $item.DirectoryName 'BepInEx') -PathType Container){'BepInEx'}else{'Doorstop'})}
    }catch{}
    return ''
}
$script:MMScriptExt=@('.bat','.cmd','.ps1','.psm1','.py','.exe','.vbs','.js','.msi','.reg')
$script:MMDocExt=@('.txt','.md','.pdf','.png','.jpg','.jpeg','.gif','.webp','.url','.html','.htm','.rtf','.docx')
$script:MMJunk=@('__MACOSX','desktop.ini','Thumbs.db','.DS_Store')

function Get-MMStoreFolder([string]$Game) {
    # Same folder as the window's profiles/reservations (Core.cs Store.Folder).
    $key=[IO.Path]::GetFullPath($Game).TrimEnd('\').ToUpperInvariant();$sha=[Security.Cryptography.SHA256]::Create()
    try{$tag=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key))).Replace('-','').Substring(0,16)}finally{$sha.Dispose()}
    return Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('LOH_ModManager\'+$tag)
}
function Get-MMStagePath([string]$Game,[string]$StageId) {
    if($StageId -notmatch '^[0-9a-f]{32}$'){throw '잘못된 작업 번호'}
    return Join-Path (Get-MMStoreFolder $Game) ('Staging\'+$StageId)
}
function Read-MMText([string]$Path,[int]$Max=4000) {
    $bytes=[IO.File]::ReadAllBytes($Path);if($bytes.Length -gt 65536){[Array]::Resize([ref]$bytes,65536)}
    try{$text=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)}catch{$text=[Text.Encoding]::GetEncoding(949).GetString($bytes)}
    $text=$text.TrimStart([char]0xFEFF);if($text.Length -gt $Max){$text=$text.Substring(0,$Max)+' …'}
    return $text
}
# Unpack into this game's work area (never the game folder). Zip uses .NET with the Korean code page for old archives;
# 7z/rar/tar use the bsdtar that ships with Windows. Folders are copied so the plan stays the same until install.
function Remove-MMStage([string]$Game,[string]$StageId) {
    $stage=Get-MMStagePath $Game $StageId;if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}
    return [pscustomobject]@{StageId=$StageId}
}
# A zip with a password (ZipCrypto or AES, e.g. the 1234 copies for download sites): bit 0 of an entry's flags in the central directory.
# .NET's ZipFile cannot open those; Windows' own tar.exe (libarchive) can, given the password. Nothing is tried on the player's behalf:
# the window asks (the mod's post gives it), LOH_MM_PASSWORD_NEEDED / LOH_MM_PASSWORD_WRONG tell it to.
function Test-MMZipEncrypted([string]$Path){
    $fs=[IO.File]::OpenRead($Path)
    try{
        $len=[int][Math]::Min($fs.Length,65557);$null=$fs.Seek(-$len,[IO.SeekOrigin]::End);$tail=New-Object byte[] $len;$null=$fs.Read($tail,0,$len)
        $eocd=-1;for($i=$len-22;$i -ge 0;$i--){if($tail[$i] -eq 0x50 -and $tail[$i+1] -eq 0x4B -and $tail[$i+2] -eq 5 -and $tail[$i+3] -eq 6){$eocd=$i;break}};if($eocd -lt 0){return $false}
        $count=[BitConverter]::ToUInt16($tail,$eocd+10);$size=[BitConverter]::ToUInt32($tail,$eocd+12);$offset=[BitConverter]::ToUInt32($tail,$eocd+16)
        if($offset -eq [uint32]::MaxValue -or $size -eq [uint32]::MaxValue -or $offset+$size -gt $fs.Length){return $false}   # zip64: left to the normal way
        $null=$fs.Seek($offset,[IO.SeekOrigin]::Begin);$cd=New-Object byte[] $size;$null=$fs.Read($cd,0,$size);$p=0
        for($n=0;$n -lt $count -and $p+46 -le $cd.Length;$n++){
            if([BitConverter]::ToUInt32($cd,$p) -ne 0x02014B50){break}
            if([BitConverter]::ToUInt16($cd,$p+8) -band 1){return $true}
            $p+=46+[BitConverter]::ToUInt16($cd,$p+28)+[BitConverter]::ToUInt16($cd,$p+30)+[BitConverter]::ToUInt16($cd,$p+32)
        }
        return $false
    }finally{$fs.Dispose()}
}
function Expand-MMPackage([string]$Game,[string]$Source,[string]$Password='') {
    $Source=[IO.Path]::GetFullPath($Source);if(!(Test-Path -LiteralPath $Source)){throw "찾을 수 없습니다: $Source"}
    # Unpacked copies left by previews that were closed without installing (our own work area only).
    $staging=Join-Path (Get-MMStoreFolder $Game) 'Staging'
    if(Test-Path -LiteralPath $staging){Get-ChildItem -LiteralPath $staging -Directory | Where-Object {$_.Name -match '^[0-9a-f]{32}$' -and $_.LastWriteTime -lt (Get-Date).AddDays(-1)} | ForEach-Object {Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue}}
    $id=[guid]::NewGuid().ToString('N');$stage=Get-MMStagePath $Game $id;$content=Join-Path $stage 'content'
    New-Item -ItemType Directory -Path $content -Force | Out-Null
    try{
        if(Test-Path -LiteralPath $Source -PathType Container){Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $content -Recurse -Force;$name=Split-Path $Source -Leaf;$hash=''}
        else{
            $ext=[IO.Path]::GetExtension($Source).ToLowerInvariant();$name=[IO.Path]::GetFileNameWithoutExtension($Source);$hash=Get-MMHash $Source
            $tar=Join-Path $env:WINDIR 'System32\tar.exe'
            if($ext -eq '.zip' -and (Test-MMZipEncrypted $Source)){
                if(!$Password){throw 'LOH_MM_PASSWORD_NEEDED'}
                if(!(Test-Path -LiteralPath $tar)){throw '이 압축은 모드매니저가 풀지 못했습니다. 압축을 직접 푼 뒤, 풀린 폴더를 모드매니저에 끌어다 놓아 주세요.'}
                # names without the UTF-8 flag are read as Korean (CP949), like the plain way below
                $old=$ErrorActionPreference;$ErrorActionPreference='Continue';$out=@(& $tar -xf $Source -C $content --options 'zip:hdrcharset=CP949' --passphrase $Password 2>&1 | ForEach-Object {[string]$_});$code=$LASTEXITCODE;$ErrorActionPreference=$old
                if($code -ne 0){if(($out -join ' ') -match 'passphrase'){throw 'LOH_MM_PASSWORD_WRONG'};throw ('압축을 풀지 못했습니다: '+(($out | Select-Object -First 3) -join ' '))}
            }
            elseif($ext -eq '.zip'){Add-Type -AssemblyName System.IO.Compression.FileSystem;[IO.Compression.ZipFile]::ExtractToDirectory($Source,$content,[Text.Encoding]::GetEncoding(949))}
            elseif($ext -in @('.7z','.rar','.tar','.gz','.tgz','.xz','.bz2')){
                if(!(Test-Path -LiteralPath $tar)){throw '이 Windows 에는 압축 풀기 도구(tar.exe)가 없습니다. zip 으로 받아 주세요.'}
                $old=$ErrorActionPreference;$ErrorActionPreference='Continue';$out=@(& $tar -xf $Source -C $content 2>&1 | ForEach-Object {[string]$_});$code=$LASTEXITCODE;$ErrorActionPreference=$old
                # a 7z·rar with a password: Windows' tar cannot open it (user decision 11-01: no extra tool bundled)
                if(($out -join ' ') -match 'encrypt|passphrase'){throw '이 압축은 모드매니저가 풀지 못했습니다. 압축을 직접 푼 뒤, 풀린 폴더를 모드매니저에 끌어다 놓아 주세요.'}
                if($code -ne 0){throw ('압축을 풀지 못했습니다: '+(($out | Select-Object -First 3) -join ' '))}
            }elseif($ext -eq '.dll'){Copy-Item -LiteralPath $Source -Destination $content;$name=Split-Path (Split-Path $Source -Parent) -Leaf}   # one DLL (an entry-style one picked with '외부 DLL 추가'), not its whole folder
            else{throw "지원하지 않는 파일입니다(zip·7z·rar·dll 또는 폴더): $Source"}
        }
        Write-MMJson (Join-Path $stage 'source.json') ([ordered]@{Path=$Source;Name=$name;Sha256=$hash;Expanded=(Get-Date).ToString('s')})
        $root=[IO.Path]::GetFullPath($content).TrimEnd('\')+'\'   # what was unpacked: a file gone before the install (Windows security) is found before anything is taken out
        [IO.File]::WriteAllLines((Join-Path $stage 'files.txt'),[string[]]@(Get-ChildItem -LiteralPath $content -Recurse -File -Force | ForEach-Object {$_.FullName.Substring($root.Length)}),[Text.UTF8Encoding]::new($false))
    }catch{Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue;throw}
    return $id
}
function Get-MMStageMissing([string]$Game,[string]$StageId){   # the names of unpacked files no longer there
    $stage=Get-MMStagePath $Game $StageId;$list=Join-Path $stage 'files.txt';if(!(Test-Path -LiteralPath $list -PathType Leaf)){return @()}
    $content=Join-Path $stage 'content'
    return @([IO.File]::ReadAllLines($list,[Text.UTF8Encoding]::new($false)) | Where-Object {$_ -and !(Test-Path -LiteralPath (Join-Path $content $_) -PathType Leaf)} | ForEach-Object {Split-Path $_ -Leaf})
}
function Get-MMModInfo([string]$Path) {
    try{$info=@(Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)[0]}catch{return [pscustomobject]@{Name='';Suffix=''}}
    $name=[string]$info.nameKr;if(!$name){$name=[string]$info.name}
    return [pscustomobject]@{Name=$name;Suffix=[string]$info.strIDEnd}
}
# The game reads MODs\<folder>\ModInfo.json with JArray.Parse (JIPLGJCIBGK.KGGBPFFEENO, build 26159462): a file holding one
# object instead of a list throws, the folder is skipped ('MOD读取失败') and the game's next rewrite of ModSettings drops its
# entry, so the mod falls back to off. 빠른거래's QuickTradeNative/CityPriceHighlightNative ship that shape; [ ] around it is enough.
$script:MMModInfoTarget='^MODs\\[^\\]+\\ModInfo\.json$'
function Test-MMModInfoObject([string]$Path) {
    try{$text=[IO.File]::ReadAllText($Path,[Text.UTF8Encoding]::new($false)).TrimStart([char]0xFEFF).Trim()}catch{return $false}
    if(!$text.StartsWith('{')){return $false}
    try{$null=$text | ConvertFrom-Json;return $true}catch{return $false}
}
function Repair-MMModInfo([string]$Path) {
    if(!(Test-MMModInfoObject $Path)){return $false}
    $text=[IO.File]::ReadAllText($Path,[Text.UTF8Encoding]::new($false)).TrimStart([char]0xFEFF).Trim()
    [IO.File]::WriteAllText($Path,("[`r`n"+$text+"`r`n]`r`n"),[Text.UTF8Encoding]::new($false))
    return $true
}
# Same repair for what earlier installs already put there: only files still as recorded (the original goes to $BackupDir),
# and the record takes the new hash so removal still recognises them. Returns the strIDEnd of each repaired mod.
function Repair-MMInstalledModInfo([string]$Game,[string]$BackupDir) {
    $fixed=@()
    foreach($r in @(Get-MMInstallRecords $Game)){
        $changed=$false
        foreach($p in @($r.Parts | Where-Object {$_.Kind -in @('Data','HostPlugin')})){
            foreach($e in @($p.Files | Where-Object {$_.Target -match $script:MMModInfoTarget})){
                $path=Join-Path $Game $e.Target
                if(!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-MMHash $path) -ne $e.Sha256 -or !(Test-MMModInfoObject $path)){continue}
                if($BackupDir){$copy=Join-Path $BackupDir ('ModInfo\'+$e.Target);New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null;Copy-Item -LiteralPath $path -Destination $copy -Force}
                if(Repair-MMModInfo $path){$e.Sha256=Get-MMHash $path;$changed=$true;if($p.Suffix){$fixed+=[string]$p.Suffix}}
            }
        }
        if($changed){Save-MMInstallRecord $Game $r}
    }
    return @($fixed | Select-Object -Unique)
}
# Data mods the game can already see: MODs\<folder> and Steam workshop subscriptions (steamapps\workshop\content\3020510\<id>).
function Get-MMDataFolders([string]$Game) {
    $found=@();$workshop=Join-Path (Split-Path (Split-Path $Game -Parent) -Parent) 'workshop\content\3020510'
    foreach($place in @(@{Root=(Join-Path $Game 'MODs');Local=$true},@{Root=$workshop;Local=$false})){
        if(!(Test-Path -LiteralPath $place.Root -PathType Container)){continue}
        foreach($dir in Get-ChildItem -LiteralPath $place.Root -Directory -ErrorAction SilentlyContinue){
            $info=Join-Path $dir.FullName 'ModInfo.json';if(!(Test-Path -LiteralPath $info)){continue}
            $mi=Get-MMModInfo $info;$found+=[pscustomobject]@{Folder=$dir.Name;Suffix=$mi.Suffix;Local=$place.Local;Label=$(if($place.Local){'MODs\'+$dir.Name}else{'창작마당 '+$dir.Name+$(if($mi.Name){' ('+$mi.Name+')'})})}
        }
    }
    return $found
}
function Get-MMSafeName([string]$Name){$n=($Name -replace '[\\/:*?"<>|]','_').Trim().TrimEnd('.');if(!$n){$n='모드'};return $n}
function Get-MMRelative([string]$Base,[string]$Path){return $Path.Substring($Base.TrimEnd('\').Length+1)}
function Test-MMUnder([string]$Path,[string[]]$Dirs){foreach($d in $Dirs){if($Path -ieq $d -or $Path.StartsWith($d.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){return $true}};return $false}
# ---------- self-install folders (모드정보 양식 '자체 설치 압축'): <folder>\_files\manifest.json = {product, version, files[src,dst,sha256],
# variant?, variantNote?}. One archive may hold several: kinds of one mod (밸런스판·편의판, same product) to pick one of, other mods
# beside them. Old manifests (Module + Files[Path]) have no product and no dst, and are not taken. Only "files" is read: dst is the place
# in the game folder; MODs\… is a data mod, ThreeKingdom_Data\… replaces game files, a folder holding a DLL is a DLL mod the manager
# loads. Its entry DLLs and scripts are not used.
function Get-MMSelfUnits([string]$Base){
    $units=@()
    foreach($f in @(Get-ChildItem -LiteralPath $Base -Recurse -Force -Filter 'manifest.json' -File -ErrorAction SilentlyContinue | Where-Object {$_.Directory.Name -eq '_files'} | Sort-Object FullName)){
        try{$m=Read-MMJson $f.FullName}catch{continue}
        if(!$m -or !$m.PSObject.Properties['product'] -or !([string]$m.product).Trim()){continue}
        $list=@($m.files | Where-Object {$_ -and $_.PSObject.Properties['dst'] -and [string]$_.dst} | ForEach-Object {
            $dst=([string]$_.dst).Replace('/','\').TrimStart('\');$src=$(if($_.PSObject.Properties['src'] -and [string]$_.src){([string]$_.src).Replace('/','\').TrimStart('\')}else{$dst})
            [pscustomobject]@{Src=$src;Dst=$dst}} | Where-Object {$_.Src -notmatch '(^|\\)\.\.(\\|$)|:' -and $_.Dst -notmatch '(^|\\)\.\.(\\|$)|:' -and (Test-Path -LiteralPath (Join-Path $f.DirectoryName $_.Src) -PathType Leaf)})
        if(!$list.Count){continue}
        $dir=$f.Directory.Parent.FullName;if(@($units | Where-Object {Test-MMUnder $f.DirectoryName @($_.Files)}).Count){continue}
        $units+=[pscustomobject]@{Dir=$dir;Files=$f.DirectoryName;Product=([string]$m.product).Trim();Version=[string]$m.version;List=$list
            Variant=$(if($m.PSObject.Properties['variant']){([string]$m.variant).Trim()}else{''});VariantNote=$(if($m.PSObject.Properties['variantNote']){([string]$m.variantNote).Trim()}else{''})}
    }
    # two folders of one mod without kind names: their folder names tell them apart
    foreach($g in @($units | Group-Object Product | Where-Object {$_.Count -gt 1})){foreach($u in $g.Group){if(!$u.Variant){$u.Variant=Split-Path $u.Dir -Leaf}}}
    return $units
}
function Get-MMSelfUnitComponent([string]$Game,$Unit,[string]$Content,$Source){
    $subs=@();$where=@();$blocked='';$base=$Unit.Files
    function Pick($e,[string]$Target){return [pscustomobject]@{Source=(Get-MMRelative $Content (Join-Path $base $e.Src));Target=$Target}}
    $tag=@{Product=$Unit.Product;Variant=$Unit.Variant}
    foreach($rel in @($Unit.List | ForEach-Object {$_.Dst} | Where-Object {$_ -match '\.dll$' -and $_ -notmatch '^(MODs|ThreeKingdom_Data)\\' -and $_ -match '\\'} | ForEach-Object {Split-Path $_ -Parent} | Select-Object -Unique)){
        if((Split-Path $rel -Leaf) -eq 'entry'){continue}
        $mine=@($Unit.List | Where-Object {$_.Dst.StartsWith($rel+'\',[StringComparison]::OrdinalIgnoreCase)});$dllE=@($mine | Where-Object {(Split-Path $_.Dst -Parent) -ieq $rel -and $_.Dst -match '\.dll$'} | Sort-Object @{Expression={[IO.Path]::GetFileNameWithoutExtension($_.Dst) -ne (Split-Path $rel -Leaf)}},Dst)[0]
        $dllPath=Join-Path $base $dllE.Src;$dllName=[IO.Path]::GetFileName($dllE.Dst)
        $pe=Get-MMPE $dllPath;if(!$pe.Valid -or !$pe.X64 -or !$pe.Dll -or $pe.Managed){$blocked='x64 네이티브 DLL 이 아닙니다.';continue}
        $typed=Get-MMExternalType $Game $dllPath;if($typed.Type -eq '기존 모드의 다른 버전·보조 DLL'){$blocked=$typed.Evidence;continue}
        $folder='LOH_ModManager\External\'+(Get-MMSafeName $Source.Name)+'\'+(Split-Path $rel -Leaf)
        $list=@(@($dllE)+@($mine | Where-Object {$_ -ne $dllE} | Sort-Object Dst) | ForEach-Object {Pick $_ ($folder+'\'+$_.Dst.Substring($rel.Length+1))})
        $subs+=[pscustomobject]@{Key=('dll:'+$dllName);Kind='ExternalDll';Title=$dllName;Suffix='';Files=$list;Product=$tag.Product;Variant=$tag.Variant};$where+=('DLL 모드 → '+$folder)
    }
    foreach($g in @($Unit.List | Where-Object {$_.Dst -match '^MODs\\[^\\]+\\'} | Group-Object {($_.Dst -split '\\')[1]})){
        $folder=$g.Name;$infoE=@($g.Group | Where-Object {$_.Dst -ieq ('MODs\'+$folder+'\ModInfo.json')})[0]
        $mi=if($infoE){Get-MMModInfo (Join-Path $base $infoE.Src)}else{[pscustomobject]@{Name='';Suffix=''}}
        $list=@($g.Group | ForEach-Object {Pick $_ $_.Dst})
        $subs+=[pscustomobject]@{Key=('data:'+$folder);Kind='Data';Title=$(if($mi.Name){$mi.Name}else{$folder});Suffix=$mi.Suffix;Files=$list;Product=$tag.Product;Variant=$tag.Variant};$where+=('데이터 모드 → MODs\'+$folder)
    }
    $td=@($Unit.List | Where-Object {$_.Dst -match '^ThreeKingdom_Data\\'} | ForEach-Object {Pick $_ $_.Dst})
    if($td.Count){$subs+=[pscustomobject]@{Key='overlay:ThreeKingdom_Data';Kind='Overlay';Title='게임 파일 교체';Suffix='';Files=$td;Product=$tag.Product;Variant=$tag.Variant};$where+='게임 파일 교체 → ThreeKingdom_Data'}
    # the name to show: the mod's own loh-mod.json, else its data mod, else the product
    $name='';foreach($e in @($Unit.List | Where-Object {[IO.Path]::GetFileName($_.Dst) -ieq 'loh-mod.json'} | Sort-Object {$_.Dst.Length})){try{$name=([string](Read-MMJson (Join-Path $base $e.Src)).name).Trim()}catch{};if($name){break}}
    if(!$name){$name=[string]@($subs | Where-Object {$_.Kind -eq 'Data'} | ForEach-Object {$_.Title})[0]};if(!$name){$name=$Unit.Product}
    $files=@($subs | ForEach-Object {$_.Files});$warn=$(if($blocked){$blocked}elseif(!$subs.Count){'설치할 수 있는 내용(데이터 모드·게임 파일·DLL)을 찾지 못했습니다.'}else{''})
    return [pscustomobject]@{Key=('self:'+$Unit.Product+':'+$Unit.Variant);Kind='SelfInstall';Title=($name+$(if($Unit.Variant){' — '+$Unit.Variant}));ModName=$name;Product=$Unit.Product;Variant=$Unit.Variant;Version=$Unit.Version
        Detail=($(if($Unit.VariantNote){$Unit.VariantNote+' · '})+'모드 자체 설치 정보대로 넣습니다: '+($where -join ', ')+' · 파일 '+$files.Count+'개');Suffix='';Files=$files;SubParts=$subs
        Default=$true;Blocked=([bool]$blocked -or !$subs.Count);Warning=$warn;Group='';Options=@()}
}
# Settings the player changed go along to the install that takes this one's place ('지금 업데이트', and back with '이전 버전으로 되돌리기'):
# the files the mod's loh-mod.json lists under "settings", matched by part and by their place in the mod folder. A file still as the mod
# shipped it stays behind (the new version's default wins); another kind of the mod (밸런스판 → 편의판) has its own settings, so its file is used.
function Get-MMPartHome($Part){   # the mod folder of a part: where its loh-mod.json goes, else the folder all its files share
    $t=@($Part.Files | ForEach-Object {[string]$_.Target} | Where-Object {$_})
    $info=@($t | Where-Object {[IO.Path]::GetFileName($_) -ieq 'loh-mod.json'} | Sort-Object Length)[0];if($info){return (Split-Path $info -Parent)}
    if(!$t.Count){return ''};$common=Split-Path $t[0] -Parent
    foreach($x in $t){while($common -and !$x.StartsWith($common+'\',[StringComparison]::OrdinalIgnoreCase)){$common=Split-Path $common -Parent}}
    return $common
}
function Copy-MMUserSettings([string]$Game,[string]$FromId,[string]$ToId){
    $root=Get-MMInstalledRoot $Game;$toDir=Join-Path $root $ToId;$out=[pscustomobject]@{Carried=@();Fresh=@();Was='';Now=''}
    $from=Read-MMJson (Join-Path (Join-Path $root $FromId) 'record.json');$to=Read-MMJson (Join-Path $toDir 'record.json')
    if(!$from -or !$to -or $from.State -ne 'Removed' -or $to.State -ne 'Installed' -or ![string]$from.Vault){return $out}
    $vault=[string]$from.Vault;$kept=@($from.Kept | Where-Object {$_});$fromChanged=$false;$toChanged=$false
    foreach($p in @($to.Parts | Where-Object {$_.Kind -in @('Data','ExternalDll','AbsorbedDll','Overlay','ProxyDll','HostPlugin')})){
        $q=@($from.Parts | Where-Object {$_.Key -eq $p.Key})[0];if(!$q){continue}
        $at=Get-MMPartHome $p;$oldAt=Get-MMPartHome $q;if(!$at -or !$oldAt){continue}
        $info=Join-Path $Game ($at+'\loh-mod.json');if(!(Test-Path -LiteralPath $info -PathType Leaf)){continue}
        $set=$null;try{$set=(Read-MMJson $info).settings}catch{};$names=@($set | ForEach-Object {([string]$_).Trim().Replace('/','\').TrimStart('\')} | Where-Object {$_ -and $_ -notmatch '(^|\\)\.\.(\\|$)|:'} | Select-Object -Unique)
        foreach($n in $names){
            $e=@($p.Files | Where-Object {$_.Target -ieq ($at+'\'+$n)})[0];$o=@($q.Files | Where-Object {$_.Target -ieq ($oldAt+'\'+$n)})[0];if(!$e -or !$o){continue}
            $isKept=$o.Target -in $kept;$samePlace=$o.Target -ieq $e.Target;$keep=Join-Path $vault $o.Target
            # where the player's file is: left in place on removal because it was changed (under the new version's file when both go to the
            # same place: then in its backup), or in the removed record's vault
            $user=if(!$isKept){$keep}elseif($samePlace){if($e.Backup){Join-Path $toDir ('backup\'+$e.Backup)}else{''}}else{Join-Path $Game $o.Target}
            if(!$user -or !(Test-Path -LiteralPath $user -PathType Leaf)){continue}
            if($isKept){   # it stays with the removed record (restoring that brings it back), not loose in the game folder or as a 'game original'
                New-Item -ItemType Directory -Path (Split-Path $keep -Parent) -Force | Out-Null;Copy-Item -LiteralPath $user -Destination $keep -Force
                if($samePlace){$e.Backup=$null;$toChanged=$true}
                else{Remove-Item -LiteralPath $user -Force;$dir=Split-Path $user -Parent;while($dir.Length -gt $Game.Length -and (Test-Path -LiteralPath $dir) -and !@(Get-ChildItem -LiteralPath $dir -Force).Count){Remove-Item -LiteralPath $dir -Force;$dir=Split-Path $dir -Parent}}
                $kept=@($kept | Where-Object {$_ -ne $o.Target});$from.Kept=$kept;$fromChanged=$true
            }
            $shipped=$(if($o.PSObject.Properties['Shipped'] -and $o.Shipped){[string]$o.Shipped}else{[string]$o.Sha256})
            if((Get-MMHash $keep) -eq $shipped){continue}   # never changed: the new version's own file
            if([string]$p.Variant -ne [string]$q.Variant){$out.Fresh+=[IO.Path]::GetFileName($n);$out.Was=[string]$q.Variant;$out.Now=[string]$p.Variant;continue}
            $dst=Assert-MMPath $Game (Join-Path $Game $e.Target);Copy-Item -LiteralPath $keep -Destination $dst -Force
            if(!$e.PSObject.Properties['Shipped'] -or !$e.Shipped){$e | Add-Member -NotePropertyName Shipped -NotePropertyValue ([string]$e.Sha256) -Force}
            $e.Sha256=Get-MMHash $dst;$toChanged=$true;$out.Carried+=[IO.Path]::GetFileName($n)
        }
    }
    if($fromChanged){Save-MMInstallRecord $Game $from};if($toChanged){Save-MMInstallRecord $Game $to}
    return $out
}
function Get-MMSettingsNote($Carry,[switch]$Back){
    $out=@();if(!$Carry){return ''}
    if(@($Carry.Carried).Count){$out+=('고쳐 쓰던 설정 파일은 그대로 두었습니다('+(@($Carry.Carried | Select-Object -Unique) -join ', ')+').')}
    if(@($Carry.Fresh).Count){$out+=('종류를 바꿔'+$(if($Carry.Was -and $Carry.Now){'('+$Carry.Was+' → '+$Carry.Now+')'}else{''})+' 설정 파일은 새 종류 것을 씁니다('+(@($Carry.Fresh | Select-Object -Unique) -join ', ')+'). 전에 쓰던 설정은 '+$(if($Back){"'지운 모드'에"}else{"'지운 모드'의 이전 버전에"})+' 남아 있습니다.')}
    return ($out -join ' ')
}
# Decide what the unpacked files are and where each part would go. Nothing is copied here.
function Get-MMPackagePlan([string]$Game,[string]$StageId,[string]$Replaces='') {   # Replaces: the install '지금 업데이트' takes the place of (its kinds are ticked)
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');$stage=Get-MMStagePath $Game $StageId;$content=Join-Path $stage 'content'
    $source=Read-MMJson (Join-Path $stage 'source.json');if(!$source){throw '살펴본 기록이 없습니다. 다시 끌어다 놓아 주세요.'}
    $base=$content
    while($true){$items=@(Get-ChildItem -LiteralPath $base -Force | Where-Object {$_.Name -notin $script:MMJunk});if($items.Count -eq 1 -and $items[0].PSIsContainer -and $items[0].Name -notin @('MODs','ThreeKingdom_Data','_files')){$base=$items[0].FullName}else{break}}
    $root=$base;$files=Join-Path $base '_files'
    if((Test-Path -LiteralPath $files -PathType Container) -and ((Test-Path -LiteralPath (Join-Path $files 'MODs')) -or (Test-Path -LiteralPath (Join-Path $files 'ThreeKingdom_Data')))){$root=$files}
    $components=[Collections.Generic.List[object]]::new();$notes=[Collections.Generic.List[string]]::new();$kind='files';$selfDirs=@()
    $packageJson=Read-MMJson (Join-Path $base 'package.json');$promise=Get-MMPromiseManifest $base
    if($promise){
        # the mod's own loh-mod.json says how to run its patch (모드정보 양식 'patch'): one rule for any such patch, no recipe
        $kind='ScriptPatch';$pname=$(if($promise.Info.name){[string]$promise.Info.name}else{Split-Path $promise.Dir -Leaf})
        $opts=@($promise.Items | ForEach-Object {[pscustomobject]@{Key=('item_'+$_.Id);Label=($_.Name+$(if($_.Note){' — '+$_.Note}));Type='bool';Default=$true;Choices=@()}})
        $components.Add([pscustomobject]@{Key='recipe:promise';Kind='ScriptPatch';Flavor='promise';ModName=$pname;Run=$promise.Run;Items=@($promise.Items);Title=($pname+' — 패치 (모드 정보 파일에 적힌 방법으로 설치)')
            Detail='모드매니저가 모드 정보 파일(loh-mod.json)에 적힌 대로 패치를 돌리고, 바뀐 파일을 기록해 둡니다. 끄거나 지우면 기록해 둔 원래 파일로 되돌립니다.'
            Suffix='';Files=@();Default=$true;Blocked=$false;PreferFirst=$false;Warning='';Options=$opts;Root=$(if($promise.Dir -eq $content){''}else{Get-MMRelative $content $promise.Dir})})
    }
    elseif($packageJson -and [string]$packageJson.product -eq 'HeroesVow.FeaturePackages'){
        $kind='HeroesVow';$recipe=Get-MMHeroesVowRecipe $Game $base $content
        foreach($c in $recipe.Components){$components.Add($c)};foreach($n in $recipe.Notes){$notes.Add($n)}
    }
    elseif((Test-Path -LiteralPath (Join-Path $base '스크립트\패처.ps1')) -and (Select-String -LiteralPath (Join-Path $base '스크립트\패처.ps1') -Pattern "^\`$Title = '창천UI" -Quiet)){
        $kind='ScriptPatch';$others=@(Get-MMInstallRecords $Game | Where-Object {@($_.Parts | ForEach-Object {$_.Files} | Where-Object {$_.Target -eq 'GameAssembly.dll'}).Count} | ForEach-Object {$_.Name})
        $opt=[pscustomobject]@{Key='ccui_sortonly';Label='일부 기능이 이 게임 버전과 안 맞으면 정렬 기능만이라도 설치(끄면 그때는 설치하지 않음)';Type='bool';Default=$false;Choices=@()}
        $components.Add([pscustomobject]@{Key='recipe:ccui';Kind='ScriptPatch';Title='창천UI — 소지품·거래창 정렬';Detail='패치 스크립트(스크립트\패처.ps1)를 모드매니저가 게임 폴더를 정해 묻지 않고(-GameDir, -Yes) 돌려 GameAssembly.dll 을 고칩니다. 고치기 전 원본을 설치 기록에 백업해 두어, 제거하면 그 원본으로 되돌립니다(제작자 설치기에는 되돌리기가 없음).';Suffix='';Files=@();Default=$true;Blocked=$false;PreferFirst=$true;
            Warning=$(if($others.Count){'GameAssembly.dll 을 고친 패치가 이미 있습니다('+($others -join ', ')+'). 제작자 권장대로 창천UI 를 먼저 두고, 모드매니저가 깨끗한 원본에서 창천UI → '+($others -join ' → ')+' 순서로 다시 쌓습니다(1~2분).'}else{''});Options=@($opt);Root=$(if($base -eq $content){''}else{Get-MMRelative $content $base})})
        $notes.Add('정렬 설정(order.json)과 정렬 편집기는 패치 사본(LOH_ModManager\Tools)에 함께 둡니다. 게임 업데이트 뒤에는 다시 설치하세요.')
    }
    elseif((Test-Path -LiteralPath (Join-Path $base '스크립트\패처.ps1')) -and (Select-String -LiteralPath (Join-Path $base '스크립트\패처.ps1') -Pattern '무장 검색 개선 패치 / $Mode' -SimpleMatch -Quiet)){
        # 무장 검색 개선 패치: the same maker's PowerShell patcher, with apply/restore modes. It also rewrites the UI asset bundle,
        # so its own restore (not our GameAssembly.dll copy) undoes it, and a rebuild runs that restore before starting from the original.
        $kind='ScriptPatch';$others=@(Get-MMInstallRecords $Game | Where-Object {Test-MMPatchRecord $_} | ForEach-Object {$_.Name})
        $opts=@([pscustomobject]@{Key='kosearch_button';Label='「여성무장」 버튼 — 게임 중 목록과 역사 무장 편집에 버튼 추가(화면 파일 ui 도 고침)';Type='bool';Default=$true;Choices=@()},
            [pscustomobject]@{Key='kosearch_stimulus';Label='관계 중첩 표기 — 방문 목록에 자극·낭만 열, 장수 목록 관계 탭에 경외·신세·자극·낭만 열';Type='bool';Default=$false;Choices=@()})
        $components.Add([pscustomobject]@{Key='recipe:kosearch';Kind='ScriptPatch';Flavor='kosearch';Title='무장 검색 개선 — 초성·여성 검색';Detail='패치 스크립트(스크립트\패처.ps1)를 모드매니저가 묻지 않고(-Mode apply -GameDir -Yes, 아래 선택 그대로) 돌려 GameAssembly.dll(과 버튼을 고르면 화면 파일 ui)을 고칩니다. 제거하면 패치의 「복구」(-Mode restore)를 돌립니다.';Suffix='';Files=@();Default=$true;Blocked=$false;PreferFirst=$false;
            Warning=$(if($others.Count){'GameAssembly.dll 을 고친 패치가 이미 있습니다('+($others -join ', ')+'). 모드매니저가 깨끗한 원본에서 '+($others -join ' → ')+' → 무장 검색 순서로 다시 쌓습니다(1~2분).'}else{''});Options=$opts;Root=$(if($base -eq $content){''}else{Get-MMRelative $content $base})})
        $notes.Add('패치 사본은 LOH_ModManager\Tools 에 두고 복구·다시 쌓기에 씁니다. 패치가 만드는 백업은 게임 폴더 「패처백업」에 있습니다.')
    }
    elseif(@(Get-ChildItem -LiteralPath $base -Recurse -Filter 'manifest.json' -File -ErrorAction SilentlyContinue | Where-Object {$_.Directory.Name -eq '_files' -and (Test-Path -LiteralPath (Join-Path $_.DirectoryName 'tool.ps1')) -and [string](Read-MMJson $_.FullName).Module.Name -in @('LOH_GiftHighlight','LOH_GoldenOrchidJade')}).Count){
        # 선물표시·금란선옥 public packages: their own 설치.cmd puts the DLL folder and a small shared entry; the manager's save then
        # takes over that entry (Retire) and lists them as its built-in rows. Copying their files here would only half-install them.
        $kind='SocialMods'
        $notes.Add('선물·화제 취향 표시 / 금란선옥 묶음입니다. 들어 있는 「설치.cmd」를 게임을 끈 채 한 번 실행한 뒤, 이 창에서 「저장」을 누르면 모드매니저가 그 모드를 받아 목록에 올리고 켜고 끕니다(자체 입구 파일은 모드매니저 입구로 바뀜). 모드매니저가 파일을 직접 넣지는 않습니다.')
    }
    elseif(Get-MMScenicDir $base){
        $kind='ScriptPatch';foreach($c in @(Get-MMScenicComponents $Game $base $content)){$components.Add($c)}
        if((Get-MMScenicDir $base) -ne $base){$notes.Add('압축 안의 4.0 테스트 버전(BepInEx)은 게임 폴더에 BepInEx 6 IL2CPP 로더가 있어야 돌아가는데, 모드매니저는 그 로더를 설치하지 않습니다. 그래서 같은 압축의 3.1 버전(GameAssembly.dll 패치, 벨핀 필요 없음)으로 설치합니다. 선부 ID·MODs 폴더는 두 버전이 같습니다.')}
        $notes.Add('패치 사본은 LOH_ModManager\Tools 에 두고 다시 쌓기·제거에 씁니다. 패치가 고치기 전 GameAssembly.dll(약 100MB)을 게임 폴더 「패처백업\명승선부_배포_v3.1」에 설치·다시 쌓기 때마다 남깁니다.')
    }
    elseif(Get-MMCapUnlockExe $base){
        $kind='ScriptPatch';foreach($c in @(Get-MMCapUnlockComponents $Game $base $content)){$components.Add($c)}
        $notes.Add('패치 사본은 LOH_ModManager\Tools 에 두고 다시 쌓기에 씁니다. 실행할 때만 게임 폴더에 잠깐 두었다가 치웁니다. 이 패치에는 되돌리기가 없어서, 모드매니저가 고치기 전 GameAssembly.dll 을 설치 기록에 백업하거나 원본에서 다시 쌓아 뺍니다.')
    }
    elseif(Test-MMTreasury $base){
        $kind='Treasury';foreach($c in @(Get-MMTreasuryComponents $Game $base $content)){$components.Add($c)}
        $notes.Add('세력금고 사본(약 30MB, 동봉 파이썬 포함)을 게임 폴더 LOH_ModManager\Tools 에 두고 게임 연결 정보를 만드는 데만 씁니다(적용.bat 은 돌리지 않음). 패치를 다시 쌓거나 게임이 업데이트되면 모드매니저가 다시 만듭니다.')
    }
    elseif(@(Get-MMSeawayVariants $base).Count){
        $kind='FilePatch';foreach($c in @(Get-MMSeawayComponents $Game $base $content)){$components.Add($c)}
        $notes.Add('패치 사본(동봉 파이썬 포함, 약 50MB)은 게임 폴더 LOH_ModManager\Tools 에 두고 「원래대로」에 씁니다. 패치가 고치기 전 원본을 게임 폴더 「뱃길패치_원본백업」에 둡니다. 게임 업데이트로 뱃길이 풀리면 이 설치를 지우고 다시 넣으세요.')
    }
    elseif((Test-Path -LiteralPath (Join-Path $base 'allinone.py')) -and (Test-Path -LiteralPath (Join-Path $base 'official_hashes.json'))){
        $kind='AllInOne';$recipe=Get-MMAllInOneRecipe $Game $base
        $components.Add([pscustomobject]@{Key='recipe:allinone';Kind='Patcher';Title=$recipe.Title;Detail='동봉된 파이썬 패치가 게임 파일(GameAssembly.dll·글꼴·피부·군단전 표 등)을 직접 고칩니다. 아래 항목은 패치에 든 질문을 그대로 읽은 것이고, 고른 항목만 패치의 자동 적용 방식(--auto)으로 묻지 않고 설치합니다. 바뀔 파일의 원본은 모드매니저가 보관했다가 지울 때 되돌립니다.';Suffix='';Files=@();Default=$recipe.Readable;Blocked=(!$recipe.Runtime -or !$recipe.Readable);Warning=$recipe.Warning;Options=$recipe.Options;Root=$(if($base -eq $content){''}else{Get-MMRelative $content $base})})
        $notes.Add('패치 파일(약 '+$recipe.SizeMB+'MB)은 게임 폴더 LOH_ModManager\Tools 에 두고 항목 켜고 끄기·다시 쌓기에 씁니다. 바뀔 파일의 원본은 게임 폴더 LOH_ModManager\AllInOneOriginals 에 보관합니다(고른 항목에 따라 최대 약 2.5GB).')
        if($recipe.Current){$notes.Add('처음 값은 지난번 올인원 설치 때 고른 항목(게임 폴더 패처백업\올인원자동.json)입니다.')}
    }
    if($kind -eq 'files' -and !@(Get-ChildItem -LiteralPath $base -Recurse -Force -Filter 'ModInfo.json' -File -ErrorAction SilentlyContinue).Count){
        $guide=Get-MMGuidePatch $base
        if($guide){
            $kind='ScriptPatch';$gname=$(if($guide.Dir -ne $content){Split-Path $guide.Dir -Leaf}else{[string]$source.Name})
            $bat=$(if(@($guide.Applies).Count -eq 1){'「'+@($guide.Applies)[0].Name+'」'}else{'「설치할 것」에서 고른 bat'})
            $opts=@();if(@($guide.Applies).Count -gt 1){$i=0;$opts+=[pscustomobject]@{Key='guide_apply';Label='설치할 것';Type='choice';Default='0';Choices=@($guide.Applies | ForEach-Object {[pscustomobject]@{Value=[string]($i++);Label=$_.Name}})}}
            $components.Add([pscustomobject]@{Key='recipe:guide';Kind='ScriptPatch';Flavor='guide';ModName=$gname;Script=$guide.Script;Applies=@($guide.Applies);Restore=@($guide.Restore)
                Title=($gname+' — 패치 (설치 bat 을 모드매니저가 대신 실행)')
                Detail=('압축 안의 '+$bat+'이 실행하는 패치를 모드매니저가 게임 폴더를 정해 묻지 않고 돌리고, 바뀐 파일을 기록합니다. GameAssembly.dll 을 고치는 다른 패치와 함께 쓰도록 목록 순서대로 원본에서 다시 쌓습니다. 끄거나 지우면 이 패치를 뺀 채 다시 쌓습니다.')
                Suffix='';Files=@();Default=$true;Blocked=$false;PreferFirst=$false;Warning='';Options=$opts;Root=$(if($guide.Dir -eq $content){''}else{Get-MMRelative $content $guide.Dir})})
        }
    }
    if($kind -eq 'files'){
        # self-install folders in kinds, several of them, or not at the top: each folder one line, kinds of one mod picked one of (4 below
        # keeps the plain one at the top as it was); the rules 1~3 leave those folders alone
        $units=@(Get-MMSelfUnits $base);$unitDirs=@()
        if($units.Count -gt 1 -or @($units | Where-Object {$_.Variant}).Count -or ($units.Count -eq 1 -and $units[0].Dir -ne $base)){$unitDirs=@($units | ForEach-Object {$_.Dir})}
        # 0) A patch for a Steam workshop mod: workshop\content\3020510\<id>\… goes over that subscription (번역 등).
        $wsPatches=@(Get-MMWorkshopPatchComponents $Game $content (Get-MMWorkshopPatchDirs $base));foreach($c in $wsPatches){$components.Add($c)}
        # 1) Data mods: every folder holding ModInfo.json (outside ThreeKingdom_Data and a workshop patch), outermost only.
        $dataDirs=@()
        foreach($info in @(Get-ChildItem -LiteralPath $root -Recurse -Force -Filter 'ModInfo.json' -File | Where-Object {$_.FullName -notmatch '\\ThreeKingdom_Data\\' -and $_.FullName -notmatch '\\workshop\\content\\3020510\\' -and !(Test-MMUnder $_.FullName $unitDirs)} | Sort-Object {$_.FullName.Length})){
            $dir=$info.DirectoryName;if(@($dataDirs | Where-Object {$dir.StartsWith($_+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){continue};$dataDirs+=$dir
            $mi=Get-MMModInfo $info.FullName
            $folder=if($dir -eq $root){Get-MMSafeName $(if($mi.Name){$mi.Name}else{$source.Name})}else{Split-Path $dir -Leaf}
            # Installer scripts never go into MODs; a readme next to a root-level ModInfo.json stays out too.
            $list=@(Get-ChildItem -LiteralPath $dir -Recurse -Force -File | Where-Object {$_.Name -notin $script:MMJunk -and $_.Extension.ToLowerInvariant() -notin $script:MMScriptExt -and !($dir -eq $root -and $_.DirectoryName -eq $dir -and $_.Extension.ToLowerInvariant() -in @('.txt','.md'))} | ForEach-Object {[pscustomobject]@{Source=(Get-MMRelative $content $_.FullName);Target=('MODs\'+$folder+'\'+(Get-MMRelative $dir $_.FullName))}})
            $exists=Test-Path -LiteralPath (Join-Path $Game ('MODs\'+$folder))
            # Another folder (MODs or a workshop subscription) with the same ID is the same mod or its original: loading both clashes.
            $twins=if($mi.Suffix){@(Get-MMDataFolders $Game | Where-Object {$_.Suffix -eq $mi.Suffix -and !($_.Local -and $_.Folder -eq $folder)} | ForEach-Object {$_.Label})}else{@()}
            $warn=if($twins.Count){'같은 ID('+$mi.Suffix+')를 쓰는 모드가 이미 있습니다: '+($twins -join ', ')+' — 같은 모드의 다른 판입니다. 둘을 함께 켜지 말고 하나는 게임 모드 목록에서 끄세요.'}else{''}
            $stamp=[long](@(Get-ChildItem -LiteralPath $dir -Recurse -Force -File | ForEach-Object {$_.LastWriteTimeUtc.Ticks}) | Measure-Object -Maximum).Maximum
            $components.Add([pscustomobject]@{Key=('data:'+$folder);Kind='Data';Title=$(if($mi.Name){$mi.Name}else{$folder});Detail=('데이터 모드 → MODs\'+$folder+' · 파일 '+$list.Count+'개'+$(if($mi.Suffix){' · ID '+$mi.Suffix})+$(if($exists){' · 이미 있는 같은 폴더는 백업 후 덮음'}));Suffix=$mi.Suffix;Files=$list;Default=$true;Blocked=$false;Warning=$warn;Stamp=$stamp})
        }
        # A full copy beside a workshop patch for the same mod is the fallback for when the original is updated or taken down.
        foreach($c in @($components | Where-Object {$_.Kind -eq 'Data' -and $_.Suffix})){
            $w=@($wsPatches | Where-Object {$_.ParentSuffix -eq $c.Suffix})[0];if(!$w){continue}
            $c.Default=$false;$c.Title=$c.Title+' (대비용 통째 사본)'
            $c.Warning='원본 창작마당 모드가 업데이트로 패치와 안 맞거나 내려갔을 때 쓰는 대비용입니다. 넣으면 같은 ID 인 창작마당 원본은 게임 모드 목록에서 꺼 두세요. 보통은 위의 패치만 넣으면 됩니다.'
        }
        # Several mods with one ID in one archive (kinds to pick from, or old copies): all are shown as packed — the player picks, the
        # modder tidies. Each says which others share its ID; one whose newest file is over a minute later than all the others' is (최신).
        foreach($g in @($components | Where-Object {$_.Kind -eq 'Data' -and $_.Suffix} | Group-Object Suffix | Where-Object {$_.Count -gt 1})){
            $list=@($g.Group | Sort-Object Stamp -Descending)
            $dup=@($list | Group-Object Title | Where-Object {$_.Count -gt 1}).Count
            $names=@{};foreach($c in $list){$names[$c.Key]=$(if($dup){$c.Key -replace '^data:',''}else{$c.Title})}
            foreach($c in $list){
                $others=@($list | Where-Object {$_ -ne $c} | ForEach-Object {$names[$_.Key]});$last=[int][char]($others[-1])[-1]
                $and=$(if(($last -ge 0xAC00 -and $last -le 0xD7A3 -and ($last-0xAC00)%28) -or [char]$last -in [char[]]'013678'){'과'}else{'와'})
                $line='이 압축 안의 '+($others -join ', ')+$and+' ID('+$g.Name+')가 같습니다. '+$(if($list.Count -eq 2){'둘을 '})+'함께 켜면 부딪칩니다.'
                $c.Warning=$(if($c.Warning){$c.Warning+' '+$line}else{$line})
            }
            if($list[0].Stamp -gt $list[1].Stamp+[TimeSpan]::TicksPerMinute){$list[0].Title=$list[0].Title+' (최신)'}
        }
        # 2) Game files replaced in place (ThreeKingdom_Data\...), originals backed up.
        $td=Join-Path $root 'ThreeKingdom_Data'
        if((Test-Path -LiteralPath $td -PathType Container) -and !(Test-MMUnder $root $unitDirs)){
            $list=@(Get-ChildItem -LiteralPath $td -Recurse -Force -File | Where-Object {$_.Name -notin $script:MMJunk} | ForEach-Object {[pscustomobject]@{Source=(Get-MMRelative $content $_.FullName);Target=(Get-MMRelative $root $_.FullName)}})
            $replaced=@($list | Where-Object {Test-Path -LiteralPath (Join-Path $Game $_.Target)}).Count
            $components.Add([pscustomobject]@{Key='overlay:ThreeKingdom_Data';Kind='Overlay';Title='게임 파일 교체';Detail=('ThreeKingdom_Data 파일 '+$list.Count+'개 · 그중 원본 '+$replaced+'개는 백업 후 덮음');Suffix='';Files=$list;Default=$true;Blocked=$false;Warning=''})
        }
        # 3) DLLs in the package root: entry-style (proxy name) go next to the game exe; others load through the manager.
        $rootFiles=@(Get-ChildItem -LiteralPath $root -Force -File | Where-Object {$_.Name -notin $script:MMJunk -and !(Test-MMUnder $root $unitDirs)})
        $dlls=@($rootFiles | Where-Object {$_.Extension -eq '.dll'})
        $extras=@($rootFiles | Where-Object {$_.Extension -ne '.dll' -and $_.Extension.ToLowerInvariant() -notin $script:MMScriptExt -and $_.Extension.ToLowerInvariant() -notin $script:MMDocExt -and $_.Name -ne 'ModInfo.json'})
        foreach($dll in $dlls){
            $pe=Get-MMPE $dll.FullName;$name=$dll.Name;$lower=$name.ToLowerInvariant()
            $with=@($dll)+$(if($dlls.Count -eq 1){$extras}else{@()})
            if(!$pe.Valid -or !$pe.X64 -or !$pe.Dll){$components.Add([pscustomobject]@{Key=('dll:'+$name);Kind='Unsupported';Title=$name;Detail='x64 네이티브 DLL 이 아닙니다.';Suffix='';Files=@();Default=$false;Blocked=$true;Warning='설치하지 않음'});continue}
            if($pe.Managed){$components.Add([pscustomobject]@{Key=('dll:'+$name);Kind='Unsupported';Title=$name;Detail='.NET 플러그인(BepInEx 등 전용 로더용)';Suffix='';Files=@();Default=$false;Blocked=$true;Warning='이 모드매니저로는 불러올 수 없음'});continue}
            $loader=if($lower -in $script:MMProxyNames){Get-MMLoaderEntry $dll.FullName}else{''}
            if($loader){$components.Add([pscustomobject]@{Key=('dll:'+$name);Kind='Unsupported';Title=$name;Detail=('로더 입구 파일('+$loader+')');Suffix='';Files=@();Default=$false;Blocked=$true;Warning='모드매니저는 로더를 설치하지 않음 — 로더 설명대로 직접 설치'});continue}
            if($lower -in $script:MMProxyNames){
                # Entry absorption (default): the proxy goes next to the exe under another name and the manager's entry loads it,
                # so mods sharing an entry name (version/hid/dwmapi...) can live together. Unticked: the maker's way, under its own name.
                $target=Join-Path $Game $name;$taken=Test-Path -LiteralPath $target;$owner=if($taken){Get-MMEntry $target}else{''}
                $list=@($with | ForEach-Object {[pscustomobject]@{Source=(Get-MMRelative $content $_.FullName);Target=$_.Name}})
                $mustAbsorb=$owner -eq 'manager'
                $warn=if($mustAbsorb){'모드매니저 입구와 같은 이름이라 흡수로만 넣을 수 있습니다.'}elseif($taken){'게임 폴더에 같은 이름 입구 파일이 있습니다 — 흡수하지 않으면 백업 후 덮어 그 모드가 꺼집니다.'}else{''}
                $opt=[pscustomobject]@{Key=('absorb:'+$lower);Label=('모드매니저 입구로 흡수 — 게임 폴더에 다른 이름으로 두고 모드매니저 입구가 불러옵니다(같은 입구 이름을 쓰는 모드와도 공존, 목록에서 켜고 끔). 끄면 제작자 방식대로 '+$name+' 로 넣습니다'+$(if($mustAbsorb){' — 이 모드는 흡수만 가능'}));Type='bool';Default=$true;Choices=@()}
                $components.Add([pscustomobject]@{Key=('proxy:'+$name);Kind='ProxyDll';Title=$name;Detail=('입구형 DLL 모드('+$name+')'+$(if($list.Count -gt 1){' · 함께 넣을 파일 '+($list.Count-1)+'개'}));Suffix='';Files=$list;Default=$true;Blocked=$false;Warning=$warn;Options=@($opt);MustAbsorb=$mustAbsorb})
            }else{
                $typed=Get-MMExternalType $Game $dll.FullName
                if($typed.Type -eq '기존 모드의 다른 버전·보조 DLL'){$components.Add([pscustomobject]@{Key=('dll:'+$name);Kind='Unsupported';Title=$name;Detail=$typed.Evidence;Suffix='';Files=@();Default=$false;Blocked=$true;Warning='모드매니저 기본 모드의 DLL 입니다'});continue}
                $folder='LOH_ModManager\External\'+(Get-MMSafeName $source.Name)
                $list=@($with | ForEach-Object {[pscustomobject]@{Source=(Get-MMRelative $content $_.FullName);Target=($folder+'\'+$_.Name)}})
                $components.Add([pscustomobject]@{Key=('dll:'+$name);Kind='ExternalDll';Title=$name;Detail=('DLL 모드 → '+$folder+' 에 넣고 모드매니저 목록에 등록(version.dll 입구로 불러옴)');Suffix='';Files=$list;Default=$true;Blocked=$false;Warning=''})
            }
        }
        # 4) A package in the self-install format (_files\manifest.json with a "files" list, e.g. 독박육아·만물상): the folders its
        #    list puts a DLL in become DLL mods the manager loads (the whole folder: ini, loh-mod.json…); its entry DLLs and scripts are not used
        $selfManifest=$(if($unitDirs.Count){$null}else{Read-MMJson (Join-Path $base '_files\manifest.json')});$selfDirs=@()
        if($unitDirs.Count){
            $selfDirs=$unitDirs;foreach($u in $units){$components.Add((Get-MMSelfUnitComponent $Game $u $content $source))}
            $old=$(if($Replaces -match '^[0-9a-f]{32}$'){Read-MMJson (Join-Path (Get-MMInstalledRoot $Game) ($Replaces+'\record.json'))}else{$null})
            foreach($g in @($components | Where-Object {$_.Kind -eq 'SelfInstall'} | Group-Object Product)){
                $list=@($g.Group);$seen=@{};foreach($c in $list){if($seen.ContainsKey($c.Key)){$c.Key=$c.Key+':'+$seen.Count};$seen[$c.Key]=$true}
                if($list.Count -lt 2){continue}
                foreach($c in $list){$c.Group='self:'+$g.Name;$c.Default=($c -eq $list[0])}
                $notes.Add($list[0].ModName+': 이 압축에는 종류가 '+$list.Count+'개('+(($list | ForEach-Object {$_.Variant}) -join ', ')+') 들어 있습니다. 하나만 고르세요.')
                # an update: the kind in use now (its record's parts carry it), else nothing ticked — the player picks
                $inner=@($list | ForEach-Object {$_.SubParts | ForEach-Object {$_.Key}})
                $mine=@($(if($old){$old.Parts | Where-Object {[string]$_.Product -eq $g.Name -or $_.Key -in $inner}}))
                if(!$mine.Count){continue}
                $prev=[string]@($mine | Where-Object {$_.PSObject.Properties['Variant'] -and $_.Variant} | ForEach-Object {$_.Variant})[0];$hit=@($list | Where-Object {$prev -and $_.Variant -eq $prev})[0]
                foreach($c in $list){$c.Default=($null -ne $hit -and $c -eq $hit)}
                if($hit){$notes.Add($list[0].ModName+': 지금 쓰는 종류('+$prev+')를 골라 두었습니다.')}elseif($prev){$notes.Add($list[0].ModName+': 지금 쓰는 종류('+$prev+')가 이 버전에는 없습니다. 쓸 것을 골라 주세요.')}
            }
            $notes.Add('모드 자체 설치 정보(_files\manifest.json)대로 넣습니다. 함께 든 입구 DLL·설치 스크립트는 쓰지 않고, DLL 모드는 모드매니저가 불러옵니다.')
        }
        elseif($selfManifest -and @($selfManifest.files).Count){
            foreach($rel in @($selfManifest.files | ForEach-Object {[string]$_.dst} | Where-Object {$_ -match '\.dll$' -and $_ -notmatch '^(MODs|ThreeKingdom_Data)\\' -and $_ -match '\\'} | ForEach-Object {Split-Path $_ -Parent} | Select-Object -Unique)){
                $dir=Join-Path (Join-Path $base '_files') $rel;if(!(Test-Path -LiteralPath $dir -PathType Container) -or (Split-Path $rel -Leaf) -eq 'entry'){continue}
                $dll=@(Get-ChildItem -LiteralPath $dir -Filter '*.dll' -File | Select-Object -First 1)[0];if(!$dll){continue};$selfDirs+=$rel
                $pe=Get-MMPE $dll.FullName;if(!$pe.Valid -or !$pe.X64 -or !$pe.Dll -or $pe.Managed){$components.Add([pscustomobject]@{Key=('dll:'+$dll.Name);Kind='Unsupported';Title=$dll.Name;Detail='x64 네이티브 DLL 이 아닙니다.';Suffix='';Files=@();Default=$false;Blocked=$true;Warning='설치하지 않음'});continue}
                $typed=Get-MMExternalType $Game $dll.FullName
                if($typed.Type -eq '기존 모드의 다른 버전·보조 DLL'){$components.Add([pscustomobject]@{Key=('dll:'+$dll.Name);Kind='Unsupported';Title=$dll.Name;Detail=$typed.Evidence;Suffix='';Files=@();Default=$false;Blocked=$true;Warning='모드매니저 기본 모드의 DLL 입니다'});continue}
                $folder='LOH_ModManager\External\'+(Get-MMSafeName $source.Name)+'\'+(Split-Path $rel -Leaf)   # per archive (two versions side by side do not collide)
                $list=@(Get-ChildItem -LiteralPath $dir -Recurse -Force -File | Where-Object {$_.Name -notin $script:MMJunk} | Sort-Object @{Expression={$_.FullName -ne $dll.FullName}},FullName | ForEach-Object {[pscustomobject]@{Source=(Get-MMRelative $content $_.FullName);Target=($folder+'\'+(Get-MMRelative $dir $_.FullName))}})
                $components.Add([pscustomobject]@{Key=('dll:'+$dll.Name);Kind='ExternalDll';Title=$dll.Name;Detail=('DLL 모드 → '+$folder+' 에 넣고 모드매니저 목록에 등록(version.dll 입구로 불러옴) · 파일 '+$list.Count+'개');Suffix='';Files=$list;Default=$true;Blocked=$false;Warning=''})
            }
            if($selfDirs.Count){$notes.Add('모드 자체 설치 정보(_files\manifest.json)대로 넣습니다. 함께 든 입구 DLL·설치 스크립트는 쓰지 않고, DLL 모드는 모드매니저가 불러옵니다.')}
        }
        if(!$dlls.Count -and $extras.Count -and !$components.Count){$notes.Add('어디에 넣을지 알 수 없는 파일만 있습니다: '+(($extras | Select-Object -First 5 | ForEach-Object {$_.Name}) -join ', '))}
    }
    $scripts=@(Get-ChildItem -LiteralPath $base -Recurse -Force -File | Where-Object {$_.Extension.ToLowerInvariant() -in $script:MMScriptExt})
    if($scripts.Count -and $kind -eq 'files' -and !$components.Count){$notes.Add('모드매니저가 지원하는 형식(데이터 모드·DLL 모드·모드 정보 파일의 패치 약속·가이드 방식 패치)이 아닙니다. 들어 있는 설치 프로그램: '+(($scripts | Select-Object -First 3 | ForEach-Object {$_.Name}) -join ', ')+$(if($scripts.Count -gt 3){', …'}))}
    elseif($scripts.Count -and $kind -eq 'files' -and !$selfDirs.Count){$notes.Add('설치 스크립트가 들어 있습니다('+(($scripts | Select-Object -First 4 | ForEach-Object {$_.Name}) -join ', ')+'). 실행하지 않고 안의 파일만 넣습니다. 스크립트가 파일 복사 말고 다른 일을 한다면 그 모드는 레시피가 필요합니다.')}
    $docs=@(Get-ChildItem -LiteralPath $base -Recurse -Force -File | Where-Object {$_.Extension.ToLowerInvariant() -in @('.txt','.md') -and $_.FullName -notmatch '\\(MODs|ThreeKingdom_Data|licenses?)\\'} | Sort-Object @{Expression={if($_.Name -match '설명|사용법|설치|readme|안내'){0}else{1}}},@{Expression={$_.FullName.Length}})
    $doc='';$docName='';if($docs.Count){$docName=$docs[0].Name;$doc=Read-MMText $docs[0].FullName 3000}
    if(!$components.Count -and $kind -eq 'files' -and !$notes.Count){$notes.Add('설치할 수 있는 내용(데이터 모드·게임 파일·DLL)을 찾지 못했습니다.')}
    return [pscustomobject]@{StageId=$StageId;Name=$source.Name;Source=$source.Path;Kind=$kind;Components=@($components.ToArray());Notes=@($notes.ToArray());Doc=$doc;DocName=$docName}
}
# ---------- recipe: HeroesVow feature packages (빠른거래 등: package.json + 기능\<id>\package.json + runtime\version.dll) ----------
# Their shared loader is a version.dll proxy, the same name as the manager's entry, and the original installer refuses to run
# beside a version.dll it does not own. So the loader is absorbed: it goes next to the exe under another name and the manager's
# entry loads it like any DLL mod. It finds NativeModHost.ini and MODs\NativePlugins next to itself, i.e. in the game folder.
# Each feature is a checkbox; its files (and assets) go where the feature's own package.json says, with the listed hashes.
$script:MMHostName='HeroesVowNativeModHost.dll'   # no dots: the version entry forwards two loader exports to 'HeroesVowNativeModHost.<name>'
function Get-MMHeroesVowRecipe([string]$Game,[string]$Folder,[string]$Content) {
    $root=Read-MMJson (Join-Path $Folder 'package.json');$hostInfo=Read-MMJson (Join-Path $Folder 'runtime\host-package.json')
    $loader=Join-Path $Folder 'runtime\version.dll';$notes=@();$components=@()
    $loaderOk=(Test-Path -LiteralPath $loader) -and $hostInfo -and (Get-MMHash $loader) -eq ([string]$hostInfo.hashes.'version.dll').ToUpperInvariant()
    if(!$loaderOk){$notes+='공통 로더(runtime\version.dll)가 없거나 묶음에 적힌 해시와 달라 설치할 수 없습니다.'}
    # The loader is shared by the packages of this family (Add-MMHeroesVowHost); a feature another package already put in is offered
    # unticked (ticking it puts this package's copy over that one)
    $have=@{};foreach($r in @(Get-MMInstallRecords $Game)){foreach($p in @($r.Parts | Where-Object {$_.Kind -eq 'HostPlugin'})){$have[[string]$p.Key]=$r.Name}}
    $acf=Join-Path (Split-Path $Game -Parent | Split-Path -Parent) 'appmanifest_3020510.acf';$build=''
    if(Test-Path -LiteralPath $acf){$build=([regex]::Match((Get-Content -LiteralPath $acf -Raw),'"buildid"\s+"(\d+)"')).Groups[1].Value}
    foreach($id in @($root.features)){
        if($id -notmatch '^[A-Za-z0-9_-]+$'){continue}
        $dir=Join-Path $Folder ('기능\'+$id);$m=Read-MMJson (Join-Path $dir 'package.json');if(!$m){continue}
        $list=@();$bad=@();$suffix=''
        foreach($f in @($m.files)+@($m.assets)){
            if(!$f -or !$f.source -or !$f.target){continue}
            $target=([string]$f.target).Replace('/','\');if($target -match '(^|\\)\.\.(\\|$)|^[A-Za-z]:|^\\'){$bad+=$target;continue}
            $src=Join-Path $dir ([string]$f.source).Replace('/','\')
            if(!(Test-Path -LiteralPath $src) -or ($f.sha256 -and (Get-MMHash $src) -ne ([string]$f.sha256).ToUpperInvariant())){$bad+=$target;continue}
            if((Split-Path $target -Leaf) -eq 'ModInfo.json'){$suffix=(Get-MMModInfo $src).Suffix}
            $list+=[pscustomobject]@{Source=(Get-MMRelative $Content $src);Target=$target}
        }
        $warn=@();if($bad.Count){$warn+=('해시가 맞지 않거나 위치가 잘못된 파일: '+($bad -join ', '))}
        if([string]$m.status -match 'game-unverified'){$warn+='제작자 기록: 게임 안 확인 전'}
        if($build -and $m.target_build -and $build -ne [string]$m.target_build){$warn+=('제작자 대상 빌드 '+$m.target_build+', 지금 게임 '+$build)}
        $already=$have[('hv:'+$id)];if($already){$warn+=('이미 '''+$already+''' 로 설치돼 있습니다 — 고르면 이 묶음 것으로 바꿉니다')}
        $components+=[pscustomobject]@{Key=('hv:'+$id);Kind='HostPlugin';Title=$(if($m.display_name){[string]$m.display_name}else{$id});
            Detail=('공통 로더 기능 '+$m.version+' · 파일 '+$list.Count+'개 → '+((@($list | ForEach-Object {$_.Target} | Select-Object -First 3)) -join ', ')+$(if($list.Count -gt 3){' …'}));
            Suffix=$suffix;Files=$list;Default=($loaderOk -and !$bad.Count -and !$already);Blocked=(!$loaderOk -or $bad.Count -gt 0);Warning=($warn -join ' · ');Host=[string]$m.host_requirement}
    }
    $notes+=('공통 로더는 모드매니저 입구와 이름(version.dll)이 같아, 게임 폴더에 '+$script:MMHostName+' 로 두고 모드매니저 입구가 불러옵니다(모드매니저 목록의 DLL 한 줄). 원래 설치기(모드설치.bat)와 설치 스크립트는 실행하지 않습니다.')
    $notes+='이름을 바꿔 불러오는 방식은 제작자가 시험하지 않았습니다. 설치 뒤 게임에서 기능이 동작하는지 확인해 주세요. 안 되면 제거하면 넣은 파일이 모두 빠집니다.'
    $owned=@{};foreach($r in @(Get-MMInstallRecords $Game)){foreach($p in $r.Parts){foreach($f in @($p.Files)){$owned[[string]$f.Target]=$true}}}   # the manager's own files are no trace
    foreach($old in @('NativeModHost.ini','QuickTrade.Native.dll','CityPriceHighlight.Native.dll','.native-mod-host-install.json')){if((Test-Path -LiteralPath (Join-Path $Game $old)) -and !$owned.ContainsKey($old)){$notes+=('게임 폴더에 원래 설치기로 넣은 흔적('+$old+')이 있습니다. 같은 이름 파일은 백업 후 덮습니다.');break}}
    return [pscustomobject]@{Components=$components;Notes=$notes}
}
# The loader itself, once per game: the packages of this family share it (빠른거래 · 삼혼비급탭 …, 11-02: the maker split them and
# says they install side by side). When the same loader is already in (another package's), this package adds only its features.
# A fixed adapter is a feature's game-root '<Name>.Native.dll' and gets a [<Name>] section in NativeModHost.ini; a plugin under
# MODs\NativePlugins registers itself with its own ini. The ini is then made from everything installed (Update-MMHeroesVowIni).
function Get-MMHeroesVowAdapters($Files){@($Files | ForEach-Object {$t=[string]$_.Target;if($t -notmatch '[\\/]' -and $t -match '^(.+)\.Native\.dll$'){[pscustomobject]@{Name=$Matches[1];Target=$t;Source=[string]$_.Source}}})}
function Get-MMHeroesVowHostRecord([string]$Game){@(Get-MMInstallRecords $Game | Where-Object {@($_.Parts | Where-Object {$_.Key -eq 'hv:host'}).Count})[0]}
function Add-MMHeroesVowHost($Plan,$Chosen,[string]$Content,[string]$Stage,[string]$Game) {
    $folder=$null;foreach($dir in Get-ChildItem -LiteralPath $Content -Recurse -Directory){if(Test-Path -LiteralPath (Join-Path $dir.FullName 'runtime\host-package.json')){$folder=$dir.FullName;break}}
    if(!$folder -and (Test-Path -LiteralPath (Join-Path $Content 'runtime\host-package.json'))){$folder=$Content}
    if(!$folder){throw '공통 로더를 찾지 못했습니다.'}
    $loader=Join-Path $folder 'runtime\version.dll';$there=Join-Path $Game $script:MMHostName
    if((Get-MMHeroesVowHostRecord $Game) -and (Test-Path -LiteralPath $there) -and (Get-MMHash $there) -eq (Get-MMHash $loader)){return @($Chosen)}   # the same loader serves this package too
    $ini=[Text.StringBuilder]::new()
    foreach($c in $Chosen){foreach($a in @(Get-MMHeroesVowAdapters $c.Files)){[void]$ini.Append('['+$a.Name+"]`r`nEnabled=1`r`nSHA256="+(Get-MMHash (Join-Path $Content $a.Source)).ToLowerInvariant()+"`r`n")}}
    $files=@([pscustomobject]@{Source=(Get-MMRelative $Content $loader);Target=$script:MMHostName})
    if($ini.Length){$made=Join-Path $Content '_mm';New-Item -ItemType Directory -Path $made -Force | Out-Null;[IO.File]::WriteAllText((Join-Path $made 'NativeModHost.ini'),$ini.ToString(),[Text.UTF8Encoding]::new($false));$files+=[pscustomobject]@{Source='_mm\NativeModHost.ini';Target='NativeModHost.ini'}}
    $hostPart=[pscustomobject]@{Key='hv:host';Kind='ExternalDll';Title='공통 로더(HeroesVow NativeModHost)';Suffix='';Files=$files;Default=$true;Blocked=$false;Warning='';Host=''}
    return @($hostPart)+@($Chosen)
}
# NativeModHost.ini from what is installed: every package's fixed adapters on, with the hash of the file now in the game folder;
# a section whose adapter DLL is gone is switched off (Enabled=0, as the loader's own installer leaves it); sections of anything put
# in from outside the manager (their DLL still there) stay as they are. The file belongs to the record that holds the loader.
function Update-MMHeroesVowIni([string]$Game){
    $owner=Get-MMHeroesVowHostRecord $Game;if(!$owner){return}
    $hostPart=@($owner.Parts | Where-Object {$_.Key -eq 'hv:host'})[0];$path=Join-Path $Game 'NativeModHost.ini'
    $sections=[ordered]@{};$current=$null
    if(Test-Path -LiteralPath $path){foreach($line in [IO.File]::ReadAllLines($path)){
        if($line -match '^\s*\[(.+?)\]\s*$'){$current=$Matches[1];if(!$sections.Contains($current)){$sections[$current]=[ordered]@{}};continue}
        if($current -and $line -match '^\s*([^=;#]+?)\s*=\s*(.*?)\s*$'){$sections[$current][$Matches[1]]=$Matches[2]}}}
    $want=[ordered]@{}
    foreach($r in @(Get-MMInstallRecords $Game)){foreach($p in @($r.Parts | Where-Object {$_.Kind -eq 'HostPlugin'})){foreach($a in @(Get-MMHeroesVowAdapters $p.Files)){$f=Join-Path $Game $a.Target;if(Test-Path -LiteralPath $f){$want[$a.Name]=(Get-MMHash $f).ToLowerInvariant()}}}}
    foreach($k in @($want.Keys)){if(!$sections.Contains($k)){$sections[$k]=[ordered]@{}};$sections[$k]['Enabled']='1';$sections[$k]['SHA256']=$want[$k]}
    foreach($k in @($sections.Keys)){if(!$want.Contains($k) -and !(Test-Path -LiteralPath (Join-Path $Game ($k+'.Native.dll')))){$sections[$k]['Enabled']='0'}}
    if(!$sections.Count){return}   # plugins only: no ini, as the loader's installer leaves it
    $text=[Text.StringBuilder]::new();foreach($k in $sections.Keys){[void]$text.Append('['+$k+"]`r`n");foreach($n in $sections[$k].Keys){[void]$text.Append($n+'='+$sections[$k][$n]+"`r`n")}}
    $old=$(if(Test-Path -LiteralPath $path){[IO.File]::ReadAllText($path)}else{$null});if($old -eq $text.ToString()){return}
    $entry=@($hostPart.Files | Where-Object {$_.Target -eq 'NativeModHost.ini'})[0]
    if(!$entry){   # a first section: the ini joins the loader's files (one from the loader's own installer is kept as the original)
        $saved=$null;if($null -ne $old){$saved='NativeModHost.ini';$bk=Join-Path (Join-Path (Get-MMInstalledRoot $Game) $owner.Id) 'backup';New-Item -ItemType Directory -Path $bk -Force | Out-Null;Copy-Item -LiteralPath $path -Destination (Join-Path $bk $saved) -Force}
        $entry=[pscustomobject]@{Target='NativeModHost.ini';Sha256='';Backup=$saved;Previous=''};$hostPart.Files=@($hostPart.Files)+@($entry)}
    [IO.File]::WriteAllText($path,$text.ToString(),[Text.UTF8Encoding]::new($false));$entry.Sha256=Get-MMHash $path
    Save-MMInstallRecord $Game $owner
}
# Files an install needs that another install may hold: a shared loader's plugins need the loader (HeroesVow, known from its
# files), and any mod can say so itself with "needs" in its loh-mod.json (the copy kept with its record, or one it put in the game).
function Get-MMNeeds([string]$Game,$Record){
    $need=@();if(@($Record.Parts | Where-Object {$_.Kind -eq 'HostPlugin'}).Count){$need+=$script:MMHostName}
    $infos=@(Join-Path (Join-Path (Get-MMInstalledRoot $Game) $Record.Id) 'loh-mod.json')+@($Record.Parts | ForEach-Object {$_.Files} | Where-Object {[string]$_.Target -match '(^|[\\/.])loh-mod\.json$'} | ForEach-Object {Join-Path $Game ([string]$_.Target)})
    foreach($m in $infos){try{$j=Read-MMJson $m;if($j -and $j.PSObject.Properties['needs']){foreach($n in @($j.needs | Where-Object {$_})){$need+=([string]$n).Replace('/','\').TrimStart('\')}}}catch{}}
    return @($need | ForEach-Object {$_.ToLowerInvariant()} | Select-Object -Unique)
}
# A need is a path in the game folder, or a bare file name wherever the manager put it (DLL mods go under LOH_ModManager\External)
function Test-MMNeedHit([string]$Target,[string[]]$Needs){$t=$Target.ToLowerInvariant();$leaf=Split-Path $t -Leaf;foreach($n in $Needs){if($t -eq $n -or (!$n.Contains('/') -and !$n.Contains('\') -and $leaf -eq $n)){return $true}};return $false}
function Test-MMNeedThere([string]$Game,[string]$Need){
    if(Test-Path -LiteralPath (Join-Path $Game $Need)){return $true};if($Need.Contains('/') -or $Need.Contains('\')){return $false}
    foreach($r in @(Get-MMInstallRecords $Game)){foreach($p in @($r.Parts)){foreach($f in @($p.Files)){if((Test-MMNeedHit ([string]$f.Target) @($Need)) -and (Test-Path -LiteralPath (Join-Path $Game ([string]$f.Target)))){return $true}}}}
    return $false
}
# Removing or switching off an install while another one still needs a file it put in (a shared loader, with its ini and the
# originals it keeps): that part moves to the newest install needing it instead of going away. One in use first; else one switched
# off (끔) takes it into its set-aside files, so it comes back with it (both off, then either on: 11-02).
function Get-MMParkedRecords([string]$Game){
    $root=Get-MMInstalledRoot $Game;if(!(Test-Path -LiteralPath $root)){return @()}
    return @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name -match '^[0-9a-f]{32}$'} | ForEach-Object {Read-MMJson (Join-Path $_.FullName 'record.json')} | Where-Object {$_ -and $_.State -eq 'Removed' -and $_.Parked -and $_.Vault -and (Test-Path -LiteralPath ([string]$_.Vault))} | Sort-Object Removed -Descending)
}
function Move-MMSharedParts([string]$Game,$Record){
    $others=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -ne $Record.Id} | Sort-Object Installed -Descending)+@(Get-MMParkedRecords $Game | Where-Object {$_.Id -ne $Record.Id});if(!$others.Count){return ''}
    $needs=@{};foreach($o in $others){$needs[$o.Id]=@(Get-MMNeeds $Game $o)}
    $notes=@()
    foreach($part in @($Record.Parts)){
        if($part.Kind -in @('Patcher','FilePatch','ScriptPatch','WorkshopPatch','Link','Recorded')){continue}
        $targets=@($part.Files | ForEach-Object {([string]$_.Target).ToLowerInvariant()})
        $heir=$null;$shared=@();foreach($o in $others){$hit=@($targets | Where-Object {Test-MMNeedHit $_ $needs[$o.Id]});if($hit.Count){$heir=$o;$shared=$hit;break}}
        if(!$heir){continue}
        $from=Join-Path (Join-Path (Get-MMInstalledRoot $Game) $Record.Id) 'backup';$to=Join-Path (Join-Path (Get-MMInstalledRoot $Game) $heir.Id) 'backup'
        foreach($f in @($part.Files | Where-Object {$_.Backup})){$src=Join-Path $from $f.Backup;if(Test-Path -LiteralPath $src){$dst=Join-Path $to $f.Backup;New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Copy-Item -LiteralPath $src -Destination $dst -Force}}
        $heir.Parts=@($heir.Parts)+@($part);Save-MMInstallRecord $Game $heir
        $key=$part.Key;$Record.Parts=@($Record.Parts | Where-Object {$_.Key -ne $key})
        if($heir.State -ne 'Installed'){   # switched off: the files join its set-aside copy (the game's originals back), out of the DLL list
            foreach($f in @($part.Files)){$dst=Join-Path $Game ([string]$f.Target)
                if(Test-Path -LiteralPath $dst -PathType Leaf){$keep=Join-Path ([string]$heir.Vault) ([string]$f.Target);New-Item -ItemType Directory -Path (Split-Path $keep -Parent) -Force | Out-Null;Move-Item -LiteralPath $dst -Destination $keep -Force}
                if($f.Backup -and (Test-Path -LiteralPath (Join-Path $to $f.Backup))){Copy-Item -LiteralPath (Join-Path $to $f.Backup) -Destination $dst -Force}}
            if($part.ExternalId){$catalog=Read-MMJson (Join-Path $Game 'LOH_ModManager\Catalog.json');if(@($catalog.Modules | Where-Object {$_.Id -eq $part.ExternalId}).Count){$null=Invoke-MMApply $Game @{_remove=@($part.ExternalId)}}}
            continue
        }
        $names=@($part.Files | Where-Object {([string]$_.Target).ToLowerInvariant() -in $shared} | ForEach-Object {Split-Path ([string]$_.Target) -Leaf})
        $notes+=('함께 쓰는 '+($names -join ', ')+' 은 '''+$heir.Name+'''에 넘겨 그대로 둠')
    }
    return ($notes -join ' ')
}
# Switched on again (or brought back) while a file it needs sits with an install that is off: that part comes along.
function Get-MMSharedPartsBack([string]$Game,$Record){
    $backup=Join-Path (Join-Path (Get-MMInstalledRoot $Game) $Record.Id) 'backup';$externals=@()
    foreach($n in @(Get-MMNeeds $Game $Record)){
        if(Test-MMNeedThere $Game $n){continue}
        foreach($holder in @(Get-MMParkedRecords $Game | Where-Object {$_.Id -ne $Record.Id})){
            $part=@($holder.Parts | Where-Object {@($_.Files | Where-Object {Test-MMNeedHit ([string]$_.Target) @($n)}).Count})[0];if(!$part){continue}
            foreach($f in @($part.Files)){$src=Join-Path ([string]$holder.Vault) ([string]$f.Target);$dst=Join-Path $Game ([string]$f.Target);if(!(Test-Path -LiteralPath $src)){continue}
                if(Test-Path -LiteralPath $dst -PathType Leaf){$copy=Join-Path $backup ([string]$f.Target);New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null;Copy-Item -LiteralPath $dst -Destination $copy -Force;$f.Backup=[string]$f.Target}else{$f.Backup=$null}
                New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Move-Item -LiteralPath $src -Destination $dst -Force}
            $key=$part.Key;$holder.Parts=@($holder.Parts | Where-Object {$_.Key -ne $key});Save-MMInstallRecord $Game $holder
            $Record.Parts=@($Record.Parts)+@($part)
            if($part.ExternalId){$externals+=[pscustomobject]@{Id=$part.ExternalId;Name=($Record.Name+' · '+$part.Title);Path=(Join-Path $Game @($part.Files)[0].Target);Route=(Get-MMJoinRoute $Game @($part.Original))}}
            break
        }
    }
    if($externals.Count){$choices=@{_add=@($externals)};foreach($x in $externals){$choices[$x.Id]=$true};$null=Invoke-MMApply $Game $choices}
}

# ---------- recipe: 올인원 패처 (allinone.py + official_hashes.json) ----------
# Nothing of it is copied here by hand, so a new version works as it is (18차, 11-01: a new [9/10] 화계 and its patcher's -Fire):
# its questions come from its own ask_choices() (read as text, never run), what an answer runs from its own make_plan() (run by
# its bundled python, as its --auto does). Its --auto installs the answers saved in 올인원선택.json without asking. Since 18차 it
# keeps no backup, so the manager keeps the originals of the files it changes (LOH_ModManager\AllInOneOriginals\<build>).
function ConvertFrom-MMPyString([string]$Text){return [regex]::Replace($Text,'\\(.)',{param($m)$m.Groups[1].Value})}
# Its questions: '[n/N] 이름 — 설명' then ch['key'] = <answer>. Answers: ask_yn (True/False), a yes/no wrapper such as
# yn = lambda q: '2' if ask_yn(q) else '1', ask_num(…, lo, hi, default=…) with its numbered lines, or a dict of choose_font slots.
# Anything else, or no numbered question at all: Ok=$false (the window then offers no install of it, only 수동으로 설치).
function Get-MMAllInOneQuestions([string]$Folder){
    $no={param($why)[pscustomobject]@{Ok=$false;Why=$why;Questions=@();Defaults=[ordered]@{}}}
    $file=Join-Path $Folder 'allinone.py';if(!(Test-Path -LiteralPath $file -PathType Leaf)){return & $no 'allinone.py 없음'}
    $py=Get-Content -LiteralPath $file -Raw -Encoding UTF8
    $m=[regex]::Match($py,'(?ms)^def ask_choices\(\):[^\n]*\n(.*?)(?=^\S|\z)');if(!$m.Success){return & $no '질문하는 부분(ask_choices) 없음'}
    $body=$m.Groups[1].Value
    $d=[regex]::Match($body,'(?s)\bch\s*=\s*\{(.*?)\}');if(!$d.Success){return & $no '기본 답(ch) 없음'}
    $defaults=[ordered]@{}
    foreach($kv in [regex]::Matches($d.Groups[1].Value,"'(\w+)'\s*:\s*(None|True|False|'[^']*'|-?\d+)")){$v=$kv.Groups[2].Value
        $defaults[$kv.Groups[1].Value]=$(if($v -eq 'None'){$null}elseif($v -eq 'True'){$true}elseif($v -eq 'False'){$false}elseif($v.StartsWith("'")){$v.Trim("'")}else{[int]$v})}
    $wrap=@{};foreach($w in [regex]::Matches($body,"(\w+)\s*=\s*lambda\s+\w+\s*:\s*'([^']*)'\s+if\s+ask_yn\(\w+\)\s+else\s+'([^']*)'")){$wrap[$w.Groups[1].Value]=@($w.Groups[2].Value,$w.Groups[3].Value)}
    $qs=@();$head=$null;$desc=@();$slots=@()
    foreach($l in ($body -split "\r?\n")){
        $lm=[regex]::Match($l,'^\s*log\(([''"])(.*)\1\)\s*$')
        if($lm.Success){$t=ConvertFrom-MMPyString $lm.Groups[2].Value;$hm=[regex]::Match($t,'^\[(\d+)\s*/\s*\d+\]\s*(.*)$')
            if($hm.Success){$head=[pscustomobject]@{No=[int]$hm.Groups[1].Value;Text=$hm.Groups[2].Value.Trim()};$desc=@();$slots=@()}
            elseif($t -match '^\[[^\]]+\]'){$head=[pscustomobject]@{No=0;Text=$t.Trim()};$desc=@();$slots=@()}
            elseif($head){$desc+=$t}
            continue}
        $cf=[regex]::Match($l,"choose_font\(\s*'([^']*)'\s*,\s*'[^']*'\s*,\s*(\w+)\s*,\s*'([^']*)'\s*\)");if($cf.Success){$slots+=[pscustomobject]@{Label=$cf.Groups[1].Value;List=$cf.Groups[2].Value;About=$cf.Groups[3].Value};continue}
        $am=[regex]::Match($l,"^\s*ch\[\s*'(\w+)'\s*\]\s*=\s*(.+?)\s*$");if(!$am.Success){continue}
        $key=$am.Groups[1].Value;$rhs=$am.Groups[2].Value;if(!$head){return & $no ('제목 없는 질문: '+$key)}
        $split=@($head.Text -split '\s+[—–-]\s+',2);$name=([regex]::Replace($split[0],'\s*\([^)]*\)','') -replace '\s+(패치|모드)$','').Trim();if(!$name){$name=$key}
        $about=$(if($split.Count -gt 1){$split[1].Trim()}else{''});if($about -and $about -notmatch '[.)」다]$' -and $desc.Count -and $desc[0] -notmatch '^\s*(\d+\.|·|※)'){$about+=' '+$desc[0].Trim()}   # a sentence that goes on to the next line
        $q=[ordered]@{Key=$key;No=$head.No;Name=$name;About=$about;Head=$head.Text;Bullets=@($desc | ForEach-Object {[regex]::Match($_,'^\s*·\s*(.+?)\s*[:：]').Groups[1].Value} | Where-Object {$_});Type='';On=$null;Off=$null;Lo=0;Hi=0;Text=$false;Choices=@();Slots=@()}
        $call=[regex]::Match($rhs,'^(\w+)\(')
        if($call.Success -and $wrap.ContainsKey($call.Groups[1].Value)){$q.Type='yn';$q.On=$wrap[$call.Groups[1].Value][0];$q.Off=$wrap[$call.Groups[1].Value][1]}
        elseif($rhs -match '^ask_yn\('){$q.Type='bool';$q.On=$true;$q.Off=$false}
        elseif($rhs -match 'ask_num\('){$n=[regex]::Match($rhs,'ask_num\((?:''[^'']*''|"[^"]*")\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*default\s*=\s*(\d+))?');if(!$n.Success){return & $no ('번호 질문을 읽지 못함: '+$key)}
            $q.Type='num';$q.Lo=[int]$n.Groups[1].Value;$q.Hi=[int]$n.Groups[2].Value;$q.Text=$rhs -match '^str\(';$off=$(if($n.Groups[3].Success){[int]$n.Groups[3].Value}else{$q.Lo});$q.Off=$(if($q.Text){[string]$off}else{$off})
            $q.Choices=@(for($i=$q.Lo;$i -le $q.Hi;$i++){$line=@($desc | Where-Object {$_ -match ('^\s*'+$i+'\.\s')})[0]
                [pscustomobject]@{Value=[string]$i;Label=$(if($line){$line.Trim() -replace '\s+',' '}else{[string]$i});Short=$(if($line){[regex]::Match($line,'^\s*\d+\.\s*([^:：]+)').Groups[1].Value.Trim()}else{[string]$i})}})}
        elseif($rhs.StartsWith('{') -and $slots.Count){$names=@([regex]::Matches($rhs,"'(\w+)'\s*:\s*rel_here\(") | ForEach-Object {$_.Groups[1].Value});if($names.Count -ne $slots.Count){return & $no ('폰트 질문을 읽지 못함: '+$key)}
            $q.Type='font';$q.Slots=@(for($i=0;$i -lt $slots.Count;$i++){$s=$slots[$i];$block=([regex]::Match($py,'(?s)\n'+[regex]::Escape($s.List)+' = \[(.*?)\n\]')).Groups[1].Value
                [pscustomobject]@{Key=$names[$i];Label=$s.Label;About=$s.About;Fonts=@([regex]::Matches($block,"\('([^']*)',\s*'([^']*)'\)") | ForEach-Object {[pscustomobject]@{Value=('패치\폰트\추천목록\'+$_.Groups[2].Value.Replace('/','\'));Label=$_.Groups[1].Value}})}})}
        else{return & $no ('모르는 질문 형식: '+$key)}
        $qs+=[pscustomobject]$q;$head=$null
    }
    if(!@($qs | Where-Object {$_.No}).Count){return & $no '번호 붙은 질문 없음'}
    return [pscustomobject]@{Ok=$true;Why='';Questions=$qs;Defaults=$defaults}
}
function Test-MMAllInOneOn($Item,$Value){
    switch($Item.Type){
        'font'{return [bool]($Value -and @($Item.Slots | Where-Object {$Value.($_.Key)}).Count)}
        'num'{return $null -ne $Value -and [string]$Value -match '^\d+$' -and [string]$Value -ne [string]$Item.Off}
        'yn'{return $Value -eq $true -or ($null -ne $Value -and $Value -isnot [bool] -and [string]$Value -notin @('',[string]$Item.Off))}   # older answers were True/False
        default{return $Value -eq $true}
    }
}
# What follows an item's name on its card line and in the summary: the level of a numbered question, the fonts.
function Get-MMAllInOneInfo($Item,$Value,[string]$Lead=''){
    if($Item.Type -eq 'num'){$v=$(if((Test-MMAllInOneOn $Item $Value)){[string]$Value}else{[string]$Item.Off});$c=@($Item.Choices | Where-Object {$_.Value -eq $v})[0];if($c){return $Lead+$c.Short};return ''}
    if($Item.Type -eq 'font'){if(!(Test-MMAllInOneOn $Item $Value)){return $Lead+'바꾸지 않음'};return $Lead+((@($Item.Slots | ForEach-Object {$_.Label+' '+$(if($Value.($_.Key+'_label')){$Value.($_.Key+'_label')}else{'바꾸지 않음'})})) -join ' / ')}
    return ''
}
function Get-MMAllInOneRecipe([string]$Game,[string]$Folder) {
    $py=Get-Content -LiteralPath (Join-Path $Folder 'allinone.py') -Raw -Encoding UTF8
    $title=([regex]::Match($py,"TITLE = '([^']+)'")).Groups[1].Value;if(!$title){$title='올인원 패처'}
    $builds=@([regex]::Matches(([regex]::Match($py,'KNOWN_BUILDS = \[([^\]]*)\]')).Groups[1].Value,"'(\d+)'") | ForEach-Object {$_.Groups[1].Value})
    $state=Read-MMJson (Join-Path $Game '패처백업\올인원자동.json');$cur=if($state){$state.choices}else{$null};$build=Get-MMGameBuild $Game
    $q=Get-MMAllInOneQuestions $Folder;$options=@()
    foreach($i in @($q.Questions)){
        $v=$(if($cur -and $cur.PSObject.Properties[$i.Key]){$cur.($i.Key)}elseif($i.No){$null}else{$q.Defaults[$i.Key]})
        $bul=@($i.Bullets) -join '·';$about=$(if($bul -and $i.About -match '아래'){$bul}elseif($bul){$i.About+' ('+$bul+')'}else{$i.About})   # '아래를 한 번에 설치합니다' + its lines: the lines
        $label=$(if($i.No){'['+$i.No+'] '+$i.Name+$(if($about){' — '+$about})}else{$i.Head})
        switch($i.Type){
            'font'{foreach($s in $i.Slots){$options+=[pscustomobject]@{Key=('font_'+$s.Key);Label=('['+$i.No+'] '+$i.Name+' — '+$s.Label+'('+$s.About+')');Type='choice';Default=$(if($v -and $v.($s.Key)){[string]$v.($s.Key)}else{''});Choices=@([pscustomobject]@{Value='';Label='바꾸지 않음'})+@($s.Fonts)}}}
            'num'{$options+=[pscustomobject]@{Key=$i.Key;Label=$label;Type='choice';Default=$(if(Test-MMAllInOneOn $i $v){[string]$v}else{[string]$i.Off});Choices=@($i.Choices | ForEach-Object {[pscustomobject]@{Value=$_.Value;Label=$_.Label}})}}
            default{$options+=[pscustomobject]@{Key=$i.Key;Label=$label;Type='bool';Default=[bool](Test-MMAllInOneOn $i $v);Choices=@()}}
        }
    }
    $warn=if($build -and $builds.Count -and $build -notin $builds){'이 패치가 확인한 게임 빌드가 아닙니다(지금 '+$build+', 확인한 것 '+$builds[-1]+' 까지). 맞는 자리를 못 찾은 패치는 패치가 건너뜁니다 — 새 버전이 있는지 먼저 확인하세요.'}else{''}
    $runtime=Test-Path -LiteralPath (Join-Path $Folder 'runtime\python.exe')
    if(!$runtime){$warn='동봉 파이썬(runtime\python.exe)이 없어 설치할 수 없습니다.'}
    elseif(!$q.Ok){$warn='이 버전의 올인원은 항목을 읽지 못했습니다('+$q.Why+'). 아래 「수동으로 설치」로 넣으세요(항목별 켜고 끄기 없이 한 덩어리, 지우면 통째로 되돌림).'}
    $size=[math]::Round((Get-ChildItem -LiteralPath $Folder -Recurse -Force -File | Measure-Object Length -Sum).Sum/1MB)
    return [pscustomobject]@{Title=$title;Options=$options;Current=[bool]$cur;Warning=$warn;Runtime=$runtime;Readable=$q.Ok;SizeMB=$size;Build=$build}
}
# The window's answers in the patcher's own format: its defaults (ch = {...}) with every question's answer over them.
function ConvertTo-MMAllInOneChoices($Questions,$Options) {
    function Opt($k){if($null -eq $Options){return $null};if($Options -is [Collections.IDictionary]){return $Options[$k]};$p=$Options.PSObject.Properties[$k];if($p){return $p.Value};return $null}
    $ch=[ordered]@{};foreach($k in $Questions.Defaults.Keys){$ch[$k]=$Questions.Defaults[$k]}
    foreach($i in $Questions.Questions){
        switch($i.Type){
            'font'{$font=[ordered]@{};$any=$false
                foreach($s in $i.Slots){$f=[string](Opt ('font_'+$s.Key));if($f -and ($f -notmatch '^패치\\폰트\\' -or $f -match '\.\.')){throw "알 수 없는 폰트 경로: $f"};if($f){$any=$true;$font[$s.Key]=$f}else{$font[$s.Key]=$null}}
                foreach($s in $i.Slots){$f=$font[$s.Key];$font[$s.Key+'_label']=$(if($f){[IO.Path]::GetFileNameWithoutExtension($f)}else{'바꾸지 않음'})}
                $ch[$i.Key]=$(if($any){$font}else{$null})}
            'num'{$v=[string](Opt $i.Key);if($v -notmatch '^\d+$' -or [int]$v -lt $i.Lo -or [int]$v -gt $i.Hi){$v=[string]$i.Off};$ch[$i.Key]=$(if($i.Text){$v}else{[int]$v})}
            'yn'{$ch[$i.Key]=$(if([bool](Opt $i.Key)){$i.On}else{$i.Off})}
            default{$ch[$i.Key]=[bool](Opt $i.Key)}
        }
    }
    return $ch
}
function Get-MMAllInOneSummary($Questions,$Choices) {
    $s=@();foreach($i in @($Questions.Questions | Where-Object {$_.No})){$v=$Choices[$i.Key];if(Test-MMAllInOneOn $i $v){$s+=($i.Name+(Get-MMAllInOneInfo $i $v ' '))}}
    if(!$s.Count){$s+='모두 원작(되돌림)'}
    return ($s -join ' · ')
}
# One item on or off in a copy of the answers. Off remembers a level or the fonts (Last<key> on the record's part) for the next on;
# on without one takes the middle level / the first fonts.
function Copy-MMAllInOneChoices($Choices){$h=[ordered]@{};foreach($k in @($Choices.Keys)){$h[$k]=$Choices[$k]};return $h}
function Set-MMAllInOneAnswer($Choices,$Item,[bool]$On,$Part){
    $last=$(if($Part){$Part.('Last'+$Item.Key)}else{$null})
    if($On){
        if($Item.Type -eq 'font'){if($last){$Choices[$Item.Key]=$last}else{$f=[ordered]@{};foreach($s in $Item.Slots){$f[$s.Key]=$(if(@($s.Fonts).Count){$s.Fonts[0].Value}else{$null})}
                foreach($s in $Item.Slots){$f[$s.Key+'_label']=$(if($f[$s.Key]){[IO.Path]::GetFileNameWithoutExtension($f[$s.Key])}else{'바꾸지 않음'})};$Choices[$Item.Key]=$f}}
        elseif($Item.Type -eq 'num'){$v=$(if(Test-MMAllInOneOn $Item $last){[int][string]$last}else{[int][Math]::Ceiling(($Item.Lo+$Item.Hi)/2)});if([string]$v -eq [string]$Item.Off){$v=$Item.Hi};$Choices[$Item.Key]=$(if($Item.Text){[string]$v}else{$v})}
        else{$Choices[$Item.Key]=$Item.On}
    }else{
        if($Part -and $Item.Type -in @('font','num') -and (Test-MMAllInOneOn $Item $Choices[$Item.Key])){$Part | Add-Member -NotePropertyName ('Last'+$Item.Key) -NotePropertyValue $Choices[$Item.Key] -Force}
        $Choices[$Item.Key]=$(if($Item.Type -eq 'font'){$null}else{$Item.Off})
    }
    return $Choices
}
# What its make_plan() runs for some answers: [{Name,Cmd,Cwd,Files}] per answer set, by its own python ($null when that fails).
$script:MMAioPlanPy=@'
# 모드매니저: 올인원의 make_plan() 이 이 답들로 무엇을 돌릴지만 적는다(아무것도 설치하지 않음)
import importlib.util, json, os, sys
tool, game, src, dst = sys.argv[1:5]
sys.path.insert(0, tool)
spec = importlib.util.spec_from_file_location('allinone_mm', os.path.join(tool, 'allinone.py'))
aio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(aio)
aio.log = lambda *a, **k: None
py = [sys.executable, '-X', 'utf8', '-W', 'ignore']
with open(src, encoding='utf-8-sig') as f:
    wanted = json.load(f)
plans = [[{'name': str(p[0]), 'cmd': [str(c) for c in p[1]], 'cwd': str(p[2]), 'files': [str(r) for r in (p[4] if len(p) > 4 else [])]}
          for p in aio.make_plan(game, py, ch)] for ch in wanted]
with open(dst, 'w', encoding='utf-8') as f:
    json.dump({'plans': plans}, f, ensure_ascii=False)
'@
function Get-MMAllInOnePlans([string]$Game,[string]$Tool,$ChoiceList){
    $python=Join-Path $Tool 'runtime\python.exe';if(!(Test-Path -LiteralPath $python) -or !(Test-Path -LiteralPath (Join-Path $Tool 'allinone.py'))){return $null}
    $work=Join-Path $Game ('LOH_ModManager\Work\aio-plan-'+[guid]::NewGuid().ToString('N').Substring(0,8))   # gone again after the run
    $script=Join-Path $work 'aio_plan.py';$in=Join-Path $work 'in.json';$out=Join-Path $work 'out.json'
    try{New-Item -ItemType Directory -Path $work -Force | Out-Null
        [IO.File]::WriteAllText($script,$script:MMAioPlanPy,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($in,(ConvertTo-Json -InputObject @($ChoiceList) -Depth 6),[Text.UTF8Encoding]::new($false))
        $run=Invoke-MMAioProcess $python @('-X','utf8','-W','ignore',$script,$Tool,$Game,$in,$out) $Tool (Join-Path $work 'aio_plan.log')
        if($run.Code -ne 0 -or !(Test-Path -LiteralPath $out)){return $null}
        $data=Get-Content -LiteralPath $out -Raw -Encoding UTF8 | ConvertFrom-Json
        $plans=@(foreach($plan in @($data.plans)){[pscustomobject]@{Entries=@(foreach($e in @($plan)){if($e){[pscustomobject]@{Name=[string]$e.name;Cmd=@($e.cmd | ForEach-Object {[string]$_});Cwd=[string]$e.cwd;Files=@($e.files | ForEach-Object {([string]$_).Replace('/','\')})}}})}})
        if($plans.Count -ne @($ChoiceList).Count){return $null}
        return ,$plans
    }catch{return $null}finally{Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue}
}
function Compare-MMAllInOnePlans($Old,$New){
    $o=@{};foreach($e in @($Old)){$o[$e.Name]=$e};$n=@{};foreach($e in @($New)){$n[$e.Name]=$e}
    $changed=@($New | Where-Object {$_ -and (!$o.ContainsKey($_.Name) -or (@($o[$_.Name].Cmd) -join [char]31) -ne (@($_.Cmd) -join [char]31))})
    return [pscustomobject]@{Changed=$changed;Gone=@($Old | Where-Object {$_ -and !$n.ContainsKey($_.Name)});Old=$o}
}
# The questions plus, per numbered item, the plan entries it brings in or changes (and their files): all off against that one on.
# Kept beside the patcher copy (LOH_MM_items.json) for that allinone.py; a failed run is kept too, so listing never retries it.
function Get-MMAllInOneModel([string]$Game,[string]$Tool,[switch]$Retry){
    $q=Get-MMAllInOneQuestions $Tool;if(!$q.Ok){return $q}
    $items=@($q.Questions | Where-Object {$_.No});$cache=Join-Path $Tool 'LOH_MM_items.json';$hash=Get-MMHash (Join-Path $Tool 'allinone.py');$c=Read-MMJson $cache;$map=$null
    if($c -and $c.Hash -eq $hash -and ($c.Map -or !$Retry)){$map=$c.Map}
    else{
        $base=ConvertTo-MMAllInOneChoices $q $null;$list=@($base);foreach($i in $items){$list+=,(Set-MMAllInOneAnswer (Copy-MMAllInOneChoices $base) $i $true $null)}
        $plans=Get-MMAllInOnePlans $Game $Tool $list
        if($plans){$map=[ordered]@{};for($n=0;$n -lt $items.Count;$n++){$d=Compare-MMAllInOnePlans $plans[0].Entries $plans[$n+1].Entries;$e=@($d.Changed)+@($d.Gone)
            $map[$items[$n].Key]=[pscustomobject]@{Entries=@($e | ForEach-Object {$_.Name});Files=@($e | ForEach-Object {$_.Files} | Select-Object -Unique)}}}
        try{Write-MMJson $cache ([ordered]@{Hash=$hash;Map=$map})}catch{}
    }
    foreach($i in $items){$m=$(if($map){$map.($i.Key)}else{$null})
        $i | Add-Member -NotePropertyName Entries -NotePropertyValue @($m.Entries | Where-Object {$_}) -Force;$i | Add-Member -NotePropertyName Files -NotePropertyValue @($m.Files | Where-Object {$_}) -Force
        $i | Add-Member -NotePropertyName Assembly -NotePropertyValue (@($m.Files) -contains 'GameAssembly.dll') -Force}
    $q | Add-Member -NotePropertyName Mapped -NotePropertyValue ([bool]$map) -Force
    return $q
}
# Originals. The manager's own copy (taken before 올인원 or one of its items first changes a file, per game build) comes first, then
# older copies (before builds were kept apart; 올인원's own pre-install backup up to 17차) — those only while they still match the
# official hash 올인원 lists for this build. A file as 올인원 left it (its state file's size and time) is never taken for an original.
$script:MMOfficial=@{}
function Get-MMAllInOneOfficial([string]$Tool,[string]$Build,[string]$Rel){
    if(!$Build -or !$Tool){return ''};$f=Join-Path $Tool 'official_hashes.json'
    if(!$script:MMOfficial.ContainsKey($f)){$script:MMOfficial[$f]=Read-MMJson $f}
    $all=$script:MMOfficial[$f];if(!$all -or !$all.builds){return ''};$b=$all.builds.PSObject.Properties[$Build];if(!$b){return ''}
    $h=$b.Value.PSObject.Properties[$Rel.Replace('\','/')];if($h){return ([string]$h.Value).ToUpperInvariant()};return ''
}
function Get-MMAllInOneOriginalsRoot([string]$Game){$b=Get-MMGameBuild $Game;return Join-Path $Game ('LOH_ModManager\AllInOneOriginals\'+$(if($b){$b}else{'unknown'}))}
function Find-MMAllInOneOriginal([string]$Game,[string]$Tool,[string]$Rel){
    $own=Join-Path (Get-MMAllInOneOriginalsRoot $Game) $Rel;if(Test-Path -LiteralPath $own -PathType Leaf){return $own}
    $build=Get-MMGameBuild $Game;$official=Get-MMAllInOneOfficial $Tool $build $Rel
    # 올인원's own backup (up to 17차): for the build it was taken on, the hash it wrote down decides — what it found may differ from
    # Steam's file (another mod changed it first) and is still what 'off' brings back (11-01: 한글보강 Ci_City.json)
    $theirs=Join-Path $Game '패처백업\올인원_설치전';$f=Join-Path $theirs $Rel
    if(Test-Path -LiteralPath $f -PathType Leaf){$meta=Read-MMJson (Join-Path $theirs '올인원백업.json');$e=$(if($meta -and $meta.files){$meta.files.PSObject.Properties[$Rel.Replace('\','/')]}else{$null})
        if($e -and $e.Value.orig_sha -and [string]$meta.build -eq $build){if((Get-MMHash $f) -eq ([string]$e.Value.orig_sha).ToUpperInvariant()){return $f}}
        elseif(!$official -or (Get-MMHash $f) -eq $official){return $f}}
    $f=Join-Path (Join-Path $Game 'LOH_ModManager\AllInOneOriginals') $Rel   # copies from before builds were kept apart
    if((Test-Path -LiteralPath $f -PathType Leaf) -and (!$official -or (Get-MMHash $f) -eq $official)){return $f}
    return $null
}
function Save-MMAllInOneOriginals([string]$Game,[string]$Tool,[string[]]$Rels){
    $state=Read-MMJson (Join-Path $Game '패처백업\올인원자동.json');$build=Get-MMGameBuild $Game;$root=Get-MMAllInOneOriginalsRoot $Game;$kept=0
    foreach($rel in @($Rels | Where-Object {$_ -and $_ -ne 'GameAssembly.dll'} | Select-Object -Unique)){   # GameAssembly.dll: the patch stack's clean copy
        $cur=Join-Path $Game $rel;if(!(Test-Path -LiteralPath $cur -PathType Leaf) -or (Find-MMAllInOneOriginal $Game $Tool $rel)){continue}
        $made=$false;$st=$(if($state -and $state.files){$state.files.PSObject.Properties[$rel.Replace('\','/')]}else{$null})
        if($st){$item=Get-Item -LiteralPath $cur;$v=@($st.Value);$ns=([double]($item.LastWriteTimeUtc.Ticks-621355968000000000))*100;$made=[long]$v[0] -eq $item.Length -and [Math]::Abs($ns-[double]$v[1]) -lt 2e9}
        if($made){$official=Get-MMAllInOneOfficial $Tool $build $rel;if(!$official -or (Get-MMHash $cur) -ne $official){continue}}
        $to=Join-Path $root $rel;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Copy-Item -LiteralPath $cur -Destination $to -Force;$kept++
    }
    return $kept
}
# Every file 올인원 changed back to its original (GameAssembly.dll aside: the patch stack's clean copy does that). Its own 원래대로
# (--restore) first while it still has a backup (up to 17차); the rest from the manager's copies. NoOriginal: files left as they are.
function Get-MMAllInOneTouched([string]$Game,$Part){
    $rels=@();$state=Read-MMJson (Join-Path $Game '패처백업\올인원자동.json');if($state -and $state.files){$rels+=@($state.files.PSObject.Properties | ForEach-Object {$_.Name.Replace('/','\')})}
    $rels+=@($Part.Files | ForEach-Object {[string]$_.Target})
    $tool=Join-Path $Game $Part.Tool;if(Test-Path -LiteralPath (Join-Path $tool 'allinone.py')){$m=Get-MMAllInOneModel $Game $tool;$rels+=@($m.Questions | ForEach-Object {@($_.Files)})}
    return @($rels | Where-Object {$_} | Select-Object -Unique)
}
function Restore-MMAllInOneFiles([string]$Game,$Part,[string]$Log){
    $tool=Join-Path $Game $Part.Tool;$notes=@();$covered=@{}
    $meta=Read-MMJson (Join-Path $Game '패처백업\올인원_설치전\올인원백업.json')
    if($meta -and $meta.files -and @($meta.files.PSObject.Properties).Count -and (Test-Path -LiteralPath (Join-Path $tool 'allinone.py'))){
        $text=Invoke-MMPatcher $tool @($Game,'--restore') $Log 'Y'
        if($text -match '되돌리기 완료'){$notes+=('패치로 되돌림: '+(([regex]::Match($text,'되돌리기 완료[^\r\n]*')).Value));foreach($p in $meta.files.PSObject.Properties){$covered[$p.Name.Replace('/','\')]=$true}}
    }
    $back=0;$none=@()
    foreach($rel in @(Get-MMAllInOneTouched $Game $Part)){
        if($covered.ContainsKey($rel) -or $rel -eq 'GameAssembly.dll'){continue}
        $src=Find-MMAllInOneOriginal $Game $tool $rel;$dst=Join-Path $Game $rel
        if($src){New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Copy-Item -LiteralPath $src -Destination $dst -Force;$back++}elseif(Test-Path -LiteralPath $dst -PathType Leaf){$none+=$rel}
    }
    if($back){$notes+=('보관한 원본 '+$back+'개로 되돌림')}
    return [pscustomobject]@{Notes=$notes;NoOriginal=$none;Assembly=$covered.ContainsKey('GameAssembly.dll')}
}
function Get-MMAllInOneNoOriginalNote($Rels){
    if(!@($Rels).Count){return ''}
    return ('원본이 없어 되돌리지 못한 파일 '+@($Rels).Count+'개('+((@($Rels) | Select-Object -First 2) -join ', ')+$(if(@($Rels).Count -gt 2){' …'})+') — 스팀에서 「게임 파일 무결성 확인」으로 되돌려 주세요.')
}
# Run the patcher's python with a time limit; output goes to a log next to the install record.
function Invoke-MMPatcher([string]$Tool,[string[]]$Arguments,[string]$Log,[string]$InputText='',[string]$Script='allinone.py') {
    $python=Join-Path $Tool 'runtime\python.exe';if(!(Test-Path -LiteralPath $python)){throw '패치의 동봉 파이썬(runtime\python.exe)이 없습니다.'}
    $quoted=@('-X','utf8','-W','ignore',(Join-Path $Tool $Script))+$Arguments | ForEach-Object {if($_ -match '[\s"]'){'"'+$_.Replace('"','\"').TrimEnd('\')+'"'}else{$_}}
    $info=[Diagnostics.ProcessStartInfo]::new($python,($quoted -join ' '));$info.WorkingDirectory=$Tool;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.RedirectStandardInput=$true
    $info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8;$info.EnvironmentVariables['PYTHONIOENCODING']='utf-8'
    # with a UTF-8 console the stdin writer puts a BOM in the pipe as the process starts (AutoFlush), and the patcher's input()
    # then reads it before the 'Y' and refuses (10-31: 올인원 removal did nothing) — start it with a BOM-less encoding of the same code page
    $swap=$null
    if([Console]::InputEncoding.GetPreamble().Length){try{$swap=[Console]::InputEncoding;[Console]::InputEncoding=[Text.UTF8Encoding]::new($false)}catch{$swap=$null;$info.EnvironmentVariables['PYTHONIOENCODING']='utf-8-sig'}}
    try{$p=[Diagnostics.Process]::Start($info)}finally{if($swap){try{[Console]::InputEncoding=$swap}catch{}}}
    try{
        # answers as plain bytes: the stdin writer may begin with a UTF-8 BOM and Python's input() then reads '﻿Y' (10-31: 올인원 removal refused)
        $err=$p.StandardError.ReadToEndAsync();$in=$p.StandardInput.BaseStream
        if($InputText){$bytes=[Text.Encoding]::ASCII.GetBytes($InputText+"`r`n");$in.Write($bytes,0,$bytes.Length);$in.Flush()};$in.Close()
        $out=$p.StandardOutput.ReadToEndAsync()
        if(!$p.WaitForExit(1800000)){try{$p.Kill()}catch{};throw '패치가 30분 안에 끝나지 않아 멈췄습니다.'}
        $text=$out.Result+$(if($err.Result){"`r`n[stderr]`r`n"+$err.Result})
    }finally{$p.Dispose()}
    New-Item -ItemType Directory -Path (Split-Path $Log -Parent) -Force | Out-Null;[IO.File]::AppendAllText($Log,$text,[Text.UTF8Encoding]::new($true))
    return $text
}
# Keep a copy of the patcher in the game's manager folder (item changes and the stack need it), keep the originals of the files the
# chosen answers change, write the answers, run --auto. --auto ends by starting whatever command follows it; 'cmd /c exit 0' stands in.
function Install-MMAllInOne([string]$Game,[string]$Name,$Component,[string]$Content,[string]$Id,$Options) {
    if($null -eq $Options){throw '올인원 항목 선택이 없습니다.'}
    $src=if($Component.Root){Join-Path $Content $Component.Root}else{$Content}
    $questions=Get-MMAllInOneQuestions $src;if(!$questions.Ok){throw ('이 버전의 올인원은 항목을 읽지 못했습니다('+$questions.Why+').')}
    $choices=ConvertTo-MMAllInOneChoices $questions $Options
    $toolRel='LOH_ModManager\Tools\'+(Get-MMSafeName $Name)+'-'+$Id.Substring(0,8);$tool=Assert-MMPath $Game (Join-Path $Game $toolRel)
    New-Item -ItemType Directory -Path (Split-Path $tool -Parent) -Force | Out-Null;try{Copy-Item -LiteralPath $src -Destination $tool -Recurse}catch{Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue;throw}   # e.g. Windows security stopped a file: no half copy left
    [IO.File]::WriteAllText((Join-Path $tool '올인원선택.json'),($choices | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    $log=Join-Path (Get-MMInstalledRoot $Game) ($Id+'\patcher.log')
    $plans=Get-MMAllInOnePlans $Game $tool @(,$choices);$kept=$(if($plans){Save-MMAllInOneOriginals $Game $tool @($plans[0].Entries | ForEach-Object {$_.Files})}else{-1})
    try{$text=Invoke-MMPatcher $tool @(('--game='+$Game),'--auto',$env:ComSpec,'/c','exit','0') $log}catch{Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue;throw}
    if($text -notmatch '다시 설치합니다|패치가 그대로입니다'){
        Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue
        throw ('패치가 설치를 시작하지 못했습니다: '+((($text -split "`r?`n") | Where-Object {$_.Trim()} | Select-Object -Last 3) -join ' '))
    }
    try{$null=Get-MMAllInOneModel $Game $tool -Retry}catch{}   # the card's items: worked out once now, not while listing
    $state=Read-MMJson (Join-Path $Game '패처백업\올인원자동.json');$failed=@();$entries=@()
    if($state){$failed=@($state.failed | Where-Object {$_});if($state.files){foreach($prop in $state.files.PSObject.Properties){$entries+=[ordered]@{Target=$prop.Name.Replace('/','\');Sha256='';Backup=$null;Previous=''}}}}
    $summary=Get-MMAllInOneSummary $questions $choices
    $note=if($text -match '패치가 그대로입니다'){'올인원: 이미 같은 항목으로 설치돼 있어 그대로 두었습니다.'}else{'올인원: '+$summary+'.'}
    if($kept -lt 0){$note+=' 바뀔 파일을 미리 알지 못해 원본을 보관하지 못했습니다 — 지울 때 스팀 「게임 파일 무결성 확인」이 필요할 수 있습니다.'}
    if($failed.Count){$note+=' 실패한 패치: '+($failed -join ', ')+' (기록 '+$log+')'}
    return [ordered]@{Key=$Component.Key;Kind='Patcher';Title=$Component.Title;Suffix='';ExternalId='';Tool=$toolRel;Choices=$choices;Summary=$summary;Failed=$failed;Files=$entries;Note=$note}
}
# 올인원's items one by one (the window's checkboxes). An item changes by running only what its make_plan() changes between the old
# and the new answers (seconds to a minute): a part that comes in or changes runs, a part that goes out gets its files back from the
# originals. When that is not safe (another patcher's section after 올인원's in GameAssembly.dll, a part with GameAssembly.dll going
# out, no original kept) the files go back to the originals and the whole stack is built again with the new answers (a few minutes).
function Get-MMAllInOneChoices([string]$Game,$Part){
    $file=Join-Path (Join-Path $Game $Part.Tool) '올인원선택.json';$ch=Read-MMJson $file;if(!$ch){$st=Read-MMJson (Join-Path $Game '패처백업\올인원자동.json');if($st){$ch=$st.choices}}
    $h=[ordered]@{};if($ch){foreach($p in $ch.PSObject.Properties){$h[$p.Name]=$p.Value}};return $h
}
function Invoke-MMAioProcess([string]$Exe,[string[]]$Arguments,[string]$WorkDir,[string]$Log){
    $quoted=$Arguments | ForEach-Object {if($_ -match '[\s"]'){'"'+$_.Replace('"','\"').TrimEnd('\')+'"'}else{$_}}
    $info=[Diagnostics.ProcessStartInfo]::new($Exe,($quoted -join ' '));$info.WorkingDirectory=$WorkDir;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.RedirectStandardInput=$true;$info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8;$info.EnvironmentVariables['PYTHONIOENCODING']='utf-8'
    $p=[Diagnostics.Process]::Start($info)
    try{$err=$p.StandardError.ReadToEndAsync();$p.StandardInput.Close();$out=$p.StandardOutput.ReadToEndAsync()
        if(!$p.WaitForExit(1800000)){try{$p.Kill()}catch{};throw '패치가 30분 안에 끝나지 않아 멈췄습니다.'}
        $text=$out.Result+$(if($err.Result){"`r`n[stderr]`r`n"+$err.Result});$code=$p.ExitCode}finally{$p.Dispose()}
    New-Item -ItemType Directory -Path (Split-Path $Log -Parent) -Force | Out-Null;[IO.File]::AppendAllText($Log,$text,[Text.UTF8Encoding]::new($true))
    return [pscustomobject]@{Code=$code;Tail=((($text -split "`r?`n") | Where-Object {$_.Trim()} | Select-Object -Last 2) -join ' ')}
}
# A rebuild puts back only GameAssembly.dll. When every other file of 올인원's plan is still the one it wrote (size and time as its own
# state file 패처백업\올인원자동.json keeps them), only the parts that patch GameAssembly.dll need to run again; otherwise none (it all runs).
function Get-MMAllInOneAsmOnly([string]$Game,$Entries){
    $dll='GameAssembly.dll';$asm=@($Entries | Where-Object {@($_.Files) -contains $dll});if(!$asm.Count){return @()}
    $st=Read-MMJson (Join-Path $Game '패처백업\올인원자동.json');if(!$st -or !$st.files){return @()}
    $own=@($asm | ForEach-Object {@($_.Files)})
    foreach($rel in @($Entries | Where-Object {@($_.Files) -notcontains $dll} | ForEach-Object {@($_.Files)} | Where-Object {$_ -and $_ -notin $own} | Select-Object -Unique)){
        $v=$st.files.PSObject.Properties[$rel.Replace('\','/')];$fi=Get-Item -LiteralPath (Join-Path $Game $rel) -ErrorAction SilentlyContinue
        if(!$v -or !$fi -or @($v.Value).Count -lt 2){return @()}
        if([long]@($v.Value)[0] -ne $fi.Length -or [long]@($v.Value)[1] -ne ($fi.LastWriteTimeUtc.Ticks-621355968000000000)*100){return @()}   # its time in ns since 1970
    }
    return $asm
}
function Invoke-MMAllInOneQuick([string]$Game,[string]$Tool,[string]$Name,[string]$Label,$Old,$New,[string]$Log){   # Name: the items, Label: each with 켬/끔
    $plans=Get-MMAllInOnePlans $Game $Tool @($Old,$New);if(!$plans){return [pscustomobject]@{Done=$false}}
    $d=Compare-MMAllInOnePlans $plans[0].Entries $plans[1].Entries;$dll='GameAssembly.dll'
    $goneFiles=@($d.Gone | ForEach-Object {@($_.Files)} | Where-Object {$_ -and $_ -ne $dll} | Select-Object -Unique)   # what has to come back from the originals
    # a part that stays but shares a file with one coming in or going out runs again after it, in the plan's order (11-01: 한글보강 and
    # 군단전 묶음 both change MockWarInfo.json — putting 한글보강's original back would undo 군단전's change in it)
    $order=@();$oldNames=@($plans[0].Entries | ForEach-Object {$_.Name})   # the plan's order, parts going out kept in their old place
    foreach($e in @($plans[1].Entries)){$at=[array]::IndexOf($oldNames,$e.Name);if($at -ge 0){foreach($g in $oldNames[0..$at]){if($g -notin $order -and $g -notin @($plans[1].Entries | ForEach-Object {$_.Name})){$order+=$g}}};$order+=$e.Name}
    foreach($g in $oldNames){if($g -notin $order){$order+=$g}}
    $changedNames=@($d.Changed | ForEach-Object {$_.Name});$moving=@(@($d.Changed)+@($d.Gone))
    $again=@($plans[1].Entries | Where-Object {$x=$_;$x.Name -notin $changedNames -and @($moving | Where-Object {[array]::IndexOf($order,$_.Name) -lt [array]::IndexOf($order,$x.Name) -and @(@($_.Files) | Where-Object {$_ -and $_ -ne $dll -and $_ -in @($x.Files)}).Count}).Count})
    $todo=@($plans[1].Entries | Where-Object {$_.Name -in $changedNames -or $_.Name -in @($again | ForEach-Object {$_.Name})})
    $asm=@(@($d.Changed)+@($d.Gone)+@($again) | Where-Object {@($_.Files) -contains $dll}).Count -gt 0
    if($asm){   # a 가이드 patch below may build on 올인원's section (무투 회피 adds to .lohmod): running 올인원's part alone would drop it
        $stack=@(Get-MMPatchStack $Game);$at=-1;for($k=0;$k -lt $stack.Count;$k++){if(@($stack[$k].Parts | Where-Object {$_.Kind -eq 'Patcher' -and $_.Tool -and (Join-Path $Game $_.Tool) -eq $Tool}).Count){$at=$k}}
        if($at -ge 0 -and @($stack | Select-Object -Skip ($at+1) | Where-Object {@($_.Parts | Where-Object {$_.Flavor -eq 'guide'}).Count}).Count){return [pscustomobject]@{Done=$false;Planned=$true;Gone=$goneFiles}}}
    if($asm){   # its patcher takes its sections off by cutting the file back, so nothing of another patcher may sit after them (상한해제's bytes go back after)
        $names=@(Get-MMAddedSections (Join-Path $Game $dll) | ForEach-Object {$_.Name});$first=-1;for($k=0;$k -lt $names.Count;$k++){if($names[$k] -in $script:MMOwnSections.Patcher){$first=$k;break}}
        if($first -ge 0 -and @($names[$first..($names.Count-1)] | Where-Object {$_ -notin $script:MMOwnSections.Patcher}).Count){return [pscustomobject]@{Done=$false;Planned=$true;Gone=$goneFiles}}}
    foreach($e in @($d.Gone)){if(@($e.Files) -contains $dll){return [pscustomobject]@{Done=$false;Planned=$true;Gone=$goneFiles}}}
    if(@($goneFiles | Where-Object {(Test-Path -LiteralPath (Join-Path $Game $_) -PathType Leaf) -and !(Find-MMAllInOneOriginal $Game $Tool $_)}).Count){return [pscustomobject]@{Done=$false;Planned=$true;Gone=$goneFiles}}
    $said=@()
    foreach($e in @($d.Gone)){$n=0;foreach($rel in @($e.Files)){$src=Find-MMAllInOneOriginal $Game $Tool $rel;if($src){Copy-Item -LiteralPath $src -Destination (Join-Path $Game $rel) -Force;$n++}};$said+=($e.Name+' 원본 파일 '+$n+'개로 되돌림')}
    foreach($e in $todo){
        $fresh=!$d.Old.ContainsKey($e.Name);$rels=@($e.Files | Where-Object {Test-Path -LiteralPath (Join-Path $Game $_) -PathType Leaf})
        if($fresh){$null=Save-MMAllInOneOriginals $Game $Tool $rels}   # coming in: the files as they are now are what 'off' brings back
        $safety=Join-Path $Game ('LOH_ModManager\Backups\AllInOne-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'));$saved=@()
        foreach($rel in $rels){if(!$fresh -or $rel -eq $dll){$to=Join-Path $safety $rel;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $Game $rel) -Destination $to -Force;$saved+=$rel}}
        $cmd=@($e.Cmd);$run=Invoke-MMAioProcess $cmd[0] @($cmd | Select-Object -Skip 1) $(if($e.Cwd -and (Test-Path -LiteralPath $e.Cwd)){$e.Cwd}else{$Tool}) $Log
        if($run.Code -ne 0){
            foreach($rel in $rels){$src=$(if($rel -in $saved){Join-Path $safety $rel}else{Find-MMAllInOneOriginal $Game $Tool $rel});if($src -and (Test-Path -LiteralPath $src)){Copy-Item -LiteralPath $src -Destination (Join-Path $Game $rel) -Force}}
            return [pscustomobject]@{Done=$false;Error=($e.Name+' 이(가) '+$Name+' 을(를) 바꾸지 못했습니다: '+$run.Tail)}
        }
        $said+=($e.Name+$(if($e.Name -in $changedNames){'만 돌림'}else{' 다시 돌림(같은 파일)'}))
    }
    if(!$said.Count){$said+='바뀌는 파일 없음'}
    return [pscustomobject]@{Done=$true;GameAssembly=$asm;Tail=($Label+' — '+($said -join ', '))}
}
function Set-MMAllInOneItem([string]$Game,[string]$Id,[string]$Key,[bool]$On,[string[]]$Order=@()){return Set-MMAllInOneItems $Game $Id @{$Key=$On} $Order}
# Several items at once (the window keeps the ticks until 저장): one answer set, one run of what their plan changes — or one rebuild.
function Set-MMAllInOneItems([string]$Game,[string]$Id,$Changes,[string[]]$Order=@()){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Assert-MMStopped $Game
    $record=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -eq $Id})[0];if(!$record){throw '설치 기록을 찾지 못했습니다.'}
    $part=@($record.Parts | Where-Object {$_.Kind -eq 'Patcher'})[0];if(!$part){throw '올인원 설치가 아닙니다.'}
    $tool=Assert-MMPath $Game (Join-Path $Game $part.Tool);if(!(Test-Path -LiteralPath $tool)){throw '패치 사본이 없어 바꿀 수 없습니다. 올인원을 다시 설치해 주세요.'}
    $model=Get-MMAllInOneModel $Game $tool -Retry;if(!$model.Ok){throw ('이 버전의 올인원은 항목을 읽지 못해 항목별로 켜고 끌 수 없습니다('+$model.Why+').')}
    $want=[ordered]@{};$keys=$(if($Changes -is [Collections.IDictionary]){@($Changes.Keys)}else{@($Changes.PSObject.Properties | ForEach-Object {$_.Name})})
    foreach($k in $keys){$item=@($model.Questions | Where-Object {$_.No -and $_.Key -eq $k})[0];if(!$item){throw ('알 수 없는 올인원 항목: '+$k)};$want[$k]=[bool]$(if($Changes -is [Collections.IDictionary]){$Changes[$k]}else{$Changes.$k})}
    # an order sent with the checkbox (moved, not saved) first: something that must now give way more means the whole stack below
    Save-MMPatchOrderFrom $Game $Order;$moved=Resolve-MMYieldChange $Game;if(!$moved.Rebuild){Save-MMYieldFree $Game $moved}
    $record=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -eq $Id})[0];$part=@($record.Parts | Where-Object {$_.Kind -eq 'Patcher'})[0]
    $old=Get-MMAllInOneChoices $Game $part;$ch=Copy-MMAllInOneChoices $old;$items=@()
    foreach($k in @($want.Keys)){$item=@($model.Questions | Where-Object {$_.Key -eq $k})[0];if((Test-MMAllInOneOn $item $old[$k]) -ne $want[$k]){$ch=Set-MMAllInOneAnswer $ch $item $want[$k] $part;$items+=$item}}
    if(!$items.Count){if($moved.Rebuild){$null=Invoke-MMPatchRebuild $Game;return [pscustomobject]@{Changed=$true;Failed=@(Get-MMAllInOneFailed $Game);Tail='목록 순서대로 다시 쌓음'}}
        return [pscustomobject]@{Changed=$false;Tail='이미 그 상태입니다.'}}
    $items=@($items | Sort-Object No)   # in the patcher's own order, whatever order the ticks came in
    $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $Id;$log=Join-Path $pkgDir 'patcher.log'
    $names=(@($items | ForEach-Object {$_.Name}) -join '·');$label=((@($items | ForEach-Object {$_.Name+' '+$(if($want[$_.Key]){'켬'}else{'끔'})})) -join ' · ')
    # our own way first: only what the items change (seconds to a minute); the whole rebuild only when that is not safe.
    # An item in GameAssembly.dll that gives way is not in the file at all: off (or on while it still has to give way) is only the
    # answer — instant. Once what it gave way to is gone (Free), turning it on runs like any quick change and it stops giving way.
    $heldOf={param($i)$record.Yield -and $i.Key -in @($record.Yield.Items)};$held=@($items | Where-Object {& $heldOf $_})
    $answerOnly=$held.Count -eq $items.Count -and !@($items | Where-Object {$want[$_.Key] -and $record.Yield.Free}).Count
    $quick=if($moved.Rebuild){[pscustomobject]@{Done=$false}}
        elseif($answerOnly){[pscustomobject]@{Done=$true;Tail=($label+' — 지금은 '+(@($record.Yield.By) -join ', ')+' 에 자리를 내준 상태라 게임 파일은 그대로(선택만 바꿈)')}}
        elseif($held.Count -and !$record.Yield.Free){[pscustomobject]@{Done=$false}}   # some still give way and others change: the rebuild sorts that out
        else{Invoke-MMAllInOneQuick $Game $tool $names $label $old $ch $log}
    if($quick.Done -and $held.Count -and $quick.GameAssembly){$record | Add-Member -NotePropertyName Yield -NotePropertyValue $null -Force}
    if($quick.Done){
        [IO.File]::WriteAllText((Join-Path $tool '올인원선택.json'),($ch | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
        $part.Summary=Get-MMAllInOneSummary $model $ch;Save-MMInstallRecord $Game $record
        $notes=@();if($quick.GameAssembly){$notes+=@(Invoke-MMReapplyLast $Game);Save-MMPatchOrder $Game @(Get-MMPatchOrder $Game);foreach($s in (Update-MMTreasuryLinks $Game).Steps){$notes+=($s.Name+': '+$s.Tail)}}
        return [pscustomobject]@{Changed=$true;Quick=$true;Failed=@();Tail=($quick.Tail+$(if($notes.Count){' · '+($notes -join ' · ')}))}
    }
    if($quick.Error){throw $quick.Error}   # its own patcher refused: nothing more is changed
    $back=$(if($quick.Planned){@($quick.Gone)}else{@($items | Where-Object {!$want[$_.Key]} | ForEach-Object {@($_.Files)} | Where-Object {$_ -ne 'GameAssembly.dll'} | Select-Object -Unique)})
    $miss=@($back | Where-Object {(Test-Path -LiteralPath (Join-Path $Game $_) -PathType Leaf) -and !(Find-MMAllInOneOriginal $Game $tool $_)})
    if($miss.Count){throw ('올인원 「'+$names+'」을(를) 끌 원본 파일이 없습니다('+((@($miss) | Select-Object -First 2) -join ', ')+'). 스팀에서 「게임 파일 무결성 확인」을 한 뒤 모드매니저의 ''패치 다시 쌓기''를 누르고 다시 해 주세요.')}
    # what goes out back to its originals (all of 올인원's files when its plan could not be read); what stays is written again by the rebuild
    $res=$(if($quick.Planned){foreach($rel in $back){$src=Find-MMAllInOneOriginal $Game $tool $rel;if($src){Copy-Item -LiteralPath $src -Destination (Join-Path $Game $rel) -Force}};[pscustomobject]@{Notes=@();NoOriginal=@()}}else{Restore-MMAllInOneFiles $Game $part $log})
    [IO.File]::WriteAllText((Join-Path $tool '올인원선택.json'),($ch | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    $part.Summary=Get-MMAllInOneSummary $model $ch;Save-MMInstallRecord $Game $record
    $built=Invoke-MMPatchRebuild $Game
    $failed=@(Get-MMAllInOneFailed $Game);$left=Get-MMAllInOneNoOriginalNote $res.NoOriginal
    return [pscustomobject]@{Changed=$true;Failed=$failed;Tail=($label+' — '+(($built.Steps | ForEach-Object {$_.Name+$(if($_.Ok){' ✓'}else{' ✗ '+$_.Tail})}) -join ' → ')+$(if($left){' · '+$left}))}
}
# ---------- recipe: 뱃길 패치 (patcher.py + lib\sealane.py + runtime\python.exe; 항구 있는 버전 / 항구 없는 버전(안전)) ----------
# A python patcher that rewrites the world map, its navmesh and the city route tables in place (not GameAssembly.dll, so it stays
# out of the patcher rebuild). Its zip carries both variants side by side; only one may be applied. It keeps its own originals in
# 뱃길패치_원본백업 and undoes itself with --restore. Run as 'patcher.py <game> --yes' (the --yes answers its unknown-build and
# port-variant questions); without a tty it never waits for Enter.
function Get-MMSeawayVariants([string]$Base){
    $found=@()
    foreach($dir in @($Base)+@(Get-ChildItem -LiteralPath $Base -Directory -ErrorAction SilentlyContinue | ForEach-Object {$_.FullName})){
        $py=Join-Path $dir 'patcher.py';if(!(Test-Path -LiteralPath $py -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path $dir 'lib\sealane.py'))){continue}
        $text=[IO.File]::ReadAllText($py,[Text.Encoding]::UTF8);$m=[regex]::Match($text,'(?m)^NO_PORT\s*=\s*(True|False)');if(!$m.Success){continue}
        $found+=[pscustomobject]@{Dir=$dir;NoPort=($m.Groups[1].Value -eq 'True');Builds=@([regex]::Matches(([regex]::Match($text,'(?m)^KNOWN_BUILDS\s*=\s*\[([^\]]*)\]')).Groups[1].Value,'\d+') | ForEach-Object {$_.Value});Runtime=(Test-Path -LiteralPath (Join-Path $dir 'runtime\python.exe'))}
    }
    return $found
}
function Get-MMSeawayComponents([string]$Game,[string]$Base,[string]$Content){
    $variants=@(Get-MMSeawayVariants $Base);$build=Get-MMGameBuild $Game
    $allinone=@(Get-MMInstallRecords $Game | Where-Object {@($_.Parts | Where-Object {$_.Kind -eq 'Patcher'}).Count} | ForEach-Object {$_.Name})
    $same=@(Get-MMSameRecipe $Game 'seaway');$old=$(if($same.Count -eq 1){$same[0]}else{$null});$installed=@($same | ForEach-Object {$_.Name})
    foreach($v in $variants | Sort-Object {!$_.NoPort}){
        $warn=@()
        if(!$v.Runtime){$warn+='동봉 파이썬(runtime\python.exe)이 없어 설치할 수 없습니다.'}
        if($installed.Count -gt 1){$warn+=('뱃길 패치가 이미 설치돼 있습니다('+($installed -join ', ')+'). 버전을 바꾸려면 그 설치를 먼저 지우세요.')}
        if($build -and $v.Builds.Count -and $build -notin $v.Builds){$warn+=('이 패치가 확인한 게임 빌드('+($v.Builds -join ', ')+')가 아닙니다(지금 '+$build+'). 패치가 바뀔 파일을 「패처백업」에 먼저 백업하고 고칩니다 — 설치 뒤 게임에서 동래·낙랑 뱃길을 확인하세요.')}
        if(!$v.NoPort){
            $warn+='항구 있는 버전은 세이브에 새 항구(동모항·열구항)가 남아, 게임 업데이트로 패치가 풀린 채 그 세이브를 불러오면 깨질 수 있습니다(제작자 경고). 업데이트 뒤에는 게임을 켜기 전에 다시 넣으세요.'
            if($allinone.Count){$warn+=('올인원('+($allinone -join ', ')+')도 마을 데이터(VillageData.json)를 다뤄, 올인원을 다시 쌓거나 지우면 항구가 사라질 수 있습니다 — 항구 없는 버전을 권합니다.')}
        }
        $files=if($v.NoPort){5}else{6}
        [pscustomobject]@{Key=('recipe:seaway:'+$(if($v.NoPort){'noport'}else{'port'}));Kind='FilePatch';Flavor='seaway';Variant=$(if($v.NoPort){'noport'}else{'port'});Group='seaway'
            Title=$(if($v.NoPort){'뱃길 패치 — 항구 없는 버전(안전), 동래 ↔ 낙랑'}else{'뱃길 패치 — 항구 있는 버전, 동모항 ↔ 열구항'})
            Detail=('동봉된 파이썬 패치(patcher.py)를 모드매니저가 게임 폴더를 정해 묻지 않고(--yes) 돌려 월드맵·길찾기·도시 경로 파일 '+$files+'개를 고칩니다. 제거하면 패치의 「원래대로」(--restore)를 돌립니다. 두 버전 중 하나만 고르세요.')
            Suffix='';Files=@();Default=($v.NoPort -or @($variants).Count -eq 1);Blocked=(!$v.Runtime -or $installed.Count -gt 1);Replaces=$(if($old){[string]$old.Id}else{''});ReplaceName=$(if($old){[string]$old.Name}else{''});Warning=($warn -join ' ');Options=@();Root=$(if($v.Dir -eq $Content){''}else{Get-MMRelative $Content $v.Dir})}
    }
}
function Get-MMPatchTail([string]$Text){return ((($Text -split "`r?`n") | Where-Object {$_.Trim()} | Select-Object -Last 3) -join ' ')}
function Install-MMFilePatch([string]$Game,[string]$Name,$Component,[string]$Content,[string]$Id) {
    $toolRel='LOH_ModManager\Tools\'+(Get-MMSafeName $Name)+'-'+$Id.Substring(0,8);$tool=Assert-MMPath $Game (Join-Path $Game $toolRel)
    $src=if($Component.Root){Join-Path $Content $Component.Root}else{$Content}
    New-Item -ItemType Directory -Path (Split-Path $tool -Parent) -Force | Out-Null;try{Copy-Item -LiteralPath $src -Destination $tool -Recurse}catch{Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue;throw}   # e.g. Windows security stopped a file: no half copy left
    $log=Join-Path (Get-MMInstalledRoot $Game) ($Id+'\patcher.log')
    try{$text=Invoke-MMPatcher $tool @($Game,'--yes') $log '' 'patcher.py'}catch{Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue;throw}
    if($text -notmatch '뱃길 패치 완료|이미 뱃길 패치가 적용되어 있습니다'){
        Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue
        throw ('패치가 뱃길을 넣지 못했습니다(게임 파일은 그대로): '+(Get-MMPatchTail $text))
    }
    # the patcher's own record names the files it wrote; their hashes now tell removal whether something changed them since
    $info=Read-MMJson (Join-Path $Game '뱃길패치_원본백업\backup.json');$entries=@()
    if($info -and $info.files){foreach($prop in $info.files.PSObject.Properties){$rel=([string]$prop.Value).Replace('/','\');$entries+=[ordered]@{Target=$rel;Sha256=(Get-MMHash (Join-Path $Game $rel));Backup=$null;Previous=''}}}
    $summary=$(if($Component.Variant -eq 'noport'){'항구 없는 버전'}else{'항구 있는 버전'})+$(if($text -match '결과 비교 검증은 건너뜀'){' · 확인된 빌드가 아니라 결과 검증은 건너뜀(게임에서 뱃길 확인)'}else{''})
    $note=if($text -match '이미 뱃길 패치가 적용되어 있습니다'){'뱃길 패치: 이미 적용돼 있어 그대로 두고 기록만 남겼습니다.'}else{'뱃길 패치: '+$summary+' 적용.'}
    return [ordered]@{Key=$Component.Key;Kind='FilePatch';Flavor='seaway';Variant=$Component.Variant;Title=$Component.Title;Suffix='';ExternalId='';Tool=$toolRel;Summary=$summary;Files=$entries;Note=$note}
}
# Undo through the patcher, unless the files changed since (a game update put new originals there: its old backup would downgrade them).
function Undo-MMFilePatch([string]$Game,$Part,[string]$Log) {
    $tool=Assert-MMPath $Game (Join-Path $Game $Part.Tool)
    if(!(Test-Path -LiteralPath $tool)){return ($Part.Title+': 패치 사본이 없어 게임 파일은 되돌리지 못했습니다. 스팀 「게임 파일 무결성 확인」으로 되돌려 주세요.')}
    $changed=@($Part.Files | Where-Object {$p=Join-Path $Game $_.Target;!(Test-Path -LiteralPath $p) -or (Get-MMHash $p) -ne $_.Sha256} | ForEach-Object {$_.Target})
    if($changed.Count){return ($Part.Title+': 설치 뒤 게임 파일이 바뀌어(게임 업데이트 등) 되돌리지 않았습니다('+(($changed | Select-Object -First 2) -join ', ')+'). 패치 백업은 게임 폴더 「뱃길패치_원본백업」에 그대로 둡니다.')}
    $text=Invoke-MMPatcher $tool @($Game,'--restore') $Log '' 'patcher.py'
    if($text -match '원래대로 되돌렸습니다'){return ($Part.Title+': 패치의 「원래대로」로 되돌림')}
    if($text -match '백업 폴더\(.*\)가 없습니다'){return ($Part.Title+': 패치 백업이 없어 되돌리지 못했습니다. 스팀 「게임 파일 무결성 확인」으로 되돌려 주세요.')}
    throw ('패치가 되돌리지 못해 지우지 않았습니다(게임 파일은 그대로, 모드매니저 기록도 설치됨 그대로): '+(Get-MMPatchTail $text)+' (기록 '+$Log+')')
}
# ---------- recipe: 세력금고 (installer.py + bank_discovery.py + contracts.json, MODs/TreasuryVault) ----------
# Its own installer puts the DLL on a free winmm.dll/version.dll slot (refuses when both are taken) and reads GameAssembly.dll +
# global-metadata.dat into Treasury.profile (function addresses and their first bytes). The DLL reads that profile beside itself
# and switches itself off when the code no longer matches. So the manager absorbs winmm.dll like any entry-style DLL, copies the
# data, and has the bundled python make only the profile (the maker's package_check / check_game / collision_check / discover,
# nothing written to the game folder by python). A kept copy of the package remakes it after a patcher or game update.
$script:MMTreasuryLink=@'
# 세력금고 Treasury.profile only (LOH_ModManager): the maker's checks and discovery; nothing in the game folder is written.
import sys,json
from pathlib import Path
def main():
    pkg=Path(sys.argv[1]).resolve();game=Path(sys.argv[2]).resolve();out=Path(sys.argv[3])
    sys.path.insert(0,str(pkg))
    try:
        import installer
        installer.package_check()
        game=installer.check_game(game)
        installer.collision_check(game)
        profile,report=installer.discover(game)
        out.write_bytes(profile)
        r={'ok':True,'sha256':installer.digest(profile),'dll_sha256':report.get('dll_sha256','')}
    except Exception as e:
        r={'ok':False,'error':str(e)}
    print('MMRESULT '+json.dumps(r,ensure_ascii=False))
main()
'@
function Test-MMTreasury([string]$Folder){return (Test-Path -LiteralPath (Join-Path $Folder 'bank_discovery.py')) -and (Test-Path -LiteralPath (Join-Path $Folder 'contracts.json')) -and (Test-Path -LiteralPath (Join-Path $Folder 'installer.py')) -and (Select-String -LiteralPath (Join-Path $Folder 'installer.py') -Pattern "MOD='MODs/TreasuryVault'" -SimpleMatch -Quiet)}
function Get-MMTreasuryComponents([string]$Game,[string]$Base,[string]$Content) {
    $rel=if($Base -eq $Content){''}else{(Get-MMRelative $Content $Base)+'\'};$mi=Get-MMModInfo (Join-Path $Base 'mod\ModInfo.json')
    $own=Test-Path -LiteralPath (Join-Path $Game '.treasury-install.json')
    $runtime=Test-Path -LiteralPath (Join-Path $Base 'runtime\python.exe')
    $block=if($own){'제작자 설치기(적용.bat)로 이미 설치돼 있습니다. 먼저 세력금고의 「원래대로.bat」으로 지운 뒤 다시 넣어 주세요(둘이 함께 올라가지 않게).'}elseif(!$runtime){'동봉 파이썬(runtime\python.exe)이 없어 게임 연결 정보를 만들 수 없습니다.'}else{''}
    $data=@(Get-ChildItem -LiteralPath (Join-Path $Base 'mod') -File | Where-Object {$_.Extension -eq '.json'} | ForEach-Object {[pscustomobject]@{Source=($rel+'mod\'+$_.Name);Target=('MODs\TreasuryVault\'+$_.Name)}})
    $taken=Test-Path -LiteralPath (Join-Path $Game 'winmm.dll');$owner=if($taken){Get-MMEntry (Join-Path $Game 'winmm.dll')}else{''}
    $opt=[pscustomobject]@{Key='absorb:winmm.dll';Label='모드매니저 입구로 흡수 — 게임 폴더에 다른 이름으로 두고 모드매니저 입구가 불러옵니다(winmm·version 자리를 쓰는 다른 모드와 공존). 끄면 제작자 방식대로 winmm.dll 로 넣습니다';Type='bool';Default=$true;Choices=@()}
    return @(
        [pscustomobject]@{Key='treasury:data';Kind='Data';Title=$(if($mi.Name){$mi.Name}else{'세력금고'});Detail=('데이터 모드 → MODs\TreasuryVault · 파일 '+$data.Count+'개 · ID '+$mi.Suffix);Suffix=$mi.Suffix;Files=$data;Default=$true;Blocked=[bool]$block;Warning=$block;Treasury=$true},
        [pscustomobject]@{Key='treasury:dll';Kind='ProxyDll';Title='winmm.dll';Detail='세력금고 DLL(winmm.dll 이름의 입구형). 금고 화면·돈 옮기기를 맡습니다.';Suffix='';Files=@([pscustomobject]@{Source=($rel+'winmm.dll');Target='winmm.dll'});Default=$true;Blocked=[bool]$block;
            Warning=$(if($block){$block}elseif($taken -and $owner -ne 'manager'){'게임 폴더에 winmm.dll 이 이미 있습니다(다른 모드) — 흡수로 넣어야 둘 다 씁니다.'}else{''});Options=@($opt);MustAbsorb=($taken -or $owner -eq 'manager');Treasury=$true},
        [pscustomobject]@{Key='treasury:link';Kind='Link';Title='게임 연결 정보(Treasury.profile)';Detail='설치할 때 동봉 파이썬이 지금 게임 파일(GameAssembly.dll·메타데이터)을 읽어 만듭니다(게임 폴더에는 쓰지 않음). 패치를 다시 쌓거나 게임이 업데이트되면 모드매니저가 다시 만듭니다.';Suffix='';Files=@();Default=$true;Blocked=[bool]$block;Warning=$block;Root=$rel.TrimEnd('\');Treasury=$true}
    )
}
# Run a python script with a time limit; output to a log. Returns the text.
function Invoke-MMPython([string]$Python,[string[]]$Arguments,[string]$WorkDir,[string]$Log,[int]$TimeoutMs=600000) {
    $quoted=$Arguments | ForEach-Object {if($_ -match '[\s"]'){'"'+$_.Replace('"','\"').TrimEnd('\')+'"'}else{$_}}
    $info=[Diagnostics.ProcessStartInfo]::new($Python,($quoted -join ' '));$info.WorkingDirectory=$WorkDir;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8;$info.EnvironmentVariables['PYTHONIOENCODING']='utf-8'
    $p=[Diagnostics.Process]::Start($info)
    try{$err=$p.StandardError.ReadToEndAsync();$out=$p.StandardOutput.ReadToEndAsync()
        if(!$p.WaitForExit($TimeoutMs)){try{$p.Kill()}catch{};throw '파이썬이 제때 끝나지 않아 멈췄습니다.'}
        $text=$out.Result+$(if($err.Result){"`r`n[stderr]`r`n"+$err.Result})}finally{$p.Dispose()}
    New-Item -ItemType Directory -Path (Split-Path $Log -Parent) -Force | Out-Null;[IO.File]::AppendAllText($Log,$text,[Text.UTF8Encoding]::new($true))
    return $text
}
# Treasury.profile from the kept package copy into $Out. Throws with the maker's message when it cannot.
function New-MMTreasuryProfile([string]$Tool,[string]$Game,[string]$Out,[string]$Log) {
    $driver=Join-Path $Tool '_mm_treasury_link.py';[IO.File]::WriteAllText($driver,$script:MMTreasuryLink,[Text.UTF8Encoding]::new($false))
    $text=Invoke-MMPython (Join-Path $Tool 'runtime\python.exe') @('-X','utf8','-B',$driver,$Tool,$Game,$Out) $Tool $Log
    $line=@($text -split "`r?`n" | Where-Object {$_ -like 'MMRESULT *'})[-1];$r=if($line){$line.Substring(9) | ConvertFrom-Json}else{$null}
    if(!$r -or !$r.ok -or !(Test-Path -LiteralPath $Out)){throw ('세력금고 게임 연결 정보를 만들지 못했습니다: '+$(if($r -and $r.error){$r.error}else{((($text -split "`r?`n") | Where-Object {$_.Trim()} | Select-Object -Last 2) -join ' ')}))}
    return $r
}
# GameAssembly.dll as the profile last saw it: size + write time (cheap, checked on every catalog read).
function Get-MMAssemblyStamp([string]$Game){$f=Get-Item -LiteralPath (Join-Path $Game 'GameAssembly.dll') -ErrorAction SilentlyContinue;if(!$f){return ''};return ([string]$f.Length+':'+[string]$f.LastWriteTimeUtc.Ticks)}
# Installed 세력금고 links whose GameAssembly.dll changed since the profile was made (patcher, game update, integrity check).
function Get-MMStaleLinks([string]$Game){$stamp=Get-MMAssemblyStamp $Game;return @(Get-MMInstallRecords $Game | Where-Object {@($_.Parts | Where-Object {$_.Kind -eq 'Link' -and $_.Assembly -ne $stamp}).Count} | ForEach-Object {[pscustomobject]@{Id=$_.Id;Name=$_.Name}})}
# Remake the profiles (all, or only stale ones). A profile changed by someone else since our install is left alone.
function Update-MMTreasuryLinks([string]$Game,[switch]$All) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');$stamp=Get-MMAssemblyStamp $Game;$steps=@()
    foreach($r in @(Get-MMInstallRecords $Game)){
        $changed=$false
        foreach($p in @($r.Parts | Where-Object {$_.Kind -eq 'Link'})){
            if(!$All -and $p.Assembly -eq $stamp){continue}
            $f=@($p.Files)[0];$dst=Assert-MMPath $Game (Join-Path $Game $f.Target);$tool=Join-Path $Game $p.Tool;$log=Join-Path (Get-MMInstalledRoot $Game) ($r.Id+'\link.log')
            try{
                if((Test-Path -LiteralPath $dst) -and (Get-MMHash $dst) -ne $f.Sha256){throw '설치 뒤 다른 프로그램이 Treasury.profile 을 바꿔 두어 덮지 않았습니다.'}
                if(!(Test-Path -LiteralPath $tool)){throw ('세력금고 사본('+$p.Tool+')이 없습니다. 다시 설치해 주세요.')}
                $tmp=$dst+'.loh-mm-new';$null=New-MMTreasuryProfile $tool $Game $tmp $log
                Move-Item -LiteralPath $tmp -Destination $dst -Force;$f.Sha256=Get-MMHash $dst;$p.Assembly=$stamp;$changed=$true
                $steps+=[pscustomobject]@{Id=$r.Id;Name=$r.Name;Ok=$true;Tail='게임 연결 정보를 다시 만듦'}
            }catch{Remove-Item -LiteralPath ($dst+'.loh-mm-new') -Force -ErrorAction SilentlyContinue;$steps+=[pscustomobject]@{Id=$r.Id;Name=$r.Name;Ok=$false;Tail=$_.Exception.Message}}
        }
        if($changed){Save-MMInstallRecord $Game $r}
    }
    return [pscustomobject]@{Steps=$steps}
}

# Absorbed entry DLLs keep the game folder (mods often look for their ini/log beside themselves) but not the entry name;
# the id keeps two packages that both ship dwmapi.dll apart.
function Get-MMAbsorbedName([string]$Name,[string]$Id){return 'Absorbed_'+[IO.Path]::GetFileNameWithoutExtension($Name)+'_'+$Id.Substring(0,8)+'.dll'}
# Switch an installed entry-style DLL between absorbed (manager entry loads it) and the maker's way (its own entry name).
function Set-MMPackageEntry([string]$Game,[string]$Id,[bool]$Absorb) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');if($Id -notmatch '^[0-9a-f]{32}$'){throw '잘못된 설치 번호'}
    Assert-MMStopped $Game
    $record=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -eq $Id})[0];if(!$record){throw '설치 기록을 찾지 못했습니다.'}
    $parts=@($record.Parts | Where-Object {$_.Kind -eq $(if($Absorb){'ProxyDll'}else{'AbsorbedDll'})});if(!$parts.Count){throw $(if($Absorb){'흡수할 입구형 DLL 이 없습니다.'}else{'원래 방식으로 되돌릴 흡수 DLL 이 없습니다.'})}
    $changed=@()
    foreach($p in $parts){
        $entry=@($p.Files)[0];$from=Assert-MMPath $Game (Join-Path $Game $entry.Target)
        if(!(Test-Path -LiteralPath $from) -or (Get-MMHash $from) -ne $entry.Sha256){throw ('설치 뒤 바뀐 파일이라 옮기지 않았습니다: '+$entry.Target)}
        if($Absorb){
            $name=Get-MMAbsorbedName $p.Title $Id;$to=Assert-MMPath $Game (Join-Path $Game $name)
            Move-Item -LiteralPath $from -Destination $to
            # the entry name is free again: give back what this install had covered there
            if($entry.Backup){Copy-Item -LiteralPath (Join-Path (Join-Path (Get-MMInstalledRoot $Game) $Id) ('backup\'+$entry.Backup)) -Destination $from;$entry.Backup=$null}
            $ext='ext_'+[guid]::NewGuid().ToString('N')
            try{$null=Invoke-MMApply $Game @{_add=@([pscustomobject]@{Id=$ext;Name=($record.Name+' · '+$p.Title);Path=$to;Route=(Get-MMJoinRoute $Game @($p.Title))});$ext=$true}}catch{Move-Item -LiteralPath $to -Destination $from;throw}
            $entry.Target=$name;$p.Kind='AbsorbedDll';$p | Add-Member -NotePropertyName Original -NotePropertyValue $p.Title -Force;$p.ExternalId=$ext
        }else{
            $name=$p.Title;$to=Assert-MMPath $Game (Join-Path $Game $name)
            # before 0.12.44 a taken-over DLL joined the 'version.dll' entry, so the manager could sit on the name it had freed: it moves to a free name
            $ours=(Test-Path -LiteralPath $to) -and (Get-MMEntry $to) -eq 'manager'
            if((Test-Path -LiteralPath $to) -and !$ours){throw ('게임 폴더에 이미 '+$name+' 이 있어 원래 방식으로 되돌릴 수 없습니다(다른 모드가 그 입구를 씀).')}
            $undo=@{};if($p.ExternalId){$undo['_remove']=@($p.ExternalId)};if($ours){$undo['_avoidEntry']=@($name)}
            if($undo.Count){$null=Invoke-MMApply $Game $undo}
            if(Test-Path -LiteralPath $to){throw ('모드매니저 입구를 '+$name+' 에서 다른 이름으로 옮기지 못해 원래 방식으로 되돌릴 수 없습니다(입구로 쓸 이름이 모두 다른 모드 것).')}
            Move-Item -LiteralPath $from -Destination $to;$entry.Target=$name;$p.Kind='ProxyDll';$p.ExternalId=''
        }
        $changed+=$p.Title
    }
    Save-MMInstallRecord $Game $record
    return [pscustomobject]@{Id=$Id;Name=$record.Name;Changed=$changed;Absorbed=$Absorb}
}
# Removed packages that can come back from their vault (the window's restore list shows them beside removed mods).
# ---------- entry DLLs put in the game folder outside the manager (a mod's own installer: 가야 지도 winhttp.dll, 11-01) ----------
# The game loads them by name whatever the manager's list says, so a mod removed in the manager could keep running from one.
# Listed with the folders that came in at the same time (±10 minutes, not the game's or the manager's own) so the player can hand
# the DLL to the manager (absorbed: renamed, loaded by the manager's entry, on/off like any DLL) or set it aside (moved into a
# removed record, so '지운 모드 되살리기' puts it back).
$script:MMOwnFolders=@('MODs','ThreeKingdom_Data','MonoBleedingEdge','D3D12','LOH_ModManager','패처백업','뱃길패치_원본백업','BepInEx','dotnet','_CommonRedist')
function Get-MMForeignEntries([string]$Game){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');$list=@()
    $recorded=@{};foreach($r in @(Get-MMInstallRecords $Game)){foreach($p in @($r.Parts)){foreach($e in @($p.Files)){if($e.Target){$recorded[([string]$e.Target).ToLowerInvariant()]=$true}}}}
    foreach($name in $script:MMProxyNames){
        $path=Join-Path $Game $name;if(!(Test-Path -LiteralPath $path -PathType Leaf) -or $recorded.ContainsKey($name) -or (Get-MMEntry $path) -ne 'foreign'){continue}
        if(Get-MMLoaderEntry $path){continue}   # a loader's entry stays where it is, not offered for taking over
        $item=Get-Item -LiteralPath $path;$when=$item.LastWriteTime
        $related=@(Get-ChildItem -LiteralPath $Game -Directory -Force | Where-Object {$_.Name -notin $script:MMOwnFolders -and $_.Name -notlike 'LOH_*'} | Where-Object {
            $newest=@(Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
            $newest.Count -and [Math]::Abs(($newest[0].LastWriteTime-$when).TotalMinutes) -le 10} | ForEach-Object {$_.Name})
        $mb=[Math]::Round(((@(foreach($d in $related){Get-ChildItem -LiteralPath (Join-Path $Game $d) -Recurse -File -Force -ErrorAction SilentlyContinue}) | Measure-Object Length -Sum).Sum+$item.Length)/1MB,1)
        $list+=[pscustomobject]@{Name=$name;Size=$item.Length;Time=$when.ToString('yyyy-MM-dd HH:mm');Related=$related;TotalMB=$mb}
    }
    return $list
}
# Loader entries taken over before 0.12.44 (absorbed under another name, so the loader no longer starts): the record to turn back.
# The entry name a newly listed DLL joins: one the manager already uses, never the name just freed by taking a DLL over (the manager
# would sit on it and block '원래 방식으로 되돌리기', 11-03), else a free one.
function Get-MMJoinRoute([string]$Game,[string[]]$Avoid=@()){
    $owner=Read-MMJson (Join-Path $Game 'LOH_ModManager\Owner.json')
    foreach($r in @(@($owner.Entries)+@($owner.Entry) | Where-Object {$_})){if($r -in $script:MMRoutes -and $r -notin $Avoid -and (Get-MMEntry (Join-Path $Game $r)) -eq 'manager'){return $r}}
    # no entry yet (a first save with nothing on): version.dll as always — 0.12.44 took the first free name (hid.dll) here, and a
    # 빠른거래 put in by the manager then had no version.dll for its plugins to find (11-03 디시 79360 댓글)
    $default=Get-MMDefaultRoute $Game;if($default -notin $Avoid -and (Get-MMEntry (Join-Path $Game $default)) -ne 'foreign'){return $default}
    $free=Get-MMFreeRoute $Game $Avoid;if($free){return $free};return 'version.dll'
}
function Get-MMTakenLoaders([string]$Game){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    return @(foreach($r in @(Get-MMInstallRecords $Game)){foreach($p in @($r.Parts | Where-Object {$_.Kind -eq 'AbsorbedDll'})){foreach($e in @($p.Files)){
        $loader=if($e.Target){Get-MMLoaderEntry (Join-Path $Game ([string]$e.Target))}else{''}
        if($loader){[pscustomobject]@{Id=$r.Id;Name=$r.Name;Original=$p.Original;Loader=$loader}}}}})
}
function Assert-MMForeignEntry([string]$Game,[string]$Name){
    $hit=@(Get-MMForeignEntries $Game | Where-Object {$_.Name -eq $Name})[0];if(!$hit){throw ($Name+' 은 모드매니저 밖에서 들어온 입구 DLL 이 아닙니다(이미 정리됐거나 모드매니저가 넣은 파일).')};return $hit
}
# Hand it to the manager: the absorbed name, a record like an install from a dropped file, and on in the list (it was running).
function Invoke-MMAdoptEntry([string]$Game,[string]$Name){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Assert-MMStopped $Game;$hit=Assert-MMForeignEntry $Game $Name
    $id=[guid]::NewGuid().ToString('N');$pkgDir=Join-Path (Get-MMInstalledRoot $Game) $id;$null=Assert-MMPath $Game $pkgDir;New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null
    $absorbed=Get-MMAbsorbedName $Name $id;Move-Item -LiteralPath (Join-Path $Game $Name) -Destination (Join-Path $Game $absorbed)
    $ext='ext_'+[guid]::NewGuid().ToString('N');$label=$Name+' (모드매니저 밖에서 들어온 DLL)'
    $record=[ordered]@{Format='LOH_ModManager.Installed 1';Id=$id;Name=$label;Source='게임 폴더에서 가져옴';Installed=(Get-Date).ToString('s');State='Installed';Doc='';DocName=''
        Parts=@([ordered]@{Key=('adopt:'+$Name);Kind='AbsorbedDll';Title=$Name;Suffix='';ExternalId=$ext;Original=$Name;Files=@([ordered]@{Target=$absorbed;Sha256=(Get-MMHash (Join-Path $Game $absorbed));Backup=$null;Previous=''})})}
    Save-MMInstallRecord $Game $record
    try{$null=Invoke-MMApply $Game @{_add=@([pscustomobject]@{Id=$ext;Name=$label;Path=(Join-Path $Game $absorbed);Route=(Get-MMJoinRoute $Game @($Name))});$ext=$true}}
    catch{Move-Item -LiteralPath (Join-Path $Game $absorbed) -Destination (Join-Path $Game $Name);Remove-Item -LiteralPath $pkgDir -Recurse -Force -ErrorAction SilentlyContinue;throw}
    return [pscustomobject]@{Id=$id;Name=$label;Related=@($hit.Related)}
}
# Set it aside: the DLL (and the folders the player ticked) move into a removed record's vault, nothing deleted.
function Invoke-MMSetAsideEntry([string]$Game,[string]$Name,[string[]]$Folders=@()){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Assert-MMStopped $Game;$hit=Assert-MMForeignEntry $Game $Name
    $Folders=@($Folders | Where-Object {$_ -and $_ -in @($hit.Related)})
    $id=[guid]::NewGuid().ToString('N');$pkgDir=Join-Path (Get-MMInstalledRoot $Game) $id;$vault=Join-Path $pkgDir ('removed-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
    $null=Assert-MMPath $Game $vault;New-Item -ItemType Directory -Path $vault -Force | Out-Null
    $rels=@($Name)+@(foreach($d in $Folders){Get-ChildItem -LiteralPath (Join-Path $Game $d) -Recurse -File -Force | ForEach-Object {$_.FullName.Substring($Game.Length+1)}})
    $files=@($rels | ForEach-Object {[ordered]@{Target=$_;Sha256=(Get-MMHash (Join-Path $Game $_));Backup=$null;Previous=''}})
    foreach($rel in $rels){$to=Join-Path $vault $rel;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Move-Item -LiteralPath (Join-Path $Game $rel) -Destination $to}
    foreach($d in $Folders){$dir=Join-Path $Game $d;if((Test-Path -LiteralPath $dir) -and !@(Get-ChildItem -LiteralPath $dir -Recurse -File -Force).Count){Remove-Item -LiteralPath $dir -Recurse -Force}}   # only empty folders left
    $record=[ordered]@{Format='LOH_ModManager.Installed 1';Id=$id;Name=($Name+' (모드매니저 밖에서 들어온 DLL'+$(if($Folders.Count){' + '+($Folders -join ', ')})+')');Source='게임 폴더에서 빼 둠';Installed=(Get-Date).ToString('s');State='Removed';Vault=$vault;Removed=(Get-Date).ToString('s');Kept=@();Doc='';DocName=''
        Parts=@([ordered]@{Key=('aside:'+$Name);Kind='ProxyDll';Title=$Name;Suffix='';ExternalId='';Original='';Files=$files})}
    Save-MMInstallRecord $Game $record
    return [pscustomobject]@{Id=$id;Name=$record.Name;Moved=$rels.Count;Vault=$vault}
}
function Get-MMRemovedPackages([string]$Game) {
    $root=Get-MMInstalledRoot $Game;if(!(Test-Path -LiteralPath $root)){return @()}
    return @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name -match '^[0-9a-f]{32}$'} | ForEach-Object {Read-MMJson (Join-Path $_.FullName 'record.json')} | Where-Object {$_ -and $_.State -eq 'Removed' -and !$_.Parked} |
        ForEach-Object {[pscustomobject]@{Id=$_.Id;Kind='package';Name=$_.Name;Date=([string]$_.Removed).Replace('T',' ');State='모드매니저로 설치했던 모드';Paths=@($_.Parts | ForEach-Object {$_.Files} | ForEach-Object {$_.Target} | Select-Object -First 12);Note='설치 기록 보관함에서 되돌립니다(패치는 다시 설치).'}})
}
# Patchers switched off in the list (끔): removed the usual way, kept with Parked — shown in the 끔 list, not in the 휴지통.
function Get-MMParkedPackages([string]$Game) {
    $root=Get-MMInstalledRoot $Game;if(!(Test-Path -LiteralPath $root)){return @()}
    return @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name -match '^[0-9a-f]{32}$'} | ForEach-Object {Read-MMJson (Join-Path $_.FullName 'record.json')} | Where-Object {$_ -and $_.State -eq 'Removed' -and $_.Parked} | Sort-Object Installed |
        ForEach-Object {[pscustomobject]@{Id=$_.Id;Name=$_.Name;Installed=$_.Installed;Source=$_.Source;Removed=$_.Removed;Vault=[string]$_.Vault;Parts=@($_.Parts | ForEach-Object {[pscustomobject]@{Kind=$_.Kind;Title=$_.Title;Summary=[string]$_.Summary;Files=@($_.Files).Count;Tool=[string]$_.Tool}})}})
}
# The list's patcher switches, saved together: off ones parked (their own undo, the others rebuilt), on ones restored where the list
# puts them; then the order on screen if it still differs.
function Invoke-MMPatchSwitch([string]$Game,[string[]]$Off=@(),[string[]]$On=@(),[string[]]$Order=@()) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('');Assert-MMStopped $Game;$steps=@()
    foreach($id in @($Off | Where-Object {$_})){
        try{$r=Uninstall-MMPackage $Game $id -Park;$steps+=[pscustomobject]@{Id=$id;Name=$r.Name;Ok=$true;Tail=('꺼 둠'+$(if($r.Note){' — '+$r.Note}))}}
        catch{$steps+=[pscustomobject]@{Id=$id;Name=$id;Ok=$false;Tail=('끄지 못함: '+$_.Exception.Message)}}
    }
    foreach($id in @($On | Where-Object {$_})){
        try{$r=Restore-MMPackage $Game $id @($Order | Where-Object {$_});$steps+=[pscustomobject]@{Id=$id;Name=$r.Name;Ok=!@($r.Missing).Count;Tail=('다시 켬'+$(if($r.Note){' — '+$r.Note}))}}
        catch{$steps+=[pscustomobject]@{Id=$id;Name=$id;Ok=$false;Tail=('켜지 못함: '+$_.Exception.Message)}}
    }
    $now=@(Get-MMPatchOrder $Game);$want=@($Order | Where-Object {$_ -in $now});foreach($x in $now){if($x -notin $want){$want+=$x}}
    $gave=@()
    if($want.Count -gt 1 -and ($want -join ',') -ne ($now -join ',')){$built=Invoke-MMPatchRebuild $Game $want;$gave=@($built.Gave);$steps+=[pscustomobject]@{Id='';Name='순서대로 다시 쌓기';Ok=!@($built.Steps | Where-Object {!$_.Ok}).Count;Tail=(($built.Steps | ForEach-Object {$_.Name+$(if($_.Ok){' ✓'}else{' ✗'})}) -join ' → ')}}
    else{$gave=@(Get-MMYieldLines @(Get-MMPatchStack $Game))}
    return [pscustomobject]@{Steps=$steps;Gave=$gave}
}
# A parked patcher deleted from the 끔 list: it moves to the 휴지통 (지운 모드 되살리기) like any removed package.
function Remove-MMParked([string]$Game,[string]$Id) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('');if($Id -notmatch '^[0-9a-f]{32}$'){throw '잘못된 설치 번호'}
    $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $Id;$record=Read-MMJson (Join-Path $pkgDir 'record.json');if(!$record -or $record.State -ne 'Removed' -or !$record.Parked){throw '꺼 둔 패치가 아닙니다.'}
    $null=$record.PSObject.Properties.Remove('Parked');Save-MMInstallRecord $Game $record
    return [pscustomobject]@{Id=$Id;Name=$record.Name}
}
# Put a removed package back: files from its vault, DLL parts registered again, data ticked; patchers are re-run with their saved answers.
function Restore-MMPackage([string]$Game,[string]$Id,[string[]]$Order=@()) {   # Order: where a patcher goes back in the list (switched on again)
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');if($Id -notmatch '^[0-9a-f]{32}$'){throw '잘못된 설치 번호'}
    Assert-MMStopped $Game
    $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $Id;$record=Read-MMJson (Join-Path $pkgDir 'record.json');if(!$record -or $record.State -ne 'Removed'){throw '복원할 설치 기록이 아닙니다.'}
    $vault=[string]$record.Vault;if(!$vault -or !(Test-Path -LiteralPath $vault)){$vault=@(Get-ChildItem -LiteralPath $pkgDir -Directory -Filter 'removed-*' | Sort-Object Name | Select-Object -Last 1 | ForEach-Object {$_.FullName})[0]}
    if(!$vault -or !(Test-Path -LiteralPath $vault)){throw '보관함을 찾지 못했습니다.'}
    if(@($record.Parts | Where-Object {$_.Key -eq 'hv:host'}).Count -and @(Get-MMInstallRecords $Game | Where-Object {@($_.Parts | Where-Object {$_.Key -eq 'hv:host'}).Count}).Count){throw '같은 공통 로더를 쓰는 다른 묶음이 설치돼 있어 복원하지 않았습니다. 그 묶음을 먼저 제거하세요.'}
    # one 올인원 at a time: both would share its backup and state files in 패처백업 (10-31: reinstalled while an old record sat removed)
    if(@($record.Parts | Where-Object {$_.Kind -eq 'Patcher'}).Count -and @(Get-MMInstallRecords $Game | Where-Object {@($_.Parts | Where-Object {$_.Kind -eq 'Patcher'}).Count}).Count){throw '올인원 패처가 이미 설치돼 있어 복원하지 않았습니다(둘이 같은 백업을 씁니다). 이 기록은 휴지통으로 지워도 됩니다.'}
    $backup=Join-Path $pkgDir 'backup';$missing=@();$externals=@();$notes=@();$restackRestore=$false
    foreach($p in $record.Parts){
        if($p.Kind -eq 'ScriptPatch'){
            $tool=Join-Path $Game $p.Tool;$kept=Join-Path $vault $p.Tool
            if(Test-Path -LiteralPath $kept){New-Item -ItemType Directory -Path (Split-Path $tool -Parent) -Force | Out-Null;Move-Item -LiteralPath $kept -Destination $tool}
            if(!(Test-Path -LiteralPath $tool)){$notes+='패치 사본이 없어 다시 설치하지 못했습니다.';continue}
            if($p.Flavor -eq 'capunlock'){   # its kept bytes on top of the file as it is
                $dll=Join-Path $Game 'GameAssembly.dll';Copy-Item -LiteralPath $dll -Destination (Join-Path $backup 'GameAssembly.dll') -Force
                try{$res=Set-MMCapUnlockBytes $Game $p $pkgDir;Set-MMCapUnlockResult $p $res;$f=@($p.Files)[0];$f.Sha256=Get-MMHash $dll;$f.Backup='GameAssembly.dll';$notes+=('상한해제: '+(Get-MMCapUnlockTail $res))}catch{$notes+=('상한해제를 다시 넣지 못했습니다: '+$_.Exception.Message)}
                continue
            }
            if(@(Get-MMInstallRecords $Game | Where-Object {Test-MMPatchRecord $_}).Count){$restackRestore=$true;continue}
            $dll=Join-Path $Game 'GameAssembly.dll';Copy-Item -LiteralPath $dll -Destination (Join-Path $backup 'GameAssembly.dll') -Force
            $run=Invoke-MMScriptPatch $tool $Game ([bool]$p.SortOnly) (Join-Path $pkgDir 'patcher.log') (Get-MMScriptPatchArgs $p 'apply')
            if($run.Code -ne 0){$notes+=('패치를 다시 설치하지 못했습니다: '+$run.Tail);continue}
            $f=@($p.Files)[0];$f.Sha256=Get-MMHash $dll;$f.Backup='GameAssembly.dll';$notes+=($p.Title+' 패치를 다시 설치했습니다.');continue
        }
        if($p.Kind -eq 'Patcher'){
            $tool=Join-Path $Game $p.Tool;$kept=Join-Path $vault $p.Tool
            if(Test-Path -LiteralPath $kept){New-Item -ItemType Directory -Path (Split-Path $tool -Parent) -Force | Out-Null;Move-Item -LiteralPath $kept -Destination $tool}
            if(!(Test-Path -LiteralPath $tool)){$notes+='패치 사본이 없어 다시 설치하지 못했습니다.';continue}
            $text=Invoke-MMPatcher $tool @(('--game='+$Game),'--auto',$env:ComSpec,'/c','exit','0') (Join-Path $pkgDir 'patcher.log');$failed=@(Get-MMAllInOneFailed $Game)
            $notes+=$(if($text -notmatch '다시 설치합니다|패치가 그대로입니다'){'패치가 다시 설치하지 못했습니다(기록 patcher.log).'}elseif($failed.Count){'패치를 다시 설치했지만 실패한 패치가 있습니다: '+($failed -join ', ')+' (기록 patcher.log)'}else{'패치를 기억한 항목으로 다시 설치했습니다.'})
            continue
        }
        if($p.Kind -eq 'FilePatch'){
            $tool=Join-Path $Game $p.Tool;$kept=Join-Path $vault $p.Tool
            if(Test-Path -LiteralPath $kept){New-Item -ItemType Directory -Path (Split-Path $tool -Parent) -Force | Out-Null;Move-Item -LiteralPath $kept -Destination $tool}
            if(!(Test-Path -LiteralPath $tool)){$notes+='패치 사본이 없어 다시 설치하지 못했습니다.';continue}
            $text=Invoke-MMPatcher $tool @($Game,'--yes') (Join-Path $pkgDir 'patcher.log') '' 'patcher.py'
            if($text -notmatch '뱃길 패치 완료|이미 뱃길 패치가 적용되어 있습니다'){$notes+=('패치가 다시 설치하지 못했습니다: '+(Get-MMPatchTail $text));continue}
            foreach($f in $p.Files){$f.Sha256=Get-MMHash (Join-Path $Game $f.Target)};$notes+=($p.Title+' 다시 설치했습니다.');continue
        }
        if($p.Kind -eq 'WorkshopPatch'){try{$r=Set-MMWorkshopPatchApplied $Game $p $pkgDir;if($r.Lost){$notes+=($p.Title+': 패치 사본 '+$r.Lost+'개가 없어 다시 넣지 못했습니다.')}}catch{$notes+=$_.Exception.Message};continue}
        if($p.Kind -eq 'Link' -and $p.Tool){$tool=Join-Path $Game $p.Tool;$kept=Join-Path $vault $p.Tool;if(Test-Path -LiteralPath $kept){New-Item -ItemType Directory -Path (Split-Path $tool -Parent) -Force | Out-Null;Move-Item -LiteralPath $kept -Destination $tool}}
        foreach($e in $p.Files){
            if($e.NoBackup -or ($p.Kind -eq 'Recorded' -and !$e.Sha256)){continue}   # left in place / a file the recorded install had removed
            $src=Join-Path $vault $e.Target;$dst=Assert-MMPath $Game (Join-Path $Game $e.Target)
            if(!(Test-Path -LiteralPath $src)){$missing+=$e.Target;continue}
            if(Test-Path -LiteralPath $dst -PathType Leaf){$copy=Join-Path $backup $e.Target;New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null;Copy-Item -LiteralPath $dst -Destination $copy -Force;$e.Backup=$e.Target}
            New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Move-Item -LiteralPath $src -Destination $dst -Force
        }
        if($p.ExternalId){$externals+=[pscustomobject]@{Id=$p.ExternalId;Name=($record.Name+' · '+$p.Title);Path=(Join-Path $Game @($p.Files)[0].Target);Route=(Get-MMJoinRoute $Game @($p.Original))}}
    }
    if($externals.Count){$choices=@{_add=@($externals)};foreach($x in $externals){$choices[$x.Id]=$true};$null=Invoke-MMApply $Game $choices}
    $record.State='Installed';$null=$record.PSObject.Properties.Remove('Parked');$record | Add-Member -NotePropertyName Restored -NotePropertyValue (Get-Date).ToString('s') -Force;Save-MMInstallRecord $Game $record
    if(Test-MMPatchRecord $record){
        $first=@($record.Parts | Where-Object {$_.Kind -eq 'ScriptPatch' -and !$_.Last}).Count -gt 0;$rest=@(Get-MMPatchOrder $Game | Where-Object {$_ -ne $Id})
        $order=$(if($first){@($Id)+$rest}else{$rest+@($Id)})
        if($Order.Count){$order=@($Order | Where-Object {$_ -eq $Id -or $_ -in $rest});foreach($x in $rest){if($x -notin $order){$order+=$x}};if($Id -notin $order){$order+=$Id}}
        if($restackRestore){try{$built=Invoke-MMPatchRebuild $Game $order;$notes+=('패치를 원본에서 다시 쌓음: '+(($built.Steps | ForEach-Object {$_.Name+$(if($_.Ok){' ✓'}else{' ✗'})}) -join ' → '))}catch{$notes+=('다시 쌓지 못했습니다: '+$_.Exception.Message)}}
        else{Save-MMPatchOrder $Game $order;if(@($record.Parts | Where-Object {$_.Kind -eq 'Patcher'}).Count){$notes+=@(Invoke-MMReapplyLast $Game $Id)}}   # 올인원 back: 상한해제's bytes on top again
    }
    $suffixes=@($record.Parts | Where-Object {$_.Kind -in @('Data','HostPlugin','ScriptPatch') -and $_.Suffix} | ForEach-Object {$_.Suffix})
    if($suffixes.Count -and (Test-MMRealInstall $Game)){try{$null=Set-MMSelectionOn $suffixes $backup}catch{}}
    foreach($s in (Update-MMTreasuryLinks $Game).Steps){$notes+=($s.Name+': '+$s.Tail)}   # GameAssembly.dll may have changed while it sat removed
    Get-MMSharedPartsBack $Game $record;Save-MMInstallRecord $Game $record
    if(@($record.Parts | Where-Object {$_.Kind -eq 'HostPlugin'}).Count){try{Update-MMHeroesVowIni $Game}catch{}}
    foreach($n in @(Get-MMNeeds $Game $record)){if(!(Test-MMNeedThere $Game $n)){$notes+=(''''+$record.Name+'''에 필요한 '+(Split-Path $n -Leaf)+' 이 없습니다. 이 파일을 넣는 모드를 다시 설치해 주세요.')}}
    return [pscustomobject]@{Id=$Id;Name=$record.Name;Missing=@($missing);Note=($notes -join ' ')}
}
# ---------- recipe: 명승지 이동 선부 (3.1 = GameAssembly.dll patcher; the 4.0 테스트판 beside it is a BepInEx 6 IL2CPP plugin) ----------
# 3.1's Install-ScenicTalisman.ps1 checks the bytes it hooks, appends a .scfr1 section and puts MODs\명승지_이동_선부 in place
# without asking (-GamePath), and undoes both with -Restore. So it joins the patch stack like 창천UI. The 4.0 plugin needs a
# BepInEx loader in the game folder (BepInEx\core, interop, dotnet), which the manager does not install.
function Get-MMScenicDir([string]$Base){foreach($d in @((Join-Path $Base 'legacy_v3_1'),$Base)){if((Test-Path -LiteralPath (Join-Path $d 'Install-ScenicTalisman.ps1')) -and (Test-Path -LiteralPath (Join-Path $d 'scenic_release.json'))){return $d}};return ''}
function Get-MMScenicComponents([string]$Game,[string]$Base,[string]$Content){
    $dir=Get-MMScenicDir $Base;$m=Read-MMJson (Join-Path $dir 'scenic_release.json');$warn=@();$blocked=$false
    $meta=Join-Path $Game 'ThreeKingdom_Data\il2cpp_data\Metadata\global-metadata.dat'
    if(!$m -or [string]$m.native_spec.section_name -ne '.scfr1' -or !$m.mod_folder -or !$m.mod_suffix){$blocked=$true;$warn+='설치 정보(scenic_release.json)를 읽지 못했습니다.'}
    elseif(!(Test-Path -LiteralPath $meta) -or (Get-MMHash $meta) -ne ([string]$m.metadata_sha256).ToUpperInvariant()){$blocked=$true;$warn+='이 게임 버전의 metadata 가 제작자가 확인한 버전과 달라 설치하지 않습니다(게임 업데이트 뒤라면 새 버전을 기다리세요).'}
    if(Test-Path -LiteralPath (Join-Path $Game 'BepInEx\plugins\ScenicTalismanRuntime')){$blocked=$true;$warn+='BepInEx 버전(4.0)이 이미 깔려 있습니다. 함께 쓰면 두 번 적용되니 그 버전의 제거.cmd 로 먼저 빼세요.'}
    $same=@(Get-MMSameRecipe $Game 'scenic');$old=$(if($same.Count -eq 1){$same[0]}else{$null});$twice=@($same | ForEach-Object {$_.Name})
    if($twice.Count -gt 1){$blocked=$true;$warn+=('이미 설치돼 있습니다: '+($twice -join ', '))}
    $others=@(Get-MMInstallRecords $Game | Where-Object {(Test-MMPatchRecord $_) -and $_.Id -notin @($same | ForEach-Object {$_.Id})} | ForEach-Object {$_.Name})
    if($others.Count -and !$blocked){$warn+=('GameAssembly.dll 을 고친 패치가 이미 있습니다('+($others -join ', ')+'). 모드매니저가 깨끗한 원본에서 '+($others -join ' → ')+' → 선부 순서로 다시 쌓습니다(1~2분).')}
    $mods=@($m.mod_files | Where-Object {[string]$_.path -notmatch '(^|[\\/])\.\.([\\/]|$)|^[\\/]|:'} | ForEach-Object {[pscustomobject]@{Target=('MODs\'+$m.mod_folder+'\'+([string]$_.path).Replace('/','\'));Sha256=([string]$_.sha256).ToUpperInvariant()}})
    return @([pscustomobject]@{Key='recipe:scenic';Kind='ScriptPatch';Flavor='scenic';Title=('명승지 이동 선부 '+$m.version+' — 선부 10종(벨핀 없이)')
        Detail=('제작자 설치기(Install-ScenicTalisman.ps1)를 모드매니저가 묻지 않고(-GamePath) 돌려 GameAssembly.dll 에 선부 코드를 붙이고 데이터 모드 MODs\'+$m.mod_folder+' 를 넣습니다. 남두·북두 선온상점에 10종(재고 5·선온 1), 대지도에서 쓰면 그 명승지로 이동. 제거하면 설치기의 복원(-Restore)이나 원본에서 다시 쌓기로 되돌립니다.')
        Suffix=[string]$m.mod_suffix;Files=@();ModFiles=$mods;Default=!$blocked;Blocked=$blocked;Replaces=$(if($old){[string]$old.Id}else{''});ReplaceName=$(if($old){[string]$old.Name}else{''});PreferFirst=$false;Warning=($warn -join ' ');Options=@();Root=$(if($dir -eq $Content){''}else{Get-MMRelative $Content $dir})})
}
# ---------- recipe: 상한해제 종합 패처 (디시 78939: one exe + loh-mod.json + Source) ----------
# A console menu (1 동행 · 2 병력 · 3 랜덤무장 능력 · 4 AI 발탁, 0 끝) that patches the GameAssembly.dll next to the exe in place (menu 4
# appends its own .aiprom section). 올인원 starts from its own kept original when it runs, so a patch laid under it is lost. So the manager
# runs it on a copy of the clean original, one menu at a time, keeps the bytes each menu changed, and writes those itself after every other
# patcher: in-place changes go in wherever their bytes are still the original. A menu that grows the file (4) on a file another patcher has
# appended to is asked of the patcher itself, on a copy of the file as it is then: V1.0.2 (11-01) places its section after the last one and
# goes in; V1.0.1 took only the original size and says no — shown as 'cannot be used together', and remembered (cap.json Stack='no') so the
# list order can make the appender give way. The exe is never left in the game folder.
$script:MMCapUnlockMenus=@(
    [pscustomobject]@{N=1;Name='동행 상한';Label='1 동행 상한 해제 — 동행 200명 · 동행·용병 합계 202명 · 무투 참가자 선택 제한 해제'},
    [pscustomobject]@{N=2;Name='병력 상한';Label='2 병력 상한 해제 — 단일 부대 병력 상한 20,000명'},
    [pscustomobject]@{N=3;Name='랜덤무장 능력';Label='3 랜덤무장 능력 상한 해제 — 선호 특성 강도 3 · 무작위 특성 0~5 · 능력치 보정 -15~+15 · 최저 5 (새로 발탁되는 무장부터)'},
    [pscustomobject]@{N=4;Name='AI 발탁';Label='4 AI 발탁 책략 사용 — AI 소속 무장이 10명 미만이면 월간 판단에서 인재 발탁을 먼저 씀'})
$script:MMSectionOwners=@{'.lohmod'='올인원';'.ccui'='창천UI';'.scfr1'='명승지 이동 선부';'.aiprom'='상한해제 4번'}
function Get-MMCapUnlockExe([string]$Base){$e=@(Get-ChildItem -LiteralPath $Base -Filter '*.exe' -File -ErrorAction SilentlyContinue | Where-Object {$_.Name -like '상한해제 종합 패처*'});if($e.Count -eq 1){return $e[0].Name};return ''}
function Get-MMCapUnlockComponents([string]$Game,[string]$Base,[string]$Content){
    $exe=Get-MMCapUnlockExe $Base;$info=Read-MMJson (Join-Path $Base 'loh-mod.json');$ver=$(if($info -and $info.version){[string]$info.version}else{([regex]::Match($exe,'V?(\d+(?:\.\d+)+)')).Groups[1].Value})
    $same=@(Get-MMSameRecipe $Game 'capunlock');$old=$(if($same.Count -eq 1){$same[0]}else{$null});$twice=@($same | ForEach-Object {$_.Name})
    $others=@(Get-MMInstallRecords $Game | Where-Object {(Test-MMPatchRecord $_) -and $_.Id -notin @($same | ForEach-Object {$_.Id})} | ForEach-Object {$_.Name})
    $oldMenus=$(if($old){@(@($old.Parts | Where-Object {$_.Flavor -eq 'capunlock'})[0].Menus)}else{@()})   # the new version starts from the numbers in use
    $warn=@();if($twice.Count -gt 1){$warn+=('이미 설치돼 있습니다: '+($twice -join ', ')+' — 고른 항목을 바꾸려면 그것을 지우고 다시 넣으세요.')}
    elseif($others.Count){$warn+=('GameAssembly.dll 을 고친 패치가 이미 있습니다('+($others -join ', ')+'). 모드매니저가 원본 사본에서 돌려 바뀐 자리를 알아낸 뒤 그 자리만 그 패치들 다음에 옮겨 쓰고, 파일 끝에 구역을 붙이는 4번은 그 패치들이 들어간 파일의 사본에서 돌립니다. 다른 패치와 같은 자리를 쓰거나 패치가 받아 주지 않는 항목은 넣지 않고 「함께 쓸 수 없음」으로 알려 줍니다.')}
    $opts=@($script:MMCapUnlockMenus | ForEach-Object {[pscustomobject]@{Key=('cap_'+$_.N);Label=$_.Label;Type='bool';Default=(!$old -or $_.N -in $oldMenus);Choices=@()}})
    return @([pscustomobject]@{Key='recipe:capunlock';Kind='ScriptPatch';Flavor='capunlock';Exe=$exe;Title=('상한해제 종합 패처 '+$ver).Trim()
        Detail='제작자 패치(exe)를 모드매니저가 깨끗한 원본의 사본에서 돌리고(고른 번호를 대신 입력), 번호마다 바뀐 자리를 기억해 두었다가 게임 파일에는 그 자리만 다른 패치 다음에 옮겨 씁니다(파일 끝에 붙이는 4번은 다른 패치가 들어간 파일의 사본에서 돌려 넣음). exe 는 게임 폴더에 남기지 않습니다. 제거하면 원본에서 다시 쌓거나 백업으로 되돌립니다.'
        Suffix='';Files=@();Default=$true;Blocked=($twice.Count -gt 1);Replaces=$(if($old){[string]$old.Id}else{''});ReplaceName=$(if($old){[string]$old.Name}else{''});PreferFirst=$false;Warning=($warn -join ' ');Options=$opts;Root=$(if($Base -eq $Content){''}else{Get-MMRelative $Content $Base})})
}
# Byte ranges where two files differ (a longer second file adds its tail as one range); gaps under 16 bytes join a range.
if(!('MMBytes' -as [type])){Add-Type -TypeDefinition @'
using System;using System.Collections.Generic;using System.IO;
public static class MMBytes {
    public static List<long[]> Diff(string a,string b){
        var list=new List<long[]>();
        using(var fa=File.OpenRead(a))using(var fb=File.OpenRead(b)){
            long common=Math.Min(fa.Length,fb.Length),pos=0,start=-1,last=-1;byte[] x=new byte[1<<22],y=new byte[1<<22];
            while(pos<common){int n=(int)Math.Min(x.Length,common-pos);Fill(fa,x,n);Fill(fb,y,n);
                for(int i=0;i<n;i++){if(x[i]!=y[i]){long at=pos+i;if(start>=0&&at-last<=16){last=at;}else{if(start>=0)list.Add(new long[]{start,last-start+1});start=at;last=at;}}}
                pos+=n;}
            if(start>=0)list.Add(new long[]{start,last-start+1});
            if(fb.Length>fa.Length)list.Add(new long[]{fa.Length,fb.Length-fa.Length});
        }
        return list;
    }
    static void Fill(Stream s,byte[] b,int n){int got=0;while(got<n){int k=s.Read(b,got,n-got);if(k<=0)throw new EndOfStreamException();got+=k;}}
    public static byte[] Read(string path,long off,int n){using(var f=File.OpenRead(path)){if(off+n>f.Length)return null;f.Position=off;var b=new byte[n];Fill(f,b,n);return b;}}
    public static void Write(string path,long off,byte[] data){using(var f=new FileStream(path,FileMode.Open,FileAccess.Write)){f.Position=off;f.Write(data,0,data.Length);}}
}
'@}
function ConvertTo-MMHex([byte[]]$b){if($null -eq $b){return ''};return [BitConverter]::ToString($b).Replace('-','')}
function ConvertFrom-MMHex([string]$h){$b=New-Object byte[] ($h.Length/2);for($i=0;$i -lt $b.Length;$i++){$b[$i]=[Convert]::ToByte($h.Substring(2*$i,2),16)};return ,$b}
# The sections a patcher appended after Unity's last one (.reloc), with who added them: what blocks a menu that grows the file.
function Test-MMPeHead([byte[]]$B,[int]$Got){
    if($Got -lt 0x40 -or $B[0] -ne 0x4D -or $B[1] -ne 0x5A){return $false}
    $pe=[BitConverter]::ToInt32($B,0x3C);return ($pe -gt 0 -and $pe+24 -le $Got)
}
# Room for more code sections: free 40-byte entries between the section table and the first section's data (-1: not a PE we read).
# The game's DLL has 5 at first; each patch that appends a section takes one (무장 검색 makes up to 6 more by moving the header up).
function Get-MMSectionRoom([string]$Path){
    if(!(Test-Path -LiteralPath $Path -PathType Leaf)){return -1}
    $fs=[IO.File]::OpenRead($Path);try{$b=New-Object byte[] 4096;$got=$fs.Read($b,0,4096)}finally{$fs.Dispose()}
    if(-not (Test-MMPeHead $b $got)){return -1}
    $pe=[BitConverter]::ToInt32($b,0x3C);$n=[BitConverter]::ToUInt16($b,$pe+6);$opt=$pe+24;$tab=$opt+[BitConverter]::ToUInt16($b,$pe+20)
    $lim=[BitConverter]::ToInt32($b,$opt+60);if($lim -le $tab){return -1}   # no header size (the tests' stand-ins)
    for($i=0;$i -lt $n -and $tab+40*$i+40 -le $got;$i++){$raw=[BitConverter]::ToInt32($b,$tab+40*$i+20);if($raw -gt 0 -and $raw -lt $lim){$lim=$raw}}
    return [Math]::Max(0,[int][Math]::Floor(($lim-($tab+40*$n))/40))
}
function Get-MMAddedSections([string]$Path){
    $fs=[IO.File]::OpenRead($Path);try{$b=New-Object byte[] 4096;$got=$fs.Read($b,0,4096)}finally{$fs.Dispose()}
    if(-not (Test-MMPeHead $b $got)){return @()}   # not a PE (the tests' stand-ins): nothing appended
    $pe=[BitConverter]::ToInt32($b,0x3C);$n=[BitConverter]::ToUInt16($b,$pe+6);$f=$pe+24+[BitConverter]::ToUInt16($b,$pe+20);$list=@();$after=$false
    for($i=0;$i -lt $n -and $f+40*$i+40 -le $got;$i++){$o=$f+40*$i;$name=[Text.Encoding]::ASCII.GetString($b,$o,8).TrimEnd([char]0)
        if($after){$list+=[pscustomobject]@{Name=$name;Owner=$(if($script:MMSectionOwners.ContainsKey($name)){$script:MMSectionOwners[$name]}else{'다른 패치'});Va=[BitConverter]::ToUInt32($b,$o+12)}}
        if($name -eq '.reloc'){$after=$true}}
    return $list
}
# Run the menu patcher: exe copied next to the GameAssembly.dll it should patch (it opens the file beside itself), one number per prompt.
function Read-MMMenuOutput($State,[int]$Ms){   # read what the patcher printed, up to Ms; true while it is still running
    $until=[DateTime]::Now.AddMilliseconds($Ms)
    while([DateTime]::Now -lt $until){
        if($State.Read.IsCompleted){$n=$State.Read.Result;if($n -le 0){return $false};[void]$State.Text.Append($State.Buffer,0,$n);$State.Read=$State.Out.ReadAsync($State.Buffer,0,$State.Buffer.Length);continue}
        Start-Sleep -Milliseconds 30
    }
    return $true
}
function Wait-MMMenuPrompt($State,[int]$Count,[int]$Ms,$Say=$null){   # Say: answers a question of the patcher's own that says what a bare Enter gives (its default)
    $until=[DateTime]::Now.AddMilliseconds($Ms)
    while([DateTime]::Now -lt $until){if(([regex]::Matches($State.Text.ToString(),'번호 입력:')).Count -ge $Count){return $true}
        if($Say){$t=$State.Text.ToString();if($t.Length -gt [int]$State.Asked -and $t -match 'Enter\s*=[^\r\n]*:\s*$'){$State.Asked=$t.Length;& $Say ''}}
        if(!(Read-MMMenuOutput $State 50)){return ([regex]::Matches($State.Text.ToString(),'번호 입력:')).Count -ge $Count}}
    return $false
}
function Invoke-MMMenuPatch([string]$Tool,[string]$Dir,[string]$Exe,[int[]]$Menus,[string]$Log){
    if(!$Menus.Count){throw '적용할 항목을 하나 이상 고르세요.'}
    $src=Join-Path $Tool $Exe;if(!$Exe -or !(Test-Path -LiteralPath $src)){throw ('패치 사본에 '+$Exe+' 이 없습니다.')}
    $run=Join-Path $Dir ('LOH_MM_패치실행_'+[guid]::NewGuid().ToString('N').Substring(0,8)+'.exe');Copy-Item -LiteralPath $src -Destination $run
    $info=[Diagnostics.ProcessStartInfo]::new($run);$info.WorkingDirectory=$Dir;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
    # .NET opens the child's input in the console's input encoding and, when that is UTF-8, writes a byte-order mark at once —
    # the patcher reads it as a wrong number (11-01 on the player's PC). A UTF-8 without the mark while starting.
    $oldIn=$null;try{$oldIn=[Console]::InputEncoding;[Console]::InputEncoding=[Text.UTF8Encoding]::new($false)}catch{}
    $p=$null;$state=$null;$problem='';$outcome=@{}
    try{
        try{$p=[Diagnostics.Process]::Start($info)}finally{if($oldIn){try{[Console]::InputEncoding=$oldIn}catch{}}}
        $null=$p.StandardError.ReadToEndAsync();$in=$p.StandardInput.BaseStream
        $state=@{Out=$p.StandardOutput;Buffer=(New-Object char[] 4096);Text=[Text.StringBuilder]::new();Read=$null};$state.Read=$state.Out.ReadAsync($state.Buffer,0,$state.Buffer.Length)
        $send={param($s)$b=[Text.Encoding]::ASCII.GetBytes($s+"`r`n");$in.Write($b,0,$b.Length);$in.Flush()}
        if(!(Wait-MMMenuPrompt $state 1 15000)){$problem='패치가 번호 입력 화면을 띄우지 않았습니다.'}
        else{
            $null=Read-MMMenuOutput $state 400   # anything it says by itself before the first number (a stray input) is not ours
            foreach($m in $Menus){
                for($try=0;$try -lt 2;$try++){
                    $prompts=([regex]::Matches($state.Text.ToString(),'번호 입력:')).Count;$mark=$state.Text.Length
                    & $send ([string]$m)
                    if(!(Wait-MMMenuPrompt $state ($prompts+1) 180000 $send)){$problem=([string]$m+'번을 입력한 뒤 패치가 답하지 않았습니다.');break}
                    $said=$state.Text.ToString().Substring($mark)
                    if($said -match '선택한 항목을 적용했습니다|이미 적용되어 있습니다'){$outcome[$m]='ok';break}
                    if($said -match '숫자를 입력하세요'){$outcome[$m]='retry';continue}   # its read took something else with our number: once more
                    $outcome[$m]=((($said -split "`r?`n") | Where-Object {$_ -match '않았습니다|실패|없습니다|다릅니다'} | Select-Object -First 1) -replace '^.*번호 입력:\s*','');break
                }
                if($problem){break}
            }
        }
        try{& $send '0';$in.Close()}catch{}
        if(!$p.WaitForExit(15000)){try{$p.Kill()}catch{}}
        $null=Read-MMMenuOutput $state 300
    }finally{
        if($p){try{if(!$p.HasExited){$p.Kill()}}catch{};$p.Dispose()}
        for($k=0;$k -lt 20 -and (Test-Path -LiteralPath $run);$k++){try{Remove-Item -LiteralPath $run -Force -ErrorAction Stop}catch{Start-Sleep -Milliseconds 250}}
    }
    $text=$(if($state){$state.Text.ToString()}else{''})
    New-Item -ItemType Directory -Path (Split-Path $Log -Parent) -Force | Out-Null;[IO.File]::AppendAllText($Log,$text+"`r`n",[Text.UTF8Encoding]::new($true))
    $bad=@($Menus | Where-Object {$outcome[$_] -ne 'ok'})
    $tail=$(if($problem){$problem}elseif($bad.Count){($bad | ForEach-Object {[string]$_+'번: '+$(if($outcome[$_] -and $outcome[$_] -ne 'retry'){$outcome[$_]}else{'적용되지 않음'})}) -join ' · '}else{'고른 '+$Menus.Count+'개 적용'})
    return [pscustomobject]@{Code=$(if(!$problem -and !$bad.Count){0}else{1});Text=$text;Tail=$tail}
}
# One run per menu on a copy of this build's clean GameAssembly.dll; the bytes each menu changed go to cap.json beside the install record.
function New-MMCapUnlockBytes([string]$Game,$Part,[string]$PkgDir){
    $pristine=Get-MMPristine $Game;$tool=Join-Path $Game $Part.Tool;$exe=[string](Read-MMJson (Join-Path $tool 'LOH_MM_menu.json')).Exe
    $work=Assert-MMPath $Game (Join-Path $Game ('LOH_ModManager\Work\cap-'+[guid]::NewGuid().ToString('N').Substring(0,8)));$menus=@()
    try{
        foreach($m in @($Part.Menus)){
            New-Item -ItemType Directory -Path $work -Force | Out-Null;$copy=Join-Path $work 'GameAssembly.dll';Copy-Item -LiteralPath $pristine -Destination $copy -Force
            $run=Invoke-MMMenuPatch $tool $work $exe @([int]$m) (Join-Path $PkgDir 'patcher.log')
            if($run.Code -ne 0){throw ('상한해제 '+$m+'번이 원본 사본에서도 적용되지 않았습니다: '+$run.Tail+' (게임이 업데이트됐다면 제작자의 새 버전이 필요합니다)')}
            $size=(Get-Item -LiteralPath $pristine).Length
            $ranges=@([MMBytes]::Diff($pristine,$copy) | ForEach-Object {$o=[long]$_[0];$n=[int]$_[1];[ordered]@{Off=$o;Old=$(if($o -lt $size){ConvertTo-MMHex ([MMBytes]::Read($pristine,$o,[int][Math]::Min($n,$size-$o)))}else{''});New=(ConvertTo-MMHex ([MMBytes]::Read($copy,$o,$n)))}})
            $menus+=[ordered]@{Menu=[int]$m;Grows=((Get-Item -LiteralPath $copy).Length -gt $size);Ranges=$ranges}
            Remove-Item -LiteralPath $copy -Force
        }
    }finally{Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue}
    $data=[ordered]@{Format='LOH_ModManager.CapUnlock 1';Build=(Get-MMGameBuild $Game);Pristine=(Get-MMHash $pristine);Size=(Get-Item -LiteralPath $pristine).Length;Menus=$menus}
    Write-MMJson (Join-Path $PkgDir 'cap.json') $data;return $data
}
# One menu run on a copy of GameAssembly.dll as it is now (other patchers in); the copy replaces the file only when the patcher took it.
function Invoke-MMCapUnlockOnFile([string]$Game,$Part,[string]$PkgDir,[int]$Menu){
    $dll=Join-Path $Game 'GameAssembly.dll';$tool=Join-Path $Game $Part.Tool;$exe=[string](Read-MMJson (Join-Path $tool 'LOH_MM_menu.json')).Exe
    $work=Assert-MMPath $Game (Join-Path $Game ('LOH_ModManager\Work\cap-'+[guid]::NewGuid().ToString('N').Substring(0,8)))
    try{
        New-Item -ItemType Directory -Path $work -Force | Out-Null;$copy=Join-Path $work 'GameAssembly.dll';Copy-Item -LiteralPath $dll -Destination $copy -Force
        $run=Invoke-MMMenuPatch $tool $work $exe @($Menu) (Join-Path $PkgDir 'patcher.log')
        if($run.Code -eq 0 -and (Get-MMHash $copy) -ne (Get-MMHash $dll)){Copy-Item -LiteralPath $copy -Destination $dll -Force}
        return $run
    }finally{Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue}
}
# Write the kept bytes into GameAssembly.dll as it is now (after the other patchers). Returns the menus put in and the ones that cannot be,
# each with the reason. A menu goes in whole or not at all.
function Set-MMCapUnlockBytes([string]$Game,$Part,[string]$PkgDir,$Skip=@{}){   # Skip: number → why it gives way to a patcher lower in the list
    $dll=Join-Path $Game 'GameAssembly.dll';$data=Read-MMJson (Join-Path $PkgDir 'cap.json');$pristine=Get-MMPristine $Game
    $want=@($Part.Menus | ForEach-Object {[int]$_});$have=@($(if($data){$data.Menus | ForEach-Object {[int]$_.Menu}}))
    if(!$data -or [string]$data.Build -ne (Get-MMGameBuild $Game) -or [string]$data.Pristine -ne (Get-MMHash $pristine) -or @($want | Where-Object {$_ -notin $have}).Count){$data=New-MMCapUnlockBytes $Game $Part $PkgDir}   # a new build or a menu turned on later: ask the patcher again
    $size=(Get-Item -LiteralPath $dll).Length;$applied=@();$blocked=@();$names=@{};foreach($x in $script:MMCapUnlockMenus){$names[$x.N]=$x.Name}
    foreach($menu in @($data.Menus | Where-Object {[int]$_.Menu -in $want} | Sort-Object {[int]$_.Menu})){
        $m=[int]$menu.Menu;$label=[string]$m+'번('+$names[$m]+')'
        if($Skip -and $Skip.ContainsKey($m)){$blocked+=[ordered]@{Menu=$m;Why=$(if($Skip[$m]){[string]$Skip[$m]}else{$label+': 목록에서 아래인 패치가 우선이라 자리를 내줬습니다.'})};continue}
        $already=@($menu.Ranges | Where-Object {$n=([string]$_.New).Length/2;$cur=[MMBytes]::Read($dll,[long]$_.Off,$n);!($cur -and (ConvertTo-MMHex $cur) -eq [string]$_.New)}).Count -eq 0
        if($already){$applied+=$m;continue}
        if([bool]$menu.Grows -and $size -ne [long]$data.Size){   # another patcher appended: the patcher itself, on a copy of the file as it is now
            $run=Invoke-MMCapUnlockOnFile $Game $Part $PkgDir $m;$stack=$(if($run.Code -eq 0){'ok'}else{'no'})
            if([string]$data.Stack -ne $stack){if($data -is [Collections.IDictionary]){$data['Stack']=$stack}else{$data | Add-Member -NotePropertyName Stack -NotePropertyValue $stack -Force};Write-MMJson (Join-Path $PkgDir 'cap.json') $data}
            if($run.Code -eq 0){$size=(Get-Item -LiteralPath $dll).Length;$applied+=$m;continue}
            $added=@(Get-MMAddedSections $dll)
            $who=$(if($added.Count){'그 자리(주소 0x'+('{0:X}' -f [long]$added[0].Va)+')를 '+(($added | ForEach-Object {$_.Owner+'('+$_.Name+')'}) -join ', ')+' 이(가) 이미 씁니다'}else{'다른 패치가 파일 끝에 이미 무언가 붙여 놓았습니다'})
            $blocked+=[ordered]@{Menu=$m;Why=($label+': 원본 파일 끝에 코드 구역을 붙이는데, '+$who+'. 이 버전의 패치는 다른 패치가 붙인 파일을 받아 주지 않아 함께 쓸 수 없습니다(패치: '+$run.Tail+').')};continue
        }
        $todo=@();$why=''
        foreach($r in @($menu.Ranges)){
            $new=ConvertFrom-MMHex ([string]$r.New);$cur=[MMBytes]::Read($dll,[long]$r.Off,$new.Length)
            if($cur -and (ConvertTo-MMHex $cur) -eq [string]$r.New){continue}   # already in
            $old=[string]$r.Old
            if($old -and $cur -and (ConvertTo-MMHex ($cur[0..($old.Length/2-1)])) -eq $old){$todo+=$r;continue}
            if(!$old -and [long]$r.Off -eq $size){$todo+=$r;continue}         # the tail it appends, at the end of the original-size file
            $why=($label+': 고치는 자리(파일 위치 0x'+('{0:X}' -f [long]$r.Off)+')를 다른 패치가 이미 바꿔 놓아 함께 쓸 수 없습니다.');break
        }
        if($why){$blocked+=[ordered]@{Menu=$m;Why=$why};continue}
        foreach($r in $todo){[MMBytes]::Write($dll,[long]$r.Off,(ConvertFrom-MMHex ([string]$r.New)))}
        $size=(Get-Item -LiteralPath $dll).Length;$applied+=$m
    }
    return [pscustomobject]@{Applied=$applied;Blocked=$blocked}
}
function Set-MMCapUnlockResult($Part,$Result){
    foreach($k in 'Applied','Blocked'){if($Part -is [Collections.IDictionary]){$Part[$k]=@($Result.$k)}else{$Part | Add-Member -NotePropertyName $k -NotePropertyValue @($Result.$k) -Force}}
}
function Get-MMCapUnlockTail($Result){
    $names=@{};foreach($x in $script:MMCapUnlockMenus){$names[$x.N]=$x.Name}
    $t=$(if(@($Result.Applied).Count){(@($Result.Applied | ForEach-Object {[string]$_+'번'}) -join '·')+' 적용'}else{'적용된 항목 없음'})
    if(@($Result.Blocked).Count){$t+=' · 함께 쓸 수 없음: '+(@($Result.Blocked | ForEach-Object {$_.Why}) -join ' / ')}
    return $t
}
# Turn 상한해제's menus on or off one by one. GameAssembly.dll goes back to how it was just before 상한해제 (the copy beside its record,
# taken at install or the last rebuild) when nothing changed the file since, then the chosen menus are written — seconds, no rebuild.
# If something else changed the file in between, the whole stack is rebuilt instead (상한해제 last, as always).
function Set-MMCapUnlockMenus([string]$Game,[string]$Id,[int[]]$Menus,[string[]]$Order=@()){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Assert-MMStopped $Game
    $Menus=@($Menus | Where-Object {$_ -in 1..4} | Sort-Object -Unique);if(!$Menus.Count){throw '하나 이상은 켜 두어야 합니다. 모두 빼려면 설치 정보 탭에서 제거하세요.'}
    Save-MMPatchOrderFrom $Game $Order
    $record=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -eq $Id})[0];if(!$record){throw '설치 기록을 찾지 못했습니다.'}
    $part=@($record.Parts | Where-Object {$_.Flavor -eq 'capunlock'})[0];if(!$part){throw '상한해제 설치가 아닙니다.'}
    $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $Id;$dll=Join-Path $Game 'GameAssembly.dll';$before=Join-Path $pkgDir 'backup\GameAssembly.dll';$f=@($part.Files)[0]
    $part.Menus=$Menus;$part.Summary=(@($script:MMCapUnlockMenus | Where-Object {$_.N -in $Menus} | ForEach-Object {$_.Name}) -join ' · ')
    Save-MMInstallRecord $Game $record
    # the new choice (or the order sent with it) may change who gives way: its own numbers stay quick, see Resolve-MMYieldChange
    $res0=Resolve-MMYieldChange $Game $Id
    if(!$res0.Rebuild -and (Test-Path -LiteralPath $before) -and $f.Sha256 -and (Get-MMHash $dll) -eq ([string]$f.Sha256).ToUpperInvariant()){
        if($Id -in @($res0.Diff.Ids)){$record | Add-Member -NotePropertyName Yield -NotePropertyValue (ConvertTo-MMYield $res0.Diff.Want[$Id]) -Force}
        Save-MMYieldFree $Game $res0;$freed=@($res0.Freed)
        $skip=@{};if($record.Yield){foreach($m in @($record.Yield.Menus)){$skip[[int]$m]=@($record.Yield.Why | Where-Object {$_ -like ([string]$m+'번*')})[0]}}
        Copy-Item -LiteralPath $before -Destination $dll -Force
        $res=Set-MMCapUnlockBytes $Game $part $pkgDir $skip;Set-MMCapUnlockResult $part $res;$f.Sha256=Get-MMHash $dll
        Save-MMInstallRecord $Game $record;Save-MMPatchOrder $Game @(Get-MMPatchOrder $Game)
        $links=@(foreach($s in (Update-MMTreasuryLinks $Game).Steps){$s.Name+': '+$s.Tail})   # GameAssembly.dll changed: 세력금고 reads its functions from it
        return [pscustomobject]@{Rebuilt=$false;Tail=((Get-MMCapUnlockTail $res)+$(if($links.Count){' · '+($links -join ' · ')}));Blocked=@($res.Blocked | ForEach-Object {$_.Why});Freed=@($freed | ForEach-Object {$_.Name});Links=$links}
    }
    Save-MMInstallRecord $Game $record   # the new choice first: the rebuild reads it from the record
    $built=Invoke-MMPatchRebuild $Game
    $p2=@(@(Get-MMInstallRecords $Game | Where-Object {$_.Id -eq $Id})[0].Parts | Where-Object {$_.Flavor -eq 'capunlock'})[0]
    return [pscustomobject]@{Rebuilt=$true;Tail=(($built.Steps | ForEach-Object {$_.Name+$(if($_.Ok){' ✓'}else{' ✗ '+$_.Tail})}) -join ' → ');Blocked=@($p2.Blocked | Where-Object {$_} | ForEach-Object {$_.Why})}
}
# After a patcher that rewrites GameAssembly.dll from its own copy (올인원 install or restore): the kept bytes go back on top.
function Invoke-MMReapplyLast([string]$Game,[string]$Skip=''){
    $notes=@()
    foreach($r in @(Get-MMInstallRecords $Game | Where-Object {$_.Id -ne $Skip -and @($_.Parts | Where-Object {$_.Flavor -eq 'capunlock'}).Count})){
        $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $r.Id
        foreach($p in @($r.Parts | Where-Object {$_.Flavor -eq 'capunlock'})){try{$res=Set-MMCapUnlockBytes $Game $p $pkgDir;Set-MMCapUnlockResult $p $res;@($p.Files)[0].Sha256=Get-MMHash (Join-Path $Game 'GameAssembly.dll');$notes+=($r.Name+': '+(Get-MMCapUnlockTail $res))}catch{$notes+=($r.Name+': '+$_.Exception.Message)}}
        Save-MMInstallRecord $Game $r
    }
    if($notes.Count){Save-MMPatchOrder $Game @(Get-MMPatchOrder $Game)}
    return $notes
}
# Script patches (창천UI): the maker's own PowerShell patcher, run with the game folder and -Yes. Its installer has no undo,
# so GameAssembly.dll is backed up into the install record first; removal puts that copy back when nothing patched it since.
# The patcher's own switches for a part: 무장 검색 takes a mode and its two feature answers; 창천UI only -SortOnly.
# ---------- 가이드 방식 패치 (any patch in the shape the 모드패치 궁극 가이드 ch.20 lays out, 11-03) ----------
# A .bat that only starts  powershell … -File "%~dp0<x>.ps1" <its own numbers> %*  and a .ps1 that takes -GameDir (stops when that is no
# game folder) and -Yes (asks nothing). The manager runs the .ps1 with the install .bat's numbers plus -GameDir <게임 폴더> -Yes; a .bat
# named 원래대로/복구/… gives the numbers that undo it. Such a patch checks the bytes it changes itself and stops on a state it does not
# know (가이드 20-4), so it is stacked by the list order like the others and never made to give way by a byte overlap with one above.
function Read-MMGuideText([string]$Path){
    $b=[IO.File]::ReadAllBytes($Path);$t=[Text.UTF8Encoding]::new($false,$false).GetString($b)
    if($t.Contains([char]0xFFFD)){$t=[Text.Encoding]::GetEncoding(949).GetString($b)};return $t.TrimStart([char]0xFEFF)
}
function Get-MMGuidePatch([string]$Base){
    foreach($dir in @($Base)+@(Get-ChildItem -LiteralPath $Base -Directory -ErrorAction SilentlyContinue | ForEach-Object {$_.FullName})){
        $found=@()
        foreach($bat in @(Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue | Where-Object {$_.Extension -in @('.bat','.cmd')} | Sort-Object Name)){
            $m=[regex]::Match((Read-MMGuideText $bat.FullName),'(?im)^[\s@]*(?:call\s+)?powershell(?:\.exe)?\b[^\r\n]*?-File\s+"%~dp0([^"\r\n]+?\.ps1)"([^\r\n]*)\r?$')
            if(!$m.Success){continue}
            $rel=$m.Groups[1].Value.TrimStart('\','/');if($rel -match '\.\.|:'){continue}
            $tokens=@([regex]::Matches(($m.Groups[2].Value -replace '%\*',''),'"[^"]*"|\S+') | ForEach-Object {$_.Value.Trim('"')} | Where-Object {$_})
            if(@($tokens | Where-Object {$_ -match '%|^-(GameDir|Yes)$'}).Count){continue}   # variables of its own, or our two switches already
            $found+=[pscustomobject]@{Name=$bat.Name;Script=$rel;Args=$tokens;Undo=($bat.BaseName -match '원래대로|복구|되돌리|제거|삭제|(?i)restore|uninstall|remove')}
        }
        $scripts=@($found | ForEach-Object {$_.Script} | Select-Object -Unique)
        if($scripts.Count -ne 1){continue}   # none, or two patch scripts in one folder: not one patch in that shape
        $ps=Join-Path $dir $scripts[0];if(!(Test-Path -LiteralPath $ps -PathType Leaf)){continue}
        $text=Read-MMGuideText $ps
        if($text -notmatch '(?i)\[string\]\s*\$GameDir\b' -or $text -notmatch '(?i)\[switch\]\s*\$Yes\b'){continue}
        $apply=@($found | Where-Object {!$_.Undo});if(!$apply.Count){continue}
        return [pscustomobject]@{Dir=$dir;Script=$scripts[0];Applies=$apply;Restore=@(@($found | Where-Object {$_.Undo})[0].Args | Where-Object {$_})}
    }
    return $null
}
# ---------- 양식 'patch' (any patch that follows it): loh-mod.json {"patch":{"run":"x.exe","items":[{"id","name","note"}]}} ----------
# The manager runs  <run> --game "<게임 폴더>" --items 1,2  to apply and  <run> --game "<게임 폴더>" --restore  to undo; exit 0 = done.
function Get-MMPromiseManifest([string]$Base){
    foreach($dir in @($Base)+@(Get-ChildItem -LiteralPath $Base -Directory -ErrorAction SilentlyContinue | ForEach-Object {$_.FullName})){
        $f=Join-Path $dir 'loh-mod.json';if(!(Test-Path -LiteralPath $f)){continue}
        try{$info=Read-MMJson $f}catch{continue}
        if(!$info -or !$info.patch -or !$info.patch.run){continue}
        $run=[string]$info.patch.run;if($run -match '\.\.|^[\\/]|:'){continue}   # inside the mod's own folder only
        if(!(Test-Path -LiteralPath (Join-Path $dir $run) -PathType Leaf) -or [IO.Path]::GetExtension($run).ToLowerInvariant() -notin @('.exe','.bat','.cmd','.ps1')){continue}
        $items=@($info.patch.items | Where-Object {$_ -and $_.id} | ForEach-Object {[pscustomobject]@{Id=[string]$_.id;Name=$(if($_.name){[string]$_.name}else{[string]$_.id});Note=[string]$_.note}})
        return [pscustomobject]@{Dir=$dir;Info=$info;Run=$run;Items=$items}
    }
    return $null
}
function Invoke-MMPromiseRun([string]$Tool,[string]$Game,[string]$Run,[string[]]$Extra,[string]$Log){
    $file=Join-Path $Tool $Run;$ext=[IO.Path]::GetExtension($file).ToLowerInvariant()
    $args1='--game "'+$Game.TrimEnd('\')+'"'+$(if($Extra.Count){' '+(($Extra | ForEach-Object {if($_ -match '[\s"]'){'"'+$_.Replace('"','\"')+'"'}else{$_}}) -join ' ')})
    if($ext -eq '.ps1'){$exe=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe';$arguments='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$file+'" '+$args1}
    elseif($ext -in @('.bat','.cmd')){$exe=$env:ComSpec;$arguments='/d /c ""'+$file+'" '+$args1+'"'}
    else{$exe=$file;$arguments=$args1}
    $info=[Diagnostics.ProcessStartInfo]::new($exe,$arguments);$info.WorkingDirectory=$Tool;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.RedirectStandardInput=$true;$info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8
    $p=[Diagnostics.Process]::Start($info)
    try{$err=$p.StandardError.ReadToEndAsync();$p.StandardInput.Close();$out=$p.StandardOutput.ReadToEndAsync()
        if(!$p.WaitForExit(900000)){try{$p.Kill()}catch{};throw '패치가 15분 안에 끝나지 않아 멈췄습니다.'}
        $text=$out.Result+$(if($err.Result){"`r`n[stderr]`r`n"+$err.Result});$code=$p.ExitCode}finally{$p.Dispose()}
    New-Item -ItemType Directory -Path (Split-Path $Log -Parent) -Force | Out-Null;[IO.File]::AppendAllText($Log,$text+"`r`n",[Text.UTF8Encoding]::new($true))
    return [pscustomobject]@{Code=$code;Text=$text;Tail=$(if($code -ne 0 -and !$text.Trim()){'종료 코드 '+$code}else{(($text -split "`r?`n") | Where-Object {$_.Trim()} | Select-Object -Last 1)})}
}
# Files a patch changed besides GameAssembly.dll (that one is the patch stack's): before/after lists of the game folder.
function Get-MMPromiseChanges($Before,$After){
    $changed=@();$added=@();foreach($k in $After.Keys){if($k -eq 'GameAssembly.dll'){continue};if(!$Before.ContainsKey($k)){$added+=$k}elseif($Before[$k][0] -ne $After[$k][0] -or $Before[$k][1] -ne $After[$k][1]){$changed+=$k}}
    return [pscustomobject]@{Changed=@($changed | Sort-Object);Added=@($added | Sort-Object)}
}
# Put the files it changed back to the copies kept at install and take away the ones it added (before it runs again on a fresh stack).
function Reset-MMPromiseFiles([string]$Game,$Part,[string]$PkgDir){
    foreach($e in @($Part.Files | Where-Object {$_.Target -ne 'GameAssembly.dll'})){$dst=Join-Path $Game $e.Target
        if($e.Backup){$src=Join-Path $PkgDir ('backup\'+$e.Backup);if(Test-Path -LiteralPath $src){New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Copy-Item -LiteralPath $src -Destination $dst -Force}}
        elseif(!$e.NoBackup -and (Test-Path -LiteralPath $dst -PathType Leaf)){Remove-Item -LiteralPath $dst -Force}}
}
function Update-MMPromiseHashes([string]$Game,$Part){foreach($e in @($Part.Files | Where-Object {$_.Target -ne 'GameAssembly.dll'})){$dst=Join-Path $Game $e.Target;if(Test-Path -LiteralPath $dst -PathType Leaf){$e.Sha256=Get-MMHash $dst}}}
# Turn items of such a patch on or off: the record takes the new choice, then the stack is rebuilt (its files reset, it runs again).
function Set-MMPromiseItems([string]$Game,[string]$Id,[string[]]$Items,[string[]]$Order=@()){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Assert-MMStopped $Game
    $record=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -eq $Id})[0];if(!$record){throw '설치 기록을 찾지 못했습니다.'}
    $part=@($record.Parts | Where-Object {$_.Flavor -eq 'promise'})[0];if(!$part){throw '모드 정보 파일로 설치한 패치가 아닙니다.'}
    $all=@($part.All | ForEach-Object {[string]$_.Id});$Items=@($Items | Where-Object {$_ -in $all});if($all.Count -and !$Items.Count){throw '하나 이상은 켜 두어야 합니다. 모두 빼려면 카드를 끔으로 옮기세요.'}
    Save-MMPatchOrderFrom $Game $Order
    $part.Items=$Items;$part.Summary=$(if($all.Count){(@($part.All | Where-Object {[string]$_.Id -in $Items} | ForEach-Object {$_.Name}) -join ' · ')}else{'전체'});Save-MMInstallRecord $Game $record
    $built=Invoke-MMPatchRebuild $Game
    $mine=@($built.Steps | Where-Object {$_.Id -eq $Id})[0]
    return [pscustomobject]@{Ok=[bool]($mine -and $mine.Ok);Tail=$(if($mine){$mine.Tail}else{''});Steps=$built.Steps;Gave=$built.Gave}
}
function Get-MMScriptPatchArgs($Part,[string]$Mode='apply'){
    if($Part.Flavor -eq 'promise'){return $(if($Mode -eq 'restore'){@('--restore')}elseif(@($Part.All).Count){@('--items',(@($Part.Items) -join ','))}else{@()})}
    if($Part.Flavor -eq 'guide'){return $(if($Mode -eq 'restore'){@($Part.Restore | Where-Object {$_})}else{@($Part.Apply | Where-Object {$_})})}
    if($Part.Flavor -eq 'capunlock'){return $(if($Mode -eq 'apply'){@($Part.Menus | ForEach-Object {[string]$_})}else{@()})}   # the menu numbers; it has no restore
    if($Part.Flavor -eq 'scenic'){return $(if($Mode -eq 'restore'){@('-Restore')}else{@()})}
    if($Part.Flavor -ne 'kosearch'){return @()}
    $a=@('-Mode',$Mode);if($Mode -eq 'apply'){$a+=$(if($Part.Button){'-WithButton'}else{'-WithoutButton'});$a+=$(if($Part.Stimulus){'-WithStimulus'}else{'-WithoutStimulus'})};return $a
}
function Invoke-MMScriptPatch([string]$Tool,[string]$Game,[bool]$SortOnly,[string]$Log,[string[]]$Extra=@()) {
    $promise=Read-MMJson (Join-Path $Tool 'LOH_MM_promise.json');if($promise -and $promise.Run){return Invoke-MMPromiseRun $Tool $Game ([string]$promise.Run) $Extra $Log}
    $menu=Read-MMJson (Join-Path $Tool 'LOH_MM_menu.json');if($menu -and $menu.Exe){return Invoke-MMMenuPatch $Tool $Game ([string]$menu.Exe) @($Extra | Where-Object {$_ -match '^\d$'} | ForEach-Object {[int]$_}) $Log}
    $script=Join-Path $Tool '스크립트\패처.ps1';$scenic=Join-Path $Tool 'Install-ScenicTalisman.ps1';$guide=Read-MMJson (Join-Path $Tool 'LOH_MM_guide.json')
    if($guide -and $guide.Script){$script=Join-Path $Tool ([string]$guide.Script);$Extra=@($Extra | ForEach-Object {if($_ -match '\s'){'"'+$_+'"'}else{$_}})}
    $target=if($guide -and $guide.Script){'-GameDir "'+$Game.TrimEnd('\')+'" -Yes'}elseif(Test-Path -LiteralPath $script){'-GameDir "'+$Game.TrimEnd('\')+'" -Yes'+$(if($SortOnly){' -SortOnly'})}elseif(Test-Path -LiteralPath $scenic){$script=$scenic;'-GamePath "'+$Game.TrimEnd('\')+'"'}else{throw '패치 스크립트(스크립트\패처.ps1)가 없습니다.'}
    $ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$script+'" '+$target+$(if($Extra.Count){' '+($Extra -join ' ')})
    $info=[Diagnostics.ProcessStartInfo]::new($ps,$arguments);$info.WorkingDirectory=$Tool;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.RedirectStandardInput=$true;$info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8
    $p=[Diagnostics.Process]::Start($info)
    try{$err=$p.StandardError.ReadToEndAsync();$p.StandardInput.Close();$out=$p.StandardOutput.ReadToEndAsync()
        if(!$p.WaitForExit(900000)){try{$p.Kill()}catch{};throw '패치 스크립트가 15분 안에 끝나지 않아 멈췄습니다.'}
        $text=$out.Result+$(if($err.Result){"`r`n[stderr]`r`n"+$err.Result});$code=$p.ExitCode}finally{$p.Dispose()}
    New-Item -ItemType Directory -Path (Split-Path $Log -Parent) -Force | Out-Null;[IO.File]::AppendAllText($Log,$text,[Text.UTF8Encoding]::new($true))
    return [pscustomobject]@{Code=$code;Text=$text;Tail=((($text -split "`r?`n") | Where-Object {$_.Trim()} | Select-Object -Last 3) -join ' ')}
}
function Install-MMScriptPatch([string]$Game,[string]$Name,$Component,[string]$Content,[string]$Id,$Options,[string]$Backup,$Active,[bool]$Stacked=$false) {
    function Opt($k,$default){if($null -eq $Options){return $default};$v=if($Options -is [hashtable]){$Options[$k]}else{$p=$Options.PSObject.Properties[$k];if($p){$p.Value}else{$null}};if($null -eq $v){return $default};return [bool]$v}
    $kosearch=$Component.Flavor -eq 'kosearch';$scenic=$Component.Flavor -eq 'scenic';$cap=$Component.Flavor -eq 'capunlock';$promise=$Component.Flavor -eq 'promise';$sortOnly=(!$kosearch -and !$scenic -and !$cap -and !$promise) -and (Opt 'ccui_sortonly' $false)
    $part=[ordered]@{Key=$Component.Key;Kind='ScriptPatch';Title=$Component.Title;Suffix='';ExternalId='';Tool='';SortOnly=$sortOnly;PreferFirst=[bool]$Component.PreferFirst;Summary=$(if($sortOnly){'정렬만 허용'}else{'전체'})}
    if($kosearch){$part.Flavor='kosearch';$part.Button=(Opt 'kosearch_button' $true);$part.Stimulus=(Opt 'kosearch_stimulus' $false)
        $part.Summary=(@('초성·여성 검색')+$(if($part.Button){@('여성무장 버튼')})+$(if($part.Stimulus){@('관계 중첩 표기')})) -join ' · '}
    # 선부 also puts its data mod in MODs (fixed files, checked by its installer): listed after GameAssembly.dll so removal takes them too
    $modEntries=@();if($scenic){$part.Flavor='scenic';$part.Suffix=$Component.Suffix;$part.Summary='선부 10종 · 선온상점 · 대지도 이동'
        $modEntries=@($Component.ModFiles | ForEach-Object {[ordered]@{Target=$_.Target;Sha256=$_.Sha256;Backup=$null;Previous=''}})}
    if($cap){$part.Flavor='capunlock';$part.Last=$true   # kept bytes written after every other patcher (올인원 rewrites from its own copy)
        $part.Menus=@($script:MMCapUnlockMenus | Where-Object {Opt ('cap_'+$_.N) $true} | ForEach-Object {$_.N});if(!$part.Menus.Count){throw '상한해제 패치에서 적용할 항목을 하나 이상 고르세요.'}
        $part.Summary=(@($script:MMCapUnlockMenus | Where-Object {$_.N -in $part.Menus} | ForEach-Object {$_.Name}) -join ' · ')}
    if($promise){$part.Flavor='promise';$part.Run=[string]$Component.Run;$part.All=@($Component.Items | ForEach-Object {[ordered]@{Id=[string]$_.Id;Name=[string]$_.Name;Note=[string]$_.Note}})
        $part.Items=@($part.All | Where-Object {Opt ('item_'+$_.Id) $true} | ForEach-Object {$_.Id});if($part.All.Count -and !$part.Items.Count){throw '적용할 항목을 하나 이상 고르세요.'}
        $part.Summary=$(if($part.All.Count){(@($part.All | Where-Object {$_.Id -in $part.Items} | ForEach-Object {$_.Name}) -join ' · ')}else{'전체'})}
    $guide=$Component.Flavor -eq 'guide'
    if($guide){$pick=0;if(@($Component.Applies).Count -gt 1 -and $null -ne $Options){$v=if($Options -is [hashtable]){$Options['guide_apply']}else{$Options.PSObject.Properties['guide_apply'].Value};if("$v" -match '^\d+$' -and [int]"$v" -lt @($Component.Applies).Count){$pick=[int]"$v"}}
        $a=@($Component.Applies)[$pick];$part.Flavor='guide';$part.Script=[string]$Component.Script;$part.Apply=@($a.Args);$part.Restore=@($Component.Restore);$part.Bat=[string]$a.Name;$part.Summary=[string]$a.Name}
    $toolRel='LOH_ModManager\Tools\'+(Get-MMSafeName $Name)+'-'+$Id.Substring(0,8);$tool=Assert-MMPath $Game (Join-Path $Game $toolRel);$part.Tool=$toolRel
    $src=if($Component.Root){Join-Path $Content $Component.Root}else{$Content}
    New-Item -ItemType Directory -Path (Split-Path $tool -Parent) -Force | Out-Null;try{Copy-Item -LiteralPath $src -Destination $tool -Recurse}catch{Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue;throw}   # e.g. Windows security stopped a file: no half copy left
    if($guide){
        Write-MMJson (Join-Path $tool 'LOH_MM_guide.json') ([ordered]@{Script=$part.Script})
        # 자리 찾기 (the player said yes after it did not go in at the bottom): Install-MMPackage tries places by rebuilding from the original
        if((Opt 'guide_place' $false)){$part.Place=$true;$part.Files=@([ordered]@{Target='GameAssembly.dll';Sha256='';Backup='GameAssembly.dll';Previous=''});$part.Note='';return $part}
    }
    if($promise -or $guide){
        # run it on the game as it is (other patches stay), note every file it changed; for a file it changed (not added) the original
        # comes from its own undo (--restore, or a 가이드 patch's 원래대로 numbers), copied into the record, then it runs again — so removal
        # and item changes need nothing of its own
        if($promise){Write-MMJson (Join-Path $tool 'LOH_MM_promise.json') ([ordered]@{Run=$part.Run})}
        $undoArgs=$(if($promise){@('--restore')}else{@($part.Restore | Where-Object {$_})})
        $dll=Join-Path $Game 'GameAssembly.dll';New-Item -ItemType Directory -Path $Backup -Force | Out-Null;Copy-Item -LiteralPath $dll -Destination (Join-Path $Backup 'GameAssembly.dll') -Force
        $previous='';foreach($r in $Active){foreach($p in $r.Parts){foreach($e in $p.Files){if($e.Target -eq 'GameAssembly.dll'){$previous=$r.Id}}}}
        $log=Join-Path (Get-MMInstalledRoot $Game) ($Id+'\patcher.log');$pname=$(if($Component.ModName){[string]$Component.ModName}else{$Name})
        $fail={param($why)if($undoArgs.Count){try{$null=Invoke-MMScriptPatch $tool $Game $false $log $undoArgs}catch{}};Copy-Item -LiteralPath (Join-Path $Backup 'GameAssembly.dll') -Destination $dll -Force
            Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue
            if($guide){throw ("'"+$pname+"' 패치를 넣지 못했습니다. 패치가 남긴 말: "+$why+'. 게임 파일은 설치 전 상태 그대로입니다.')}
            throw ($pname+' 패치가 실패했습니다: '+$why+'. 게임 파일은 실행 전 상태로 되돌렸습니다.')}
        $before=Get-MMWatchList $Game;$clock=[Diagnostics.Stopwatch]::StartNew()
        $run=Invoke-MMScriptPatch $tool $Game $false $log (Get-MMScriptPatchArgs $part 'apply')
        if($run.Code -ne 0 -and $guide){
            # not at the bottom (on the game as it is): when there are patches it could go above, the window explains and asks before the
            # tries (each one a rebuild); the stage stays for that second call
            $movable=@($Active | Where-Object {(Test-MMPatchRecord $_) -and !@($_.Parts | Where-Object {$_.Last}).Count})
            if($movable.Count){$x=[Exception]::new('MM_GUIDE_PLACE');$x.Data['Tail']=[string]$run.Tail;$x.Data['Seconds']=[int][Math]::Ceiling($clock.Elapsed.TotalSeconds);$x.Data['Name']=$pname
                Copy-Item -LiteralPath (Join-Path $Backup 'GameAssembly.dll') -Destination $dll -Force;Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue;throw $x}
        }
        if($run.Code -ne 0){& $fail $run.Tail}
        $part.Seconds=[int][Math]::Ceiling($clock.Elapsed.TotalSeconds)
        $ch=Get-MMPromiseChanges $before (Get-MMWatchList $Game);$kept=@{}
        if($ch.Changed.Count -and $undoArgs.Count){
            $undo=Invoke-MMScriptPatch $tool $Game $false $log $undoArgs;$now=Get-MMWatchList $Game
            foreach($k in $ch.Changed){if($undo.Code -eq 0 -and $now.ContainsKey($k) -and $now[$k][0] -eq $before[$k][0]){$to=Join-Path $Backup $k;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $Game $k) -Destination $to -Force;$kept[$k]=$true}}
            $run=Invoke-MMScriptPatch $tool $Game $false $log (Get-MMScriptPatchArgs $part 'apply');if($run.Code -ne 0){& $fail $run.Tail}
        }
        $part.Files=@([ordered]@{Target='GameAssembly.dll';Sha256=(Get-MMHash $dll);Backup='GameAssembly.dll';Previous=$previous})
        foreach($k in $ch.Changed){$part.Files+=[ordered]@{Target=$k;Sha256=(Get-MMHash (Join-Path $Game $k));Backup=$(if($kept[$k]){$k}else{$null});NoBackup=!$kept[$k];Previous=''}}
        foreach($k in $ch.Added){if(Test-Path -LiteralPath (Join-Path $Game $k) -PathType Leaf){$part.Files+=[ordered]@{Target=$k;Sha256=(Get-MMHash (Join-Path $Game $k));Backup=$null;Previous=''}}}
        $part.Note=($pname+': '+$run.Tail+$(if(@($ch.Changed | Where-Object {!$kept[$_]}).Count){' · 원본을 받아 두지 못한 파일 '+@($ch.Changed | Where-Object {!$kept[$_]}).Count+'개(지울 때 그대로 둠)'}))
        return $part
    }
    if($cap){
        Write-MMJson (Join-Path $tool 'LOH_MM_menu.json') ([ordered]@{Exe=[string]$Component.Exe})
        # straight onto the file as it is (no rebuild): its bytes come from a clean copy, so the other patchers stay as they are
        $dll=Join-Path $Game 'GameAssembly.dll';New-Item -ItemType Directory -Path $Backup -Force | Out-Null;Copy-Item -LiteralPath $dll -Destination (Join-Path $Backup 'GameAssembly.dll') -Force
        $previous='';foreach($r in $Active){foreach($p in $r.Parts){foreach($e in $p.Files){if($e.Target -eq 'GameAssembly.dll'){$previous=$r.Id}}}}
        try{$res=Set-MMCapUnlockBytes $Game $part (Join-Path (Get-MMInstalledRoot $Game) $Id)}
        catch{Copy-Item -LiteralPath (Join-Path $Backup 'GameAssembly.dll') -Destination $dll -Force;Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue;throw}
        Set-MMCapUnlockResult $part $res
        $part.Files=@([ordered]@{Target='GameAssembly.dll';Sha256=(Get-MMHash $dll);Backup='GameAssembly.dll';Previous=$previous});$part.Note=('상한해제: '+(Get-MMCapUnlockTail $res))
        return $part
    }
    if($Stacked){$part.Files=@([ordered]@{Target='GameAssembly.dll';Sha256='';Backup='GameAssembly.dll';Previous=''})+$modEntries;$part.Note='';return $part}
    $dll=Join-Path $Game 'GameAssembly.dll';$before=Get-MMHash $dll
    New-Item -ItemType Directory -Path $Backup -Force | Out-Null;Copy-Item -LiteralPath $dll -Destination (Join-Path $Backup 'GameAssembly.dll') -Force
    $previous='';foreach($r in $Active){foreach($p in $r.Parts){foreach($e in $p.Files){if($e.Target -eq 'GameAssembly.dll'){$previous=$r.Id}}}}
    $log=Join-Path (Get-MMInstalledRoot $Game) ($Id+'\patcher.log')
    try{$run=Invoke-MMScriptPatch $tool $Game $sortOnly $log (Get-MMScriptPatchArgs $part 'apply')}catch{Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue;throw}
    $after=Get-MMHash $dll
    if($run.Code -ne 0 -or $after -eq $before){
        if($after -ne $before){
            if($kosearch){try{$null=Invoke-MMScriptPatch $tool $Game $false $log (Get-MMScriptPatchArgs $part 'restore')}catch{}}   # its UI change too
            Copy-Item -LiteralPath (Join-Path $Backup 'GameAssembly.dll') -Destination $dll -Force}   # never leave a half-done patch
        Remove-Item -LiteralPath $tool -Recurse -Force -ErrorAction SilentlyContinue
        throw ('패치 스크립트가 설치하지 않았습니다: '+$run.Tail)
    }
    $part.Files=@([ordered]@{Target='GameAssembly.dll';Sha256=$after;Backup='GameAssembly.dll';Previous=$previous})+$modEntries;$part.Note=($Component.Title.Split(' ')[0]+': '+$run.Tail)
    return $part
}
# ---------- patch stack: patchers that rewrite GameAssembly.dll (올인원, 창천UI) applied in a chosen order on a clean copy ----------
# Binary patchers do not override each other like data rows; they patch on top, and some check the bytes they need first.
# So order changes and removals rebuild: the clean GameAssembly.dll for this game build comes back, then each patcher runs
# again in the list order (올인원 with its remembered answers, script patches with their options). Patchers whose own files
# (fonts, bundles...) are not GameAssembly.dll keep their own undo (올인원 --restore).
# 올인원 prints "다시 설치합니다" before it runs its parts, so that line alone does not mean they all went in (10-31: 군단전 묶음
# refused a GameAssembly.dll that 창천UI had patched after an old 올인원). Its state file lists the parts that failed on the last
# real run, and a later "패치가 그대로입니다" keeps that list — so it names what is missing now.
function Get-MMAllInOneFailed([string]$Game){$state=Read-MMJson (Join-Path $Game '패처백업\올인원자동.json');if(!$state){return @()};return @($state.failed | Where-Object {$_})}
function Test-MMPatchRecord($Record){return @($Record.Parts | Where-Object {$_.Kind -in @('Patcher','ScriptPatch')}).Count -gt 0}
function Get-MMGameBuild([string]$Game){$acf=Join-Path (Split-Path $Game -Parent | Split-Path -Parent) 'appmanifest_3020510.acf';if(Test-Path -LiteralPath $acf){return ([regex]::Match((Get-Content -LiteralPath $acf -Raw),'"buildid"\s+"(\d+)"')).Groups[1].Value};return ''}
function Get-MMPatchOrder([string]$Game) {
    $saved=@((Read-MMJson (Join-Path $Game 'LOH_ModManager\PatchOrder.json')).Ids | Where-Object {$_})
    $records=@(Get-MMInstallRecords $Game | Where-Object {Test-MMPatchRecord $_});$present=@($records | ForEach-Object {$_.Id})
    $ids=@($saved | Where-Object {$_ -in $present});foreach($r in @($records | Sort-Object Installed)){if($r.Id -notin $ids){$ids+=$r.Id}}
    return $ids
}
# With the order: what the manager last left in GameAssembly.dll (build, hash, size/time). A later difference means a game update or
# an integrity check put Steam's file there (Get-MMPatchState). Patched outputs are kept so none is ever taken for an original.
function Save-MMPatchOrder([string]$Game,[string[]]$Ids){
    $path=Join-Path $Game 'LOH_ModManager\PatchOrder.json';$old=Read-MMJson $path;$dll=Join-Path $Game 'GameAssembly.dll';$ids=@($Ids | Where-Object {$_})
    $data=[ordered]@{Format='LOH_ModManager.PatchOrder 1';Ids=$ids;Produced=@($old.Produced | Where-Object {$_})}
    if($ids.Count -and (Test-Path -LiteralPath $dll -PathType Leaf)){
        $item=Get-Item -LiteralPath $dll;$hash=Get-MMHash $dll;$build=Get-MMGameBuild $Game
        $pristine=Join-Path $Game ('LOH_ModManager\Pristine\'+$(if($build){$build}else{'unknown'})+'\GameAssembly.dll')
        $clean=(Test-Path -LiteralPath $pristine) -and (Get-MMHash $pristine) -eq $hash   # every step failed: the file is the original
        $data.Build=$build;$data.Stacked=$hash;$data.Size=$item.Length;$data.Ticks=$item.LastWriteTimeUtc.Ticks
        if(!$clean){$data.Produced=@(@($hash)+@($data.Produced | Where-Object {$_ -ne $hash}) | Select-Object -First 20)}
    }
    Write-MMJson $path $data
}
# A Unity GameAssembly.dll straight from Steam ends with the .reloc section; patchers append theirs after it (올인원 .lohmod, 창천UI .ccui, 선부 .scfr1).
function Get-MMAssemblySections([string]$Path){
    try{
        $fs=[IO.File]::OpenRead($Path);try{$buf=New-Object byte[] 4096;$n=$fs.Read($buf,0,4096)}finally{$fs.Dispose()}
        if($n -lt 0x40 -or $buf[0] -ne 0x4D -or $buf[1] -ne 0x5A){return @()}
        $pe=[BitConverter]::ToInt32($buf,0x3C);if($pe -lt 0 -or $pe+24 -gt $n -or [BitConverter]::ToUInt32($buf,$pe) -ne 0x4550){return @()}
        $count=[BitConverter]::ToUInt16($buf,$pe+6);$first=$pe+24+[BitConverter]::ToUInt16($buf,$pe+20);if($count -lt 1 -or $first+40*$count -gt $n){return @()}
        return @(for($i=0;$i -lt $count;$i++){[Text.Encoding]::ASCII.GetString($buf,$first+40*$i,8).TrimEnd([char]0)})
    }catch{return @()}
}
function Test-MMCleanAssembly([string]$Path){
    $names=@(Get-MMAssemblySections $Path)
    return $names.Count -gt 0 -and $names[-1] -eq '.reloc' -and !@($names | Where-Object {$_ -in @('.lohmod','.ccui','.scfr1','.aiprom')}).Count
}
# A readable PE that is not clean: a patch's section is in it. (An unreadable file says nothing either way.)
function Test-MMPatchedAssembly([string]$Path){return @(Get-MMAssemblySections $Path).Count -gt 0 -and !(Test-MMCleanAssembly $Path)}
# Hashes of files a managed patch produced (never an original): recent stacked outputs, the records' GameAssembly entries, 올인원's installed copy.
function Get-MMProducedHashes([string]$Game){
    $set=@{};foreach($h in @((Read-MMJson (Join-Path $Game 'LOH_ModManager\PatchOrder.json')).Produced)){if($h){$set[([string]$h).ToUpperInvariant()]=$true}}
    foreach($r in @(Get-MMInstallRecords $Game)){foreach($p in $r.Parts){foreach($e in @($p.Files)){if($e.Target -eq 'GameAssembly.dll' -and $e.Sha256){$set[([string]$e.Sha256).ToUpperInvariant()]=$true}}}}
    $meta=Read-MMJson (Join-Path $Game '패처백업\올인원_설치전\올인원백업.json');if($meta -and $meta.files.'GameAssembly.dll'.installed_sha){$set[([string]$meta.files.'GameAssembly.dll'.installed_sha).ToUpperInvariant()]=$true}
    return $set
}
# 올인원 ships the official SHA-256 of every build it knows (official_hashes.json): for a listed build an original must match it.
function Get-MMOfficialAssembly([string]$Game,[string]$Build){
    if(!$Build){return ''}
    foreach($r in @(Get-MMInstallRecords $Game)){foreach($p in @($r.Parts | Where-Object {$_.Kind -eq 'Patcher' -and $_.Tool})){
        $list=Read-MMJson (Join-Path (Join-Path $Game $p.Tool) 'official_hashes.json');$b=$(if($list -and $list.builds){$list.builds.$Build})
        if($b -and $b.'GameAssembly.dll'){return ([string]$b.'GameAssembly.dll').ToUpperInvariant()}
    }}
    return ''
}
# The clean GameAssembly.dll of this build, kept in LOH_ModManager\Pristine\<build>. Sources, in order:
# 1) the 올인원 pre-install backup when its record says that copy is this build's original (hash checked);
# 2) the game's own file when nothing is patched in it — right after a game update or an integrity check: no section a patcher
#    appends, not a file the manager produced, and equal to 올인원's official hash when that list knows the build;
# 3) while no managed patcher exists, the file as it is (an unmanaged patch already in it then simply stays part of the base).
# A kept copy is replaced only by Steam's own file, back in the game folder after an integrity check (디시 79407: a patch put in by hand had become
# the kept copy). A game file with a hand-made patch in it changes nothing: such a player's base stays as it was.
$script:MMNoCleanOriginal='패치를 다시 쌓을 깨끗한 GameAssembly.dll 원본을 찾지 못했습니다(게임 파일에 아직 패치가 남아 있음). Steam 「게임 파일 무결성 확인」 뒤 모드매니저의 ''패치 다시 쌓기''를 누르세요.'
$script:MMKeptNotSteam='보관해 둔 GameAssembly.dll 사본이 Steam 순정 파일이 아닙니다(모드매니저 밖에서 넣은 패치가 들어 있는 것으로 보입니다). Steam 「게임 파일 무결성 확인」을 한 뒤 다시 설치해 보세요. 손으로 넣었던 패치는 이때 풀리니 압축 파일을 창으로 드래그해 다시 넣어 주세요.'
# Is this the file Steam installed? Steam keeps the installed depots' manifests with each file's SHA-1, so no list of ours has to follow game updates.
# $true/$false, or $null when it cannot be told (no manifest here, or another game file is not in it either: the format may have changed).
function Test-MMSteamOriginal([string]$Game,[string]$Path){
    try{
        $apps=Split-Path $Game -Parent | Split-Path -Parent;$acf=Join-Path $apps 'appmanifest_3020510.acf';if(!(Test-Path -LiteralPath $acf)){return $null}
        $roots=@((Join-Path $apps 'depotcache'),(Join-Path (Split-Path $apps -Parent) 'depotcache'))
        $steam=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath;if($steam){$roots+=(Join-Path $steam 'depotcache')}
        $latin=[Text.Encoding]::GetEncoding(28591);$text=''
        foreach($m in [regex]::Matches((Get-Content -LiteralPath $acf -Raw),'"(\d+)"\s*\{\s*"manifest"\s*"(\d+)"')){foreach($r in $roots){$f=Join-Path $r ($m.Groups[1].Value+'_'+$m.Groups[2].Value+'.manifest');if(Test-Path -LiteralPath $f){$text+=$latin.GetString([IO.File]::ReadAllBytes($f));break}}}
        if(!$text){return $null}
        $sha1={param($p)$s=[Security.Cryptography.SHA1]::Create();$fs=[IO.File]::OpenRead($p);try{$latin.GetString($s.ComputeHash($fs))}finally{$fs.Dispose();$s.Dispose()}}
        if($text.IndexOf((& $sha1 (Join-Path $Game 'UnityPlayer.dll')),[StringComparison]::Ordinal) -lt 0){return $null}
        return $text.IndexOf((& $sha1 $Path),[StringComparison]::Ordinal) -ge 0
    }catch{return $null}
}
function Get-MMPristine([string]$Game) {
    $build=Get-MMGameBuild $Game;$tag=$(if($build){$build}else{'unknown'})
    $dir=Assert-MMPath $Game (Join-Path $Game ('LOH_ModManager\Pristine\'+$tag));$file=Join-Path $dir 'GameAssembly.dll';$dll=Join-Path $Game 'GameAssembly.dll'
    if(Test-Path -LiteralPath $file){
        if((Test-Path -LiteralPath $dll -PathType Leaf) -and (Test-MMCleanAssembly $dll)){
            $hash=Get-MMHash $dll
            if($hash -ne (Get-MMHash $file)){
                $original=Test-MMSteamOriginal $Game $dll;$official=Get-MMOfficialAssembly $Game $build
                # without Steam's record: a kept copy with a patch's section gives way to a game file with none, as far as that can be told
                if($original -eq $true -or ($null -eq $original -and (Test-MMPatchedAssembly $file) -and !(Get-MMProducedHashes $Game).ContainsKey($hash) -and (!$official -or $official -eq $hash))){Copy-Item -LiteralPath $dll -Destination $file -Force}
            }
        }
        return $file
    }
    $patchRecords=@(Get-MMInstallRecords $Game | Where-Object {Test-MMPatchRecord $_});$found=$null
    if(@($patchRecords | Where-Object {@($_.Parts | Where-Object {$_.Kind -eq 'Patcher'}).Count}).Count){
        $meta=Read-MMJson (Join-Path $Game '패처백업\올인원_설치전\올인원백업.json');$copy=Join-Path $Game '패처백업\올인원_설치전\GameAssembly.dll'
        if($meta -and (Test-Path -LiteralPath $copy) -and [string]$meta.build -eq $build){$e=$meta.files.'GameAssembly.dll';if($e -and $e.orig_sha -and (Get-MMHash $copy) -eq ([string]$e.orig_sha).ToUpperInvariant()){$found=$copy}}
    }
    if(!$found -and (Test-Path -LiteralPath $dll -PathType Leaf) -and (Test-MMCleanAssembly $dll)){
        $hash=Get-MMHash $dll;$official=Get-MMOfficialAssembly $Game $build
        if(!(Get-MMProducedHashes $Game).ContainsKey($hash) -and (!$official -or $official -eq $hash)){$found=$dll}
    }
    if(!$found -and !$patchRecords.Count){$found=$dll}
    if(!$found){throw $script:MMNoCleanOriginal}
    New-Item -ItemType Directory -Path $dir -Force | Out-Null;Copy-Item -LiteralPath $found -Destination $file
    return $file
}
# Only after a patch failed to go in: is the kept copy the reason?
function Test-MMKeptNotOriginal([string]$Game){$file=Get-MMPristine $Game;return (Test-MMPatchedAssembly $file) -or ((Test-MMSteamOriginal $Game $file) -eq $false)}
# For the window: are the managed patches still in GameAssembly.dll? A game update (new build) or an integrity check replaces it with
# Steam's file; the stack then has to be rebuilt from the new original. Size and time decide first, the hash only when they moved.
function Get-MMPatchState([string]$Game){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');$order=Read-MMJson (Join-Path $Game 'LOH_ModManager\PatchOrder.json');$ids=@(Get-MMPatchOrder $Game)
    $state=[ordered]@{Patchers=@();NeedsRebuild=$false;Updated=$false;Lost=$false;Outside=$false;Build='';StackBuild=[string]$order.Build;CanRebuild=$false;Blocked=@()}
    # parts of a patcher that could not go in with the others, with the reason (상한해제 4번 vs 올인원 …)
    $state.Blocked=@(Get-MMInstallRecords $Game | Where-Object {$_.State -eq 'Installed'} | ForEach-Object {$rn=$_.Name;$_.Parts | Where-Object {$_.Blocked} | ForEach-Object {$_.Blocked | Where-Object {$_ -and $_.Why} | ForEach-Object {$rn+' — '+[string]$_.Why}}
        if($_.Yield -and ($_.Yield.Whole -or @($_.Yield.Items).Count)){$rn+' — 자리를 내줌'+$(if(@($_.Yield.Items).Count){'(군단전 항목)'}else{''})+': '+(@($_.Yield.Why) -join ' ')}})
    if(!$ids.Count){return [pscustomobject]$state}
    $names=@{};foreach($r in @(Get-MMInstallRecords $Game)){$names[$r.Id]=$r.Name}
    $state.Patchers=@($ids | ForEach-Object {$names[$_]} | Where-Object {$_})
    $build=Get-MMGameBuild $Game;$state.Build=$build;$dll=Join-Path $Game 'GameAssembly.dll'
    if($order.Build){$state.Updated=[bool]$build -and $build -ne [string]$order.Build}
    else{   # stacked before 0.10.0: a Pristine copy exists for the old build only
        $root=Join-Path $Game 'LOH_ModManager\Pristine'
        if($build -and (Test-Path -LiteralPath $root)){$known=@(Get-ChildItem -LiteralPath $root -Directory | ForEach-Object {$_.Name});$state.Updated=$known.Count -gt 0 -and $build -notin $known}
    }
    if($order.Stacked -and (Test-Path -LiteralPath $dll -PathType Leaf)){
        $item=Get-Item -LiteralPath $dll
        $same=$order.Size -and [int64]$order.Size -eq $item.Length -and $order.Ticks -and [int64]$order.Ticks -eq $item.LastWriteTimeUtc.Ticks
        if(!$same){$hash=Get-MMHash $dll;$state.Lost=$hash -ne ([string]$order.Stacked).ToUpperInvariant()
            # Same build: Steam's file is exactly the kept original. Anything else was patched outside the manager (another patcher's
            # own installer) — the managed patches are still in it, so it must not be offered a rebuild that would wipe that change.
            if($state.Lost -and !$state.Updated){
                $kept=Join-Path $Game ('LOH_ModManager\Pristine\'+$(if($build){$build}else{'unknown'})+'\GameAssembly.dll')
                $restored=if((Test-Path -LiteralPath $kept) -and !(Test-MMPatchedAssembly $kept)){(Get-MMHash $kept) -eq $hash -or ((Test-MMCleanAssembly $dll) -and (Test-MMSteamOriginal $Game $dll) -eq $true)}else{(Test-MMCleanAssembly $dll) -and (Test-MMSteamOriginal $Game $dll) -ne $false}
                if(!$restored){$state.Lost=$false;$state.Outside=$true}
            }
        }
    }
    $state.SectionRoom=Get-MMSectionRoom $dll   # the window warns at 2 or fewer: one more appending patch may not fit
    $state.NeedsRebuild=$state.Updated -or $state.Lost
    if($state.NeedsRebuild){$state.CanRebuild=(Test-Path -LiteralPath (Join-Path $Game ('LOH_ModManager\Pristine\'+$(if($build){$build}else{'unknown'})+'\GameAssembly.dll'))) -or (Test-MMCleanAssembly $dll)}
    return [pscustomobject]$state
}
# The player keeps a change made outside the manager: the file as it is now becomes what the manager last left there.
function Approve-MMOutsidePatch([string]$Game){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Assert-MMStopped $Game
    $ids=@(Get-MMPatchOrder $Game);if($ids.Count){Save-MMPatchOrder $Game $ids}
    return [pscustomobject]@{Kept=$ids.Count -gt 0}
}
# ---------- what each patcher changed in GameAssembly.dll (its footprint) and who gives way when two touch the same place ----------
# A rebuild applies the patchers one by one from the clean copy, so the file before and after each one shows exactly what that one
# changed — no recipe and no game run needed. Kept per game build in the install record (footprint.json): the bytes it wrote (the PE
# header and anything past the old end left out: every appender touches those) and the sections it added. Two patchers whose bytes
# meet cannot both work; the one lower in the list wins (as data mods: the lower one overrides) and the upper one gives way — only the
# clashing items when it has items (상한해제 numbers, 올인원's 군단전 group), else whole — shown as '자리를 내줌', never half-written.
function Get-MMHeaderEnd([string]$Path){
    $fs=[IO.File]::OpenRead($Path);try{$b=New-Object byte[] 4096;$got=$fs.Read($b,0,4096)}finally{$fs.Dispose()}
    if(-not (Test-MMPeHead $b $got)){return 0}
    $pe=[BitConverter]::ToInt32($b,0x3C);$n=[BitConverter]::ToUInt16($b,$pe+6);$f=$pe+24+[BitConverter]::ToUInt16($b,$pe+20);$min=[long]::MaxValue
    for($i=0;$i -lt $n -and $f+40*$i+40 -le $got;$i++){$raw=[BitConverter]::ToUInt32($b,$f+40*$i+20);if($raw -gt 0 -and $raw -lt $min){$min=$raw}}
    if($min -eq [long]::MaxValue){$min=0x400};return $min
}
function New-MMFootprint([string]$Before,[string]$After){
    $size=(Get-Item -LiteralPath $Before).Length;$head=Get-MMHeaderEnd $Before
    $ranges=@([MMBytes]::Diff($Before,$After) | Where-Object {$_[0] -ge $head -and $_[0] -lt $size} | ForEach-Object {,@([long]$_[0],[long][Math]::Min([long]$_[1],$size-[long]$_[0]))})
    $was=@(Get-MMAddedSections $Before | ForEach-Object {$_.Name});$added=@(Get-MMAddedSections $After | Where-Object {$_.Name -notin $was} | ForEach-Object {$_.Name})
    return [pscustomobject]@{Ranges=$ranges;Added=$added}
}
function Save-MMFootprint([string]$Game,[string]$Id,$Fp){Write-MMJson (Join-Path (Join-Path (Get-MMInstalledRoot $Game) $Id) 'footprint.json') ([ordered]@{Build=(Get-MMGameBuild $Game);Ranges=@($Fp.Ranges);Added=@($Fp.Added)})}
$script:MMOwnSections=@{Patcher=@('.lohmod','.hpfix','.aiwar','.formfix','.rng102');ccui=@('.ccui');scenic=@('.scfr1');capunlock=@('.aiprom')}
function Read-MMFootprint([string]$Game,$Record){
    $f=Read-MMJson (Join-Path (Join-Path (Get-MMInstalledRoot $Game) $Record.Id) 'footprint.json')
    if($f -and [string]$f.Build -eq (Get-MMGameBuild $Game)){return $f}
    # not measured yet on this build: the sections it is known to add, as they are in the file now (no bytes)
    $own=@();foreach($p in $Record.Parts){if($p.Kind -eq 'Patcher'){$own+=$script:MMOwnSections['Patcher']}elseif($p.Flavor -and $script:MMOwnSections.ContainsKey([string]$p.Flavor)){$own+=$script:MMOwnSections[[string]$p.Flavor]}elseif($p.Kind -eq 'ScriptPatch' -and !$p.Flavor){$own+=$script:MMOwnSections['ccui']}}
    $now=@(Get-MMAddedSections (Join-Path $Game 'GameAssembly.dll') | Where-Object {$_.Name -in $own} | ForEach-Object {$_.Name})
    return [pscustomobject]@{Ranges=@();Added=$now;Guess=$true}
}
# where two sets of [offset,length] meet (both sorted): the first meeting offsets
function Get-MMRangesMeet($A,$B){
    $x=@($A | Sort-Object {[long]$_[0]});$y=@($B | Sort-Object {[long]$_[0]});$i=0;$j=0;$hits=@()
    while($i -lt $x.Count -and $j -lt $y.Count -and $hits.Count -lt 5){
        $a0=[long]$x[$i][0];$a1=$a0+[long]$x[$i][1];$b0=[long]$y[$j][0];$b1=$b0+[long]$y[$j][1]
        if($a1 -le $b0){$i++}elseif($b1 -le $a0){$j++}else{$hits+=[Math]::Max($a0,$b0);if($a1 -le $b1){$i++}else{$j++}}
    }
    return $hits
}
# The list order's decisions: record id → what gives way (Whole / 올인원 items / 상한해제 numbers), why, and to whom.
function Get-MMPatchYield([string]$Game,$Ordered){
    $fp=@{};foreach($r in $Ordered){$fp[$r.Id]=Read-MMFootprint $Game $r}
    $cap={param($r)@($r.Parts | Where-Object {$_.Flavor -eq 'capunlock'})[0]};$aio={param($r)@($r.Parts | Where-Object {$_.Kind -eq 'Patcher'}).Count -gt 0}
    $capData={param($r)$d=Read-MMJson (Join-Path (Join-Path (Get-MMInstalledRoot $Game) $r.Id) 'cap.json');if($d -and [string]$d.Build -eq (Get-MMGameBuild $Game)){$d}else{$null}}
    $asmKeys={param($r)$p=@($r.Parts | Where-Object {$_.Kind -eq 'Patcher' -and $_.Tool})[0];if(!$p){return @()};$m=Get-MMAllInOneModel $Game (Join-Path $Game $p.Tool);@($m.Questions | Where-Object {$_.No -and $_.Assembly} | ForEach-Object {$_.Key})}   # 올인원's items in GameAssembly.dll
    $y=@{}
    $give={param($r,$what,$why,$by)
        if(!$y.ContainsKey($r.Id)){$y[$r.Id]=[ordered]@{Whole=$false;Items=@();Menus=@();Why=@();By=@()}};$e=$y[$r.Id]
        if($what -eq 'whole'){$e.Whole=$true}elseif($what -eq 'battle'){$e.Items=@(@($e.Items)+@(& $asmKeys $r) | Select-Object -Unique)}else{$e.Menus=@(@($e.Menus)+@($what) | Select-Object -Unique)}
        if($why -notin $e.Why){$e.Why=@($e.Why)+$why};if($by -notin $e.By){$e.By=@($e.By)+$by}}
    for($i=0;$i -lt $Ordered.Count;$i++){for($j=$i+1;$j -lt $Ordered.Count;$j++){
        $A=$Ordered[$i];$B=$Ordered[$j];$fa=$fp[$A.Id];$fb=$fp[$B.Id];$ca=& $cap $A;$cb=& $cap $B
        if($ca){   # 상한해제 above: its numbers that meet the lower one give way (a growing number meets any appender: its own check says so)
            $d=& $capData $A;if($d -and $fb){foreach($m in @($d.Menus | Where-Object {!$_.Grows -and [int]$_.Menu -in @($ca.Menus)})){$meet=@(Get-MMRangesMeet @($m.Ranges | ForEach-Object {,@([long]$_.Off,[long](([string]$_.New).Length/2))}) @($fb.Ranges))
                if($meet.Count){& $give $A ([int]$m.Menu) ([string]$m.Menu+'번: '+$B.Name+' 와(과) 같은 자리(파일 위치 0x'+('{0:X}' -f $meet[0])+')를 고칩니다. 목록에서 아래인 '+$B.Name+' 이(가) 우선이라 자리를 내줬습니다.') $B.Name}}}
            # a growing number needs the file's end, which the lower one appends to: it gives way by the order, even while the lower one
            # is left out for now (Free) and the end happens to be free — only for a patcher known to refuse an appended file (Stack 'no');
            # one that places its section after the last one (V1.0.2) clashes with no appender
            if($d -and [string]$d.Stack -eq 'no' -and $fb -and @($fb.Added).Count){foreach($m in @($d.Menus | Where-Object {$_.Grows -and [int]$_.Menu -in @($ca.Menus)})){
                & $give $A ([int]$m.Menu) ([string]$m.Menu+'번: 원본 파일 끝 자리(코드 구역)를 써야 하는데 '+$B.Name+' 이(가) 그 자리에 '+(@($fb.Added) -join ', ')+' 을(를) 붙입니다. 목록에서 아래인 '+$B.Name+' 이(가) 우선이라 자리를 내줬습니다.') $B.Name}}
            continue
        }
        $whatA=$(if((& $aio $A) -and @(& $asmKeys $A).Count){'battle'}else{'whole'})
        if($cb){   # 상한해제 below wins: what it needs, the upper one leaves free
            $d=& $capData $B
            if($d -and $fa){foreach($m in @($d.Menus | Where-Object {!$_.Grows -and [int]$_.Menu -in @($cb.Menus)})){$meet=@(Get-MMRangesMeet @($fa.Ranges) @($m.Ranges | ForEach-Object {,@([long]$_.Off,[long](([string]$_.New).Length/2))}))
                if($meet.Count){& $give $A $whatA ($B.Name+' '+$m.Menu+'번과 같은 자리(파일 위치 0x'+('{0:X}' -f $meet[0])+')를 고칩니다. 목록에서 아래인 '+$B.Name+' 이(가) 우선이라 자리를 내줬습니다.') $B.Name}}}
            $grow=@($(if($d -and [string]$d.Stack -eq 'no'){$d.Menus | Where-Object {$_.Grows -and [int]$_.Menu -in @($cb.Menus)}}))
            if($grow.Count -and $fa -and @($fa.Added).Count){& $give $A $whatA ($B.Name+' '+(@($grow | ForEach-Object {$_.Menu}) -join '·')+'번이 원본 파일 끝 자리(코드 구역)를 써야 하는데 '+$A.Name+' 이(가) 그 자리에 '+(@($fa.Added) -join ', ')+' 을(를) 붙입니다. 목록에서 아래인 '+$B.Name+' 이(가) 우선이라 자리를 내줬습니다.') $B.Name}
            continue
        }
        # a 가이드 patch below checks the bytes it changes itself and stops on one it does not know: meeting the one above means it builds
        # on it (무투 회피 adds to 올인원's .lohmod), so nothing gives way — if they do clash, its own step fails and says why
        if(@($B.Parts | Where-Object {$_.Flavor -eq 'guide'}).Count){continue}
        if($fa -and $fb){$meet=@(Get-MMRangesMeet @($fa.Ranges) @($fb.Ranges))
            if($meet.Count){& $give $A $whatA ($B.Name+' 와(과) 같은 자리 '+$meet.Count+'곳 이상(파일 위치 0x'+('{0:X}' -f $meet[0])+')을 고칩니다. 목록에서 아래인 '+$B.Name+' 이(가) 우선이라 자리를 내줬습니다.') $B.Name}}
    }}
    return $y
}
# What a patch was seen to need: to stay above another (it did not go in under it). Kept on its record; the window warns before a save
# would put it under that one again. Learned from the patch's own refusal, never from its name.
function Add-MMPatchAbove($Record,[string]$Id,[string]$Tail){
    $list=@(@($Record.Above | Where-Object {$_ -and $_.Id -and $_.Id -ne $Id})+@([pscustomobject]@{Id=$Id;Tail=$Tail}))
    if($Record -is [Collections.IDictionary]){$Record['Above']=$list}else{$Record | Add-Member -NotePropertyName Above -NotePropertyValue $list -Force}
}
# How long a patch's step took the last time (rebuilds time each one; an install times its first run); 1 minute when never timed.
function Get-MMPatchSeconds($Record){
    if($Record.PSObject.Properties['Seconds'] -and [int]$Record.Seconds -gt 0){return [int]$Record.Seconds}
    $s=0;foreach($p in @($Record.Parts)){if($p.PSObject.Properties['Seconds']){$s+=[int]$p.Seconds}};if($s -gt 0){return $s};return 60
}
# A line the window shows under its busy text while a long job runs (empty: none).
function Write-MMProgress([string]$Game,[string]$Text){
    try{$f=Join-Path $Game 'LOH_ModManager\Work\progress.txt';if($Text){New-Item -ItemType Directory -Path (Split-Path $f -Parent) -Force | Out-Null;[IO.File]::WriteAllText($f,$Text,[Text.UTF8Encoding]::new($false))}elseif(Test-Path -LiteralPath $f){Remove-Item -LiteralPath $f -Force}}catch{}
}
function Get-MMPatchStack([string]$Game,[string[]]$Order=@(),[string]$Skip=''){
    $records=@(Get-MMInstallRecords $Game | Where-Object {(Test-MMPatchRecord $_) -and $_.Id -ne $Skip})
    if(!$Order.Count){$Order=@(Get-MMPatchOrder $Game)}
    $stack=@();foreach($oid in $Order){$hit=@($records | Where-Object {$_.Id -eq $oid});if($hit.Count){$stack+=$hit[0]}}
    foreach($r in @($records | Sort-Object Installed)){if($r.Id -notin @($stack | ForEach-Object {$_.Id})){$stack+=$r}}
    return $stack
}
# Would the list order give way differently from how the file was last built? (then a quick change must rebuild instead)
# Ids: the records whose giving way would differ; Want: what each would give way now.
function Get-MMYieldDiff([string]$Game){
    $stack=@(Get-MMPatchStack $Game);if($stack.Count -lt 2){return [pscustomobject]@{Ids=@();Want=@{}}}
    $want=Get-MMPatchYield $Game $stack;$ids=@()
    foreach($r in $stack){$now=$r.Yield;$w=$want[$r.Id]
        $a=$(if($now){(@([bool]$now.Whole)+@($now.Items)+@($now.Menus)) -join ','}else{''});$b=$(if($w){(@([bool]$w.Whole)+@($w.Items)+@($w.Menus)) -join ','}else{''})
        if($a -ne $b){$ids+=$r.Id}}
    return [pscustomobject]@{Ids=$ids;Want=$want}
}
function Test-MMYieldChanged([string]$Game){return @((Get-MMYieldDiff $Game).Ids).Count -gt 0}
# After a choice or the list order changed (a checkbox: no rebuild wanted). A patcher that would give way MORE than the file shows (its
# bytes must come out) needs the whole stack: Rebuild. One that would give way less (what it gave way to was turned off or moved above
# it) is left out as it is for now — nothing clashes in the file — and marked Free; the next rebuild puts it back (the window offers
# it). $Self: the caller writes its own giving way (상한해제's numbers that give way are simply not written).
function Resolve-MMYieldChange([string]$Game,[string]$Self=''){
    $diff=Get-MMYieldDiff $Game;$freed=@();$rebuild=$false
    $covers={param($now,$w)if(!$w){return $true};if(!$now){return $false};if($now.Whole){return $true};if($w.Whole){return $false}
        foreach($k in @($w.Items)){if($k -notin @($now.Items)){return $false}};$nm=@($now.Menus | ForEach-Object {[int]$_});foreach($m in @($w.Menus)){if([int]$m -notin $nm){return $false}};return $true}
    foreach($oid in @($diff.Ids | Where-Object {$_ -ne $Self})){$o=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -eq $oid})[0]
        if($o -and (& $covers $o.Yield $diff.Want[$oid])){$freed+=$o}else{$rebuild=$true}}
    return [pscustomobject]@{Rebuild=$rebuild;Freed=$freed;Diff=$diff;Self=$Self}
}
# who gives way in the records, as one line each for the player and as one key to compare before and after a rebuild
function Get-MMYieldLines($Records){
    foreach($r in @($Records | Where-Object {$_.Yield -and ($_.Yield.Whole -or @($_.Yield.Items).Count -or @($_.Yield.Menus).Count)})){$y=$r.Yield;$by=(@($y.By) -join ', ')
        $what=if($y.Whole){'통째로 자리를 내줌'}elseif(@($y.Items).Count){'군단전 항목 자리를 내줌'}else{(@($y.Menus | ForEach-Object {[string]$_+'번'}) -join '·')+' 함께 쓸 수 없음'}
        $r.Name+': '+$what+' ('+$by+' 이(가) 우선)'+$(if($y.Free){' — 다시 쌓으면 들어감'})}
}
# Before a save: what this order would make give way that does not give way now (one line per patch, its reason from the measured
# places). Nothing runs; the window shows it and asks.
function Get-MMYieldPreview([string]$Game,[string[]]$Order){
    $ordered=@(Get-MMPatchStack $Game $Order);if($ordered.Count -lt 2){return @()}
    $want=Get-MMPatchYield $Game $ordered;$lines=@()
    foreach($r in $ordered){$w=$want[$r.Id];if(!$w){continue};$now=$r.Yield
        if($now -and [bool]$now.Whole -eq [bool]$w.Whole -and (@($now.Items) -join ',') -eq (@($w.Items) -join ',') -and (@($now.Menus) -join ',') -eq (@($w.Menus) -join ',')){continue}
        $what=if($w.Whole){'전체'}elseif(@($w.Items).Count){'항목 '+@($w.Items).Count+'개'}else{(@($w.Menus | ForEach-Object {[string]$_+'번'}) -join '·')}
        $lines+=("'"+$r.Name+"' — "+$what+': '+(@($w.Why) -join ' '))}
    return $lines
}
function Get-MMYieldKey($Records){return (@($Records | ForEach-Object {$y=$_.Yield;$_.Id+':'+$(if($y){[string][bool]$y.Whole+'/'+(@($y.Items) -join ',')+'/'+(@($y.Menus) -join ',')})}) -join '|')}
function ConvertTo-MMYield($w){if($w){[pscustomobject]@{Whole=[bool]$w.Whole;Items=@($w.Items);Menus=@($w.Menus);Why=@($w.Why);By=@($w.By)}}else{$null}}
# write what Resolve-MMYieldChange found (only when no rebuild): Free marks, and a Free one that has to give way again for real
function Save-MMYieldFree([string]$Game,$Resolved){
    $fid=@($Resolved.Freed | ForEach-Object {$_.Id})
    foreach($o in @(Get-MMInstallRecords $Game | Where-Object {$_.Id -ne $Resolved.Self -and $_.Yield -and $_.Yield.Free -and $_.Id -notin $fid})){$w=$Resolved.Diff.Want[$o.Id]
        if($w){$o | Add-Member -NotePropertyName Yield -NotePropertyValue (ConvertTo-MMYield $w) -Force;Save-MMInstallRecord $Game $o}}
    foreach($o in $Resolved.Freed){$o.Yield | Add-Member -NotePropertyName Free -NotePropertyValue $true -Force
        $o.Yield.Why=@('자리를 받던 쪽이 바뀌어(그 번호를 끄거나 목록 순서를 바꿈) 이제 함께 들어갈 수 있습니다. 다시 쌓으면(패치 다시 적용) 들어갑니다.');Save-MMInstallRecord $Game $o}
}
# the window's list order, sent with a checkbox when the player moved cards but has not saved yet: it decides who wins, so it is kept first
function Save-MMPatchOrderFrom([string]$Game,[string[]]$Order){
    $Order=@($Order | Where-Object {$_});if(!$Order.Count){return}
    $ids=@(Get-MMPatchStack $Game $Order | ForEach-Object {$_.Id});if(($ids -join ',') -ne (@(Get-MMPatchOrder $Game) -join ',')){Save-MMPatchOrder $Game $ids}
}
function Invoke-MMPatchRebuild([string]$Game,[string[]]$Order=@(),[string]$Skip='') {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Assert-MMStopped $Game
    $ordered=@(Get-MMPatchStack $Game $Order $Skip)   # the player's order: who wins
    $gaveBefore=Get-MMYieldKey $ordered
    $isLast={param($r)@($r.Parts | Where-Object {$_.Last}).Count -gt 0}   # 상한해제: its kept bytes go on top of everything (올인원 starts from its own copy)
    $stack=@($ordered | Where-Object {!(& $isLast $_)})+@($ordered | Where-Object {& $isLast $_})   # the order they are applied in
    $prevOrder=@(Get-MMPatchOrder $Game)   # to tell which patch was moved above one that then failed
    $pristine=Get-MMPristine $Game;$dll=Join-Path $Game 'GameAssembly.dll'
    $safety=Assert-MMPath $Game (Join-Path $Game ('LOH_ModManager\Backups\Rebuild-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')))
    New-Item -ItemType Directory -Path $safety -Force | Out-Null;Copy-Item -LiteralPath $dll -Destination (Join-Path $safety 'GameAssembly.dll')
    $work=Assert-MMPath $Game (Join-Path $Game 'LOH_ModManager\Work');New-Item -ItemType Directory -Path $work -Force | Out-Null;$prev=Join-Path $work 'before-step.dll'
    $yield=if($ordered.Count -ge 2){Get-MMPatchYield $Game $ordered}else{@{}};$used=$null
    for($pass=1;$pass -le 2;$pass++){
        $steps=@()
        # 양식 patches: their other files back to the copies kept at install (they run again on the clean stack below)
        foreach($r in @($stack)[($stack.Count-1)..0]){foreach($p in @($r.Parts | Where-Object {$_.Kind -eq 'ScriptPatch' -and $_.Flavor -in @('promise','guide')})){try{Reset-MMPromiseFiles $Game $p (Join-Path (Get-MMInstalledRoot $Game) $r.Id)}catch{}}}
        # 무장 검색 also rewrote the UI bundle; the clean GameAssembly.dll does not undo that and its apply refuses a mixed state, so its own restore first
        foreach($r in @($stack)[($stack.Count-1)..0]){foreach($p in @($r.Parts | Where-Object {$_.Kind -eq 'ScriptPatch' -and $_.Flavor -eq 'kosearch' -and $_.Tool})){
            try{$run=Invoke-MMScriptPatch (Join-Path $Game $p.Tool) $Game $false (Join-Path (Join-Path (Get-MMInstalledRoot $Game) $r.Id) 'patcher.log') (Get-MMScriptPatchArgs $p 'restore');if($run.Code -ne 0){$steps+=[pscustomobject]@{Id='';Name=($r.Name+' 복구');Ok=$false;Tail=$run.Tail}}}catch{$steps+=[pscustomobject]@{Id='';Name=($r.Name+' 복구');Ok=$false;Tail=$_.Exception.Message}}}}
        Copy-Item -LiteralPath $pristine -Destination $dll -Force
        foreach($r in $stack){
            $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $r.Id;$give=$yield[$r.Id]
            $r | Add-Member -NotePropertyName Yield -NotePropertyValue $(if($give){[pscustomobject]@{Whole=[bool]$give.Whole;Items=@($give.Items);Menus=@($give.Menus);Why=@($give.Why);By=@($give.By)}}else{$null}) -Force
            if($give -and $give.Whole){$steps+=[pscustomobject]@{Id=$r.Id;Name=$r.Name;Ok=$true;Tail=('자리를 내줌 — '+(@($give.Why) -join ' '))};Save-MMInstallRecord $Game $r;continue}
            Copy-Item -LiteralPath $dll -Destination $prev -Force;$full=!$give;$clock=[Diagnostics.Stopwatch]::StartNew()
            foreach($p in $r.Parts){
                if($p.Kind -eq 'ScriptPatch' -and $p.Flavor -eq 'capunlock'){
                    $bk=Join-Path $pkgDir 'backup';New-Item -ItemType Directory -Path $bk -Force | Out-Null;Copy-Item -LiteralPath $dll -Destination (Join-Path $bk 'GameAssembly.dll') -Force   # what removal puts back
                    $menuSkip=@{};if($give){foreach($m in @($give.Menus)){$menuSkip[[int]$m]=@($give.Why | Where-Object {$_ -like ([string]$m+'번*')})[0]}}   # not $skip: that is the [string]$Skip parameter
                    try{$res=Set-MMCapUnlockBytes $Game $p $pkgDir $menuSkip;Set-MMCapUnlockResult $p $res;$ok=$true;$tail=Get-MMCapUnlockTail $res}catch{$ok=$false;$tail=$_.Exception.Message}
                    $f=@($p.Files)[0];$f.Sha256=Get-MMHash $dll;$f.Backup='GameAssembly.dll';$f.Previous=''
                    $steps+=[pscustomobject]@{Id=$r.Id;Name=$r.Name;Ok=$ok;Tail=$tail}
                }elseif($p.Kind -eq 'ScriptPatch'){
                    $tool=Join-Path $Game $p.Tool;$bk=Join-Path $pkgDir 'backup';New-Item -ItemType Directory -Path $bk -Force | Out-Null;Copy-Item -LiteralPath $dll -Destination (Join-Path $bk 'GameAssembly.dll') -Force
                    $before=Get-MMHash $dll
                    try{$run=Invoke-MMScriptPatch $tool $Game ([bool]$p.SortOnly) (Join-Path $pkgDir 'patcher.log') (Get-MMScriptPatchArgs $p 'apply');$ok=$run.Code -eq 0 -and ($p.Flavor -in @('promise','guide') -or (Get-MMHash $dll) -ne $before);$tail=$run.Tail}catch{$ok=$false;$tail=$_.Exception.Message}
                    if(!$ok -and (Get-MMHash $dll) -ne $before){Copy-Item -LiteralPath (Join-Path $bk 'GameAssembly.dll') -Destination $dll -Force}
                    if(!$ok){$full=$false}
                    if($ok -and $p.Flavor -in @('promise','guide')){Update-MMPromiseHashes $Game $p}
                    $f=@($p.Files)[0];$f.Sha256=Get-MMHash $dll;$f.Backup='GameAssembly.dll';$f.Previous=''
                    $steps+=[pscustomobject]@{Id=$r.Id;Name=$r.Name;Ok=$ok;Tail=$tail}
                }elseif($p.Kind -eq 'Patcher'){
                    $tool=Join-Path $Game $p.Tool;$answers=Join-Path $tool '올인원선택.json';$keep=$null
                    # its 군단전 group gives way: this run with those items off, the player's own choice kept in the answers file
                    if($give -and @($give.Items).Count -and (Test-Path -LiteralPath $answers)){$keep=[IO.File]::ReadAllText($answers);$ch=Get-MMAllInOneChoices $Game $p
                        $m=Get-MMAllInOneModel $Game $tool;foreach($k in @($give.Items)){$it=@($m.Questions | Where-Object {$_.Key -eq $k})[0];if($it){$ch=Set-MMAllInOneAnswer $ch $it $false $null}};[IO.File]::WriteAllText($answers,($ch | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))}
                    # after a game update or an integrity check the files are Steam's again: their originals before it runs
                    $asm=@();try{$pl=Get-MMAllInOnePlans $Game $tool @(,(Get-MMAllInOneChoices $Game $p));if($pl){$null=Save-MMAllInOneOriginals $Game $tool @($pl[0].Entries | ForEach-Object {$_.Files});if($null -eq $keep){$asm=@(Get-MMAllInOneAsmOnly $Game $pl[0].Entries)}}}catch{}
                    # only GameAssembly.dll went back to the original: just its part runs again (11-03, 군단전 묶음 ~30 s instead of the whole 올인원)
                    if($asm.Count){$ok=$true;$said=@();foreach($e in $asm){$cmd=@($e.Cmd);try{$run=Invoke-MMAioProcess $cmd[0] @($cmd | Select-Object -Skip 1) $(if($e.Cwd -and (Test-Path -LiteralPath $e.Cwd)){$e.Cwd}else{$tool}) (Join-Path $pkgDir 'patcher.log');if($run.Code -ne 0){$ok=$false};$said+=($e.Name+': '+$run.Tail)}catch{$ok=$false;$said+=($e.Name+': '+$_.Exception.Message)}}
                        $tail='게임 코드 부분만 다시 돌림 — '+($said -join ' / ')}
                    else{try{$text=Invoke-MMPatcher $tool @(('--game='+$Game),'--auto',$env:ComSpec,'/c','exit','0') (Join-Path $pkgDir 'patcher.log');$failed=@(Get-MMAllInOneFailed $Game);$ok=($text -match '다시 설치합니다|패치가 그대로입니다') -and !$failed.Count;$tail=$(if($failed.Count){'실패한 패치: '+($failed -join ', ')}else{((($text -split "`r?`n") | Where-Object {$_.Trim()} | Select-Object -Last 2) -join ' ')})}catch{$ok=$false;$tail=$_.Exception.Message}
                    finally{if($null -ne $keep){[IO.File]::WriteAllText($answers,$keep,[Text.UTF8Encoding]::new($false))}}}
                    if($give -and @($give.Items).Count){$tail+=' · 군단전 항목은 자리를 내줌'}
                    $steps+=[pscustomobject]@{Id=$r.Id;Name=$r.Name;Ok=$ok;Tail=$tail}
                }
            }
            $bad=@($steps | Where-Object {$_.Id -eq $r.Id -and !$_.Ok})[0];$was=!$r.PSObject.Properties['LastOk'] -or [bool]$r.LastOk
            # it went in last time and fails now: the patches moved from below it to above it are what it needs to stay above (the window warns
            # before a save puts it under one of them again)
            if($bad -and $was -and $prevOrder.Count){$at=[array]::IndexOf($prevOrder,$r.Id);$now=@($ordered | ForEach-Object {$_.Id});$k=[array]::IndexOf($now,$r.Id)
                if($at -ge 0 -and $k -gt 0){foreach($o in @($now[0..($k-1)] | Where-Object {[array]::IndexOf($prevOrder,$_) -gt $at})){Add-MMPatchAbove $r $o ([string]$bad.Tail)}}}
            if(!$bad -and @($r.Above).Count){$now=@($ordered | ForEach-Object {$_.Id});$k=[array]::IndexOf($now,$r.Id)   # went in under one it was thought to need: not needed
                $keep=@($r.Above | Where-Object {$_ -and [array]::IndexOf($now,[string]$_.Id) -gt $k});if($keep.Count -ne @($r.Above).Count){$r | Add-Member -NotePropertyName Above -NotePropertyValue $keep -Force}}
            $r | Add-Member -NotePropertyName LastOk -NotePropertyValue (!$bad) -Force   # 자리 찾기: a place must not make a patch that went in fail
            $r | Add-Member -NotePropertyName LastTail -NotePropertyValue $(if($bad){[string]$bad.Tail}else{''}) -Force
            $r | Add-Member -NotePropertyName Seconds -NotePropertyValue ([int][Math]::Ceiling($clock.Elapsed.TotalSeconds)) -Force   # 자리 찾기 says how long the tries may take
            if($full){try{Save-MMFootprint $Game $r.Id (New-MMFootprint $prev $dll)}catch{}}   # measured only when it ran whole: what it needs, not what it was left
            Save-MMInstallRecord $Game $r
        }
        $used=$yield
        if($ordered.Count -lt 2){break}
        $again=Get-MMPatchYield $Game $ordered   # first measurements may show a clash nobody knew of: build once more with it
        $key={param($h)(@($h.Keys | Sort-Object | ForEach-Object {$_+':'+[bool]$h[$_].Whole+':'+(@($h[$_].Items) -join ',')+':'+(@($h[$_].Menus) -join ',')}) -join '|')}
        if((& $key $again) -eq (& $key $used)){break};$yield=$again
    }
    Remove-Item -LiteralPath $prev -Force -ErrorAction SilentlyContinue
    foreach($r in @(Get-MMInstallRecords $Game | Where-Object {@($_.Parts | Where-Object {$_.ManualPatch}).Count})){$steps+=[pscustomobject]@{Id='';Name=($r.Name+' (직접 설치)');Ok=$false;Tail='모드매니저가 다시 입히지 못하는 패치 — 그 설치기를 다시 실행하고 기록해 주세요'}}
    Save-MMPatchOrder $Game @($ordered | ForEach-Object {$_.Id})   # the player's order (who wins), not the order applied
    # every patch went in: this order works on this PC — the window's 자동 정렬 goes back to it (patches mixed up, switched off and on)
    if($ordered.Count -and !@($steps | Where-Object {$_.Id -and !$_.Ok}).Count){try{Write-MMJson (Join-Path $Game 'LOH_ModManager\PatchGoodOrder.json') ([ordered]@{Format='LOH_ModManager.PatchGoodOrder 1';Ids=@($ordered | ForEach-Object {$_.Id})})}catch{}}
    $steps+=@((Update-MMTreasuryLinks $Game).Steps)   # the rebuilt GameAssembly.dll: 세력금고 reads its functions from it
    $now=@(Get-MMPatchStack $Game);$gave=@(Get-MMYieldLines $now)   # who gave way, for the notice (and a word before the game starts)
    return [pscustomobject]@{Steps=$steps;Order=@($ordered | ForEach-Object {$_.Name});Safety=$safety;Gave=$gave;GaveChanged=((Get-MMYieldKey $now) -ne $gaveBefore)}
}
# ---------- 창작마당 모드의 하위 패치(번역 등): 압축 안 workshop\content\3020510\<번호>\… 를 그 구독 폴더에 덮어쓴다 ----------
# 춘화추월 한글화(디시 70813)가 이 모양: 원본은 창작마당 구독, 번역은 그 폴더의 파일 몇 개를 바꿈. Steam 이 원본을 업데이트하면
# 덮은 파일이 원본으로 돌아가므로, 넣은 파일 사본을 기록(patch\)에 두고 '다시 적용'에 쓴다. 바뀐 원본은 backup\ 에.
function Get-MMWorkshopRoot([string]$Game){return Join-Path (Split-Path (Split-Path ([IO.Path]::GetFullPath($Game).TrimEnd('\')) -Parent) -Parent) 'workshop\content\3020510'}
function Get-MMWorkshopPatchDirs([string]$Base){
    return @(Get-ChildItem -LiteralPath $Base -Recurse -Directory -Force -ErrorAction SilentlyContinue | Where-Object {$_.Name -match '^\d+$' -and $_.Parent.Name -eq '3020510' -and $_.Parent.Parent.Name -eq 'content' -and $_.Parent.Parent.Parent.Name -eq 'workshop'})
}
function Get-MMWorkshopPatchComponents([string]$Game,[string]$Content,$Dirs) {
    $list=@()
    foreach($dir in $Dirs){
        $id=$dir.Name;$ws=Join-Path (Get-MMWorkshopRoot $Game) $id;$subscribed=Test-Path -LiteralPath $ws -PathType Container
        $parent='';$suffix='';$info=Join-Path $ws 'ModInfo.json';if($subscribed -and (Test-Path -LiteralPath $info)){$mi=Get-MMModInfo $info;$parent=$mi.Name;$suffix=$mi.Suffix}
        if(!$parent){$own=Join-Path $dir.FullName 'ModInfo.json';if(Test-Path -LiteralPath $own){$mi=Get-MMModInfo $own;$parent=$mi.Name;if(!$suffix){$suffix=$mi.Suffix}}}
        $files=@(Get-ChildItem -LiteralPath $dir.FullName -Recurse -Force -File | Where-Object {$_.Name -notin $script:MMJunk -and $_.Extension.ToLowerInvariant() -notin $script:MMScriptExt} | ForEach-Object {[pscustomobject]@{Source=(Get-MMRelative $Content $_.FullName);Target=(Get-MMRelative $dir.FullName $_.FullName)}})
        $same=0;$replace=0
        if($subscribed){foreach($f in $files){$dst=Join-Path $ws $f.Target;if(Test-Path -LiteralPath $dst -PathType Leaf){if((Get-MMHash $dst) -eq (Get-MMHash (Join-Path $Content $f.Source))){$same++}else{$replace++}}}}
        $label='창작마당 '+$id+$(if($parent){' ('+$parent+')'})
        $detail=$(if($files.Count -and $same -eq $files.Count){$label+' 에 이미 적용되어 있습니다 — 기록만 남깁니다(덮기 전 원본이 없어, 제거할 때는 Steam 에서 다시 받아야 함) · 파일 '+$files.Count+'개'}
            else{$label+' 폴더에 덮어쓰기 · 파일 '+$files.Count+'개(원본 '+$replace+'개는 백업 후 덮음'+$(if($same){', '+$same+'개는 이미 같음'})+') · 원본 카드 아래에 하위 패치로 붙습니다'})
        $warn=$(if(!$subscribed){'이 패치의 원본인 창작마당 모드(https://steamcommunity.com/sharedfiles/filedetails/?id='+$id+')를 Steam 에서 먼저 구독하고, 받아진 뒤 다시 끌어다 놓으세요.'}else{''})
        $list+=[pscustomobject]@{Key=('ws:'+$id);Kind='WorkshopPatch';Title=$(if($parent){$parent+' 패치'}else{'창작마당 '+$id+' 패치'});Detail=$detail;Suffix='';Files=$files;Default=$subscribed;Blocked=!$subscribed;Warning=$warn;WorkshopId=$id;Parent=$parent;ParentSuffix=$suffix}
    }
    return $list
}
function Get-MMWorkshopPath([string]$Game,$Part,$Entry){$ws=Join-Path (Get-MMWorkshopRoot $Game) ([string]$Part.WorkshopId);return Assert-MMPath $ws (Join-Path $ws $Entry.Target)}
function Set-MMWorkshopStamp($Entry,[string]$Path){$i=Get-Item -LiteralPath $Path;$Entry.Size=$i.Length;$Entry.Time=$i.LastWriteTimeUtc.Ticks}
function Install-MMWorkshopPatch([string]$Game,$Component,[string]$Content,[string]$PkgDir,$Written) {
    $id=[string]$Component.WorkshopId;if($id -notmatch '^\d+$'){throw '잘못된 창작마당 번호'}
    $ws=Join-Path (Get-MMWorkshopRoot $Game) $id;if(!(Test-Path -LiteralPath $ws -PathType Container)){throw ('창작마당 모드 '+$id+' 가 구독되어 있지 않습니다. Steam 에서 먼저 구독하세요.')}
    $part=[ordered]@{Key=$Component.Key;Kind='WorkshopPatch';Title=$Component.Title;Suffix='';ExternalId='';Files=@();Original='';WorkshopId=$id;Parent=[string]$Component.Parent;ParentSuffix=[string]$Component.ParentSuffix}
    $entries=@()
    foreach($f in $Component.Files){
        $src=Join-Path $Content $f.Source;$rel='workshop\'+$id+'\'+$f.Target;$entry=[ordered]@{Target=$f.Target;Sha256=(Get-MMHash $src);Backup=$null;Same=$false;Size=0;Time=0}
        $dst=Get-MMWorkshopPath $Game $part $entry
        $keep=Join-Path $PkgDir ('patch\'+$rel);New-Item -ItemType Directory -Path (Split-Path $keep -Parent) -Force | Out-Null;Copy-Item -LiteralPath $src -Destination $keep -Force   # for 다시 적용
        if(Test-Path -LiteralPath $dst -PathType Leaf){
            if((Get-MMHash $dst) -eq $entry.Sha256){$entry.Same=$true}
            else{$copy=Join-Path $PkgDir ('backup\'+$rel);New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null;Copy-Item -LiteralPath $dst -Destination $copy -Force;$entry.Backup=$rel}
        }
        if(!$entry.Same){New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Copy-Item -LiteralPath $src -Destination $dst -Force;$Written.Add([pscustomobject]@{Path=$dst;Backup=$entry.Backup;Hash=$entry.Sha256})}
        Set-MMWorkshopStamp $entry $dst;$entries+=$entry
    }
    $part.Files=$entries;return $part
}
# still as written? size and time first (Conversation.json is ~40MB), the hash only when they differ
function Test-MMWorkshopFile([string]$Path,$Entry){
    if(!(Test-Path -LiteralPath $Path -PathType Leaf)){return $false}
    $i=Get-Item -LiteralPath $Path;if($Entry.Size -and $i.Length -eq [long]$Entry.Size -and $i.LastWriteTimeUtc.Ticks -eq [long]$Entry.Time){return $true}
    return (Get-MMHash $Path) -eq $Entry.Sha256
}
# 다시 적용 (Steam updated the original) and 되살리기: the kept copy goes back; Steam's new file becomes the original to restore later
function Set-MMWorkshopPatchApplied([string]$Game,$Part,[string]$PkgDir) {
    $ws=Join-Path (Get-MMWorkshopRoot $Game) ([string]$Part.WorkshopId);if(!(Test-Path -LiteralPath $ws -PathType Container)){throw ('창작마당 모드 '+$Part.WorkshopId+' 가 구독되어 있지 않습니다. Steam 에서 다시 구독한 뒤 해 주세요.')}
    $done=0;$lost=0
    foreach($f in @($Part.Files)){
        $dst=Get-MMWorkshopPath $Game $Part $f;$rel='workshop\'+$Part.WorkshopId+'\'+$f.Target;$keep=Join-Path $PkgDir ('patch\'+$rel)
        if(Test-MMWorkshopFile $dst $f){continue}
        if(!(Test-Path -LiteralPath $keep)){$lost++;continue}
        if(Test-Path -LiteralPath $dst -PathType Leaf){$copy=Join-Path $PkgDir ('backup\'+$rel);New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null;Copy-Item -LiteralPath $dst -Destination $copy -Force;$f.Backup=$rel;$f.Same=$false}
        New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Copy-Item -LiteralPath $keep -Destination $dst -Force;Set-MMWorkshopStamp $f $dst;$done++
    }
    return [pscustomobject]@{Done=$done;Lost=$lost}
}
function Invoke-MMWorkshopReapply([string]$Game,[string]$Id) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');if($Id -notmatch '^[0-9a-f]{32}$'){throw '잘못된 설치 번호'}
    Assert-MMStopped $Game
    $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $Id;$record=Read-MMJson (Join-Path $pkgDir 'record.json');if(!$record -or $record.State -ne 'Installed'){throw '설치 기록을 찾지 못했습니다.'}
    $done=0;$lost=0;foreach($p in @($record.Parts | Where-Object {$_.Kind -eq 'WorkshopPatch'})){$r=Set-MMWorkshopPatchApplied $Game $p $pkgDir;$done+=$r.Done;$lost+=$r.Lost}
    Save-MMInstallRecord $Game $record
    return [pscustomobject]@{Id=$Id;Name=$record.Name;Done=$done;Lost=$lost}
}
function Undo-MMWorkshopPatch([string]$Game,$Part,[string]$PkgDir,[string]$Vault) {
    $ws=Join-Path (Get-MMWorkshopRoot $Game) ([string]$Part.WorkshopId);if(!(Test-Path -LiteralPath $ws -PathType Container)){return ($Part.Title+': 창작마당 폴더가 없어(구독 해지) 되돌릴 파일이 없습니다.')}
    $noOriginal=0;$steam=0
    foreach($f in @($Part.Files)){
        $dst=Get-MMWorkshopPath $Game $Part $f;$rel='workshop\'+$Part.WorkshopId+'\'+$f.Target
        if((Test-Path -LiteralPath $dst -PathType Leaf) -and !(Test-MMWorkshopFile $dst $f)){$steam++;continue}   # Steam already put its own file back
        if($f.Same -and !$f.Backup){$noOriginal++;continue}   # was patched before the record: no original to put back
        if(Test-Path -LiteralPath $dst -PathType Leaf){$to=Join-Path $Vault $rel;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Move-Item -LiteralPath $dst -Destination $to -Force}
        if($f.Backup){Copy-Item -LiteralPath (Join-Path $PkgDir ('backup\'+$f.Backup)) -Destination $dst -Force}
    }
    $out=@()
    if($noOriginal){$out+=($Part.Title+': 설치 전부터 패치되어 있던 파일 '+$noOriginal+'개는 원본이 없어 그대로 두었습니다. 원래대로 하려면 Steam 에서 이 모드 구독을 해지했다가 다시 구독하세요.')}
    if($steam){$out+=($Part.Title+': Steam 이 이미 원본으로 되돌린 파일 '+$steam+'개는 그대로 두었습니다.')}
    return ($out -join ' ')
}
function Get-MMInstalledRoot([string]$Game){return Join-Path $Game 'LOH_ModManager\Installed'}
function Get-MMInstallRecords([string]$Game) {
    $root=Get-MMInstalledRoot $Game;if(!(Test-Path -LiteralPath $root)){return @()}
    return @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name -match '^[0-9a-f]{32}$'} | ForEach-Object {Read-MMJson (Join-Path $_.FullName 'record.json')} | Where-Object {$_ -and $_.State -eq 'Installed'} | Sort-Object Installed)
}
function Save-MMInstallRecord([string]$Game,$Record){Write-MMJson (Join-Path (Join-Path (Get-MMInstalledRoot $Game) $Record.Id) 'record.json') $Record}
# Another version of a patch that can only be in once: the install window offers to replace it (the window takes the old one out first, and puts it back when the new one does not go in).
function Get-MMSameRecipe([string]$Game,[string]$Flavor){return @(Get-MMInstallRecords $Game | Where-Object {@($_.Parts | Where-Object {$_.Flavor -eq $Flavor}).Count})}
# Copy the chosen parts. Every replaced file is backed up first; the record lists what was written so removal is exact.
# An archive holding several mods (합본: self-install folders of different products) installs as one record per mod, so each is its own
# card and switches, goes, updates and goes back on its own (user decision 11-01). Anything else in the archive goes with the first.
function Install-MMPackageSplit([string]$Game,[string]$StageId,$Groups,$Options,[string]$Replaces){
    $done=@()
    try{
        for($i=0;$i -lt $Groups.Count;$i++){$g=$Groups[$i]
            $done+=(Install-MMPackage $Game $StageId @($g.Keys) $Options $Replaces -KeepStage:($i -lt $Groups.Count-1) -Name $g.Name)
        }
    }catch{
        $failure=$_
        foreach($r in $done){try{$null=Uninstall-MMPackage $Game $r.Id;Remove-Item -LiteralPath (Join-Path (Get-MMInstalledRoot $Game) $r.Id) -Recurse -Force -ErrorAction SilentlyContinue}catch{}}   # not a removal the player made: no 휴지통 entry
        throw $failure
    }
    $first=$done[0]
    return [pscustomobject]@{Id=$first.Id;Ids=@($done | ForEach-Object {$_.Id});Name=(($done | ForEach-Object {$_.Name}) -join ', ');DllOff=@($done | ForEach-Object {$_.DllOff} | Where-Object {$_});Parts=@($done | ForEach-Object {$_.Parts} | Where-Object {$_})
        Files=[int](($done | Measure-Object -Property Files -Sum).Sum);Selection=@($done | ForEach-Object {$_.Selection} | Where-Object {$_});SelectionNote=(@($done | ForEach-Object {$_.SelectionNote} | Where-Object {$_} | Select-Object -Unique) -join ' ')
        Waiting=@($done | ForEach-Object {$_.Waiting} | Where-Object {$_});Note=(@($done | ForEach-Object {$_.Note} | Where-Object {$_}) -join ' ');Blocked=@($done | ForEach-Object {$_.Blocked} | Where-Object {$_})}
}
function Install-MMPackage([string]$Game,[string]$StageId,[string[]]$Keys,$Options=$null,[string]$Replaces='',[switch]$KeepStage,[string]$Name='') {   # Replaces: the record '지금 업데이트' took out first (its changed settings come along); Name: this record's name (one mod of a 합본)
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    if(!(Test-Path -LiteralPath (Join-Path $Game 'ThreeKingdom.exe'))){throw 'ThreeKingdom.exe가 있는 게임 폴더를 선택하세요.'}
    Assert-MMStopped $Game
    $owner=Read-MMJson (Join-Path $Game 'LOH_ModManager\Owner.json');if(!$owner -or $owner.Name -ne 'LOH_ModManager'){throw '먼저 모드매니저에서 한 번 저장해 공용 입구를 설정해 주세요.'}
    $plan=Get-MMPackagePlan $Game $StageId;$stage=Get-MMStagePath $Game $StageId;$content=Join-Path $stage 'content';if($Name){$plan.Name=$Name}
    $chosen=@($plan.Components | Where-Object {$_.Key -in $Keys -and !$_.Blocked -and $_.Kind -ne 'Unsupported'})
    if(!$chosen.Count){throw '설치할 갈래를 하나 이상 고르세요.'}
    if(@($chosen | Where-Object {$_.PSObject.Properties['Replaces'] -and $_.Replaces}).Count){throw '이전 버전이 아직 설치돼 있습니다. 이전 버전을 뺀 뒤 다시 설치해 주세요.'}   # the window takes it out first
    if($plan.Kind -eq 'HeroesVow'){$chosen=@(Add-MMHeroesVowHost $plan $chosen $content $stage $Game)}
    if($plan.Kind -eq 'Treasury' -and @($chosen | Where-Object {$_.Treasury}).Count -ne 3){throw '세력금고는 데이터·DLL·게임 연결 정보 셋을 함께 넣어야 동작합니다.'}
    if(@($chosen | Where-Object {$_.Group -eq 'seaway'}).Count -gt 1){throw '뱃길 패치는 항구 있는 버전과 항구 없는 버전 중 하나만 넣을 수 있습니다.'}
    foreach($g in @($chosen | Where-Object {$_.Kind -eq 'SelfInstall' -and $_.Group} | Group-Object Group | Where-Object {$_.Count -gt 1})){throw ($g.Group[0].ModName+': 종류 하나만 넣을 수 있습니다.')}
    $mods=@($chosen | Where-Object {$_.Kind -eq 'SelfInstall'} | Group-Object Product)
    if($mods.Count -gt 1){
        $others=@($chosen | Where-Object {$_.Kind -ne 'SelfInstall'} | ForEach-Object {$_.Key})
        $groups=@(for($i=0;$i -lt $mods.Count;$i++){$c=$mods[$i].Group[0];[pscustomobject]@{Keys=@($mods[$i].Group | ForEach-Object {$_.Key})+$(if($i -eq 0){$others}else{@()});Name=($c.ModName+$(if($c.Version){'_v'+$c.Version}))}})
        return (Install-MMPackageSplit $Game $StageId $groups $Options $Replaces)
    }
    $chosen=@($chosen | ForEach-Object {if($_.Kind -eq 'SelfInstall'){$_.SubParts}else{$_}})   # a self-install folder: its DLL mod, data, game files
    $id=[guid]::NewGuid().ToString('N');$pkgDir=Join-Path (Get-MMInstalledRoot $Game) $id;$backup=Join-Path $pkgDir 'backup'
    $null=Assert-MMPath $Game $pkgDir;New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $active=@(Get-MMInstallRecords $Game);$written=[Collections.Generic.List[object]]::new();$parts=@();$externals=@();$notes=@();$restack=$null;$linkTool=$null
    # the first managed patcher: keep this build's clean GameAssembly.dll before anything rewrites it (a later rebuild starts from it)
    if(@($chosen | Where-Object {$_.Kind -in @('Patcher','ScriptPatch')}).Count -and !@($active | Where-Object {Test-MMPatchRecord $_}).Count){try{$null=Get-MMPristine $Game}catch{}}
    try{
        foreach($c in $chosen){
            if($c.Kind -eq 'Patcher'){$part=Install-MMAllInOne $Game $plan.Name $c $content $id $Options;$notes+=$part.Note;$part.Remove('Note');$parts+=$part;continue}
            if($c.Kind -eq 'FilePatch'){$part=Install-MMFilePatch $Game $plan.Name $c $content $id;$notes+=$part.Note;$part.Remove('Note');$parts+=$part;continue}
            if($c.Kind -eq 'WorkshopPatch'){$parts+=(Install-MMWorkshopPatch $Game $c $content $pkgDir $written);continue}
            if($c.Kind -eq 'ScriptPatch'){$stacked=@($active | Where-Object {Test-MMPatchRecord $_}).Count -gt 0
                $part=Install-MMScriptPatch $Game $plan.Name $c $content $id $Options $backup $active $stacked;$notes+=$part.Note;$part.Remove('Note');$parts+=$part;if(($stacked -and $c.Flavor -notin @('capunlock','promise','guide')) -or $part.Place){$restack=$c}   # 양식·가이드 패치: put on top as the game is (가이드 자리 찾기: below)
                continue}
            $original=''
            if($c.Kind -eq 'Link'){
                # keep the package (its python remakes the profile later), then make this game's profile into the stage
                $linkTool='LOH_ModManager\Tools\'+(Get-MMSafeName $plan.Name)+'-'+$id.Substring(0,8);$tool=Assert-MMPath $Game (Join-Path $Game $linkTool)
                New-Item -ItemType Directory -Path (Split-Path $tool -Parent) -Force | Out-Null;Copy-Item -LiteralPath $(if($c.Root){Join-Path $content $c.Root}else{$content}) -Destination $tool -Recurse
                $made=Join-Path $content '_mm';New-Item -ItemType Directory -Path $made -Force | Out-Null
                $null=New-MMTreasuryProfile $tool $Game (Join-Path $made 'Treasury.profile') (Join-Path $pkgDir 'link.log')
                $c=[pscustomobject]@{Key=$c.Key;Kind='Link';Title=$c.Title;Suffix='';Files=@([pscustomobject]@{Source='_mm\Treasury.profile';Target='Treasury.profile'})}
            }
            if($c.Kind -eq 'ProxyDll'){
                $absorb=$true;if($null -ne $Options){$v=if($Options -is [hashtable]){$Options['absorb:'+$c.Title.ToLowerInvariant()]}else{$Options.PSObject.Properties['absorb:'+$c.Title.ToLowerInvariant()].Value};if($null -ne $v){$absorb=[bool]$v}}
                if(!$absorb -and $c.MustAbsorb){throw ($c.Title+' 은 모드매니저 입구와 이름이 같아 흡수로만 넣을 수 있습니다.')}
                if($absorb){$original=$c.Title;$files=@($c.Files | ForEach-Object {[pscustomobject]@{Source=$_.Source;Target=$_.Target}});$files[0].Target=(Get-MMAbsorbedName $c.Title $id)
                    $c=[pscustomobject]@{Key=$c.Key;Kind='AbsorbedDll';Title=$c.Title;Suffix='';Files=$files}}
            }
            $entries=@()
            foreach($f in $c.Files){
                $src=Join-Path $content $f.Source;$dst=Assert-MMPath $Game (Join-Path $Game $f.Target);$saved=$null;$previous=''
                if(Test-Path -LiteralPath $dst -PathType Leaf){
                    $saved=$f.Target;$copy=Join-Path $backup $f.Target;New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null;Copy-Item -LiteralPath $dst -Destination $copy -Force
                    foreach($r in $active){foreach($p in $r.Parts){foreach($e in $p.Files){if($e.Target -eq $f.Target){$previous=$r.Id}}}}
                }
                New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Copy-Item -LiteralPath $src -Destination $dst -Force
                if($c.Kind -in @('Data','HostPlugin') -and $f.Target -match $script:MMModInfoTarget){$null=Repair-MMModInfo $dst}   # a list, or the game skips the mod
                $hash=Get-MMHash $dst;$written.Add([pscustomobject]@{Path=$dst;Backup=$saved;Hash=$hash})
                $entries+=[ordered]@{Target=$f.Target;Sha256=$hash;Backup=$saved;Previous=$previous}
            }
            $part=[ordered]@{Key=$c.Key;Kind=$c.Kind;Title=$c.Title;Suffix=$c.Suffix;ExternalId='';Files=$entries;Original=$original}
            if($c.PSObject.Properties['Product'] -and $c.Product){$part.Product=[string]$c.Product;$part.Variant=[string]$c.Variant}   # which kind was picked: an update picks it again
            if($c.Kind -eq 'Link'){$part.Tool=$linkTool;$part.Assembly=Get-MMAssemblyStamp $Game}
            if($c.Kind -in @('ExternalDll','AbsorbedDll')){$part.ExternalId='ext_'+[guid]::NewGuid().ToString('N');$externals+=[pscustomobject]@{Id=$part.ExternalId;Name=($plan.Name+' · '+$c.Title);Path=(Join-Path $Game $c.Files[0].Target)}}
            $parts+=$part
        }
        # the mod's own loh-mod.json beside its files in the archive (not one put in the game): kept with the record, so the install's
        # row knows its post, version and notes (삼혼비급탭 keeps it in 기능\soulbook as metadata, 11-02)
        try{foreach($c in $chosen){$f=@($c.Files)[0];if(!$f -or !$f.Source){continue};$dir=Split-Path (Join-Path $content $f.Source) -Parent
            while($dir -and $dir.Length -ge $content.Length){$m=Join-Path $dir 'loh-mod.json';if(Test-Path -LiteralPath $m -PathType Leaf){Copy-Item -LiteralPath $m -Destination (Join-Path $pkgDir 'loh-mod.json') -Force;break};$dir=Split-Path $dir -Parent}
            if(Test-Path -LiteralPath (Join-Path $pkgDir 'loh-mod.json')){break}}}catch{}
        $record=[ordered]@{Format='LOH_ModManager.Installed 1';Id=$id;Name=$plan.Name;Source=$plan.Source;Installed=(Get-Date).ToString('s');State='Installed';Doc=$plan.Doc;DocName=$plan.DocName;Parts=$parts}
        Save-MMInstallRecord $Game $record   # first: the registration below already counts this record's data folders as its own
        $dllOff=@()
        if($externals.Count){
            $choices=@{_add=@($externals | ForEach-Object {[pscustomobject]@{Id=$_.Id;Name=$_.Name;Path=$_.Path;Route=(Get-MMJoinRoute $Game)}})}
            $onNames=@{};foreach($m in @(Get-MMCatalog $Game | Where-Object {$_.Enabled})){$onNames[[IO.Path]::GetFileName([string]$m.Path).ToLowerInvariant()]=$true}
            foreach($e in $externals){$on=!$onNames.ContainsKey([IO.Path]::GetFileName($e.Path).ToLowerInvariant());$choices[$e.Id]=$on;if(!$on){$dllOff+=$e.Name}}   # same name already on: in, switched off
            $null=Invoke-MMApply $Game $choices
        }
    }catch{
        $failure=$_
        for($i=$written.Count-1;$i -ge 0;$i--){$w=$written[$i];if((Test-Path -LiteralPath $w.Path) -and (Get-MMHash $w.Path) -eq $w.Hash){Remove-Item -LiteralPath $w.Path -Force};if($w.Backup){Copy-Item -LiteralPath (Join-Path $backup $w.Backup) -Destination $w.Path -Force}}
        if($linkTool){Remove-Item -LiteralPath (Join-Path $Game $linkTool) -Recurse -Force -ErrorAction SilentlyContinue}
        foreach($w in $written){$dir=Split-Path $w.Path -Parent;while($dir.Length -gt $Game.Length -and (Test-Path -LiteralPath $dir) -and !@(Get-ChildItem -LiteralPath $dir -Force).Count){Remove-Item -LiteralPath $dir -Force;$dir=Split-Path $dir -Parent}}
        Remove-Item -LiteralPath $pkgDir -Recurse -Force -ErrorAction SilentlyContinue
        if($failure.Exception.Message -eq 'MM_GUIDE_PLACE'){
            # a 가이드 patch that did not go in at the bottom: the places it could take (above each patch, from the bottom up) and how long
            # the tries may take (each a rebuild from the original: every patch's last time, 1 minute for one never timed)
            $stack=@(Get-MMPatchStack $Game);$movable=@($stack | Where-Object {!@($_.Parts | Where-Object {$_.Last}).Count})
            $one=[int]$failure.Exception.Data['Seconds'];foreach($r in $stack){$one+=(Get-MMPatchSeconds $r)}
            return [pscustomobject]@{NeedPlace=$true;Name=[string]$failure.Exception.Data['Name'];Tail=[string]$failure.Exception.Data['Tail'];StageId=$StageId
                Places=@($movable[($movable.Count-1)..0] | ForEach-Object {[string]$_.Name});Minutes=[int][Math]::Ceiling($one*$movable.Count/60.0)}
        }
        throw $failure
    }
    $placedDone=$false
    if($restack -and $restack.Flavor -eq 'guide'){
        # 자리 찾기: above each patch from the bottom up (상한해제's bytes go last anyway), one rebuild per place, the first that takes it stays.
        # A place counts only when no other patch that went in last time fails there (each try is a whole rebuild from the original, so the
        # patches already in are checked against it too — their own order stays as it was).
        $wasOk=@{};foreach($r in @(Get-MMPatchStack $Game -Skip $id)){$wasOk[$r.Id]=!$r.PSObject.Properties['LastOk'] -or [bool]$r.LastOk};$spoiled=$false
        $base=@(Get-MMPatchStack $Game -Skip $id | ForEach-Object {$_.Id});$movable=@(Get-MMPatchStack $Game -Skip $id | Where-Object {!@($_.Parts | Where-Object {$_.Last}).Count})
        $pname=$(if($restack.ModName){[string]$restack.ModName}else{$plan.Name});$placed=$null;$lastTail=[string]$(if($Options -is [hashtable]){$Options['guide_tail']}elseif($Options){$Options.guide_tail})
        for($k=$movable.Count-1;$k -ge 0;$k--){
            Write-MMProgress $Game ("패치 자리 찾는 중 — '"+$movable[$k].Name+"' 바로 위에 넣어 보는 중 ("+($movable.Count-$k)+'/'+$movable.Count+')')
            $at=[array]::IndexOf($base,$movable[$k].Id);$order=@($(if($at -gt 0){$base[0..($at-1)]}))+@($id)+@($base[$at..($base.Count-1)])
            $built=Invoke-MMPatchRebuild $Game $order;$mine=@($built.Steps | Where-Object {$_.Id -eq $id})[0]
            $broke=@($built.Steps | Where-Object {$_.Id -and $_.Id -ne $id -and !$_.Ok -and $wasOk[$_.Id]})
            if($mine -and $mine.Ok -and !$broke.Count){$placed=$at;break}
            $spoiled=[bool]($broke.Count -or ($mine -and $mine.Ok))   # this try left the game file without another patch, or with this one
            if($broke.Count){$lastTail=($broke[0].Name+' 이(가) 그 자리에서는 들어가지 않게 됩니다 — '+$broke[0].Tail)}elseif($mine){$lastTail=[string]$mine.Tail}
        }
        Write-MMProgress $Game ''
        if($null -eq $placed){
            $failedRecord=Read-MMJson (Join-Path $pkgDir 'record.json');$failedRecord.State='Failed';Save-MMInstallRecord $Game $failedRecord
            foreach($p in $parts){if($p.Tool){Remove-Item -LiteralPath (Join-Path $Game $p.Tool) -Recurse -Force -ErrorAction SilentlyContinue}}
            if($spoiled){$null=Invoke-MMPatchRebuild $Game $base}else{Save-MMPatchOrder $Game $base}   # the others as they were
            throw ("'"+$pname+"' 패치를 넣지 못했습니다. 패치가 남긴 말: "+$lastTail+'. 게임 파일은 설치 전 상태 그대로입니다.')
        }
        $first=[string]$(if($Options -is [hashtable]){$Options['guide_tail']}elseif($Options){$Options.guide_tail})
        $mineRec=Read-MMJson (Join-Path $pkgDir 'record.json');Add-MMPatchAbove $mineRec $base[$placed] $first;Save-MMInstallRecord $Game $mineRec   # it did not go in under that one
        $notes+=("'"+$pname+"'은 목록 맨 아래에서는 들어가지 않아(패치가 남긴 말: "+$first+') '+$(if($placed -gt 0){"'"+@(Get-MMPatchStack $Game | Where-Object {$_.Id -eq $base[$placed-1]})[0].Name+"' 바로 아래에 두었습니다."}else{'목록 맨 위에 두었습니다.'}))
        $restack=$null;$placedDone=$true   # the rebuild kept the order with it in its place
    }
    if($restack){
        # rebuild from the clean copy with the new patch in its place; if its own step fails the install is undone (others stay applied)
        $order=@(Get-MMPatchOrder $Game | Where-Object {$_ -ne $id});$order=$(if($restack.PreferFirst){@($id)+$order}else{$order+@($id)})
        $built=Invoke-MMPatchRebuild $Game $order
        $mine=@($built.Steps | Where-Object {$_.Id -eq $id})[0]
        if(!$mine -or !$mine.Ok){
            $failedRecord=Read-MMJson (Join-Path $pkgDir 'record.json');$failedRecord.State='Failed';Save-MMInstallRecord $Game $failedRecord
            foreach($p in $parts){if($p.Tool){Remove-Item -LiteralPath (Join-Path $Game $p.Tool) -Recurse -Force -ErrorAction SilentlyContinue}}
            Save-MMPatchOrder $Game @($order | Where-Object {$_ -ne $id})
            throw ('패치가 설치되지 않았습니다(다른 패치는 그대로 다시 적용): '+$(if($mine){$mine.Tail}else{'실행 결과 없음'})+$(if(Test-MMKeptNotOriginal $Game){' '+$script:MMKeptNotSteam}))
        }
        $notes+=('패치를 원본에서 다시 쌓음: '+(($built.Steps | ForEach-Object {$_.Name+$(if($_.Ok){' ✓'}else{' ✗'})}) -join ' → '))
    }elseif(!$placedDone -and @($parts | Where-Object {$_.Kind -in @('Patcher','ScriptPatch')}).Count){Save-MMPatchOrder $Game (@(Get-MMPatchOrder $Game | Where-Object {$_ -ne $id})+@($id))
        if(@($parts | Where-Object {$_.Kind -eq 'Patcher'}).Count){$notes+=@(Invoke-MMReapplyLast $Game $id)}   # 올인원 wrote GameAssembly.dll from its own copy: 상한해제's bytes again
        foreach($s in (Update-MMTreasuryLinks $Game).Steps){$notes+=($s.Name+': '+$s.Tail)}}   # a patcher changed GameAssembly.dll
    if($plan.Kind -eq 'HeroesVow'){try{Update-MMHeroesVowIni $Game}catch{$notes+=('공통 로더 설정(NativeModHost.ini)을 고치지 못했습니다: '+$_.Exception.Message)}}
    if($Replaces -match '^[0-9a-f]{32}$'){try{$n=Get-MMSettingsNote (Copy-MMUserSettings $Game $Replaces $id);if($n){$notes+=$n}}catch{}}
    $suffixes=@($parts | Where-Object {$_.Kind -in @('Data','HostPlugin','ScriptPatch') -and $_.Suffix} | ForEach-Object {$_.Suffix});$selection=[pscustomobject]@{Changed=@();Note=''}
    if($suffixes.Count -and (Test-MMRealInstall $Game)){try{$selection=Set-MMSelectionOn $suffixes $backup}catch{$selection=[pscustomobject]@{Changed=@();Note=('게임 모드 목록에 체크하지 못했습니다: '+$_.Exception.Message)}}}
    if(!$KeepStage){Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue}   # a 합본: the next mod is still to come from it
    $blocked=@($parts | Where-Object {$_.Blocked} | ForEach-Object {$_.Blocked | Where-Object {$_ -and $_.Why} | ForEach-Object {[string]$_.Why}})   # parts left out because another patcher holds the place
    return [pscustomobject]@{Id=$id;Name=$plan.Name;DllOff=@($dllOff);Parts=@($parts | ForEach-Object {$_.Title});Files=$written.Count;Selection=@($selection.Changed);SelectionNote=$selection.Note;Waiting=@($selection.Waiting | Where-Object {$_});Note=(@($notes | Where-Object {$_}) -join ' ');Blocked=$blocked}
}
# Remove exactly what the record wrote: files still matching their recorded hash are moved to the record's vault,
# originals come back from the backup. If a later install overwrote a file, its backup inherits our original instead.
function Uninstall-MMPackage([string]$Game,[string]$Id,[switch]$Park) {   # Park: a patcher switched off in the list (kept beside it, not in the 휴지통)
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');if($Id -notmatch '^[0-9a-f]{32}$'){throw '잘못된 설치 번호'}
    Assert-MMStopped $Game
    $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $Id;$record=Read-MMJson (Join-Path $pkgDir 'record.json');if(!$record -or $record.State -ne 'Installed'){throw '설치 기록을 찾지 못했습니다.'}
    $vault=Join-Path $pkgDir ('removed-'+(Get-Date -Format 'yyyyMMdd-HHmmss'));$kept=@();$others=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -ne $Id})
    $handed=Move-MMSharedParts $Game $record;$hostFamily=@($record.Parts | Where-Object {$_.Kind -eq 'HostPlugin'}).Count -gt 0   # before anything is taken out
    $ext=@($record.Parts | Where-Object {$_.ExternalId} | ForEach-Object {$_.ExternalId})
    if($ext.Count){$catalog=Read-MMJson (Join-Path $Game 'LOH_ModManager\Catalog.json');$present=@($ext | Where-Object {$x=$_;@($catalog.Modules | Where-Object {$_.Id -eq $x}).Count});if($present.Count){$null=Invoke-MMApply $Game @{_remove=$present}}}
    # 올인원: its files back to the originals (its own 원래대로 while it still keeps a backup, else the manager's copies); GameAssembly.dll
    # comes from the clean copy — here when no other patcher is left, else the rebuild below. The kept copy goes to the vault.
    $notes=@()
    foreach($p in @($record.Parts | Where-Object {$_.Kind -eq 'Patcher'})){
        $tool=Assert-MMPath $Game (Join-Path $Game $p.Tool)
        if(!(Test-Path -LiteralPath $tool)){$notes+='패치 사본('+$p.Tool+')이 없어 게임 파일은 되돌리지 못했습니다. 스팀 「게임 파일 무결성 확인」으로 되돌려 주세요.';continue}
        $res=Restore-MMAllInOneFiles $Game $p (Join-Path $pkgDir 'patcher.log');$notes+=@($res.Notes)
        if(!$res.Assembly -and !@($others | Where-Object {Test-MMPatchRecord $_}).Count){
            try{Copy-Item -LiteralPath (Get-MMPristine $Game) -Destination (Join-Path $Game 'GameAssembly.dll') -Force;$notes+='GameAssembly.dll 원본으로 되돌림'}
            catch{$notes+='GameAssembly.dll 원본을 찾지 못해 그대로 두었습니다 — 스팀에서 「게임 파일 무결성 확인」으로 되돌려 주세요.'}}
        $left=Get-MMAllInOneNoOriginalNote $res.NoOriginal;if($left){$notes+=$left}
        $to=Join-Path $vault $p.Tool;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Move-Item -LiteralPath $tool -Destination $to
    }
    foreach($p in @($record.Parts | Where-Object {$_.Kind -eq 'FilePatch'})){
        $notes+=(Undo-MMFilePatch $Game $p (Join-Path $pkgDir 'patcher.log'))
        $tool=Join-Path $Game $p.Tool;if(Test-Path -LiteralPath $tool){$to=Join-Path $vault $p.Tool;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Move-Item -LiteralPath $tool -Destination $to}
    }
    $otherPatches=@($others | Where-Object {Test-MMPatchRecord $_});$restackOthers=(Test-MMPatchRecord $record) -and $otherPatches.Count -gt 0
    # 무장 검색 undoes itself (GameAssembly.dll and the UI bundle) with its own restore; then our GameAssembly.dll copy is not needed
    $selfUndone=@()
    foreach($p in @($record.Parts | Where-Object {$_.Kind -eq 'ScriptPatch' -and $_.Flavor -eq 'kosearch' -and $_.Tool})){
        $tool=Assert-MMPath $Game (Join-Path $Game $p.Tool);if(!(Test-Path -LiteralPath $tool)){continue}
        $run=Invoke-MMScriptPatch $tool $Game $false (Join-Path $pkgDir 'patcher.log') (Get-MMScriptPatchArgs $p 'restore')
        if($run.Code -ne 0){throw ('패치의 복구가 실패해 지우지 않았습니다(게임 파일은 그대로, 모드매니저 기록도 설치됨 그대로): '+$run.Tail)}
        $notes+=($p.Title+': 패치의 복구로 되돌림');$selfUndone+=$p.Key
    }
    # 선부 alone: its -Restore puts back the DLL it patched and takes its MOD folder out. With other patchers the rebuild below
    # drops it instead (its restore only accepts the very file it made), and its MOD files go like any recorded file.
    if(!$restackOthers){foreach($p in @($record.Parts | Where-Object {$_.Kind -eq 'ScriptPatch' -and $_.Flavor -eq 'scenic' -and $_.Tool})){
        $tool=Assert-MMPath $Game (Join-Path $Game $p.Tool);if(!(Test-Path -LiteralPath $tool)){continue}
        $run=Invoke-MMScriptPatch $tool $Game $false (Join-Path $pkgDir 'patcher.log') (Get-MMScriptPatchArgs $p 'restore')
        if($run.Code -eq 0){$notes+=($p.Title+': 설치기의 복원으로 되돌림');$selfUndone+=$p.Key}else{$notes+=($p.Title+': 설치기 복원이 안 돼 모드매니저 백업으로 되돌림 ('+$run.Tail+')')}
    }}
    foreach($p in @($record.Parts | Where-Object {$_.Kind -in @('ScriptPatch','Link') -and $_.Tool})){$tool=Assert-MMPath $Game (Join-Path $Game $p.Tool);if(Test-Path -LiteralPath $tool){$to=Join-Path $vault $p.Tool;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Move-Item -LiteralPath $tool -Destination $to}}
    foreach($p in @($record.Parts | Where-Object {$_.Kind -eq 'WorkshopPatch'})){$n=Undo-MMWorkshopPatch $Game $p $pkgDir $vault;if($n){$notes+=$n}}
    $files=@($record.Parts | Where-Object {$_.Kind -notin @('Patcher','FilePatch','WorkshopPatch') -and $_.Key -notin $selfUndone} | ForEach-Object {$sp=$_.Kind -eq 'ScriptPatch';$_.Files | Where-Object {!($restackOthers -and $sp -and $_.Target -eq 'GameAssembly.dll')}});[array]::Reverse($files)
    $leftAlone=@()
    foreach($f in $files){
        $dst=Assert-MMPath $Game (Join-Path $Game $f.Target)
        if($f.NoBackup){$leftAlone+=$f.Target;continue}   # 기록하며 직접 설치: changed with no copy of the original
        $later=$null;foreach($r in $others){foreach($p in $r.Parts){foreach($e in $p.Files){if($e.Target -eq $f.Target -and $e.Previous -eq $Id){$later=@{Record=$r;Entry=$e}}}}}
        if($later){
            # A later install now owns this file: hand it our original (or tell it there was none) and leave the file alone.
            $laterBackup=Join-Path (Join-Path (Get-MMInstalledRoot $Game) $later.Record.Id) ('backup\'+$f.Target)
            if($f.Backup){New-Item -ItemType Directory -Path (Split-Path $laterBackup -Parent) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $pkgDir ('backup\'+$f.Backup)) -Destination $laterBackup -Force;$later.Entry.Backup=$f.Target}
            else{if(Test-Path -LiteralPath $laterBackup){Remove-Item -LiteralPath $laterBackup -Force};$later.Entry.Backup=$null}
            $later.Entry.Previous=$f.Previous;Save-MMInstallRecord $Game $later.Record;continue
        }
        if(Test-Path -LiteralPath $dst -PathType Leaf){
            if((Get-MMHash $dst) -eq $f.Sha256){$to=Join-Path $vault $f.Target;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Move-Item -LiteralPath $dst -Destination $to -Force}
            else{$kept+=$f.Target;continue}
        }
        if($f.Backup){New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $pkgDir ('backup\'+$f.Backup)) -Destination $dst -Force}
        $dir=Split-Path $dst -Parent
        while($dir.Length -gt $Game.Length -and (Test-Path -LiteralPath $dir) -and !@(Get-ChildItem -LiteralPath $dir -Force).Count){Remove-Item -LiteralPath $dir -Force;$dir=Split-Path $dir -Parent}
    }
    if($restackOthers){
        try{$built=Invoke-MMPatchRebuild $Game @(Get-MMPatchOrder $Game | Where-Object {$_ -ne $Id}) $Id;$notes+=('남은 패치를 원본에서 다시 쌓음: '+(($built.Steps | ForEach-Object {$_.Name+$(if($_.Ok){' ✓'}else{' ✗'})}) -join ' → '))}
        catch{$notes+=('남은 패치를 다시 쌓지 못했습니다: '+$_.Exception.Message)}
    }
    if($leftAlone.Count){$notes+=('백업 없이 바뀌었던 파일 '+$leftAlone.Count+'개는 그대로 두었습니다(되돌리려면 Steam 「게임 파일 무결성 확인」): '+(($leftAlone | Select-Object -First 3) -join ', '))}
    if(@($record.Parts | Where-Object {$_.ManualPatch}).Count -and @(Get-MMPatchOrder $Game).Count){Save-MMPatchOrder $Game @(Get-MMPatchOrder $Game)}   # GameAssembly.dll is the managed stack again
    if(Test-MMPatchRecord $record){Save-MMPatchOrder $Game @(Get-MMPatchOrder $Game | Where-Object {$_ -ne $Id})
        if(!$restackOthers){foreach($s in (Update-MMTreasuryLinks $Game).Steps){$notes+=($s.Name+': '+$s.Tail)}}}   # the patcher's restore changed GameAssembly.dll
    $record.State='Removed';$record | Add-Member -NotePropertyName Vault -NotePropertyValue $vault -Force;$record | Add-Member -NotePropertyName Removed -NotePropertyValue (Get-Date).ToString('s') -Force;$record | Add-Member -NotePropertyName Kept -NotePropertyValue @($kept) -Force
    if($Park){$record | Add-Member -NotePropertyName Parked -NotePropertyValue $true -Force}
    Save-MMInstallRecord $Game $record
    if($handed){$notes+=$handed};if($hostFamily){try{Update-MMHeroesVowIni $Game}catch{}}
    return [pscustomobject]@{Id=$Id;Name=$record.Name;Kept=@($kept);Vault=$vault;Note=($notes -join ' ')}
}
# ---------- 기록하며 직접 설치: the player runs a mod's own installer (or copies files by hand); the manager notes the game folder
# before and after. Before: every file's size and time (LOH_ModManager left out) and a copy of the game folder's own top files and the
# il2cpp metadata, the files installers patch in place. After: what appeared, changed or went becomes an ordinary install record, so
# '설치 기록' removes it exactly where a copy exists; a file changed without a copy is left as it is on removal and named.
function Get-MMWatchList([string]$Game){
    $skip=(Join-Path $Game 'LOH_ModManager')+'\';$map=@{}
    foreach($f in [IO.Directory]::EnumerateFiles($Game,'*',[IO.SearchOption]::AllDirectories)){
        if($f.StartsWith($skip,[StringComparison]::OrdinalIgnoreCase)){continue}
        $i=[IO.FileInfo]::new($f);$map[$f.Substring($Game.Length+1)]=@($i.Length,$i.LastWriteTimeUtc.Ticks)
    }
    return $map
}
function Save-MMWatchList($Map,[string]$Path){
    $sb=[Text.StringBuilder]::new();foreach($k in $Map.Keys){[void]$sb.Append($Map[$k][0]).Append("`t").Append($Map[$k][1]).Append("`t").Append($k).Append("`n")}
    [IO.File]::WriteAllText($Path,$sb.ToString(),[Text.UTF8Encoding]::new($false))
}
function Read-MMWatchList([string]$Path){
    $map=@{};foreach($line in [IO.File]::ReadAllLines($Path,[Text.Encoding]::UTF8)){$p=$line.Split([char]9,3);if($p.Count -eq 3){$map[$p[2]]=@([int64]$p[0],[int64]$p[1])}}
    return $map
}
function Get-MMWatching([string]$Game){
    $root=Get-MMInstalledRoot $Game;if(!(Test-Path -LiteralPath $root)){return @()}
    return @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name -match '^[0-9a-f]{32}$'} | ForEach-Object {Read-MMJson (Join-Path $_.FullName 'record.json')} | Where-Object {$_ -and $_.State -eq 'Watching'})
}
function Start-MMWatch([string]$Game,[string]$Name,[string]$Source,[string]$StageId){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    if(!(Test-Path -LiteralPath (Join-Path $Game 'ThreeKingdom.exe'))){throw 'ThreeKingdom.exe가 있는 게임 폴더를 선택하세요.'}
    Assert-MMStopped $Game
    $open=@(Get-MMWatching $Game);if($open.Count){throw ('이미 기록 중인 직접 설치가 있습니다: '+$open[0].Name)}
    if(!$Name -or !$Name.Trim()){$Name='직접 설치한 모드'}
    # its archive carries a DLL named like our entry (its installer will put it there): our entry moves to a free name first, so the
    # installer finds the place empty and its own uninstaller later finds just its own file
    $entryMoved=@()
    if($StageId){$content=Join-Path (Get-MMStagePath $Game $StageId) 'content'
        $taken=@(Get-ChildItem -LiteralPath $content -Recurse -File -ErrorAction SilentlyContinue | Where-Object {$_.Name -in $script:MMRoutes} | ForEach-Object {$_.Name.ToLowerInvariant()} | Select-Object -Unique)
        if($taken.Count -and (Test-Path -LiteralPath (Join-Path $Game 'LOH_ModManager\Owner.json')) -and @(Get-MMCatalog $Game | Where-Object {([string]$_.Route).ToLowerInvariant() -in $taken}).Count){
            $entryMoved=@((Invoke-MMApply $Game @{_avoidEntry=$taken}).EntryMoved)}}
    # the clean GameAssembly.dll kept now, before an installer may patch it (a managed patcher later rebuilds from it)
    if(Test-MMCleanAssembly (Join-Path $Game 'GameAssembly.dll')){try{$null=Get-MMPristine $Game}catch{}}
    $id=[guid]::NewGuid().ToString('N');$pkgDir=Join-Path (Get-MMInstalledRoot $Game) $id;$backup=Join-Path $pkgDir 'backup'
    $null=Assert-MMPath $Game $pkgDir;New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $copied=@(Get-ChildItem -LiteralPath $Game -File | ForEach-Object {$_.Name})
    $meta='ThreeKingdom_Data\il2cpp_data\Metadata\global-metadata.dat';if(Test-Path -LiteralPath (Join-Path $Game $meta)){$copied+=$meta}
    foreach($rel in $copied){$to=Join-Path $backup $rel;New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $Game $rel) -Destination $to -Force}
    Save-MMWatchList (Get-MMWatchList $Game) (Join-Path $pkgDir 'before.txt')
    $record=[ordered]@{Format='LOH_ModManager.Installed 1';Id=$id;Name=$Name.Trim();Source=$Source;Installed=(Get-Date).ToString('s');State='Watching';StageId=$StageId;Copied=$copied;Doc='';DocName='';Parts=@()}
    Save-MMInstallRecord $Game $record
    return [pscustomobject]@{Id=$id;Name=$record.Name;Copied=$copied.Count;EntryMoved=$entryMoved}
}
function Stop-MMWatch([string]$Game,[string]$Id){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');if($Id -notmatch '^[0-9a-f]{32}$'){throw '잘못된 설치 번호'}
    Assert-MMStopped $Game
    $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $Id;$record=Read-MMJson (Join-Path $pkgDir 'record.json');if(!$record -or $record.State -ne 'Watching'){throw '기록 중인 직접 설치를 찾지 못했습니다.'}
    $before=Read-MMWatchList (Join-Path $pkgDir 'before.txt');$after=Get-MMWatchList $Game
    $copied=@{};foreach($c in @($record.Copied)){if($c){$copied[[string]$c]=$true}}
    $added=@();$changed=@();$gone=@()
    foreach($k in $after.Keys){if(!$before.ContainsKey($k)){$added+=$k}elseif($before[$k][0] -ne $after[$k][0] -or $before[$k][1] -ne $after[$k][1]){$changed+=$k}}
    foreach($k in $before.Keys){if(!$after.ContainsKey($k)){$gone+=$k}}
    if(!($added.Count+$changed.Count+$gone.Count)){throw '게임 폴더에 바뀐 파일이 없습니다. 그 모드의 설치기를 실행했는지(또는 파일을 넣었는지) 확인한 뒤 다시 눌러 주세요. 기록은 계속됩니다.'}
    # a new folder under MODs with a ModInfo.json is a data mod: its own part, ticked in the game's list like an install
    $parts=@();$taken=@{}
    foreach($folder in @($added | Where-Object {$_ -match '^MODs\\[^\\]+\\'} | ForEach-Object {($_ -split '\\')[1]} | Select-Object -Unique)){
        $prefix='MODs\'+$folder+'\';if(@($before.Keys | Where-Object {$_.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)}).Count){continue}   # an existing mod's folder changed
        $info=Join-Path $Game ($prefix+'ModInfo.json');if(!(Test-Path -LiteralPath $info)){continue}
        $null=Repair-MMModInfo $info;$mi=Get-MMModInfo $info
        $files=@($added | Where-Object {$_.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)});foreach($f in $files){$taken[$f]=$true}
        $parts+=[ordered]@{Key=('data:'+$folder);Kind='Data';Title=$folder;Suffix=$mi.Suffix;ExternalId='';Original='';Files=@($files | ForEach-Object {[ordered]@{Target=$_;Sha256=(Get-MMHash (Join-Path $Game $_));Backup=$null;Previous=''}})}
    }
    $entries=@();$noBackup=@();$lost=@();$kept=@{}
    foreach($f in $added){if(!$taken.ContainsKey($f)){$entries+=[ordered]@{Target=$f;Sha256=(Get-MMHash (Join-Path $Game $f));Backup=$null;Previous=''}}}
    foreach($f in $changed){$b=$(if($copied.ContainsKey($f)){$f}else{$null});if($b){$kept[$b]=$true}else{$noBackup+=$f}
        $entries+=[ordered]@{Target=$f;Sha256=(Get-MMHash (Join-Path $Game $f));Backup=$b;Previous='';NoBackup=(!$b)}}
    foreach($f in $gone){if($copied.ContainsKey($f)){$kept[$f]=$true;$entries+=[ordered]@{Target=$f;Sha256='';Backup=$f;Previous=''}}else{$lost+=$f}}
    $patched=@($changed | Where-Object {$_ -eq 'GameAssembly.dll'}).Count -gt 0
    if($entries.Count){$parts+=[ordered]@{Key='recorded';Kind='Recorded';Title=$record.Name;Suffix='';ExternalId='';Original='';ManualPatch=$patched;Files=$entries}}
    # keep only the copies of files the install changed or removed
    $backup=Join-Path $pkgDir 'backup';foreach($rel in @($record.Copied)){if($rel -and !$kept.ContainsKey([string]$rel)){Remove-Item -LiteralPath (Join-Path $backup $rel) -Force -ErrorAction SilentlyContinue}}
    $notes=@()
    $owner=Read-MMJson (Join-Path $Game 'LOH_ModManager\Owner.json');$entryNames=@($owner.Entries | Where-Object {$_})
    $hit=@(@($changed)+@($gone) | Where-Object {$_ -in $entryNames});$entryHit=$hit.Count -gt 0   # put back (or moved) below
    if($patched){
        $ids=@(Get-MMPatchOrder $Game)
        if($ids.Count){Save-MMPatchOrder $Game $ids;$notes+='이 설치는 게임 본체(GameAssembly.dll)를 고쳤습니다. 모드매니저가 다른 패치를 다시 적용할 때(순서 변경·게임 업데이트)는 이 패치가 빠지니, 그때 이 설치기를 다시 실행해 기록해 주세요.'}
        else{$notes+='이 설치는 게임 본체(GameAssembly.dll)를 고쳤습니다. 제거하면 설치 전 파일로 되돌립니다.'}
    }
    if($noBackup.Count){$notes+=('백업 없이 바뀐 파일 '+$noBackup.Count+'개는 제거할 때 그대로 남습니다(되돌리려면 Steam 「게임 파일 무결성 확인」): '+(($noBackup | Select-Object -First 3) -join ', '))}
    if($lost.Count){$notes+=('설치기가 지운 파일 '+$lost.Count+'개는 백업이 없어 제거할 때 되돌리지 못합니다: '+(($lost | Select-Object -First 3) -join ', '))}
    if($record.StageId){try{$null=Remove-MMStage $Game ([string]$record.StageId)}catch{}}
    Remove-Item -LiteralPath (Join-Path $pkgDir 'before.txt') -Force -ErrorAction SilentlyContinue
    $done=[ordered]@{Format=$record.Format;Id=$Id;Name=$record.Name;Source=$record.Source;Installed=(Get-Date).ToString('s');State='Installed';Doc='';DocName='';Recorded=$true;Parts=$parts}
    Save-MMInstallRecord $Game $done
    $suffixes=@($parts | Where-Object {$_.Kind -eq 'Data' -and $_.Suffix} | ForEach-Object {$_.Suffix});$selection=[pscustomobject]@{Changed=@();Note=''}
    if($suffixes.Count -and (Test-MMRealInstall $Game)){try{$selection=Set-MMSelectionOn $suffixes $backup}catch{$selection=[pscustomobject]@{Changed=@();Note=('게임 모드 목록에 체크하지 못했습니다: '+$_.Exception.Message)}}}
    # the installer wrote over our entry: it moves to a free name now, the player's mods load again (nothing to do for them)
    if((Test-Path -LiteralPath (Join-Path $Game 'LOH_ModManager\Owner.json')) -and ($entryHit -or @(Get-MMCatalog $Game | Where-Object {$_.Route -and (Get-MMEntry (Join-Path $Game ([string]$_.Route))) -eq 'foreign'}).Count)){
        try{foreach($m in @((Invoke-MMApply $Game @{}).EntryMoved)){$notes+=('다른 모드가 '+$m.From+' 자리를 써서, 모드매니저 입구를 '+$m.To+' 로 옮겼습니다. 따로 하실 일은 없습니다.')}}
        catch{$notes+=('설치기가 모드매니저가 쓰는 '+($hit -join ', ')+' 을 바꿨습니다. 모드 관리 탭에서 저장을 한 번 눌러 주세요.')}}
    return [pscustomobject]@{Id=$Id;Name=$record.Name;Added=$added.Count;Changed=$changed.Count;Removed=$gone.Count;NoBackup=$noBackup.Count;Selection=@($selection.Changed);SelectionNote=$selection.Note;Waiting=@($selection.Waiting | Where-Object {$_});Note=($notes -join ' ')}
}
# Stop recording: the manager forgets it (files the installer already put in stay where they are).
function Remove-MMWatch([string]$Game,[string]$Id){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');if($Id -notmatch '^[0-9a-f]{32}$'){throw '잘못된 설치 번호'}
    $pkgDir=Join-Path (Get-MMInstalledRoot $Game) $Id;$record=Read-MMJson (Join-Path $pkgDir 'record.json');if(!$record -or $record.State -ne 'Watching'){throw '기록 중인 직접 설치를 찾지 못했습니다.'}
    if($record.StageId){try{$null=Remove-MMStage $Game ([string]$record.StageId)}catch{}}
    Remove-Item -LiteralPath $pkgDir -Recurse -Force
    return [pscustomobject]@{Id=$Id;Name=$record.Name}
}
# Installed packages for the window: parts, data on/off in the game list, files shared with another package.
# An install that replaced an older one ('지금 업데이트'): which record it replaced and the two versions. The old record waits in the
# 휴지통, so going back is: the new one to the 휴지통, the old one restored (the new one put back if that fails). Works for any mod.
function Set-MMReplaces([string]$Game,[string]$Id,[string]$Old,[string]$Version,[string]$NewVersion){
    $path=Join-Path (Get-MMInstalledRoot $Game) ($Id+'\record.json');$r=Read-MMJson $path;if(!$r){throw ('설치 기록이 없습니다: '+$Id)}
    $r | Add-Member -NotePropertyName Replaces -NotePropertyValue ([ordered]@{Id=$Old;Version=$Version;NewVersion=$NewVersion}) -Force
    Save-MMInstallRecord $Game $r;return [pscustomobject]@{Id=$Id;Replaces=$Old}
}
function Get-MMRollBack([string]$Game,$Record){
    if(!$Record.Replaces -or ![string]$Record.Replaces.Id){return $null}
    $old=Read-MMJson (Join-Path (Get-MMInstalledRoot $Game) ([string]$Record.Replaces.Id+'\record.json'))
    if(!$old -or $old.State -ne 'Removed' -or $old.Parked){return $null}   # restored or emptied already: nothing to go back to
    return [pscustomobject]@{Id=[string]$Record.Replaces.Id;Version=[string]$Record.Replaces.Version;NewVersion=[string]$Record.Replaces.NewVersion}
}
function Invoke-MMRollBack([string]$Game,[string]$Id){
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Assert-MMStopped $Game
    $r=Read-MMJson (Join-Path (Get-MMInstalledRoot $Game) ($Id+'\record.json'));$back=if($r){Get-MMRollBack $Game $r}
    if(!$back){throw '이전 버전이 지운 모드에 없습니다'}
    $null=Uninstall-MMPackage $Game $Id
    try{$null=Restore-MMPackage $Game $back.Id}
    catch{$e=$_.Exception.Message;try{$null=Restore-MMPackage $Game $Id}catch{};throw $e}   # the new one back: nothing changed
    $note='';try{$note=Get-MMSettingsNote (Copy-MMUserSettings $Game $Id $back.Id) -Back}catch{}   # settings changed since the update come back along
    return [pscustomobject]@{Id=$back.Id;Name=$r.Name;From=$back.NewVersion;To=$back.Version;Note=$note}
}
function Get-MMInstalled([string]$Game) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');$records=@(Get-MMInstallRecords $Game);$selection=if(Test-MMRealInstall $Game){Get-MMSelection}else{[pscustomobject]@{Known=$false;Values=@{}}};$items=@()
    # which install put each file in: a DLL part's card goes to the install that put in its <dll>.loh-mod.json (the window's DescribedBy)
    $fileOwner=@{};foreach($r in $records){foreach($p in $r.Parts){foreach($e in @($p.Files)){if($e.Target){$fileOwner[([string]$e.Target).ToLowerInvariant()]=$r.Id}}}}
    $neededBy=@{};foreach($r in $records){$need=@(Get-MMNeeds $Game $r);if(!$need.Count){continue}
        foreach($t in @($fileOwner.Keys)){$o=$fileOwner[$t];if($o -ne $r.Id -and (Test-MMNeedHit $t $need)){$neededBy[$o]=@($neededBy[$o] | Where-Object {$_})+@($r.Id)}}}
    foreach($r in $records){
        $parts=@();$missing=0;$conflicts=@()
        foreach($p in $r.Parts){
            $ws=$p.Kind -eq 'WorkshopPatch';$changed=0;$subscribed=$true
            if($ws){
                # files Steam put back (an update of the original) mean the patch has come undone: '다시 적용'
                $subscribed=Test-Path -LiteralPath (Join-Path (Get-MMWorkshopRoot $Game) ([string]$p.WorkshopId)) -PathType Container
                if($subscribed){foreach($e in $p.Files){if(!(Test-MMWorkshopFile (Get-MMWorkshopPath $Game $p $e) $e)){$changed++}}}
            }else{
                foreach($e in $p.Files){
                    if(!(Test-Path -LiteralPath (Join-Path $Game $e.Target))){$missing++}
                    foreach($o in $records){if($o.Id -ne $r.Id){foreach($q in @($o.Parts | Where-Object {$_.Kind -ne 'WorkshopPatch'})){foreach($x in $q.Files){if($x.Target -eq $e.Target){$conflicts+=($o.Name+': '+$e.Target)}}}}}
                }
            }
            $on=if($p.Kind -in @('Data','HostPlugin','ScriptPatch') -and $p.Suffix -and $selection.Known){if($selection.Values.ContainsKey($p.Suffix)){[bool]$selection.Values[$p.Suffix]}else{$false}}else{$null}
            $parts+=[pscustomobject]@{Kind=$p.Kind;Title=$p.Title;Suffix=$p.Suffix;Files=@($p.Files).Count;GameListOn=$on;ExternalId=$p.ExternalId;Original=[string]$p.Original;Summary=[string]$p.Summary;Failed=@($p.Failed | Where-Object {$_});
                WorkshopId=[string]$p.WorkshopId;Parent=[string]$p.Parent;Exts=@($p.Files | ForEach-Object {[IO.Path]::GetExtension([string]$_.Target).ToLowerInvariant()} | Select-Object -Unique);Tool=[string]$p.Tool;PromiseItems=@($p.All | Where-Object {$_});Chosen=@($p.Items | Where-Object {$null -ne $_ -and $p.Flavor -eq 'promise'});Changed=$changed;Subscribed=$subscribed;NoOriginal=@($p.Files | Where-Object {$_.Same -and !$_.Backup}).Count
                DescribedBy=$(if($p.ExternalId -and @($p.Files).Count){$t=[string]@($p.Files)[0].Target;$d=$fileOwner[([IO.Path]::ChangeExtension($t,'.loh-mod.json')).ToLowerInvariant()];if($d -and $d -ne $r.Id){$d}else{''}}else{''})
                Flavor=[string]$p.Flavor;Menus=@($p.Menus | Where-Object {$null -ne $_});Applied=@($p.Applied | Where-Object {$null -ne $_});Blocked=@($p.Blocked | Where-Object {$_ -and $_.Why})   # 상한해제: its menus as lines of the card
                Items=$(if($p.Kind -eq 'Patcher' -and $p.Tool){$model=Get-MMAllInOneModel $Game (Join-Path $Game $p.Tool);$ch=Get-MMAllInOneChoices $Game $p;$bad=@(Get-MMAllInOneFailed $Game)   # 올인원: its questions as lines
                    @(foreach($q in @($model.Questions | Where-Object {$_.No})){$v=$ch[$q.Key];$on=Test-MMAllInOneOn $q $v;$held=$on -and $r.Yield -and $q.Key -in @($r.Yield.Items)
                        [pscustomobject]@{Key=$q.Key;Name=$q.Name;On=$on;Failed=($on -and @($q.Entries | Where-Object {$_ -in $bad}).Count -gt 0);Yielded=$held;Free=($held -and $r.Yield.Free);Info=(Get-MMAllInOneInfo $q $v)}})}else{@()})}
        }
        $items+=[pscustomobject]@{Id=$r.Id;Name=$r.Name;Installed=$r.Installed;Source=$r.Source;Doc=$r.Doc;DocName=$r.DocName;Parts=$parts;Missing=$missing;Conflicts=@($conflicts | Select-Object -Unique);Yield=$r.Yield;RollBack=(Get-MMRollBack $Game $r);NeededBy=@($neededBy[$r.Id] | Where-Object {$_} | Select-Object -Unique)
            LastOk=$(if($r.PSObject.Properties['LastOk']){[bool]$r.LastOk}else{$true});LastTail=[string]$r.LastTail;Above=@($r.Above | Where-Object {$_ -and $_.Id})}   # 패치: did it go in last time, and the patches it was seen to need to stay above
    }
    $order=@(Get-MMPatchOrder $Game);foreach($it in $items){$it | Add-Member -NotePropertyName PatchIndex -NotePropertyValue ([array]::IndexOf($order,$it.Id)) -Force}
    $items=@($items | Sort-Object @{Expression={if($_.PatchIndex -ge 0){$_.PatchIndex}else{100000}}},@{Expression={$_.Installed}})
    return [pscustomobject]@{Items=$items;PatchOrder=$order}
}
# Tick or untick a package's data mods in the game's own mod list (folders stay where they are).
function Set-MMPackageData([string]$Game,[string]$Id,[bool]$On) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');if($Id -notmatch '^[0-9a-f]{32}$'){throw '잘못된 설치 번호'}
    Assert-MMStopped $Game
    $record=@(Get-MMInstallRecords $Game | Where-Object {$_.Id -eq $Id})[0];if(!$record){throw '설치 기록을 찾지 못했습니다.'}
    $suffixes=@($record.Parts | Where-Object {$_.Kind -in @('Data','HostPlugin','ScriptPatch') -and $_.Suffix} | ForEach-Object {$_.Suffix});if(!$suffixes.Count){throw '이 설치에는 게임 목록에서 켜고 끌 데이터 모드가 없습니다.'}
    if(!(Test-MMRealInstall $Game)){return [pscustomobject]@{Changed=@();Note='시험용 게임 폴더라 게임 모드 목록은 바꾸지 않았습니다.'}}
    $backup=Join-Path (Get-MMInstalledRoot $Game) ($Id+'\selection-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
    $result=if($On){Set-MMSelectionOn $suffixes $backup}else{Set-MMSelectionOff $suffixes $backup}
    return [pscustomobject]@{Changed=@($result.Changed);Note=$result.Note;Waiting=@($result.Waiting | Where-Object {$_});Unwait=@($result.Unwait | Where-Object {$_})}
}
