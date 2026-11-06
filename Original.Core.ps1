# Reviewed first-party uninstall adapters. Never execute an arbitrary discovered CMD.
# Original uninstallers operate only on a disposable copy with their original entry.
function Get-MMOriginalMethod([string]$Id) {
    switch($Id){
        'dokbak'{return '독박육아 원본 제거 CMD 검증 → 선택 모드 백업 제거'}
        'gift'{return '선물 표시 원본 제거 CMD 검증 → 선택 모드 백업 제거'}
        'jade'{return '금란선옥 원본 제거 CMD 검증 → 선택 모드 백업 제거'}
        default{return '별도 원본 제거 CMD 없음 · 관리창 백업 제거'}
    }
}
function Invoke-MMOriginalCheck([string]$Game,$Plan,[string]$Vault) {
    if($Plan.Kind -ne 'managed' -or $Plan.ModuleId -notin @('dokbak','gift','jade')){return $null}
    $payload=Join-Path $script:MMSource '_files';$manifest=Read-MMJson (Join-Path $payload 'manifest.json')
    $files=@($manifest.Files | Where-Object {$_.Path -like 'originals/*'})
    if(!$files.Count){return $null}   # public edition carries no original uninstallers: the plain backup removal follows
    foreach($file in $files){$p=[IO.Path]::GetFullPath((Join-Path $payload $file.Path));if(!$p.StartsWith([IO.Path]::GetFullPath($payload)+'\',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $p) -or (Get-MMHash $p) -ne $file.SHA256){throw ('원본 제거 도구 검증 실패: '+$file.Path)}}
    $stage=Assert-MMPath $Game (Join-Path $Vault 'OriginalCheck') -Tree;New-Item -ItemType Directory -Path $stage -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage 'ThreeKingdom.exe'),'UNINSTALL TEST COPY - NEVER EXECUTE')
    $def=$script:MMDefinitions | Where-Object {$_.Id -eq $Plan.ModuleId};$expected=@()
    foreach($target in $Plan.Targets){
        if($target.Role -eq 'module'){$dest=Join-Path $stage (Split-Path $target.Original -Leaf)}
        elseif($target.Role -eq 'linked-data'){$dest=Join-Path $stage ('MODs\'+$def.Data)}
        else{throw '원본 제거 사본 범위 오류'}
        if(Test-Path -LiteralPath $dest){throw '원본 제거 사본 데이터가 중복됩니다.'}
        New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null;Copy-Item -LiteralPath $target.Original -Destination $dest -Recurse -Force;$expected+=@($dest)
        if($Plan.ModuleId -eq 'jade' -and $target.Role -eq 'linked-data'){
            # The manager validates this data by its fixed suffix. Recreate the
            # standalone install receipt ONLY in the disposable original layout.
            Assert-MMData $Game $Plan.SavedRow $target.Original
            Write-MMJson (Join-Path $dest '.LOH_GoldenOrchidJade.json') @{Name='LOH_GoldenOrchidJade';Data='금란선옥';Version='1.1.0'}
        }
    }
    $entry='version.dll';$kind='social';$entrySource=Join-Path $payload 'originals\social-version.dll'
    if($Plan.ModuleId -eq 'dokbak'){
        $entry='hid.dll';$kind='dokbak';$entrySource=Join-Path $payload 'originals\dokbak-hid.dll'
        $owner=Read-MMJson (Join-Path $Game 'LOH_ModManager\Owner.json')
        if($owner.OriginalBackup){
            $backup=Assert-MMPath $Game $owner.OriginalBackup -Tree
            foreach($name in $script:MMRoutes){$candidate=Join-Path $backup $name;if((Get-MMEntry $candidate) -eq 'dokbak'){$entry=$name;$entrySource=$candidate;break}}
        }
    }
    $actualKind=Get-MMEntry $entrySource
    if(($kind -eq 'dokbak' -and $actualKind -ne 'dokbak') -or ($kind -eq 'social' -and $actualKind -notin @('social','gift'))){throw '원본 입구의 소유권을 확인하지 못했습니다.'}
    $restored=Assert-MMPath $Game (Join-Path $stage $entry) -Tree;Copy-Item -LiteralPath $entrySource -Destination $restored
    $entryHash=Get-MMHash $restored
    if($Plan.ModuleId -eq 'dokbak'){$scriptPath=Join-Path $payload 'originals\dokbak\제거.cmd';$extra=''}
    else{$scriptPath=Join-Path $payload 'originals\social\제거.cmd';$extra=' -Target '+$(if($Plan.ModuleId -eq 'gift'){'Gift'}else{'Jade'})}
    # These are reviewed bundled CMDs with a quoted GamePath override, not shell text from metadata.
    foreach($value in @($scriptPath,$stage)){if($value -match '[\r\n"%!^&|<>]'){throw '원본 CMD 실행에 지원하지 않는 경로 문자입니다.'}}
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=Join-Path ([Environment]::GetFolderPath('System')) 'cmd.exe'
    $info.Arguments='/d /s /c ""'+$scriptPath+'" -GamePath "'+$stage+'"'+$extra+'"';$info.WorkingDirectory=Split-Path $scriptPath -Parent
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false);$info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
    $process=[Diagnostics.Process]::Start($info)
    try{
        $process.StandardInput.Close();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(!$process.WaitForExit(45000)){$process.Kill();throw '원본 제거기 응답 시간이 초과됐습니다. 실제 설치본은 변경하지 않았습니다.'}
        $output=$stdout.Result+"`r`n"+$stderr.Result;$code=$process.ExitCode
    }finally{$process.Dispose()}
    [IO.File]::WriteAllText((Join-Path $stage 'Original-Uninstall.log'),$output,[Text.UTF8Encoding]::new($true))
    if($code -ne 0){throw ('원본 제거기가 작업 사본에서 실패했습니다. 실제 설치본은 변경하지 않았습니다. 로그: '+(Join-Path $stage 'Original-Uninstall.log'))}
    foreach($path in $expected){if(Test-Path -LiteralPath $path){throw ('원본 제거기가 대상 사본을 남겼습니다. 실제 설치본은 변경하지 않았습니다: '+$path)}}
    if(Test-Path -LiteralPath $restored){throw '원본 제거기가 복원한 입구 사본을 정리하지 못했습니다. 실제 설치본은 변경하지 않았습니다.'}
    return [pscustomobject]@{Verified=$true;Method=(Get-MMOriginalMethod $Plan.ModuleId);Script=$scriptPath;RestoredEntry=$entry;EntrySource=$entrySource;EntryHash=$entryHash;Log=(Join-Path $stage 'Original-Uninstall.log');Scope='원본 입구 복원 및 원본 CMD 실행은 작업 사본에서 수행. 실제 공용 입구·다른 모드는 관리창이 보존.'}
}
