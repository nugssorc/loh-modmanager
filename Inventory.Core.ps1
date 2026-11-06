# Read-only inspection. No DLL is loaded to identify its format.
function Get-MMPE([string]$Path) {
    $stream=$null;$reader=$null
    try {
        $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        $reader=[IO.BinaryReader]::new($stream)
        if($reader.ReadUInt16() -ne 0x5a4d){throw 'PE 파일 아님'}
        $stream.Position=0x3c;$pe=$reader.ReadUInt32();if($pe -gt $stream.Length-256){throw 'PE 헤더 오류'}
        $stream.Position=$pe;if($reader.ReadUInt32() -ne 0x4550){throw 'PE 서명 오류'}
        $machine=$reader.ReadUInt16();$sectionCount=$reader.ReadUInt16();$stream.Position=$pe+20;$optionalSize=$reader.ReadUInt16();$flags=$reader.ReadUInt16();$magic=$reader.ReadUInt16()
        $directory=$pe+24+$(if($magic -eq 0x20b){112}elseif($magic -eq 0x10b){96}else{throw 'PE 형식 오류'})
        $stream.Position=$directory-4;$directoryCount=$reader.ReadUInt32();$clr=0
        if($directoryCount -ge 15 -and $optionalSize -ge ($directory-($pe+24)+15*8)){$stream.Position=$directory+14*8;$clr=$reader.ReadUInt32()}
        $sections=@()
        if($sectionCount -gt 96){throw '과도한 섹션 수'}
        for($i=0;$i -lt $sectionCount;$i++){$stream.Position=$pe+24+$optionalSize+40*$i;$sections += [Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).Trim([char]0)}
        return [pscustomobject]@{Valid=$true;Machine=$machine;X64=($machine -eq 0x8664);Dll=!!($flags -band 0x2000);Managed=($clr -ne 0);Sections=$sections;Error=''}
    }catch{return [pscustomobject]@{Valid=$false;Machine=0;X64=$false;Dll=$false;Managed=$false;Sections=@();Error=$_.Exception.Message}}
    finally{if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}}
}
function Get-MMExternalType([string]$Game,[string]$Path) {
    $full=[IO.Path]::GetFullPath($Path);$name=[IO.Path]::GetFileName($full)
    $pe=Get-MMPE $full;$type='지원하지 않는 파일';$method='조회 전용';$can=$false;$reason=$pe.Error
    if($pe.Valid){
        if($name -in @('GameAssembly.dll','UnityPlayer.dll','baselib.dll','steam_api.dll','steam_api64.dll')){$type='게임 본체 DLL';$method='게임이 직접 로드';$reason='모드 DLL로 등록하지 않습니다.'}
        elseif($name -in @('hid.dll','version.dll','winhttp.dll','winmm.dll')){$type='입구·프록시 DLL';$method=$name;$reason='게임 폴더에 이 이름으로 두는 입구형 DLL 입니다. 「외부 DLL 추가」나 끌어다 놓기로 넣으면 모드매니저 입구로 흡수해(다른 이름으로 두고 모드매니저가 불러옴) 다른 입구형 모드와 함께 씁니다.'}
        elseif(@($script:MMDefinitions | Where-Object {[IO.Path]::GetFullPath((Join-Path $Game $_.Relative)) -eq $full}).Count){$type='기존 모드의 다른 버전·보조 DLL';$method='기본 모드의 DLL 지정 사용';$reason='같은 기능을 중복 등록하지 않습니다.'}
        elseif($pe.Managed){$type='.NET 플러그인';$method='BepInEx 등 전용 로더';$reason='일반 LoadLibrary로 초기화되는 DLL이 아닙니다.'}
        elseif(!$pe.X64 -or !$pe.Dll){$type='지원하지 않는 PE';$reason='x64 네이티브 DLL만 직접 등록할 수 있습니다.'}
        elseif($full.StartsWith([Environment]::GetFolderPath('Windows')+'\',[StringComparison]::OrdinalIgnoreCase)){$type='Windows 구성 DLL';$reason='운영체제 파일은 모드로 등록하지 않습니다.'}
        else{$type='네이티브 x64 DLL';$method='독립 DLL 로딩 후보';$reason='파일 형식만 확인됨. 이 방식으로 초기화되는 모드인지 제작자 안내 확인이 필요합니다.';$can=$true}
    }
    $version='';try{$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($full).FileVersion}catch{}
    $entry=if($type -eq '입구·프록시 DLL'){$name+' (파일명으로 확인)'}else{'최초 로더 확인 불가'}
    return [pscustomobject]@{Name=[IO.Path]::GetFileNameWithoutExtension($full);Path=$full;Type=$type;Method=$method;Entry=$entry;State='파일 있음 · 동작 미확인';Evidence=$reason;CanRegister=$can;Version=$version}
}
# The game's own mod list belongs to the real install. A test copy never writes it, even after a mod pack put a
# ThreeKingdom_Data folder there: the Unity player must sit next to the exe too.
function Test-MMRealInstall([string]$Game){return (Test-Path -LiteralPath (Join-Path $Game 'ThreeKingdom_Data') -PathType Container) -and (Test-Path -LiteralPath (Join-Path $Game 'UnityPlayer.dll') -PathType Leaf)}
function Get-MMSelectionPath {if($script:MMSelectionPath){return $script:MMSelectionPath};if($env:LOH_MM_SELECTION_PATH){return $env:LOH_MM_SELECTION_PATH};return Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\FreeWing\ThreeKingdom\SaveData\ModSettings'}
function Get-MMSelection([string]$Path='') {
    if(!$Path){$Path=Get-MMSelectionPath}
    $reader=$null;$missing=!(Test-Path -LiteralPath $Path -PathType Leaf)
    try {
        if($missing -or (Get-Item -LiteralPath $Path).Length -gt 1048576){throw '선택 기록 없음'}
        $reader=[IO.BinaryReader]::new([IO.File]::OpenRead($Path),[Text.UTF8Encoding]::new($false,$true));$version=$reader.ReadInt32();$auto=$reader.ReadByte();$count=$reader.ReadInt32();$values=@{};$order=[Collections.Generic.List[string]]::new()
        if($version -lt 202000000 -or $version -gt 210000000 -or $auto -gt 1 -or $count -lt 0 -or $count -gt 10000){throw '지원하지 않는 선택 기록'}
        for($i=0;$i -lt $count;$i++){$suffix=$reader.ReadString();$on=$reader.ReadByte();if(!$suffix -or $suffix.Length -gt 128 -or $on -gt 1 -or $values.ContainsKey($suffix)){throw '선택 기록 형식 오류'};$values[$suffix]=[bool]$on;$order.Add($suffix)}
        if($reader.BaseStream.Position -ne $reader.BaseStream.Length){throw '선택 기록 잔여 데이터'}
        return [pscustomobject]@{Known=$true;Missing=$false;Values=$values;Order=@($order);Auto=$auto;Path=$Path;Version=$version}
    }catch{return [pscustomobject]@{Known=$false;Missing=$missing;Values=@{};Order=@();Auto=0;Path=$Path;Version=0}}finally{if($reader){$reader.Dispose()}}
}
# Why the game's mod list was left alone. No file at all is the usual case for a fresh install: the game writes it on its first run.
# Then the result carries Waiting (to tick) / Unwait (to forget) and the window keeps them in FreshData.json until the game has run.
function Get-MMSelectionNote($Selection,[string]$Unreadable) {
    if($Selection.Missing){return '게임을 아직 한 번도 켜지 않아 게임 모드 목록 파일이 없습니다. 켜 둔 데이터 모드는 모드매니저가 기억해 두었다가, 게임을 타이틀 화면까지 한 번 켰다 끄면 자동으로 켭니다.'}
    return '게임 모드 목록 파일을 읽지 못해 '+$Unreadable
}
# The window's one list: set on/off for data mods by strIDEnd and put them in the window's order. The listed entries take the
# slots they already hold, in the new order (others stay where they are); new ones go to the end. Same safety as Set-MMSelectionOn;
# a backup already in $BackupDir (an earlier write in the same save) is kept as the original.
function Set-MMSelectionState($Values,[string[]]$Order=@(),[string]$BackupDir='',[string]$Path='') {
    if(!$Path){$Path=Get-MMSelectionPath}
    $want=@{};if($Values -is [hashtable]){foreach($k in $Values.Keys){$want[[string]$k]=[bool]$Values[$k]}}elseif($Values){foreach($p in $Values.PSObject.Properties){$want[$p.Name]=[bool]$p.Value}}
    foreach($k in @($want.Keys)){if(!$k -or $k.Length -gt 128){throw '잘못된 데이터 모드 ID'}}
    $selection=Get-MMSelection $Path
    if(!$selection.Known){
        $note=Get-MMSelectionNote $selection '데이터 모드 켜기·끄기·순서만 적용하지 못했습니다(나머지는 그대로 됨). 게임 안 모드 목록에서 바꿔 주세요.'
        if(!$selection.Missing){return [pscustomobject]@{Changed=@();Note=$note}}
        return [pscustomobject]@{Changed=@();Note=$note;Waiting=@($want.Keys | Where-Object {$want[$_]});Unwait=@($want.Keys | Where-Object {!$want[$_]});WaitingOrder=@($Order | Where-Object {$_})}
    }
    $state=@{};foreach($k in $selection.Values.Keys){$state[$k]=$selection.Values[$k]}
    $list=[Collections.Generic.List[string]]::new();foreach($s in $selection.Order){$list.Add($s)}
    foreach($k in $want.Keys){if(!$state.ContainsKey($k)){$list.Add($k)};$state[$k]=$want[$k]}
    $listed=@($Order | Where-Object {$_ -and $state.ContainsKey($_)} | Select-Object -Unique)
    $slots=@(for($i=0;$i -lt $list.Count;$i++){if($list[$i] -in $listed){$i}})
    for($i=0;$i -lt $slots.Count;$i++){$list[$slots[$i]]=$listed[$i]}
    $changed=@($want.Keys | Where-Object {!$selection.Values.ContainsKey($_) -or $selection.Values[$_] -ne $want[$_]})
    $moved=($list -join "`n") -ne (@($selection.Order) -join "`n")
    if(!$changed.Count -and !$moved){return [pscustomobject]@{Changed=@();Note=''}}
    if($BackupDir){New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null;$saved=Join-Path $BackupDir 'ModSettings';if(!(Test-Path -LiteralPath $saved)){Copy-Item -LiteralPath $Path -Destination $saved}}
    $temp=$Path+'.loh-mm-new';$writer=[IO.BinaryWriter]::new([IO.File]::Create($temp),[Text.UTF8Encoding]::new($false))
    try{$writer.Write([int]$selection.Version);$writer.Write([byte]$selection.Auto);$writer.Write([int]$list.Count);foreach($suffix in $list){$writer.Write([string]$suffix);$writer.Write([byte][int][bool]$state[$suffix])}}finally{$writer.Dispose()}
    $check=Get-MMSelection $temp
    if(!$check.Known -or $check.Order.Count -ne $list.Count -or @($want.Keys | Where-Object {$check.Values[$_] -ne $want[$_]}).Count){Remove-Item -LiteralPath $temp -Force;throw '게임 모드 목록 새 파일 확인 실패'}
    [IO.File]::Replace($temp,$Path,[NullString]::Value)
    return [pscustomobject]@{Changed=$changed;Moved=$moved;Note=''}
}
# Untick: the listed suffixes become 0 (entries stay, so the game keeps its order). Same safety as Set-MMSelectionOn.
function Set-MMSelectionOff([string[]]$Suffixes,[string]$BackupDir='',[string]$Path='') {
    if(!$Path){$Path=Get-MMSelectionPath}
    $selection=Get-MMSelection $Path
    if($selection.Missing){return [pscustomobject]@{Changed=@();Note='';Unwait=@($Suffixes | Where-Object {$_})}}   # the game's first run lists them as off anyway
    if(!$selection.Known){return [pscustomobject]@{Changed=@();Note=(Get-MMSelectionNote $selection '바꾸지 않았습니다. 게임 안 모드 목록에서 꺼 주세요.')}}
    $changed=@($Suffixes | Where-Object {$_ -and $selection.Values.ContainsKey($_) -and $selection.Values[$_]} | Select-Object -Unique)
    if(!$changed.Count){return [pscustomobject]@{Changed=@();Note=''}}
    if($BackupDir){New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null;Copy-Item -LiteralPath $Path -Destination (Join-Path $BackupDir 'ModSettings') -Force}
    $temp=$Path+'.loh-mm-new';$writer=[IO.BinaryWriter]::new([IO.File]::Create($temp),[Text.UTF8Encoding]::new($false))
    try{$writer.Write([int]$selection.Version);$writer.Write([byte]$selection.Auto);$writer.Write([int]$selection.Order.Count);foreach($suffix in $selection.Order){$writer.Write([string]$suffix);$on=if($changed -contains $suffix){0}else{[int]$selection.Values[$suffix]};$writer.Write([byte]$on)}}finally{$writer.Dispose()}
    $check=Get-MMSelection $temp
    if(!$check.Known -or @($changed | Where-Object {$check.Values[$_]}).Count -or $check.Order.Count -ne $selection.Order.Count){Remove-Item -LiteralPath $temp -Force;throw '게임 모드 목록 새 파일 확인 실패'}
    [IO.File]::Replace($temp,$Path,[NullString]::Value)
    return [pscustomobject]@{Changed=$changed;Note=''}
}
# Tick data mods in the game's own mod list (ModSettings) when the manager brings their data back, so the player
# does not have to re-check them in game. Only the listed suffixes change; order, other entries and the header stay.
# A file this reader does not fully understand is left untouched (the caller then asks the player to tick it in game).
function Set-MMSelectionOn([string[]]$Suffixes,[string]$BackupDir='',[string]$Path='') {
    if(!$Path){$Path=Get-MMSelectionPath}
    $want=@($Suffixes | Where-Object {$_} | Select-Object -Unique);$none=[pscustomobject]@{Changed=@();Note=''}
    if(!$want.Count){return $none}
    $selection=Get-MMSelection $Path
    if(!$selection.Known){return [pscustomobject]@{Changed=@();Note=(Get-MMSelectionNote $selection '체크하지 않았습니다. 게임 안 모드 목록에서 켜 주세요.');Waiting=@(if($selection.Missing){$want})}}
    $changed=@($want | Where-Object {!$selection.Values.ContainsKey($_) -or !$selection.Values[$_]})
    if(!$changed.Count){return $none}
    $order=@($selection.Order)+@($changed | Where-Object {!$selection.Values.ContainsKey($_)})
    if($BackupDir){New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null;Copy-Item -LiteralPath $Path -Destination (Join-Path $BackupDir 'ModSettings') -Force}
    $temp=$Path+'.loh-mm-new';$writer=[IO.BinaryWriter]::new([IO.File]::Create($temp),[Text.UTF8Encoding]::new($false))
    try{
        $writer.Write([int]$selection.Version);$writer.Write([byte]$selection.Auto);$writer.Write([int]$order.Count)
        foreach($suffix in $order){$writer.Write([string]$suffix);$on=if($changed -contains $suffix){1}else{[int]$selection.Values[$suffix]};$writer.Write([byte]$on)}
    }finally{$writer.Dispose()}
    $check=Get-MMSelection $temp
    if(!$check.Known -or @($changed | Where-Object {!$check.Values[$_]}).Count -or $check.Order.Count -ne $order.Count){Remove-Item -LiteralPath $temp -Force;throw '게임 모드 목록 새 파일 확인 실패'}
    [IO.File]::Replace($temp,$Path,[NullString]::Value)
    return [pscustomobject]@{Changed=$changed;Note=''}
}
function Get-MMSelectionLabel($Selection,[string]$Suffix) {
    if(!$Selection.Known){return '최근 게임 선택: 확인 불가'}
    if(!$Selection.Values.ContainsKey($Suffix)){return '최근 게임 선택: 기록 없음'}
    if($Selection.Values[$Suffix]){return '최근 게임 선택: 켬'};return '최근 게임 선택: 끔'
}
function Get-MMInstallSource([string]$Game,[string]$Path) {
    $g=[IO.Path]::GetFullPath($Game).TrimEnd('\');$p=[IO.Path]::GetFullPath($Path)
    $workshop=Join-Path (Split-Path (Split-Path $g -Parent) -Parent) 'workshop\content\3020510'
    if($p.StartsWith($workshop+'\',[StringComparison]::OrdinalIgnoreCase)){
        $part=$p.Substring($workshop.Length+1).Split('\')[0]
        if($part -match '^\d+$'){return '창작마당 경로'};return '로컬 · 창작마당 폴더'
    }
    if($p.StartsWith($g+'\',[StringComparison]::OrdinalIgnoreCase)){return '로컬 설치'}
    return '로컬 · 외부 경로'
}
function Get-MMOldEditorCheck([string]$Game,[string]$Path) {
    $g=[IO.Path]::GetFullPath($Game).TrimEnd('\');$p=[IO.Path]::GetFullPath($Path);$folder=Join-Path $g 'LOH_IngameEditor';$name=[IO.Path]::GetFileName($p)
    $reason='확인된 구버전 DLL이 아닙니다.';$eligible=$false
    if((Split-Path $p -Parent) -eq $folder -and $name -match '^LOH_IngameEditor_v(020|030)\.dll$'){
        $version=[int]$Matches[1];$rows=@(Get-MMCatalog $g -IncludeHidden);$current=$rows | Where-Object {$_.Id -eq 'editor'} | Select-Object -First 1
        $pe=Get-MMPE $p;$currentName=[IO.Path]::GetFileName([string]$current.Path)
        if(@($rows | Where-Object {$_.Path -eq $p}).Count){$reason='현재 등록된 DLL이므로 구버전 정리로 제거할 수 없습니다.'}
        elseif(!$current -or !$current.Present -or $currentName -notmatch '^LOH_IngameEditor_v(\d{3})\.dll$'){$reason='사용 중인 최신 만물상 DLL을 확인하지 못했습니다.'}
        elseif([int]$Matches[1] -le $version){$reason='현재 버전보다 이전인 DLL만 정리합니다.'}
        elseif(!$pe.Valid -or !$pe.X64 -or !$pe.Dll -or $pe.Managed){$reason='기존 x64 네이티브 DLL 형식이 아닙니다.'}
        else{
            $references=@();$candidates=@((Join-Path $folder '만물상_켜기.exe'))+@('hid.dll','version.dll','winhttp.dll','winmm.dll' | ForEach-Object {Join-Path $g $_})
            foreach($file in $candidates){if(Test-Path -LiteralPath $file -PathType Leaf){$bytes=[IO.File]::ReadAllBytes($file);if([Text.Encoding]::ASCII.GetString($bytes).Contains($name) -or [Text.Encoding]::Unicode.GetString($bytes).Contains($name)){$references+=$file}}}
            if($references.Count){$reason='기존 실행 도구 또는 입구가 이 파일을 참조합니다: '+($references -join ', ')}
            else{$eligible=$true;$reason='현재 만물상: '+$currentName+' / 이 구버전 DLL과 같은 이름의 .log만 백업 제거합니다. V040·설정·데이터·등록은 유지합니다.'}
        }
    }
    return [pscustomobject]@{Eligible=$eligible;Reason=$reason}
}
function Get-MMInventory([string]$Game) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    $items=[Collections.Generic.List[object]]::new();$roots=@((Join-Path $Game 'MODs'));$linkedData=@{};$snapshot=Get-MMSnapshot $Game;$selection=Get-MMSelection
    foreach($row in $snapshot.Rows){
        $dataPath='';$state=if($row.Present){'DLL 파일 있음'}else{'DLL 파일 없음'}
        if($row.Data){$dataPath=Join-Path $Game ('MODs\'+$row.Data);$linkedData[[IO.Path]::GetFullPath($dataPath)]=$true
            if(!(Test-Path -LiteralPath $dataPath)){$dataPath=Join-Path $Game ('LOH_ModManager\DisabledData\'+$row.Id+'\'+$row.Data)}
            $state+=' · '+$row.DataState+' · '+(Get-MMSelectionLabel $selection $row.Suffix)
        }
        if(!$row.Present -and (!$dataPath -or !(Test-Path -LiteralPath $dataPath))){continue}
        $items.Add([pscustomobject]@{Key=('managed:'+$row.Id);Kind='managed';ModuleId=$row.Id;Name=$row.Name;Path=$row.Path;DataPath=$dataPath;Type=$(if($row.Data){'DLL + 데이터·에셋 모드'}else{'DLL 모드'});Method=$(if($row.Data){'지정 DLL 입구 + 게임 모드 목록'}else{'지정 DLL 입구'});Entry=('지정: '+$row.Route+' / 현재: '+$row.CurrentEntry);State=$state;Evidence=('DLL: '+$row.Path+$(if($dataPath){' / 데이터: '+$dataPath})+' / 다음 실행: '+$(if($row.Enabled){'켬'}else{'끔'}));CanRegister=$false;CanRemove=(!$row.External);RemoveHint='관리 등록과 게임 내부 파일을 백업 보관 후 제거합니다. 게임 밖의 원본 DLL은 남깁니다.';Version=$row.Version})
    }
    $steamapps=Split-Path (Split-Path $Game -Parent) -Parent
    $workshop=Join-Path $steamapps 'workshop\content\3020510'
    if(Test-Path -LiteralPath $workshop){$roots += $workshop}
    foreach($root in $roots){
        if(!(Test-Path -LiteralPath $root)){continue}
        $infos=@(Get-ChildItem -LiteralPath $root -Directory | ForEach-Object {
            $direct=Join-Path $_.FullName 'ModInfo.json'
            if(Test-Path -LiteralPath $direct){Get-Item -LiteralPath $direct}
            elseif(!($_.Attributes -band [IO.FileAttributes]::ReparsePoint)){Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object {$p=Join-Path $_.FullName 'ModInfo.json';if(Test-Path -LiteralPath $p){Get-Item -LiteralPath $p}}}
        })
        foreach($infoPath in $infos){try{
            $info=@(Read-MMJson $infoPath.FullName)[0];$folder=$infoPath.DirectoryName;$name=$info.nameKr;if(!$name){$name=$info.name};if(!$name){$name=$infoPath.Directory.Name}
            if($linkedData.ContainsKey([IO.Path]::GetFullPath($folder))){continue}
            $bundles=@(Get-ChildItem -LiteralPath $folder -Directory | Where-Object {$_.Name -like 'AssetBundles*'})
            $type=if($bundles.Count){'데이터·에셋 모드'}else{'데이터 모드'}
            $workshopId='';$kind='local'
            if($root -eq $workshop){$relative=$folder.Substring($workshop.Length).TrimStart('\');$workshopId=$relative.Split('\')[0];$kind=if($workshopId -match '^\d+$'){'workshop'}else{'workshop-manual'}}
            $protected=($info.strIDEnd -eq 'fat' -or $kind -eq 'workshop')
            $hint=if($kind -eq 'workshop'){'Steam 창작마당에서 구독 해제해야 재설치되지 않습니다. 이 창에서는 삭제하지 않습니다.'}elseif($info.strIDEnd -eq 'fat'){'공식 제작 예제는 모드 제작에 사용하므로 보호합니다.'}else{'폴더를 백업 보관 후 설치 위치에서 제거할 수 있습니다.'}
            $items.Add([pscustomobject]@{Key=('data:'+$folder);Kind=$kind;ModuleId='';WorkshopId=$workshopId;Name=$name;Path=$folder;DataPath=$folder;Type=$type;Method='게임 모드 목록';Entry='게임 모드 로더 · DLL 연계 미확인';State=('설치됨 · '+(Get-MMSelectionLabel $selection ([string]$info.strIDEnd)));Evidence=('ModInfo.json / ID 구간 '+$info.strIDEnd+' / '+$hint);CanRegister=$false;CanRemove=(!$protected);RemoveHint=$hint;Version=''})
        }catch{}}
    }
    $core=Join-Path $Game 'GameAssembly.dll'
    if(Test-Path -LiteralPath $core){$pe=Get-MMPE $core;if($pe.Sections -contains '.lohmod'){
        $items.Add([pscustomobject]@{Name='올인원 코드 패치 흔적';Path=$core;Type='게임 본체 수정';Method='GameAssembly.dll 내부 코드';Entry='별도 입구 없음';State='.lohmod 섹션 확인';Evidence='현재 게임 DLL에 추가 코드 섹션이 있음. 개별 패치의 효과까지 검증한 것은 아닙니다.';CanRegister=$false;Version=''})
    }}
    $patchPath=Join-Path $Game '패처백업\올인원자동.json';$patch=Read-MMJson $patchPath
    if($patch){
        $items.Add([pscustomobject]@{Name='올인원 설치 기록 (한글·폰트·군단전 등)';Path=$patchPath;Type='본체·데이터·에셋 교체';Method='패치가 원본 파일 수정';Entry='별도 입구 없음';State='선택 기록 확인 · 효과 개별 미검증';Evidence=('kr='+$patch.choices.kr+', aiwar='+$patch.choices.aiwar+', tactics='+$patch.choices.tactics+', formation='+$patch.choices.formation);CanRegister=$false;Version=[string]$patch.build})
    }
    $dlls=[Collections.Generic.List[IO.FileInfo]]::new()
    foreach($p in @(Get-ChildItem -LiteralPath $Game -Filter '*.dll' -File)){$dlls.Add($p)}
    foreach($directory in @(Get-ChildItem -LiteralPath $Game -Directory | Where-Object {$_.Name -notmatch '^(ThreeKingdom_Data|MODs|MODProjects|ModAuthoring|패처백업|.*백업.*|.*Backup.*|\.loh.*|LOH_ModManager)$'})){
        if($directory.Attributes -band [IO.FileAttributes]::ReparsePoint){continue}
        foreach($p in @(Get-ChildItem -LiteralPath $directory.FullName -Filter '*.dll' -File)){$dlls.Add($p)}
    }
    $plugins=Join-Path $Game 'BepInEx\plugins'
    if(Test-Path -LiteralPath $plugins){foreach($p in @(Get-ChildItem -LiteralPath $plugins -Filter '*.dll' -File -Recurse)){$dlls.Add($p)}}
    $known=@($snapshot.Rows | ForEach-Object {$_.Path})
    foreach($p in @($dlls.ToArray() | Sort-Object FullName -Unique)){
        if($p.FullName -in $known -or $p.Name -in @('baselib.dll','UnityPlayer.dll','GameAssembly.dll','steam_api.dll','steam_api64.dll')){continue}
        if($p.Name -in @('hid.dll','version.dll','winhttp.dll','winmm.dll') -and (Get-MMEntry $p.FullName) -ne 'foreign'){continue}
        $typed=Get-MMExternalType $Game $p.FullName
        if($p.Name -match '^LOH_IngameEditor_v(020|030)\.dll$' -and $p.DirectoryName -eq (Join-Path $Game 'LOH_IngameEditor')){
            $check=Get-MMOldEditorCheck $Game $p.FullName
            $typed.Type='만물상 구버전 DLL';$typed.Method='파일 단위 구버전 정리';$typed.Evidence=$check.Reason
            $typed | Add-Member -NotePropertyName Key -NotePropertyValue ('obsolete-editor:'+$p.FullName)
            $typed | Add-Member -NotePropertyName Kind -NotePropertyValue 'obsolete-editor'
            $typed | Add-Member -NotePropertyName CanRemove -NotePropertyValue $check.Eligible
            $typed | Add-Member -NotePropertyName RemoveHint -NotePropertyValue $check.Reason
        }
        $items.Add($typed)
    }
    foreach($item in $items){
        $item | Add-Member -NotePropertyName Source -NotePropertyValue (Get-MMInstallSource $Game $item.Path)
        if(!$item.PSObject.Properties['Key']){$item | Add-Member -NotePropertyName Key -NotePropertyValue ('inspect:'+$item.Path)}
        if(!$item.PSObject.Properties['Kind']){$item | Add-Member -NotePropertyName Kind -NotePropertyValue 'inspect'}
        if(!$item.PSObject.Properties['CanRemove']){$item | Add-Member -NotePropertyName CanRemove -NotePropertyValue $false}
        if(!$item.PSObject.Properties['RemoveHint']){$item | Add-Member -NotePropertyName RemoveHint -NotePropertyValue '본체 패치와 출처가 불명확한 DLL은 전용 제거 방법이 필요합니다.'}
    }
    return [pscustomobject]@{Items=@($items.ToArray());Note='설치 파일과 최근 저장된 게임 선택을 구별합니다. 현재 실행의 실제 적용 여부는 DLL 상태/로그로 따로 확인합니다. 데이터 모드의 우선순위는 바꾸지 않습니다.'}
}
