"""Build four interchangeable Windows forwarding entries and managed social modules."""
from pathlib import Path
import os
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent
# 2026-10-30 독박육아 저장소에서 옮김: 함께 묶는 모드·공통 빌드 도구는 그대로 2_독박육아 에 있다
WORK = ROOT.parent/'2_독박육아'
OUT = ROOT/'build'
NATIVE = WORK/'아이성장선물모드/installer/native'
COMMON = WORK/'모드공통'
WIN_H, KERNEL_DEF = COMMON/'native/win.h', COMMON/'native/lib/kernel32.def'
VENDOR = ROOT/'native'   # the public source package carries these entry-build files itself (package.py 공개판), so it builds alone
if VENDOR.is_dir(): NATIVE, WIN_H, KERNEL_DEF = VENDOR, VENDOR/'win.h', VENDOR/'kernel32.def'
ZIG = ROOT/'.tools/zig-x86_64-windows-0.14.1/zig.exe'

def csc_references():
    wpf=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/WPF'
    return [f'/reference:{wpf/name}' for name in ['PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll']]+\
        ['/reference:System.Xaml.dll','/reference:System.Management.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll']   # zip: 모드매니저 자기 업데이트

def main():
    OUT.mkdir(exist_ok=True)
    temp = OUT/'_tmp';temp.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(dir=temp) as tmp:
        w = Path(tmp)
        env = dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(temp/'zig-cache'))
        def run(args, exe=ZIG):
            r = subprocess.run([str(exe), *map(str,args)], cwd=w, env=env, capture_output=True, text=True, encoding='utf-8', errors='replace')
            if r.stdout or r.stderr:print(r.stdout+r.stderr)
            if r.returncode:raise SystemExit(r.returncode)
        shutil.copy2(ROOT/'src/entry.c',w/'entry.c')
        shutil.copy2(WIN_H,w/'win.h')
        defs=KERNEL_DEF.read_text('utf-8')
        for name in ['GetPrivateProfileStringW','GetPrivateProfileIntW','WritePrivateProfileStringW','GetProcessTimes','LoadLibraryW']:
            if '\n'+name+'\n' not in '\n'+defs+'\n':defs+='\n'+name+'\n'
        (w/'kernel32.def').write_text(defs,'utf-8')
        run(['dlltool','-m','i386:x86-64','-d','kernel32.def','-l','kernel32.lib'])
        flags=['-target','x86_64-windows-msvc','-nostdlib','-nostdinc','-O2','-ffreestanding','-fno-stack-protector','-mno-stack-arg-probe','-fno-builtin','-Wall','-Wextra','-Wno-misleading-indentation','-I','.']
        run(['cc',*flags,'-c','entry.c','-o','entry.obj'])
        fix=(NATIVE/'fix_exports.py').read_text('utf-8').replace('LOH_DokbakYuga_Entry','LOH_ModManager_Entry')
        # 0.7.2: plugins of an absorbed HeroesVow loader (QuickTrade, CityPriceHighlight) find it as 'version.dll'
        # (GetModuleHandleW + GetProcAddress). The version entry forwards the loader's two exports to the absorbed copy
        # HeroesVowNativeModHost.dll (no dots: a forwarder is "module.function"). Without that file they resolve to nothing.
        anchor="items.append((mx+1,'LOH_ModManager_Entry',None))"
        assert anchor in fix
        fix=fix.replace(anchor,anchor+"\nif n=='version': items+=[(mx+2+i,s,'HeroesVowNativeModHost.'+s) for i,s in enumerate(['NativeModHost_RegisterManagedCallbackV1','NativeModHost_StatusV1'])]")
        (w/'fix.py').write_text(fix,'utf-8')
        for name in ['hid','version','winhttp','winmm']:
            definition=(NATIVE/f'entry_{name}.def').read_text('utf-8').replace('LOH_DokbakYuga_Entry','LOH_ModManager_Entry')
            (w/'entry.def').write_text(definition,'utf-8')
            run(['lld-link','/dll','/nodefaultlib','/entry:DllMain','/def:entry.def',f'/out:{name}.dll','entry.obj','kernel32.lib','/Brepro'])
            run([w/'fix.py',w/f'{name}.dll',name,NATIVE/'sys_exports.json'],sys.executable)
            destination=OUT/'entries';destination.mkdir(exist_ok=True);shutil.copy2(w/f'{name}.dll',destination/f'{name}.dll')
        # 0.5: WPF window (src/App.cs + MainWindow.xaml as a resource read at run time) over src/Core.cs.
        # Built by the Windows-included csc 4.x; no SDK or extra runtime.
        # csc does not read the XAML, so a typo there only shows when the window opens (it closes at once): catch it here.
        import xml.dom.minidom
        try:xml.dom.minidom.parse(str(ROOT/'src/MainWindow.xaml'))
        except Exception as e:raise SystemExit('MainWindow.xaml 이 올바른 XML 이 아닙니다: '+str(e))
        csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        run(['/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',*csc_references(),
             f'/resource:{ROOT/"src/MainWindow.xaml"},MainWindow.xaml',f'/win32icon:{ROOT/"src/app.ico"}',f'/resource:{ROOT/"src/app.ico"},app.ico',f'/resource:{ROOT/"src/skin_bg.jpg"},skin_bg.jpg',  # icon: tools/make_icon.cs
             f'/win32manifest:{ROOT/"src/app.manifest"}',  # PerMonitorV2: sharp on every monitor
             '/out:manager.exe',ROOT/'src/Core.cs',ROOT/'src/App.cs'],csc)
        exe=(w/'manager.exe').read_bytes()   # 0.9.8 lost the window icon to a comment in the line above: check what must be inside
        for need in [b'app.ico',b'MainWindow.xaml',b'PerMonitorV2']:assert need in exe,'모드관리.exe 에 '+need.decode()+' 가 없습니다'
        shutil.copy2(w/'manager.exe',OUT/'모드관리.exe')
    if not WORK.is_dir():return   # public source package: the manager and its entries only (the bundled mods are not part of it)
    # The bundled gift/jade DLLs are built from the 2_독박육아 working tree: warn when it holds uncommitted changes.
    dirty=subprocess.run(['git','-C',str(WORK),'status','--porcelain','--','선물선호표시모드','금란선옥모드','모드공통'],capture_output=True,text=True,encoding='utf-8').stdout.strip()
    if dirty:print('주의: 2_독박육아 의 묶이는 모드 소스에 커밋 안 한 변경이 있습니다. 이 빌드의 표시·금란선옥 DLL 은 시험 중인 소스일 수 있으니 전체 설치(install_app.ps1)에 쓰지 마세요.\n'+dirty)
    # The version table (Manager.Core.ps1, Original.Core.ps1, package.py) must name the versions this build bundles.
    for folder,src,mid in [('선물선호표시모드','gift.c','gift'),('금란선옥모드','jade.c','jade')]:
        want=re.search(r'#define MODULE_VERSION "([^"]+)"',(WORK/folder/'src'/src).read_text('utf-8')).group(1)
        core=(ROOT/'Manager.Core.ps1').read_text('utf-8-sig');module=dict(gift='LOH_GiftHighlight',jade='LOH_GoldenOrchidJade')[mid]
        assert re.search(rf"Id='{mid}';[^\n]*Version='{re.escape(want)}'",core),f'Manager.Core.ps1 의 {mid} 판이 {src} 의 {want} 와 다릅니다'
        assert re.search(rf"Name='{module}',Version='{re.escape(want)}'",(ROOT/'package.py').read_text('utf-8')),f'package.py 의 {module} 판이 {want} 와 다릅니다'
        if mid=='jade':assert f"Version='{want}'" in (ROOT/'Original.Core.ps1').read_text('utf-8-sig'),f'Original.Core.ps1 의 금란선옥 판이 {want} 와 다릅니다'
    sys.path.insert(0,str(COMMON))
    from build_native import build
    build(WORK/'선물선호표시모드','LOH_GiftHighlight','gift.c',['test_rules'],output=OUT/'modules/LOH_GiftHighlight',manager=True)
    build(WORK/'금란선옥모드','LOH_GoldenOrchidJade','jade.c',['test_hooks'],output=OUT/'modules/LOH_GoldenOrchidJade',manager=True)

if __name__=='__main__':main()
