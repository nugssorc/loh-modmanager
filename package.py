from pathlib import Path
import hashlib
import json
import re
import shutil
import subprocess
import sys
import zipfile

ROOT=Path(__file__).resolve().parent
DIST=ROOT/'dist'
# 함께 묶는 모드·원본 제거기는 2_독박육아 저장소에 있다(2026-10-30 옮김). 공개 소스 묶음에는 없다(그때는 공개판만 만든다).
WORK=ROOT.parent/'2_독박육아'
NAME='내모드관리_v0.12.52'
VERSION='0.12.52'
PUBLIC='영웅입지전_모드관리_v'+VERSION
SCRIPTS=['Manager.Cli.ps1','Manager.Core.ps1','Inventory.Core.ps1','Removal.Core.ps1','Original.Core.ps1','Install.Core.ps1']

def manifest(payload,edition):
    files=[dict(Path=p.relative_to(payload).as_posix(),SHA256=hashlib.sha256(p.read_bytes()).hexdigest().upper()) for p in sorted(payload.rglob('*')) if p.is_file()]
    (payload/'manifest.json').write_text(json.dumps(dict(Version=VERSION,Edition=edition,Files=files),ensure_ascii=False,indent=2),'utf-8')

def zip_folder(folder,zip_path):
    with zipfile.ZipFile(zip_path,'w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(folder.rglob('*')):
            if p.is_file():z.write(p,Path(folder.name)/p.relative_to(folder))

def protect(folder,zip_path,password):
    """The same zip with a password (for posting): Bandizip's console tool, ZIP format so Windows' own unzip asks for it."""
    bz=Path(r'C:\Program Files\Bandizip\bz.exe')
    if not bz.exists():raise SystemExit('비밀번호 압축에는 반디집 콘솔 도구(bz.exe)가 필요합니다.')
    if zip_path.exists():zip_path.unlink()
    r=subprocess.run([str(bz),'c','-y','-r','-p:'+password,'-fmt:zip',str(zip_path),str(folder)],capture_output=True,text=True,encoding='utf-8',errors='replace')
    if r.returncode or not zip_path.exists():raise SystemExit('비밀번호 압축 실패: '+r.stdout+r.stderr)
    with zipfile.ZipFile(zip_path) as z:   # must open with the password using the classic (ZipCrypto) scheme Windows understands
        z.setpassword(password.encode());names=z.namelist()
        assert names and all(n.startswith(folder.name+'/') for n in names),names[:3]
        for n in names:
            if not n.endswith('/'):z.read(n)

def fresh(destination):
    """Empty the package folder, keeping an identical (possibly running) exe."""
    if destination.exists():
        assert destination.resolve().is_relative_to(DIST.resolve())
        installed=destination/'모드관리.exe'
        if installed.exists() and installed.read_bytes() != (ROOT/'build/모드관리.exe').read_bytes():
            # A running window keeps its exe locked; only then refuse (before, any rebuild was refused).
            try:installed.unlink()
            except PermissionError:raise SystemExit('관리창 실행 파일이 바뀌었습니다. 열린 관리창을 닫은 뒤 다시 묶으세요.')
        for child in destination.iterdir():
            if child.name=='모드관리.exe':continue
            assert child.resolve().is_relative_to(DIST.resolve())
            if child.is_dir():shutil.rmtree(child)
            else:child.unlink()
    destination.mkdir(parents=True,exist_ok=True)
    if not (destination/'모드관리.exe').exists():shutil.copy2(ROOT/'build/모드관리.exe',destination/'모드관리.exe')

def personal():
    """The developer's own package: manager + the bundled mods and original uninstallers (tests and install_app.ps1 use it)."""
    destination=DIST/NAME;fresh(destination)
    payload=destination/'_files';payload.mkdir()
    for name in SCRIPTS+['사용법.md']:
        shutil.copy2(ROOT/name,destination/name)
    shutil.copytree(ROOT/'build/entries',payload/'entries')
    for folder,module in [('선물선호표시모드','LOH_GiftHighlight'),('금란선옥모드','LOH_GoldenOrchidJade')]:
        target=payload/'modules'/module;target.mkdir(parents=True)
        for file in [module+'.dll',module+'Loader.exe']:
            shutil.copy2(ROOT/'build/modules'/module/file,target/file)
        for file in ['사용법.md','LICENSE']:
            shutil.copy2(WORK/folder/file,target/file)
    shutil.copytree(WORK/'금란선옥모드/build/금란선옥',payload/'data/금란선옥')
    original=payload/'originals';original.mkdir()
    dokbak=original/'dokbak';(dokbak/'_files').mkdir(parents=True)
    shutil.copy2(WORK/'아이성장선물모드/installer/제거.cmd',dokbak/'제거.cmd')
    shutil.copy2(WORK/'아이성장선물모드/installer/tool.ps1',dokbak/'_files/tool.ps1')
    social=original/'social';(social/'_files').mkdir(parents=True)
    shutil.copy2(WORK/'모드공통/설치관리.ps1',social/'remove.ps1')
    # Original CMD command, without interactive pause; preserve the PS exit status.
    (social/'제거.cmd').write_bytes(('@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0remove.ps1" -Action Remove %*\r\nexit /b %errorlevel%\r\n').encode('ascii'))
    (social/'_files/manifest.json').write_text(json.dumps(dict(DefaultTarget='Both',Modules=[dict(Name='LOH_GiftHighlight',Version='0.5.3',Data=None),dict(Name='LOH_GoldenOrchidJade',Version='1.1.0',Data='금란선옥')],Files=[]),ensure_ascii=False),'utf-8')
    shutil.copy2(WORK/'선물선호표시모드/build/version.dll',original/'social-version.dll')
    shutil.copy2(WORK/'아이성장선물모드/installer/pkg/독박육아_v1.2.5/_files/entry/hid.dll',original/'dokbak-hid.dll')
    manifest(payload,'personal')
    zip_folder(destination,DIST/(NAME+'.zip'))
    print(destination.resolve())

# Anything that points at the developer's PC or account must never reach a public package.
PERSONAL=[re.compile(p) for p in ['작업'+'실','lemon'+'cake','booyong'+'soda','LEMON'+'C~1',r'@gmail\.com',r'[A-Za-z]:\\Users\\',r'C:\\\\Users']]   # split words: this file ships in the source package
def assert_clean(folder):
    for p in folder.rglob('*'):
        if not p.is_file():continue
        data=p.read_bytes()
        for text in {data.decode('utf-8','ignore'),data.decode('utf-16-le','ignore')}:
            for pattern in PERSONAL:
                if pattern.search(text):raise SystemExit(f'공개 묶음에 개인 흔적({pattern.pattern})이 있습니다: {p}')

def public(password=''):
    """For sharing: the manager alone (no bundled mods, no developer paths) + its source, buildable on its own."""
    out=DIST/'공개';out.mkdir(parents=True,exist_ok=True)
    destination=out/PUBLIC;fresh(destination)
    payload=destination/'_files';payload.mkdir()
    for name in SCRIPTS:shutil.copy2(ROOT/name,destination/name)
    shutil.copy2(ROOT/'public/사용법.txt',destination/'사용법.txt');shutil.copy2(ROOT/'public/LICENSE',destination/'LICENSE')
    shutil.copy2(ROOT/'public/모드정보_양식.md',destination/'모드정보_양식.md')   # for mod makers: loh-mod.json
    shutil.copytree(ROOT/'build/entries',payload/'entries')
    manifest(payload,'public')
    assert_clean(destination)
    zip_folder(destination,out/(PUBLIC+'.zip'))
    # the same zip without a password, ASCII name: the GitHub release asset the manager's '지금 업데이트' downloads (publish_github.ps1)
    shutil.copy2(out/(PUBLIC+'.zip'),out/f'loh-modmanager-{VERSION}.zip')
    # source: what build.py needs, the entry-build files copied in from 2_독박육아, no notes, logs or tests
    source=out/(PUBLIC+'_소스')
    if source.exists():
        assert source.resolve().is_relative_to(DIST.resolve());shutil.rmtree(source)
    (source/'src').mkdir(parents=True);(source/'native').mkdir();(source/'tools').mkdir();(source/'public').mkdir()
    for name in ['App.cs','Core.cs','MainWindow.xaml','app.ico','app.manifest','app_icon_source.png','entry.c','skin_bg.jpg']:shutil.copy2(ROOT/'src'/name,source/'src'/name)
    for name in SCRIPTS+['build.py','package.py']:shutil.copy2(ROOT/name,source/name)
    shutil.copy2(ROOT/'tools/make_icon.cs',source/'tools/make_icon.cs')
    shutil.copy2(ROOT/'public/사용법.txt',source/'public/사용법.txt');shutil.copy2(ROOT/'public/사용법.txt',source/'사용법.txt')
    shutil.copy2(ROOT/'public/README_소스.md',source/'README.md');shutil.copy2(ROOT/'public/README_소스.md',source/'public/README_소스.md')
    shutil.copy2(ROOT/'public/LICENSE',source/'LICENSE');shutil.copy2(ROOT/'public/LICENSE',source/'public/LICENSE')   # MIT
    shutil.copy2(ROOT/'public/모드정보_양식.md',source/'public/모드정보_양식.md')
    native=ROOT/'native' if (ROOT/'native').is_dir() else None
    if native:   # building from the source package itself
        for p in native.iterdir():shutil.copy2(p,source/'native'/p.name)
    else:
        n=WORK/'아이성장선물모드/installer/native'
        for name in ['fix_exports.py','sys_exports.json']+[f'entry_{e}.def' for e in ['hid','version','winhttp','winmm']]:shutil.copy2(n/name,source/'native'/name)
        shutil.copy2(WORK/'모드공통/native/win.h',source/'native/win.h');shutil.copy2(WORK/'모드공통/native/lib/kernel32.def',source/'native/kernel32.def')
    assert_clean(source)
    zip_folder(source,out/(PUBLIC+'_소스.zip'))
    if password:   # python package.py --password=1234
        protect(destination,out/(PUBLIC+'.zip'),password);protect(source,out/(PUBLIC+'_소스.zip'),password)
    post=ROOT/'public/디시_게시글.txt'
    if post.exists():   # draft post text beside the zips (not inside); title and file names carry the version, the password line only when there is one
        lines=[l.replace('{PASSWORD}',password).replace('{VERSION}',VERSION) for l in post.read_text('utf-8-sig').splitlines() if password or '{PASSWORD}' not in l]
        (out/'디시_게시글.txt').write_text('\n'.join(lines)+'\n','utf-8-sig')
    print(destination.resolve());print(source.resolve())
    if password:release(out,password)

# 다른 모드처럼 올릴 판 사본을 공용 '배포_올릴것' 에 둔다(비밀번호 판만, 원본은 dist\공개). 읽어주세요.txt 의 관리창 줄을 새 판으로 바꾼다.
RELEASE=ROOT.parent/'배포_올릴것'
def release(out,password):
    if not RELEASE.is_dir():return   # source package built elsewhere
    names=[PUBLIC+'.zip',PUBLIC+'_소스.zip']
    for n in names:shutil.copy2(out/n,RELEASE/n)
    readme=RELEASE/'읽어주세요.txt'
    lines=readme.read_text('utf-8-sig').splitlines() if readme.exists() else ['배포_올릴것 — 드라이브에 올릴 최신 배포 파일 사본','']
    lines=[l for l in lines if not l.startswith('영웅입지전_모드관리_v')]
    entry=f'{names[0]} · {names[1]}  비밀번호 {password}  (원본: 5_모드매니저\\dist\\공개, 게시글 초안 디시_게시글.txt 도 거기)'
    at=next((i for i,l in enumerate(lines) if i>0 and l.strip()),len(lines))   # first file line, under the title
    lines.insert(at,entry)
    raw=readme.read_bytes() if readme.exists() else b'\r\n'   # keep its encoding (no BOM) and Windows line ends
    readme.write_bytes((b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'')+('\r\n'.join(lines)+'\r\n').encode('utf-8'))
    print(RELEASE.resolve()/names[0])

def main():
    password=next((a.split('=',1)[1] for a in sys.argv[1:] if a.startswith('--password=')),'')
    if WORK.is_dir():personal()
    public(password)

if __name__=='__main__':main()
