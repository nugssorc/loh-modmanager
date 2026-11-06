# 영웅입지전 모드매니저

영웅입지전(Legend of Heroes) 모드를 한 창에서 켜고 끄고, 순서를 정하고, 설치·삭제하는 관리 프로그램입니다. 쓰는 법은 `사용법.txt`.

## 받기
오른쪽 **Releases** 에서 가장 위(최신) 버전의 zip 을 받아 압축을 풀고 `모드관리.exe` 를 실행하세요.

## 업데이트
0.12.0 부터는 관리창이 새 버전을 스스로 확인합니다. 새 버전이 있으면 위쪽에 알림이 뜨고, **지금 업데이트** 를 누르면 받아서 바꿔 줍니다.

## 문의
디시인사이드 영웅입지전 마이너 갤러리: https://gall.dcinside.com/mgallery/board/view/?id=san14&no=78820

## 구성

| 파일 | 하는 일 |
|---|---|
| `src/App.cs`, `src/MainWindow.xaml` | 관리창 화면(WPF). XAML 은 실행할 때 읽고, 색은 모두 테마 키(밝게/어둡게) |
| `src/Core.cs` | 화면 없는 부분: 상태 읽기, 목록 합치기, 충돌 찾기, 프로필, 게임 폴더 찾기, 백엔드 호출 |
| `src/entry.c` | 게임 폴더에 두는 입구 DLL(version/hid/winhttp/winmm). `LOH_ModManager\Modules.ini` 의 DLL 을 순서대로 불러오고 원래 시스템 DLL 로 이어 줌 |
| `src/app.manifest` | 일반 권한 실행, 모니터별 배율(PerMonitorV2) |
| `Manager.Cli.ps1` | 백엔드 입구(화면이 `-Action` 으로 부름, JSON 으로 답함) |
| `Manager.Core.ps1` | 저장(입구·Modules.ini·Catalog), 실행 확인, 백업 |
| `Install.Core.ps1` | 압축 분석·설치·제거·복원, 패처 레시피(올인원·창천UI·HeroesVow), 패처 쌓기·게임 업데이트 감지 |
| `Inventory.Core.ps1`, `Removal.Core.ps1`, `Original.Core.ps1` | 설치 항목 조회, 백업 후 제거·복원 |
| `native/` | 입구 DLL 빌드에 쓰는 정의 파일(내보내기 목록, win.h 등) |
| `build.py`, `package.py` | 빌드와 배포 묶음 만들기 |

## 빌드

필요: Windows 10/11, Python 3.11 이상, [zig 0.14.1](https://ziglang.org/download/) (입구 DLL 용).
관리창은 Windows 에 들어 있는 .NET Framework 4.x 의 `csc.exe` 로 빌드하므로 따로 SDK 가 필요 없습니다.

1. zig 0.14.1 Windows x86_64 압축을 이 폴더의 `.tools\zig-x86_64-windows-0.14.1\` 에 풉니다(`zig.exe` 가 그 안에 있게).
2. `python build.py` → `build\모드관리.exe`, `build\entries\*.dll`
3. `python package.py` → `dist\공개\영웅입지전_모드관리_v…\` 와 zip

## 동작 원칙

- 게임 폴더 밖은 건드리지 않고, 바꾸는 파일은 모두 기록 · 백업한 뒤 바꿉니다.
- 압축 안의 설치 프로그램은 실행하지 않습니다(알려진 패처 레시피만, 사용자가 체크했을 때).
- 게임 실행 중에는 DLL 을 바꾸지 않습니다.
- 게임 파일(GameAssembly.dll 등)은 묶음에 넣지 않습니다. 패처용 원본 사본은 각 사용자 PC 의 게임 폴더 안에서만 만들어 둡니다.

## 라이선스

코드는 MIT입니다(`LICENSE`). 아이콘(`src/app_icon_source.png`, `src/app.ico`)과 배경 그림(`src/skin_bg.jpg`)은 MIT에 포함되지 않습니다.
이 프로그램은 팬이 만든 비공식 도구이며 게임 제작사와 관련이 없습니다.

