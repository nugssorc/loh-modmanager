param([ValidateSet('Check','Catalog','Apply','Scan','InspectFile','RemovalPlan','Remove','Removed','Restore','Analyze','StageMissing','InstallPackage','UninstallPackage','Installed','PackageData','DiscardStage','PackageEntry','DataOn','PatchRebuild','PatchKeep','LinkRefresh','WatchStart','WatchFinish','WatchCancel','WorkshopReapply','EntryAdopt','EntryAside','CapMenus','AioItem','AioItems','PatchSwitch','ParkedRemove','PromiseItems','ReplaceNote','RollBack','YieldPreview')][string]$Action='Check',[string]$GamePath,[string]$ChoicesFile)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
try{
    . (Join-Path $PSScriptRoot 'Manager.Core.ps1')
    . (Join-Path $PSScriptRoot 'Removal.Core.ps1')
    . (Join-Path $PSScriptRoot 'Install.Core.ps1')
    $mutex=$null;$locked=$false
    if($Action -in @('Apply','Remove','Restore','InstallPackage','UninstallPackage','PackageData','PackageEntry','DataOn','PatchRebuild','PatchKeep','LinkRefresh','WatchStart','WatchFinish','WatchCancel','WorkshopReapply','EntryAdopt','EntryAside','CapMenus','AioItem','AioItems','PatchSwitch','ParkedRemove','PromiseItems','ReplaceNote','RollBack')){
        $key=[IO.Path]::GetFullPath($GamePath).TrimEnd('\').ToUpperInvariant();$sha=[Security.Cryptography.SHA256]::Create()
        try{$tag=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key))).Replace('-','')}finally{$sha.Dispose()}
        $mutex=[Threading.Mutex]::new($false,('Local\LOH_ModManager_Edit_'+$tag));try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
        if(!$locked){throw '다른 관리창이 같은 게임 폴더를 변경 중입니다. 완료 후 다시 시도하세요.'}
    }
    if($Action -eq 'Check'){$result=Get-MMSnapshot $GamePath}
    elseif($Action -eq 'Catalog'){$result=Get-MMSnapshot $GamePath -NoProcess;$result | Add-Member -NotePropertyName Installed -NotePropertyValue @((Get-MMInstalled $GamePath).Items) -Force
        $patch=try{Get-MMPatchState $GamePath}catch{$null};$result | Add-Member -NotePropertyName PatchState -NotePropertyValue $patch -Force
        $stale=try{@(Get-MMStaleLinks $GamePath)}catch{@()};$result | Add-Member -NotePropertyName StaleLinks -NotePropertyValue $stale -Force
        $foreign=try{@(Get-MMForeignEntries $GamePath)}catch{@()};$result | Add-Member -NotePropertyName Foreign -NotePropertyValue $foreign -Force
        $loaders=try{@(Get-MMTakenLoaders $GamePath)}catch{@()};$result | Add-Member -NotePropertyName TakenLoaders -NotePropertyValue $loaders -Force
        $parked=try{@(Get-MMParkedPackages $GamePath)}catch{@()};$result | Add-Member -NotePropertyName Parked -NotePropertyValue $parked -Force}
    elseif($Action -eq 'Scan'){$result=Get-MMInventory $GamePath}
    elseif($Action -eq 'InspectFile'){$request=Read-MMJson $ChoicesFile;$result=Get-MMExternalType $GamePath $request.Path}
    elseif($Action -eq 'RemovalPlan'){$request=Read-MMJson $ChoicesFile;$result=Get-MMRemovalPlan $GamePath $request.Key}
    elseif($Action -eq 'Remove'){$request=Read-MMJson $ChoicesFile;$result=Invoke-MMRemove $GamePath $request.Key $request.Token}
    elseif($Action -eq 'Removed'){$result=Get-MMRemoved $GamePath;$result.Items=@($result.Items)+@(Get-MMRemovedPackages $GamePath)}
    elseif($Action -eq 'Restore'){$request=Read-MMJson $ChoicesFile;$result=if($request.Kind -eq 'package'){Restore-MMPackage $GamePath $request.Id}else{Invoke-MMRestore $GamePath $request.Id}}
    elseif($Action -eq 'Analyze'){$request=Read-MMJson $ChoicesFile;$stage=Expand-MMPackage $GamePath $request.Path ([string]$request.Password);$result=Get-MMPackagePlan $GamePath $stage ([string]$request.Replaces)}
    elseif($Action -eq 'StageMissing'){$request=Read-MMJson $ChoicesFile;$result=[pscustomobject]@{Missing=@(Get-MMStageMissing $GamePath ([string]$request.StageId))}}
    elseif($Action -eq 'InstallPackage'){$request=Read-MMJson $ChoicesFile;$result=Install-MMPackage $GamePath $request.StageId @($request.Keys) $request.Options ([string]$request.Replaces)}
    elseif($Action -eq 'UninstallPackage'){$request=Read-MMJson $ChoicesFile;$result=Uninstall-MMPackage $GamePath $request.Id}
    elseif($Action -eq 'Installed'){$result=Get-MMInstalled $GamePath}
    elseif($Action -eq 'PackageEntry'){$request=Read-MMJson $ChoicesFile;$result=Set-MMPackageEntry $GamePath $request.Id ([bool]$request.Absorb)}
    elseif($Action -eq 'DataOn'){$request=Read-MMJson $ChoicesFile;Assert-MMStopped $GamePath;$result=if(Test-MMRealInstall $GamePath){$dir=Join-Path $GamePath ('LOH_ModManager\Backups\Selection-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
            # after the game's first run: the order the player set before there was a list comes along
            if(@($request.Order).Count){$on=@{};foreach($s in @($request.Suffixes)){$on[[string]$s]=$true};Set-MMSelectionState $on @($request.Order) $dir}else{Set-MMSelectionOn @($request.Suffixes) $dir}}else{[pscustomobject]@{Changed=@();Note='시험용 게임 폴더'}}}
    elseif($Action -eq 'PatchRebuild'){$request=Read-MMJson $ChoicesFile;$result=Invoke-MMPatchRebuild $GamePath @($request.Order | Where-Object {$_})}
    elseif($Action -eq 'WatchStart'){$request=Read-MMJson $ChoicesFile;$result=Start-MMWatch $GamePath ([string]$request.Name) ([string]$request.Source) ([string]$request.StageId)}
    elseif($Action -eq 'WatchFinish'){$request=Read-MMJson $ChoicesFile;$result=Stop-MMWatch $GamePath ([string]$request.Id)}
    elseif($Action -eq 'EntryAdopt'){$request=Read-MMJson $ChoicesFile;$result=Invoke-MMAdoptEntry $GamePath ([string]$request.Name)}
    elseif($Action -eq 'EntryAside'){$request=Read-MMJson $ChoicesFile;$result=Invoke-MMSetAsideEntry $GamePath ([string]$request.Name) @($request.Folders | Where-Object {$_})}
    elseif($Action -eq 'CapMenus'){$request=Read-MMJson $ChoicesFile;$result=Set-MMCapUnlockMenus $GamePath ([string]$request.Id) @($request.Menus | ForEach-Object {[int]$_}) @($request.Order | Where-Object {$_})}
    elseif($Action -eq 'AioItem'){$request=Read-MMJson $ChoicesFile;$result=Set-MMAllInOneItem $GamePath ([string]$request.Id) ([string]$request.Key) ([bool]$request.On) @($request.Order | Where-Object {$_})}
    elseif($Action -eq 'AioItems'){$request=Read-MMJson $ChoicesFile;$result=Set-MMAllInOneItems $GamePath ([string]$request.Id) $request.Changes @($request.Order | Where-Object {$_})}
    elseif($Action -eq 'PatchSwitch'){$request=Read-MMJson $ChoicesFile;$result=Invoke-MMPatchSwitch $GamePath @($request.Off | Where-Object {$_}) @($request.On | Where-Object {$_}) @($request.Order | Where-Object {$_})}
    elseif($Action -eq 'ReplaceNote'){$request=Read-MMJson $ChoicesFile;$result=Set-MMReplaces $GamePath ([string]$request.Id) ([string]$request.Old) ([string]$request.Version) ([string]$request.NewVersion)}
    elseif($Action -eq 'RollBack'){$request=Read-MMJson $ChoicesFile;$result=Invoke-MMRollBack $GamePath ([string]$request.Id)}
    elseif($Action -eq 'PromiseItems'){$request=Read-MMJson $ChoicesFile;$result=Set-MMPromiseItems $GamePath ([string]$request.Id) @($request.Items | ForEach-Object {[string]$_}) @($request.Order | Where-Object {$_})}
    elseif($Action -eq 'ParkedRemove'){$request=Read-MMJson $ChoicesFile;$result=Remove-MMParked $GamePath ([string]$request.Id)}
    elseif($Action -eq 'PatchKeep'){$result=Approve-MMOutsidePatch $GamePath}
    elseif($Action -eq 'WorkshopReapply'){$request=Read-MMJson $ChoicesFile;$result=Invoke-MMWorkshopReapply $GamePath ([string]$request.Id)}
elseif($Action -eq 'WatchCancel'){$request=Read-MMJson $ChoicesFile;$result=Remove-MMWatch $GamePath ([string]$request.Id)}
    elseif($Action -eq 'LinkRefresh'){Assert-MMStopped $GamePath;$result=Update-MMTreasuryLinks $GamePath}
    elseif($Action -eq 'DiscardStage'){$request=Read-MMJson $ChoicesFile;$result=Remove-MMStage $GamePath $request.StageId}
    elseif($Action -eq 'YieldPreview'){$request=Read-MMJson $ChoicesFile;$result=[pscustomobject]@{Lines=@(Get-MMYieldPreview $GamePath @($request.Order | Where-Object {$_}))}}
    elseif($Action -eq 'PackageData'){$request=Read-MMJson $ChoicesFile;$result=Set-MMPackageData $GamePath $request.Id ([bool]$request.On)}
    else{$choices=@{};$inputChoices=Read-MMJson $ChoicesFile;foreach($property in $inputChoices.PSObject.Properties){$choices[$property.Name]=$property.Value};$result=Invoke-MMApply $GamePath $choices
        $result | Add-Member -NotePropertyName Repaired -NotePropertyValue @(Repair-MMInstalledModInfo $GamePath $result.Backup)}   # earlier installs' one-object ModInfo.json
    # after any change: a mod whose link is made from GameAssembly.dll (세력금고) is remade if the file moved under it — one rule for
    # every path (patch on/off, items, install, removal, restore, rebuild), present and future; failures stay in the stale-link notice
    if($locked -and $Action -ne 'LinkRefresh' -and $null -ne $result){try{if(@(Get-MMStaleLinks $GamePath).Count){Assert-MMStopped $GamePath;$relinked=@((Update-MMTreasuryLinks $GamePath).Steps)
        if($relinked.Count){$result | Add-Member -NotePropertyName Relinked -NotePropertyValue $relinked -Force}}}catch{}}
    [Console]::WriteLine((@{Success=$true;Result=$result} | ConvertTo-Json -Depth 12 -Compress))
}catch{[Console]::WriteLine((@{Success=$false;Error=$_.Exception.Message} | ConvertTo-Json -Compress));exit 1}

finally{if($locked -and $mutex){$mutex.ReleaseMutex()};if($mutex){$mutex.Dispose()}}
