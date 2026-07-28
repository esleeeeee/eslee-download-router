# 사용자 단위 설치 파일

`DownloadRouter.iss`는 관리자 권한 없이 현재 사용자에게 설치하는 Inno Setup 6 정의입니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

산출물은 `artifacts\installer\eslee-download-router-setup.exe`입니다. Release self-contained App, Agent, Native Host, 확장 dist와 등록/해제 스크립트를 포함합니다.

설치 프로그램의 `AppVersion`은 `DownloadRouter.iss`에 하드코딩하지 않습니다. `build-installer.ps1`이 `Directory.Build.props`의 `VersionPrefix`를 읽어 Inno Setup에 전달하고 App/Agent/Native Host ProductVersion의 일치를 먼저 검사합니다.

## 설치 정책

- 기본 경로: `%LOCALAPPDATA%\Programs\eslee\DownloadRouter`
- 시작 메뉴 바로가기, 선택적 바탕화면 바로가기
- 선택한 경우 HKCU Run에 `DownloadRouter.App.exe --background`
- Whale, Edge, Chrome, Brave, Vivaldi, Opera의 HKCU Native Host 등록
- 업그레이드 전 설치 App에 `--shutdown`을 전달해 App과 Agent를 정상 종료
- unpackaged WinUI 필수 리소스 `App.xbf`, `MainWindow.xbf`, `DownloadRouter.App.pri` 포함
- `%LOCALAPPDATA%\eslee\DownloadRouter`의 DB, 규칙, 이력과 `config.local.json` 테마는 업그레이드에서 보존

## 제거 정책

제거는 자동 시작, Native Host 등록, 바로가기와 설치 파일만 삭제합니다. 사용자 규칙과 이력이 들어 있는 `%LOCALAPPDATA%\eslee\DownloadRouter`는 기본 보존합니다. 사용자 데이터 삭제 옵션은 현재 제공하지 않으므로 제거기에서 이 경로를 추가로 지우지 마세요.

## 배포 전 확인

- 설치/업그레이드/제거 종료 코드
- 설치 App 10초 이상 안정 실행과 X 후 트레이 상주
- 시작 메뉴/자동 시작/Native Host 등록 및 제거
- 제거 전후 SQLite 행 수와 파일 SHA-256 동일
- App/Agent/Native Host/Installer 제품 버전 일치와 정보 화면 표시
- System/Light/Dark 즉시 전환, 트레이 복원·완전 재실행·백그라운드 시작 후 유지
- 서명된 배포라면 `SignTool` 설정과 인증서 체인을 별도 검토

현재 로컬 검증 산출물은 코드 서명되지 않았습니다.
