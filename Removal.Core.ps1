# Reversible removal. Only inventory identities, never arbitrary caller paths, select targets.
function Get-MMWorkshopRoot([string]$Game) {return Join-Path (Split-Path (Split-Path ([IO.Path]::GetFullPath($Game)) -Parent) -Parent) 'workshop\content\3020510'}
. (Join-Path $PSScriptRoot 'Original.Core.ps1')
function Assert-MMRemovalPath([string]$Game,[string]$Path,[string]$Role,[string]$ModuleId='') {
    $g=[IO.Path]::GetFullPath($Game).TrimEnd('\');$p=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    if($Role -eq 'module'){
        $def=$script:MMDefinitions | Where-Object {$_.Id -eq $ModuleId}
        if(!$def -or $p -ne (Split-Path (Join-Path $g $def.Relative) -Parent)){throw '모드 DLL 제거 범위 오류'}
        return Assert-MMPath $g $p -Tree
    }
    if($Role -eq 'linked-data'){
        $def=$script:MMDefinitions | Where-Object {$_.Id -eq $ModuleId}
        if(!$def -or !$def.Data -or $p -notin @((Join-Path $g ('MODs\'+$def.Data)),(Join-Path $g ('LOH_ModManager\DisabledData\'+$def.Id+'\'+$def.Data)))){throw '연동 데이터 제거 범위 오류'}
        return Assert-MMPath $g $p -Tree
    }
    if($Role -eq 'local-data'){
        if((Split-Path $p -Parent) -ne (Join-Path $g 'MODs')){throw '로컬 모드 제거 범위 오류'}
        return Assert-MMPath $g $p -Tree
    }
    if($Role -eq 'obsolete-editor'){
        if((Split-Path $p -Parent) -ne (Join-Path $g 'LOH_IngameEditor') -or (Split-Path $p -Leaf) -notmatch '^LOH_IngameEditor_v(020|030)\.(dll|log)$'){throw '구버전 DLL 제거 범위 오류'}
        return Assert-MMPath $g $p -Tree
    }
    if($Role -eq 'manual-workshop'){
        $root=[IO.Path]::GetFullPath((Get-MMWorkshopRoot $g)).TrimEnd('\')
        if(!$p.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw '창작마당 제거 범위 오류'}
        $parts=$p.Substring($root.Length+1).Split('\')
        if($parts.Length -gt 2 -or $parts[0] -match '^\d+$'){throw '구독 모드는 Steam에서 구독 해제하세요.'}
        return Assert-MMPath $root $p -Tree
    }
    throw '지원하지 않는 제거 대상'
}
function Get-MMRemovalToken($Plan) {
    $text=$Plan.Key+'|'+$Plan.Revision+'|'+$Plan.Name+'|'+($Plan.SavedRow | ConvertTo-Json -Depth 6 -Compress)+'|'+($Plan.Order -join ',')
    foreach($item in $Plan.Targets){
        $text+='|'+$item.Original+'|'+$item.Role
        if(Test-Path -LiteralPath $item.Original -PathType Leaf){$text+='|'+(Get-MMHash $item.Original)}
        elseif(Test-Path -LiteralPath $item.Original){foreach($file in @(Get-ChildItem -LiteralPath $item.Original -File -Recurse -Force | Sort-Object FullName)){$text+='|'+$file.FullName+'|'+$file.Length+'|'+$file.LastWriteTimeUtc.Ticks}}
    }
    $sha=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-','')}finally{$sha.Dispose()}
}
function Get-MMRemovalPlan([string]$Game,[string]$Key) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    Assert-MMStopped $Game
    $item=@((Get-MMInventory $Game).Items | Where-Object {$_.Key -eq $Key})
    if($item.Count -ne 1){throw '설치 목록이 바뀌었습니다. 다시 조회하세요.'};$item=$item[0]
    if(!$item.CanRemove){throw $item.RemoveHint}
    $targets=@();$savedRow=$null;$order=@();$note=$item.RemoveHint
    if($item.Kind -eq 'managed'){
        $note+=("`r`n제거 경로: "+(Get-MMOriginalMethod $item.ModuleId))
        if($item.ModuleId -in @('dokbak','gift','jade')){$note+=' 원본 입구를 작업 사본에 복원해 제거 CMD를 검증한 뒤, 실제 설치본은 선택 모드만 백업 제거합니다. 현재 공용 입구를 과거 파일로 덮어쓰지 않습니다.'}
        $rows=@(Get-MMCatalog $Game);$savedRow=$rows | Where-Object {$_.Id -eq $item.ModuleId};$order=@($rows | ForEach-Object {$_.Id})
        if(!$savedRow -or $savedRow.External){throw '외부 DLL은 등록 해제를 사용하세요.'}
        $def=$script:MMDefinitions | Where-Object {$_.Id -eq $savedRow.Id};$folder=Split-Path (Join-Path $Game $def.Relative) -Parent
        if(Test-Path -LiteralPath $folder){
            if(!$savedRow.Path.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase)){throw '기본 DLL 폴더와 지정 DLL이 다릅니다. DLL 경로를 확인한 뒤 제거하세요.'}
            $targets+=@{Original=$folder;Role='module';ModuleId=$savedRow.Id}
        }
        if($savedRow.Data){foreach($path in @((Join-Path $Game ('MODs\'+$savedRow.Data)),(Join-Path $Game ('LOH_ModManager\DisabledData\'+$savedRow.Id+'\'+$savedRow.Data)))){if(Test-Path -LiteralPath $path){Assert-MMData $Game $savedRow $path;$targets+=@{Original=$path;Role='linked-data';ModuleId=$savedRow.Id}}}}
        if(!$savedRow.Path.StartsWith([IO.Path]::GetFullPath($Game).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){$note+=' 외부 원본 보존: '+$savedRow.Path}
    }elseif($item.Kind -eq 'obsolete-editor'){
        $check=Get-MMOldEditorCheck $Game $item.Path;if(!$check.Eligible){throw $check.Reason}
        $targets+=@{Original=$item.Path;Role='obsolete-editor';ModuleId=''}
        $log=[IO.Path]::ChangeExtension($item.Path,'.log');if(Test-Path -LiteralPath $log -PathType Leaf){$targets+=@{Original=$log;Role='obsolete-editor';ModuleId=''}}
        $note=$check.Reason
    }else{
        $role=if($item.Kind -eq 'local'){'local-data'}elseif($item.Kind -eq 'workshop-manual'){'manual-workshop'}else{throw '이 종류는 제거할 수 없습니다.'}
        $targets+=@{Original=$item.Path;Role=$role;ModuleId=''};$note+=' 연결이 확인되지 않은 외부 DLL은 건드리지 않습니다.'
    }
    foreach($target in $targets){$null=Assert-MMRemovalPath $Game $target.Original $target.Role $target.ModuleId}
    $owner=Read-MMJson (Join-Path $Game 'LOH_ModManager\Owner.json')
    $plan=[pscustomobject]@{Key=$Key;Name=$item.Name;Kind=$item.Kind;ModuleId=$item.ModuleId;Revision=[string]$owner.Revision;Targets=$targets;SavedRow=$savedRow;Order=$order;Note=($note+' 세이브와 게임 선택 기록은 수정하지 않습니다.')}
    $plan | Add-Member -NotePropertyName Token -NotePropertyValue (Get-MMRemovalToken $plan);return $plan
}
function Undo-MMApply([string]$Game,[string]$Backup) {
    $null=Assert-MMPath $Game $Backup -Tree;$snapshots=Read-MMJson (Join-Path $Backup 'Snapshots.json')
    if(!$snapshots){throw '설정 복구 목록이 없습니다.'}
    foreach($snapshot in $snapshots){
        $p=Assert-MMPath $Game (Join-Path $Game $snapshot.Relative) -Tree
        if(Test-Path -LiteralPath $p){Remove-Item -LiteralPath $p -Recurse -Force}
        if($snapshot.Existed){New-Item -ItemType Directory -Path (Split-Path $p -Parent) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $Backup $snapshot.Relative) -Destination $p -Recurse -Force}
    }
}
function Invoke-MMRemove([string]$Game,[string]$Key,[string]$Token) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    $plan=Get-MMRemovalPlan $Game $Key
    if(!$Token -or $plan.Token -ne $Token){throw '확인 후 파일이나 설정이 바뀌었습니다. 제거 미리보기를 다시 열어 주세요.'}
    $vault=Join-Path $Game ('LOH_ModManager\Removed\'+[guid]::NewGuid().ToString('N'));$null=Assert-MMPath $Game $vault -Tree
    $managerDir=Join-Path $Game 'LOH_ModManager';$owner=Read-MMJson (Join-Path $managerDir 'Owner.json')
    if($owner -and $owner.Name -ne 'LOH_ModManager'){throw '관리 폴더 소유권이 다릅니다.'}
    if((Test-Path -LiteralPath $managerDir) -and !$owner){throw '설치 기록 없는 관리 폴더는 변경하지 않습니다.'}
    if(!$owner){throw '먼저 DLL 설정 탭에서 선택을 저장해 관리 폴더를 준비해 주세요.'}
    New-Item -ItemType Directory -Path $vault -Force | Out-Null
    $entries=@();$i=0;foreach($target in $plan.Targets){$entries += [ordered]@{Original=$target.Original;Role=$target.Role;ModuleId=$target.ModuleId;Stored=('Files\'+$i)};$i++}
    $record=[ordered]@{Id=(Split-Path $vault -Leaf);Game=[IO.Path]::GetFullPath($Game).TrimEnd('\');Name=$plan.Name;Key=$plan.Key;State='Removing';Date=(Get-Date -Format 'yyyy-MM-dd HH:mm:ss');Kind=$plan.Kind;ModuleId=$plan.ModuleId;SavedRow=$plan.SavedRow;Order=$plan.Order;Targets=$entries;Note=$plan.Note;ApplyBackup=''}
    $recordPath=Join-Path $vault 'Record.json';Write-MMJson $recordPath $record;$applied=$null;$moved=@()
    try{
        $original=Invoke-MMOriginalCheck $Game $plan $vault
        if($original){$record['OriginalUninstaller']=$original;Write-MMJson $recordPath $record}
        if($plan.Kind -eq 'managed'){$applied=Invoke-MMApply $Game @{_hide=@($plan.ModuleId)};$record.ApplyBackup=$applied.Backup;Write-MMJson $recordPath $record}
        foreach($target in $entries){
            $path=Assert-MMRemovalPath $Game $target.Original $target.Role $target.ModuleId;$dest=Assert-MMPath $Game (Join-Path $vault $target.Stored) -Tree
            New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null;Move-Item -LiteralPath $path -Destination $dest;$moved+=@($target)
        }
        $record.State='Removed';Write-MMJson $recordPath $record
    }catch{
        $failure=$_
        foreach($target in $moved){$dest=Assert-MMRemovalPath $Game $target.Original $target.Role $target.ModuleId;Move-Item -LiteralPath (Join-Path $vault $target.Stored) -Destination $dest}
        if($applied){Undo-MMApply $Game $applied.Backup}
        $record.State='RolledBack';Write-MMJson $recordPath $record;throw $failure
    }
    return [pscustomobject]@{Name=$plan.Name;Backup=$vault;Note=$plan.Note}
}
function Get-MMRemoved([string]$Game) {
    $root=Join-Path $Game 'LOH_ModManager\Removed';$items=@()
    if(Test-Path -LiteralPath $root){foreach($folder in Get-ChildItem -LiteralPath $root -Directory){if($folder.Name -notmatch '^[0-9a-f]{32}$' -or ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)){continue};try{$r=Read-MMJson (Join-Path $folder.FullName 'Record.json');if($r -and $r.State -in @('Removed','Removing','Restoring')){$items+=[pscustomobject]@{Id=$folder.Name;Name=$r.Name;Date=$r.Date;State=$r.State;Note=$r.Note;Paths=@($r.Targets | ForEach-Object {$_.Original})}}}catch{}}}
    return [pscustomobject]@{Items=$items}
}
function Invoke-MMRestore([string]$Game,[string]$Id) {
    $Game=[IO.Path]::GetFullPath($Game).TrimEnd('\')
    Assert-MMStopped $Game;if($Id -notmatch '^[0-9a-f]{32}$'){throw '복원 기록 ID 오류'}
    $vault=Assert-MMPath $Game (Join-Path $Game ('LOH_ModManager\Removed\'+$Id)) -Tree;$recordPath=Join-Path $vault 'Record.json';$record=Read-MMJson $recordPath
    if(!$record -or $record.Game -ne [IO.Path]::GetFullPath($Game).TrimEnd('\') -or $record.State -notin @('Removed','Removing','Restoring')){throw '유효한 복원 기록이 아닙니다.'}
    $index=0
    foreach($target in $record.Targets){
        $null=Assert-MMRemovalPath $Game $target.Original $target.Role $target.ModuleId
        if($target.Stored -ne ('Files\'+$index)){throw '백업 파일 경로 오류'};$index++
        $stored=Assert-MMPath $Game (Join-Path $vault $target.Stored) -Tree
        if((Test-Path -LiteralPath $target.Original) -and (Test-Path -LiteralPath $stored)){throw ('같은 위치에 새 파일이 있어 복원하지 않습니다: '+$target.Original)}
        if(!(Test-Path -LiteralPath $target.Original) -and !(Test-Path -LiteralPath $stored)){throw ('복원 파일이 없습니다: '+$target.Original)}
    }
    $moved=@();$applied=$null;$previous=$record.State
    try{
        $record.State='Restoring';Write-MMJson $recordPath $record
        foreach($target in $record.Targets){$stored=Join-Path $vault $target.Stored;if(Test-Path -LiteralPath $stored){New-Item -ItemType Directory -Path (Split-Path $target.Original -Parent) -Force | Out-Null;Move-Item -LiteralPath $stored -Destination $target.Original;$moved+=@($target)}}
        if($record.Kind -eq 'managed'){
            $registry=Read-MMJson (Join-Path $Game 'LOH_ModManager\Catalog.json');$hidden=@($registry.HiddenModules | Where-Object {$_.Id -eq $record.ModuleId})
            if($hidden.Count){
                $id=$record.ModuleId;$choices=@{_unhide=@($id)};$choices[$id]=[bool]$record.SavedRow.Enabled;$choices[$id+'_route']=[string]$record.SavedRow.Route;$choices[$id+'_path']=[string]$record.SavedRow.Path
                $current=@(Get-MMCatalog $Game | ForEach-Object {$_.Id});$original=@($record.Order);$oldIndex=[Array]::IndexOf($original,$id);$insert=[Math]::Min([Math]::Max($oldIndex,0),$current.Count)
                $order=[Collections.Generic.List[string]]::new();foreach($value in $current){$order.Add($value)};$order.Insert($insert,$id);$choices['_order']=@($order.ToArray())
                $applied=Invoke-MMApply $Game $choices
            }
        }
        $record.State='Restored';Write-MMJson $recordPath $record
    }catch{
        $failure=$_;if($applied){Undo-MMApply $Game $applied.Backup}
        foreach($target in $moved){$path=Assert-MMRemovalPath $Game $target.Original $target.Role $target.ModuleId;Move-Item -LiteralPath $path -Destination (Join-Path $vault $target.Stored)}
        $record.State=$previous;Write-MMJson $recordPath $record;throw $failure
    }
    return [pscustomobject]@{Name=$record.Name;Backup=$vault;Note=$record.Note}
}
