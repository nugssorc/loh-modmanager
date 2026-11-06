$script:MMSource = $PSScriptRoot
# Public edition (package.py 공개판: the manager alone, no bundled mods): a known mod gets a row only when it is in the game folder.
$script:MMPublic=$false;try{$script:MMPublic=[string]((Get-Content -LiteralPath (Join-Path $PSScriptRoot '_files\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json).Edition) -eq 'public'}catch{}
. (Join-Path $PSScriptRoot 'Inventory.Core.ps1')
$script:MMRoutes=@('hid.dll','version.dll','winhttp.dll','winmm.dll')
# A free entry name for the manager: not one another mod's DLL holds, nor one about to be taken (a mod installing by hand)
function Get-MMFreeRoute([string]$Game,[string[]]$Avoid=@()){foreach($r in $script:MMRoutes){if($r -in $Avoid){continue};if((Get-MMEntry (Join-Path $Game $r)) -ne 'foreign'){return $r}};return $null}
function Get-MMDefaultRoute([string]$Game) {
    $owner=Read-MMJson (Join-Path $Game 'LOH_ModManager\Owner.json')
    if($owner.Entry -in $script:MMRoutes){return $owner.Entry}
    if((Get-MMEntry (Join-Path $Game 'version.dll')) -ne 'foreign'){return 'version.dll'}
    foreach($route in $script:MMRoutes){if((Get-MMEntry (Join-Path $Game $route)) -ne 'foreign'){return $route}}
    return 'version.dll'
}
# Version from our DLL's LOH_ModInfo export text ("LOH_ModInfo 1;…;version=0.5.1;…"), or $null when absent.
# MODs folders an install record wrote (its files there): a built-in row with the same Data folder does not move them (개발판 beside 배포판)
function Get-MMOwnedData([string]$Game){
    $set=@{};$root=Join-Path $Game 'LOH_ModManager\Installed';if(!(Test-Path -LiteralPath $root)){return $set}
    foreach($d in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)){$r=Read-MMJson (Join-Path $d.FullName 'record.json');if(!$r -or $r.State -ne 'Installed'){continue}
        foreach($p in @($r.Parts)){foreach($f in @($p.Files)){if([string]$f.Target -match '^MODs\\([^\\]+)\\'){$set[$Matches[1]]=$true}}}}
    return $set
}
function Get-MMModuleVersion([string]$Path) {
    if(!(Test-Path -LiteralPath $Path -PathType Leaf)){return $null}
    $m=[regex]::Match([Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($Path)),'LOH_ModInfo 1;[^\x00]*?version=(\d+(?:\.\d+){1,3})')
    if($m.Success){return [version]$m.Groups[1].Value};return $null
}
$script:MMDefinitions = @(
    @{ Id='dokbak'; Name='독박육아'; Relative='LOH_DokbakYuga\LOH_DokbakYuga.dll'; Version='1.2.7'; Data='독박육아'; Suffix='hsd01'; Log='LOH_DokbakYuga.log' },
    @{ Id='gift'; Name='선물·화제 취향 표시'; Relative='LOH_GiftHighlight\LOH_GiftHighlight.dll'; Version='0.5.3'; Data=''; Suffix=''; Log='LOH_GiftHighlight.log' },
    @{ Id='jade'; Name='금란선옥'; Relative='LOH_GoldenOrchidJade\LOH_GoldenOrchidJade.dll'; Version='1.1.0'; Data='금란선옥'; Suffix='gh02'; Log='LOH_GoldenOrchidJade.log' },
    @{ Id='editor'; Name='만물상 인게임 에디터'; Relative='LOH_IngameEditor\LOH_IngameEditor_v040.dll'; Version='0.4.1'; Data='LOH_IngameEditor'; Suffix='ige02'; Log='LOH_IngameEditor_v040.log' },
    @{ Id='pad'; Name='패드 모드'; Relative='LOH_PadMod\LOH_PadMod.dll'; Version=''; Data=''; Suffix=''; Log='LOH_PadMod.log' },
    @{ Id='research'; Name='사비 연구지원'; Relative='LOH_PrivateResearch\LOH_PrivateResearch.dll'; Version='0.1.2'; Data=''; Suffix=''; Log='LOH_PrivateResearch.log'; Optional=$true }
)
function Read-MMJson([string]$Path) { if (Test-Path -LiteralPath $Path -PathType Leaf) { return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json) }; return $null }
function Write-MMJson([string]$Path,$Value) { [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($true)) }
function Get-MMHash([string]$Path) { $s=[IO.File]::OpenRead($Path);$a=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($a.ComputeHash($s)).Replace('-','')}finally{$s.Dispose();$a.Dispose()} }
function Read-MMIni([string]$Path) {
    $result=@{};$section=''
    if (Test-Path -LiteralPath $Path -PathType Leaf) { try { foreach($line in [IO.File]::ReadAllLines($Path)){if($line -match '^\[([^\]]+)\]$'){$section=$Matches[1];$result[$section]=@{}}elseif($line -match '^([^=;]+)=(.*)$') {if($result.ContainsKey($section)){$result[$section][$Matches[1]]=$Matches[2]}}} } catch {return @{}} }
    return $result
}
function Get-MMEntry([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return 'empty' }
    $text=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($Path))
    foreach($pair in @(@('manager','LOH_ModManager entry 1'),@('social','LOH_SocialMods entry 1'),@('gift','LOH_GiftHighlight entry 1'),@('dokbak','LOH_DokbakYuga entry 1'),@('editor','LOH_IngameEditor entry 1'))) { if($text.Contains($pair[1])) {return $pair[0]} }
    return 'foreign'
}
function Assert-MMPath([string]$Game,[string]$Path,[switch]$Tree) {
    $g=[IO.Path]::GetFullPath($Game).TrimEnd('\');$p=[IO.Path]::GetFullPath($Path)
    if(!$p.StartsWith($g+'\',[StringComparison]::OrdinalIgnoreCase)){throw "게임 폴더 밖의 변경 경로: $p"}
    $current=$p
    while($current -and $current.Length -ge $g.Length){if((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "연결 경로는 변경하지 않습니다: $current"};$current=[IO.Path]::GetDirectoryName($current)}
    if($Tree -and (Test-Path -LiteralPath $p -PathType Container)){if(Get-ChildItem -LiteralPath $p -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw "연결 경로가 포함된 폴더: $p"}}
    return $p
}
function Get-MMProcesses {
    $result=@();$errorText=''
    try { foreach($p in [Diagnostics.Process]::GetProcessesByName('ThreeKingdom')) {
        try {$files=@($p.Modules | ForEach-Object {$_.FileName});$result += [pscustomobject]@{Pid=$p.Id;Start=$p.StartTime.ToUniversalTime();Files=$files;Readable=$true}}
        catch {$result += [pscustomobject]@{Pid=$p.Id;Start=$null;Files=@();Readable=$false}}
    }} catch {$errorText=$_.Exception.Message}
    return [pscustomobject]@{Processes=$result;Error=$errorText}
}
function Assert-MMStopped([string]$Game) {
    $probe=Get-MMProcesses
    if($probe.Error -or @($probe.Processes).Count){throw '게임을 종료한 뒤 적용해 주세요. 실행 중 DLL을 강제로 빼지 않습니다.'}
    # File mappings can remain visible even when process inspection is restricted.
    foreach($relative in @('hid.dll','version.dll','winhttp.dll','winmm.dll','LOH_DokbakYuga\LOH_DokbakYuga.dll','LOH_GiftHighlight\LOH_GiftHighlight.dll','LOH_GoldenOrchidJade\LOH_GoldenOrchidJade.dll','LOH_IngameEditor\LOH_IngameEditor_v040.dll','LOH_PrivateResearch\LOH_PrivateResearch.dll')){
        $p=Join-Path $Game $relative;if(Test-Path -LiteralPath $p){
            # a reader (the window reading a DLL version right after a save) holds the file for a moment: try a few times
            for($try=1;;$try++){try{$s=[IO.File]::Open($p,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::Read);$s.Dispose();break}catch [UnauthorizedAccessException]{throw "게임 폴더 쓰기 권한이 없습니다. 모드매니저를 관리자 권한으로 실행해 주세요: $relative"}catch{if($try -ge 6){throw "게임 종료 또는 파일 사용 상태를 확인할 수 없습니다: $relative"};Start-Sleep -Milliseconds 400}}}
    }
}
function Get-MMCatalog([string]$Game,[switch]$IncludeHidden) {
    $registry=Read-MMJson (Join-Path $Game 'LOH_ModManager\Catalog.json');$rows=@();$defaultRoute=Get-MMDefaultRoute $Game
    $oldDll=Join-Path $Game 'LOH_GiftHighlight\LOH_GiftHighlight.dll'
    $combined=(Test-Path -LiteralPath $oldDll) -and [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($oldDll)).Contains('Golden Orchid')
    foreach($def in $script:MMDefinitions){
        if(!$IncludeHidden -and @($registry.HiddenModules | Where-Object {$_.Id -eq $def.Id}).Count){continue}
        $saved=if($registry){@(@($registry.Modules)+@($registry.HiddenModules) | Where-Object {$_.Id -eq $def.Id}) | Select-Object -First 1}else{$null}
        $path=if($saved){$saved.Path}else{Join-Path $Game $def.Relative}
        if($def.Id -eq 'pad' -and !(Test-Path -LiteralPath $path) -and !$saved){$loose=Join-Path $Game 'LOH_PadMod.dll';if(Test-Path -LiteralPath $loose){$path=$loose}}   # a DLL kept elsewhere is chosen with 'DLL 위치 바꾸기'
        if($def.Id -eq 'editor' -and !$saved){$available=@(Get-ChildItem -LiteralPath (Join-Path $Game 'LOH_IngameEditor') -Filter 'LOH_IngameEditor_v*.dll' -ErrorAction SilentlyContinue | Where-Object {$_.Name -match '^LOH_IngameEditor_v\d+\.dll$'} | Sort-Object Name -Descending);if($available.Count){$path=$available[0].FullName}}
        $present=Test-Path -LiteralPath $path -PathType Leaf
        # Optional standalone installs should not add an empty row or install
        # themselves merely because another mode's settings were saved.
        if(($def.Optional -or $script:MMPublic) -and !$present -and !$saved){continue}
        $enabled=if($saved){[bool]$saved.Enabled}else{$present -or ($def.Id -eq 'jade' -and $combined)}
        $version=if($saved -and $saved.Version){$saved.Version}else{$def.Version}
        if(!$saved -and $script:MMPublic -and $present){$own=Read-MMJson (Join-Path ([IO.Path]::GetDirectoryName($path)) '설치기록.json');if($own -and $own.Version){$version=[string]$own.Version}}   # installed on its own
        if($def.Id -eq 'pad' -and $present){
            $padLog=Join-Path ([IO.Path]::GetDirectoryName($path)) $def.Log
            if(Test-Path -LiteralPath $padLog){try{$stream=[IO.File]::Open($padLog,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite);$reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8);try{$head=$reader.ReadLine();if($head -match 'LOH_PadMod\s+([0-9.]+)'){$version=$Matches[1]}}finally{$reader.Dispose()}}catch{}}
        }
        if($def.Id -eq 'gift' -and $combined){$version='0.3 통합판'}
        $payload=(Join-Path $script:MMSource ('_files\modules\'+[IO.Path]::GetFileNameWithoutExtension($path)+'\'+[IO.Path]::GetFileName($path)))
        $rows += [pscustomobject]@{Id=$def.Id;Name=$def.Name;Path=$path;Present=$present;Enabled=$enabled;Version=$version;Data=$def.Data;Suffix=$def.Suffix;Log=$def.Log;Prepared=(Test-Path -LiteralPath $payload);Combined=($def.Id -eq 'gift' -and $combined);Route=$(if($saved.Route){$saved.Route}else{$defaultRoute});External=$false}
    }
    if($registry){foreach($saved in @($registry.Modules | Where-Object {$_.External})){
        $rows += [pscustomobject]@{Id=$saved.Id;Name=$saved.Name;Path=$saved.Path;Present=(Test-Path -LiteralPath $saved.Path -PathType Leaf);Enabled=[bool]$saved.Enabled;Version=$saved.Version;Data='';Suffix='';Log='';Prepared=$false;Combined=$false;Route=$(if($saved.Route){$saved.Route}else{$defaultRoute});External=$true}
    }}
    $ordered=@();if($registry){foreach($saved in $registry.Modules){$ordered+=@($rows | Where-Object {$_.Id -eq $saved.Id})}}
    $ordered+=@($rows | Where-Object {$_.Id -notin @($ordered | ForEach-Object {$_.Id})});return $ordered
}
function Get-MMLogState($Row,$Process) {
    if(!$Row.Log){return '외부 DLL · 초기화 확인 수단 없음'}
    if(!$Process.Start){return '조회 불가'}
    $path=Join-Path ([IO.Path]::GetDirectoryName($Row.Path)) $Row.Log
    if(!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $Process.Start){return '현재 실행 초기화 미확인'}
    try{$stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite);$reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8);try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}}catch{return '로그 조회 불가'}
    if($text -match 'pid=\d+' -and $text -notmatch ('pid='+$Process.Pid+'\b')){return '현재 실행 로그 미확인'}
    if($Row.Id -eq 'editor'){$switches=[regex]::Matches($text,'mod list: (ON|OFF)');if($switches.Count -and $switches[$switches.Count-1].Groups[1].Value -eq 'OFF'){return '게임 모드 목록에서 꺼짐'}}
    if($text -match 'READY [0-9]|자가 점검 6/6|준비 완료\.') {return '초기화 확인 (현재 로그)'}
    if($text -match 'DISABLED|metadata mismatch|자가 점검 [0-5]/6'){return '호환성/초기화 오류 (로그 확인)'}
    return '초기화 대기 또는 미확인'
}
function Get-MMSnapshot([string]$Game,[switch]$NoProcess) {
    # -NoProcess: 0.5 관리창은 실행 상태를 C# 에서 직접 읽으므로 무거운 프로세스 모듈 조회를 건너뛴다.
    $rows=@(Get-MMCatalog $Game);$probe=if($NoProcess){[pscustomobject]@{Processes=@();Error=''}}else{Get-MMProcesses};$issues=[Collections.Generic.List[object]]::new();$entries=@()
    $owner=Read-MMJson (Join-Path $Game 'LOH_ModManager\Owner.json')
    foreach($name in @('hid.dll','version.dll','winhttp.dll','winmm.dll')){
        $kind=Get-MMEntry (Join-Path $Game $name);$entries += [pscustomobject]@{Name=$name;Kind=$kind}
        if($owner -and $kind -notin @('manager','foreign','empty')){$issues.Add([pscustomobject]@{Level='충돌';Text="$name 별도 입구가 다시 설치되어 끄기 설정을 우회할 수 있습니다. 공용 입구 통합을 다시 적용하세요."})}
    }
    $expectedEntries=if($owner.Entries){@($owner.Entries)}elseif($owner.Entry){@($owner.Entry)}else{@()}
    foreach($entry in $entries | Where-Object {$_.Kind -eq 'manager' -and $_.Name -notin $expectedEntries}){if($owner){$issues.Add([pscustomobject]@{Level='충돌';Text=($entry.Name+': 등록되지 않은 공용 입구가 남아 있습니다.')})}}
    # The last save put this entry in the game folder and it is gone: every DLL mod behind it silently stops
    foreach($name in @($expectedEntries | Where-Object {$_})){if(@($entries | Where-Object {$_.Name -eq $name -and $_.Kind -eq 'empty'}).Count){$issues.Add([pscustomobject]@{Level='오류';Text=('게임 폴더에 '+$name+'이 없어 DLL 모드가 실행되지 않습니다. 백신이 막았을 수 있으니 Windows 보안의 보호 기록을 확인한 뒤 저장을 눌러 주세요.')})}}
    if($probe.Error){$issues.Add([pscustomobject]@{Level='확인 불가';Text='게임 프로세스 상태를 조회하지 못했습니다.'})}
    $revision=if($owner){$owner.Revision}else{''}
    $ownedData=Get-MMOwnedData $Game
    foreach($row in $rows){
        $current='게임 종료';$currentEntry='실행 전';$health='다음 실행에서 확인';$dataState='불필요'
        if($row.Data -and $ownedData.ContainsKey($row.Data)){$dataState=$(if(Test-Path -LiteralPath (Join-Path $Game ('MODs\'+$row.Data))){'파일 있음 · 게임 적용 별도'}else{'데이터 없음'})}   # installed by a record: that one's
        elseif($row.Data){$active=Join-Path $Game ('MODs\'+$row.Data);$parked=Join-Path $Game ('LOH_ModManager\DisabledData\'+$row.Id+'\'+$row.Data)
            $dataState=if(Test-Path -LiteralPath $active){'파일 있음 · 게임 적용 별도'}elseif(Test-Path -LiteralPath $parked){'모드매니저에서 꺼 둠'}else{'데이터 없음'}
            if((Test-Path -LiteralPath $active) -and (Test-Path -LiteralPath $parked)){$issues.Add([pscustomobject]@{Level='충돌';Text=($row.Name+': 활성·보관 데이터 폴더가 동시에 있습니다.')})}
            if($owner -and $row.Enabled -and $row.Present -and $dataState -eq '데이터 없음'){$issues.Add([pscustomobject]@{Level='오류';Text=($row.Name+': 필요한 데이터 파일이 없습니다.')})}
        }
        foreach($p in $probe.Processes){
            $current='미로드';$currentEntry='미로드';$health='초기화 미확인'
            if(!$p.Readable){$current='조회 불가';$health='프로세스 접근 권한 확인';continue}
            $loaded=@($p.Files | Where-Object {[IO.Path]::GetFileName($_) -eq [IO.Path]::GetFileName($row.Path)})
            if($loaded.Count){$current='DLL 로드됨';$currentEntry='외부 로더 · 입구 미확인';$health=Get-MMLogState $row $p
                if($loaded[0] -ne $row.Path){$issues.Add([pscustomobject]@{Level='충돌';Text=($row.Name+': 다른 경로의 같은 이름 DLL이 로드되어 있습니다.')})}
                if($owner -and !$row.Enabled){$issues.Add([pscustomobject]@{Level='충돌';Text=($row.Name+': 끄기로 설정했지만 외부 입구나 수동 로더가 실행했습니다.')})}
            }
            $runtime=Read-MMIni (Join-Path $Game ('LOH_ModManager\Runtime-'+$p.Pid+'-'+$row.Route+'.ini'))
            if(!$runtime.Manager){$runtime=Read-MMIni (Join-Path $Game ('LOH_ModManager\Runtime-'+$p.Pid+'.ini'))}
            if($runtime.Manager -and $p.Start){
                $creation=([uint64]$runtime.Manager.CreationHigh -shl 32) -bor [uint64]$runtime.Manager.CreationLow
                if($creation -eq [uint64]$p.Start.ToFileTimeUtc() -and $runtime.ContainsKey($row.Id)){
                    $r=$runtime[$row.Id]
                    if([int]$r.State -in @(3,4)){$currentEntry=if([int]$r.State -eq 3){if($runtime.Manager.Entry){$runtime.Manager.Entry}else{$owner.Entry}}else{'먼저 로드됨 · 입구 미확인'};$current=if([int]$r.State -eq 4){'이미 로드됨'}else{'공용 입구로 로드됨'}
                        switch([int]$r.Detail){1{$health='초기화 대기'}2{$health='초기화 확인 (직접 응답)'}3{$health='호환성/초기화 오류'}4{$health='동작 중지'}5{$health='일부 기능만 준비됨'}6{$health='게임 데이터 모드 미적용'}}
                    }elseif([int]$r.State -eq 1 -and !$loaded.Count){$current='이번 실행에서 꺼짐';$currentEntry='미실행';$health='미실행'}
                    elseif([int]$r.State -in @(2,5,6,7)){
                        $reason=switch([int]$r.State){2{'DLL 파일 없음'}5{'다른 경로에서 같은 이름의 DLL을 사용 중'}6{'DLL을 불러오지 못함 (Windows 오류 '+$r.Error+')'}7{'등록 경로나 모드 식별자가 잘못됨'}}
                        if(!$loaded.Count){$current='로딩 실패';$health=$reason}else{$current='외부에서 DLL 로드됨'}
                        $issues.Add([pscustomobject]@{Level=$(if([int]$r.State -eq 5){'충돌'}else{'오류'});Text=($row.Name+': '+$reason)})
                    }
                    if([int]$r.Detail -eq 3){$issues.Add([pscustomobject]@{Level='오류';Text=($row.Name+': 게임 버전·함수 연결 또는 초기화 오류를 직접 보고했습니다.')})}
                    $runtimePath=Join-Path $Game ('LOH_ModManager\Runtime-'+$p.Pid+'-'+$row.Route+'.ini');if(!(Test-Path -LiteralPath $runtimePath -PathType Leaf)){$runtimePath=Join-Path $Game ('LOH_ModManager\Runtime-'+$p.Pid+'.ini')}
                    if((Get-Item -LiteralPath $runtimePath).LastWriteTimeUtc -lt [datetime]::UtcNow.AddSeconds(-15)){$health='상태 응답 지연 · 마지막 결과만 있음'}
                    if($runtime.Manager.Revision -ne $revision){$issues.Add([pscustomobject]@{Level='대기';Text='실행 중인 게임은 이전 설정을 사용 중입니다.'})}
                }
            }
        }
        $row | Add-Member -NotePropertyName CurrentEntry -NotePropertyValue $currentEntry
        $row | Add-Member -NotePropertyName Current -NotePropertyValue $current
        $row | Add-Member -NotePropertyName Health -NotePropertyValue $health
        $row | Add-Member -NotePropertyName DataState -NotePropertyValue $dataState
    }
    $seen=@{}
    foreach($row in $rows | Where-Object {$_.Enabled -and $_.Present}){if($seen.ContainsKey($row.Path)){$issues.Add([pscustomobject]@{Level='충돌';Text='동일 DLL이 중복 등록되어 있습니다.'})};$seen[$row.Path]=$true}
    if(Test-Path -LiteralPath (Join-Path $Game 'MODs\선물취향편의')){if(Test-Path -LiteralPath (Join-Path $Game 'MODs\금란선옥')){$issues.Add([pscustomobject]@{Level='충돌';Text='금란선옥 데이터가 통합판과 분리판에 중복되어 있습니다.'})}}
    if($owner -and $owner.State -eq 'Failed'){$issues.Add([pscustomobject]@{Level='오류';Text='이전 적용이 실패해 원래 파일로 복구했습니다. 원인을 확인한 뒤 다시 적용할 수 있습니다.'})}
    return [pscustomobject]@{Rows=$rows;Processes=$probe.Processes;Issues=@($issues.ToArray());Entries=$entries;Managed=($null -ne $owner -and $owner.State -ne 'Failed');Owner=$owner}
}
function Assert-MMData([string]$Game,$Row,[string]$Path) {
    $null=Assert-MMPath $Game $Path -Tree
    if(Test-Path -LiteralPath $Path){$info=@(Read-MMJson (Join-Path $Path 'ModInfo.json'));if(!$info.Count -or $info[0].strIDEnd -ne $Row.Suffix){throw ('데이터 모드 소유권 확인 실패: '+$Row.Name)}}
}
function Get-MMPlan([string]$Game,$Rows=$null) {
    $snapshot=Get-MMSnapshot $Game
    if($null -eq $Rows){$Rows=@($snapshot.Rows)}
    $routes=@($Rows | Where-Object {$_.Enabled} | ForEach-Object {$_.Route} | Select-Object -Unique)
    foreach($route in $routes){
        if($route -notin $script:MMRoutes){throw ('지원하지 않는 입구: '+$route)}
        if((Get-MMEntry (Join-Path $Game $route)) -eq 'foreign'){throw ($route+'을 다른 모드가 사용 중입니다. 다른 입구를 지정하세요. 기존 파일은 변경하지 않았습니다.')}
    }
    $retire=@($snapshot.Entries | Where-Object {$_.Kind -notin @('foreign','empty') -and $_.Name -notin $routes})
    return [pscustomobject]@{Snapshot=$snapshot;Entry=$(if($routes.Count -eq 1){$routes[0]}else{''});Routes=$routes;Retire=$retire}
}
# The manager's folder before the first save: actions like taking over a DLL, setting one aside or installing a mod make it (and their
# install records) first, so a folder with no files, or with our own install records, is ours; only one holding something else is refused
function Test-MMOwnFolder([string]$Dir){
    if(Test-Path -LiteralPath (Join-Path $Dir 'Owner.json')){return $true}
    $files=@(Get-ChildItem -LiteralPath $Dir -Recurse -File -Force -ErrorAction SilentlyContinue);if(!$files.Count){return $true}
    return @($files | Where-Object {$_.Name -eq 'record.json' -and [string](Read-MMJson $_.FullName).Format -like 'LOH_ModManager.Installed*'}).Count -gt 0
}
function Invoke-MMApply([string]$Game,$Choices,[string]$Payload=(Join-Path $script:MMSource '_files')) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    if(!(Test-Path -LiteralPath (Join-Path $Game 'ThreeKingdom.exe'))){throw 'ThreeKingdom.exe가 있는 게임 폴더를 선택하세요.'}
    Assert-MMStopped $Game
    $rows=@(Get-MMCatalog $Game);$managerDir=Join-Path $Game 'LOH_ModManager'
    $null=Assert-MMPath $Game $managerDir -Tree
    $initialSnapshot=Get-MMSnapshot $Game
    if((Test-Path -LiteralPath $managerDir) -and !(Test-MMOwnFolder $managerDir)){throw '설치 기록 없는 LOH_ModManager 폴더는 변경하지 않습니다.'}
    if($initialSnapshot.Owner -and $initialSnapshot.Owner.Name -ne 'LOH_ModManager'){throw '관리 폴더 소유권이 다릅니다.'}
    $manifest=Read-MMJson (Join-Path $Payload 'manifest.json')
    if(!$manifest){throw '배포 파일 목록이 없습니다.'}
    $registry=Read-MMJson (Join-Path $managerDir 'Catalog.json');$hidden=@($registry.HiddenModules | Where-Object {$_})
    foreach($id in @($Choices['_unhide'])){if(!$id){continue};if($id -notin @($hidden | ForEach-Object {$_.Id})){throw '복원할 관리 모드를 찾지 못했습니다.'};$rows+=@(Get-MMCatalog $Game -IncludeHidden | Where-Object {$_.Id -eq $id});$hidden=@($hidden | Where-Object {$_.Id -ne $id})}
    foreach($id in @($Choices['_hide'])){if(!$id){continue};$row=@($rows | Where-Object {$_.Id -eq $id -and !$_.External});if($row.Count -ne 1){throw '제거할 관리 모드를 찾지 못했습니다.'};$hidden+=$row[0];$rows=@($rows | Where-Object {$_.Id -ne $id})}
    $remove=@($Choices['_remove'])
    foreach($id in $remove){if($id -and !@($rows | Where-Object {$_.Id -eq $id -and $_.External}).Count){throw '기본 모드는 등록 해제할 수 없습니다.'}}
    $rows=@($rows | Where-Object {$_.Id -notin $remove})
    foreach($add in @($Choices['_add'])){if(!$add){continue}
        if($add.Id -notmatch '^ext_[0-9a-f]{32}$' -or [string]::IsNullOrWhiteSpace($add.Name)){throw '외부 DLL 등록 정보가 잘못됐습니다.'}
        $typed=Get-MMExternalType $Game $add.Path
        if(!$typed.CanRegister){throw ($typed.Type+': '+$typed.Evidence)}
        if(@($rows | Where-Object {$_.Id -eq $add.Id -or $_.Path -eq $typed.Path}).Count){throw '이미 등록된 DLL입니다.'}
        $rows += [pscustomobject]@{Id=$add.Id;Name=$add.Name;Path=$typed.Path;Present=$true;Enabled=$false;Version=$typed.Version;Data='';Suffix='';Log='';Prepared=$false;Combined=$false;Route=$add.Route;External=$true}
    }
    if($Choices.ContainsKey('_order')){
        $order=@($Choices['_order']);$ids=@($rows | ForEach-Object {$_.Id})
        if($order.Count -ne $ids.Count -or @($order | Select-Object -Unique).Count -ne $ids.Count -or @($order | Where-Object {$_ -notin $ids}).Count){throw '모드 순서가 현재 등록 목록과 다릅니다. 새로고침 후 다시 적용하세요.'}
        $rows=@(foreach($id in $order){$rows | Where-Object {$_.Id -eq $id}})
    }
    if($rows.Count -gt 32){throw '등록 가능한 DLL은 기본 모드를 포함해 32개까지입니다.'}
    $savedRoute=@{};foreach($row in $rows){$savedRoute[[string]$row.Id]=[string]$row.Route}
    foreach($row in $rows){if($Choices.ContainsKey($row.Id)){$row.Enabled=[bool]$Choices[$row.Id]};if($Choices.ContainsKey($row.Id+'_path')){$row.Path=[IO.Path]::GetFullPath([string]$Choices[$row.Id+'_path'])};if($Choices.ContainsKey($row.Id+'_route')){$row.Route=[string]$Choices[$row.Id+'_route']};if($row.Route -notin $script:MMRoutes){throw ('지원하지 않는 입구: '+$row.Route)}
        if($row.Path -match '[\r\n]'){throw '잘못된 DLL 경로입니다.'}
        if($row.Enabled -and !$row.Present -and !$row.Prepared -and !(Test-Path -LiteralPath $row.Path)){throw ('파일을 찾지 못했습니다: '+$row.Name)}
        if($row.Id -in @('gift','jade','research')){$def=$script:MMDefinitions | Where-Object {$_.Id -eq $row.Id};$row.Path=Join-Path $Game $def.Relative
            # the public edition bundles none of them: a copy the player installed on its own keeps its own version
            if(Test-Path -LiteralPath (Join-Path $Payload ('modules\'+[IO.Path]::GetFileNameWithoutExtension($def.Relative)))){$row.Version=$def.Version}
            else{$own=Read-MMJson (Join-Path $Game ([IO.Path]::GetDirectoryName($def.Relative)+'\설치기록.json'));if($own -and $own.Version){$row.Version=[string]$own.Version}}}
    }
    foreach($row in $rows | Where-Object {$_.External -and $_.Enabled}){$typed=Get-MMExternalType $Game $row.Path;if(!$typed.CanRegister){throw ($row.Name+': '+$typed.Evidence)}}
    # another mod's DLL took our entry's name (its installer wrote over it), or is about to: the rows go to a free name, so they
    # load again without the player knowing what an entry is; the other mod's file is left as its installer put it (11-02).
    # A name the player picks now is theirs: an occupied one is refused below, not moved.
    $avoid=@($Choices['_avoidEntry'] | Where-Object {$_});$entryMoved=@()
    foreach($row in @($rows | Where-Object {$_.Route -and [string]$_.Route -eq $savedRoute[[string]$_.Id] -and (([string]$_.Route -in $avoid) -or (Get-MMEntry (Join-Path $Game ([string]$_.Route))) -eq 'foreign')})){
        $from=[string]$row.Route;$free=Get-MMFreeRoute $Game (@($avoid)+@($from));if(!$free){continue}
        $row.Route=$free;if(!@($entryMoved | Where-Object {$_.From -eq $from}).Count){$entryMoved+=[pscustomobject]@{From=$from;To=$free}}
    }
    # the HeroesVow common loader the manager put in (빠른거래 · 삼혼비급탭) works only behind the version.dll entry: that entry alone
    # forwards the loader's two exports its plugins look up in 'version.dll' (build.py 0.7.2). Back there whenever the name is free
    # (0.12.44~0.12.45 could leave it on hid.dll: the plugins then stopped without a word, 11-03 디시 79360)
    foreach($row in @($rows | Where-Object {[IO.Path]::GetFileName([string]$_.Path) -eq 'HeroesVowNativeModHost.dll' -and [string]$_.Route -ne 'version.dll' -and !$Choices.ContainsKey([string]$_.Id+'_route')})){
        if('version.dll' -notin $avoid -and (Get-MMEntry (Join-Path $Game 'version.dll')) -ne 'foreign'){$entryMoved+=[pscustomobject]@{From=[string]$row.Route;To='version.dll'};$row.Route='version.dll'}}
    $activeNames=@{};foreach($row in $rows | Where-Object {$_.Enabled}){$base=[IO.Path]::GetFileName($row.Path);if($activeNames.ContainsKey($base)){throw ('동일 이름 DLL 중복 등록: '+$base)};$activeNames[$base]=$true}
    $plan=Get-MMPlan $Game $rows
    # Check what this save will use; an entry it does not copy can't block it (one DLL gone from the manager's folder stopped every save, 11-05 디시)
    foreach($file in $manifest.Files){if($file.Path -like 'entries/*' -and [IO.Path]::GetFileName($file.Path) -notin @($plan.Routes)){continue}
        $p=[IO.Path]::GetFullPath((Join-Path $Payload $file.Path));if(!$p.StartsWith([IO.Path]::GetFullPath($Payload)+'\',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $p) -or (Get-MMHash $p) -ne $file.SHA256){throw ("모드매니저 폴더의 파일이 없거나 바뀌었습니다: _files\"+([string]$file.Path).Replace('/','\')+"`n모드 문제가 아닙니다. 백신이 막았을 수 있으니 검역 기록을 확인한 뒤, 압축을 새 폴더에 다시 풀어 실행해 주세요.")}}
    $touched=@($plan.Routes)+@($plan.Retire | ForEach-Object {$_.Name})+@('LOH_ModManager\Catalog.json','LOH_ModManager\Modules.ini','LOH_ModManager\Owner.json','MODs\선물취향편의')
    foreach($row in $rows | Where-Object {$_.Id -in @('gift','jade')}){$touched+=[IO.Path]::GetDirectoryName($row.Path).Substring($Game.Length+1)}
    foreach($row in $rows){if($row.Data){foreach($rel in @(('MODs\'+$row.Data),('LOH_ModManager\DisabledData\'+$row.Id+'\'+$row.Data))){$p=Join-Path $Game $rel;Assert-MMData $Game $row $p;$touched+=$rel}}}
    $ownedData=Get-MMOwnedData $Game
    foreach($row in $rows | Where-Object {$_.Enabled -and $_.Data -and !$ownedData.ContainsKey($_.Data) -and ($_.Id -ne 'jade' -or !(Test-Path -LiteralPath (Join-Path $Payload 'data\금란선옥')))}){
        if(!(Test-Path -LiteralPath (Join-Path $Game ('MODs\'+$row.Data))) -and !(Test-Path -LiteralPath (Join-Path $Game ('LOH_ModManager\DisabledData\'+$row.Id+'\'+$row.Data)))){throw ('필요한 데이터 모드가 없습니다: '+$row.Name)}
    }
    $legacy=Join-Path $Game 'MODs\선물취향편의'
    if(Test-Path -LiteralPath $legacy){$record=Read-MMJson (Join-Path $legacy '.LOH_GiftHighlight.json');if(!$record -or $record.Name -ne 'LOH_GiftHighlight'){throw '기존 통합 데이터의 소유권을 확인하지 못했습니다.'}}
    foreach($rel in $touched){$null=Assert-MMPath $Game (Join-Path $Game $rel) -Tree}
    foreach($row in $rows | Where-Object {$_.Id -in @('gift','jade')}){$dir=[IO.Path]::GetDirectoryName($row.Path);if(Test-Path -LiteralPath $dir){$r=Read-MMJson (Join-Path $dir '설치기록.json');if(!$r -or $r.Name -ne [IO.Path]::GetFileNameWithoutExtension($row.Path)){throw ('기존 DLL 폴더 소유권 확인 실패: '+$row.Name)}}}
    New-Item -ItemType Directory -Path $managerDir -Force | Out-Null
    $first=!$plan.Snapshot.Owner;$backup=Join-Path $managerDir ('Backups\'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'));New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $snapshots=@()
    foreach($rel in @($touched | Select-Object -Unique)){$path=Join-Path $Game $rel;$saved=Join-Path $backup $rel;$existed=Test-Path -LiteralPath $path;if($existed){New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($saved)) -Force | Out-Null;Copy-Item -LiteralPath $path -Destination $saved -Recurse -Force};$snapshots += [pscustomobject]@{Relative=$rel;Existed=$existed}}
    Write-MMJson (Join-Path $backup 'Snapshots.json') $snapshots
    $wasActive=@{};foreach($row in $rows | Where-Object {$_.Data}){$wasActive[$row.Id]=Test-Path -LiteralPath (Join-Path $Game ('MODs\'+$row.Data))}
    try{
        foreach($row in $rows | Where-Object {$_.Id -in @('gift','jade')}){
            $module=[IO.Path]::GetFileNameWithoutExtension($row.Path);$source=Join-Path $Payload ('modules\'+$module);$dir=[IO.Path]::GetDirectoryName($row.Path)
            # Upgrade known bundled DLLs, but retain user settings and accompanying files. The public edition bundles none
            # (10-31 디시: '…\_files\modules\LOH_GiftHighlight' not found broke every save): it uses the copy already in the game.
            # Never downgrade (10-31: an older manager copy put 취향 표시 0.4.0·금란선옥 1.0.0 back over 0.5.1·1.1.0): when the game's
            # DLL reports a newer LOH_ModInfo version than the bundled one, keep the game's DLL and loader.
            $have=Get-MMModuleVersion $row.Path;$bundled=Get-MMModuleVersion (Join-Path $source ($module+'.dll'))
            $keep=$have -and (!$bundled -or $bundled -lt $have)
            if(Test-Path -LiteralPath $source -PathType Container){New-Item -ItemType Directory -Path $dir -Force | Out-Null;Get-ChildItem -LiteralPath $source -File | Where-Object {(!$keep -and $_.Extension -in @('.dll','.exe')) -or !(Test-Path -LiteralPath (Join-Path $dir $_.Name))} | Copy-Item -Destination $dir -Force}
            elseif(!(Test-Path -LiteralPath $row.Path -PathType Leaf)){if($row.Enabled){throw ('파일을 찾지 못했습니다: '+$row.Name)};continue}
            $now=Get-MMModuleVersion $row.Path;if($now){$row.Version=$now.ToString()}
            Write-MMJson (Join-Path $dir '설치기록.json') ([ordered]@{Name=$module;Version=$row.Version;Entry=$row.Route;Data=$row.Data;Manager='LOH_ModManager'})
        }
        $jade=$rows | Where-Object {$_.Id -eq 'jade'};$jadeActive=Join-Path $Game 'MODs\금란선옥';$jadeParked=Join-Path $Game 'LOH_ModManager\DisabledData\jade\금란선옥'
        if($jade -and !(Test-Path -LiteralPath $jadeActive) -and !(Test-Path -LiteralPath $jadeParked) -and (Test-Path -LiteralPath (Join-Path $Payload 'data\금란선옥'))){
            New-Item -ItemType Directory -Path $jadeActive -Force | Out-Null
            Get-ChildItem -LiteralPath (Join-Path $Payload 'data\금란선옥') | Copy-Item -Destination $jadeActive -Recurse -Force
        }
        foreach($row in $rows | Where-Object {$_.Data -and !$ownedData.ContainsKey($_.Data)}){   # a record's data folder stays where it is
            $active=Join-Path $Game ('MODs\'+$row.Data);$parked=Join-Path $managerDir ('DisabledData\'+$row.Id+'\'+$row.Data)
            if((Test-Path -LiteralPath $active) -and (Test-Path -LiteralPath $parked)){throw ('데이터 폴더 중복: '+$row.Name)}
            if($row.Enabled -and (Test-Path -LiteralPath $parked)){New-Item -ItemType Directory -Path (Split-Path $active -Parent) -Force | Out-Null;Move-Item -LiteralPath $parked -Destination $active}
            elseif(!$row.Enabled -and (Test-Path -LiteralPath $active)){New-Item -ItemType Directory -Path (Split-Path $parked -Parent) -Force | Out-Null;Move-Item -LiteralPath $active -Destination $parked}
        }
        if(Test-Path -LiteralPath $legacy){Remove-Item -LiteralPath $legacy -Recurse -Force}
        foreach($entry in $plan.Retire){Remove-Item -LiteralPath (Join-Path $Game $entry.Name) -Force}
        foreach($route in $plan.Routes){$from=Join-Path $Payload ('entries\'+$route);$to=Join-Path $Game $route;Copy-Item -LiteralPath $from -Destination $to -Force
            if(!(Test-Path -LiteralPath $to -PathType Leaf) -or (Get-MMHash $to) -ne (Get-MMHash $from)){throw ('게임 폴더에 '+$route+'을 넣지 못했습니다. 백신이 막았을 수 있으니 Windows 보안의 보호 기록을 확인해 주세요. 이번 저장은 되돌렸습니다.')}}
        $revision=[guid]::NewGuid().ToString('N');$registered=@($rows | ForEach-Object {[ordered]@{Id=$_.Id;Name=$_.Name;Path=$_.Path;Enabled=$_.Enabled;Version=$_.Version;Route=$_.Route;External=$_.External}})
        $hiddenSaved=@($hidden | ForEach-Object {[ordered]@{Id=$_.Id;Name=$_.Name;Path=$_.Path;Enabled=$_.Enabled;Version=$_.Version;Route=$_.Route;External=$false}})
        Write-MMJson (Join-Path $managerDir 'Catalog.json') ([ordered]@{Revision=$revision;Modules=$registered;HiddenModules=$hiddenSaved})
        $lines=@('[Manager]','Version=2','DefaultEntry=version.dll',('Revision='+$revision),('Count='+$rows.Count));$i=0
        foreach($row in $rows){$lines += @('',('[Module'+$i+']'),('Id='+$row.Id),('Path='+$row.Path),('Enabled='+[int]$row.Enabled),('Route='+$row.Route));$i++}
        [IO.File]::WriteAllLines((Join-Path $managerDir 'Modules.ini'),$lines,[Text.Encoding]::Unicode)
        $original=if($first){$backup}else{$plan.Snapshot.Owner.OriginalBackup}
        Write-MMJson (Join-Path $managerDir 'Owner.json') ([ordered]@{Name='LOH_ModManager';Version='0.12.11';Entry=$plan.Entry;Entries=@($plan.Routes);Revision=$revision;OriginalBackup=$original;LastBackup=$backup})
    }catch{
        $failure=$_
        foreach($snapshot in $snapshots){$p=Join-Path $Game $snapshot.Relative;$null=Assert-MMPath $Game $p -Tree;if(Test-Path -LiteralPath $p){Remove-Item -LiteralPath $p -Recurse -Force};if($snapshot.Existed){New-Item -ItemType Directory -Path (Split-Path $p -Parent) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $backup $snapshot.Relative) -Destination $p -Recurse -Force}}
        if($first -and !(Test-Path -LiteralPath (Join-Path $managerDir 'Owner.json'))){Write-MMJson (Join-Path $managerDir 'Owner.json') ([ordered]@{Name='LOH_ModManager';State='Failed';OriginalBackup=$backup})}
        throw $failure
    }
    # Data that came (back) into MODs in this apply is ticked in the game's own mod list too. Only for a real install
    # (Test-MMRealInstall) so test copies never touch the player's ModSettings. Not fatal: worst case the player ticks it.
    $turnOn=@($rows | Where-Object {$_.Data -and $_.Enabled -and $_.Suffix -and !$wasActive[$_.Id] -and (Test-Path -LiteralPath (Join-Path $Game ('MODs\'+$_.Data)))} | ForEach-Object {$_.Suffix})
    $selection=[pscustomobject]@{Changed=@();Note=''}
    if($turnOn.Count -and (Test-MMRealInstall $Game)){
        try{$selection=Set-MMSelectionOn $turnOn $backup}catch{$selection=[pscustomobject]@{Changed=@();Note=('게임 모드 목록에 체크하지 못했습니다('+$_.Exception.Message+'). 게임 안 모드 목록에서 켜 주세요.')}}
    }
    # Data and workshop mods from the window's one list: on/off (by strIDEnd) and their order in the game's mod list.
    $data=[pscustomobject]@{Changed=@();Note=''}
    if($Choices.ContainsKey('_data') -or $Choices.ContainsKey('_dataOrder')){
        if(Test-MMRealInstall $Game){try{$data=Set-MMSelectionState $Choices['_data'] @($Choices['_dataOrder'] | Where-Object {$_}) $backup}catch{$data=[pscustomobject]@{Changed=@();Note=('데이터 모드 켜기·끄기를 게임 모드 목록에 쓰지 못했습니다: '+$_.Exception.Message)}}}
        else{$data=[pscustomobject]@{Changed=@();Note='시험용 게임 폴더라 게임 모드 목록(데이터 모드 켜기·끄기·순서)은 바꾸지 않았습니다.'}}
    }
    if($data.Note -and $selection.Note -eq $data.Note){$selection.Note=''}   # no list yet: say it once
    return [pscustomobject]@{Entry=$plan.Entry;Entries=@($plan.Routes);EntryMoved=@($entryMoved);Backup=$backup;Modules=$registered;Selection=@($selection.Changed);SelectionNote=$selection.Note;Data=@($data.Changed);DataNote=$data.Note
        Waiting=@(@($selection.Waiting)+@($data.Waiting) | Where-Object {$_} | Select-Object -Unique);Unwait=@($data.Unwait | Where-Object {$_});WaitingOrder=@($data.WaitingOrder | Where-Object {$_})}
}
