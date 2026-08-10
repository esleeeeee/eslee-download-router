# 다른 PC 인수인계

정식 배포 기준은 GitHub의 `main`과 [최신 Release](https://github.com/esleeeeee/eslee-download-router/releases/latest)입니다. 회사 PC의 빌드 산출물, 사용자 DB, 브라우저 프로필이나 개인 경로 없이 개발을 이어갈 수 있습니다.

## 기준점

- 저장소: `https://github.com/esleeeeee/eslee-download-router.git`
- 기본 브랜치: `main`
- 제품 버전: `1.1.4`
- 최종 병합 PR: [#6](https://github.com/esleeeeee/eslee-download-router/pull/6)
- .NET SDK: `10.0.302`
- Node.js: 24 이상
- npm: 11 이상
- 운영체제: Windows 11 x64

## 새 PC 절차

```powershell
git clone https://github.com/esleeeeee/eslee-download-router.git
cd eslee-download-router
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -PublishNativeHost
```

일반 사용자 설치 검증은 GitHub Release의 `eslee-download-router-setup.exe`를 사용합니다. 개발 출력으로 검증할 때만 `scripts\register-native-host.ps1`과 `scripts\run-dev.ps1`을 사용하세요.

## 정식 정상 기준

- Debug와 Release build 성공, 경고 0, 오류 0
- .NET tests 192/192 이상
- Extension tests 28/28 이상
- npm audit high 이상 취약점 0건
- Agent Named Pipe와 Native Host framed ping 성공
- App, Agent와 Native Host win-x64 self-contained publish 성공
- App, Agent, Native Host와 Installer 제품 버전 1.1.4 일치
- Per-Monitor V2, 공통 반응형 폭, 전역 테마와 독립 FolderSelectionWindow
- Whale Automatic, SelectSubfolder, 취소, FIFO와 영구 `Skipped`
- 기존 설치 위 업그레이드에서 DB, 규칙, 이력, Pending, 설정, 테마와 자동 시작 보존
- 고정 Extension ID `gilicenlclaemgiijcjjejilikbooggj`와 Native Messaging identity 유지

## 개발자 모드 확장

Release 설치본은 `%LOCALAPPDATA%\Programs\eslee\DownloadRouter\extension`에 Extension을 설치합니다. Whale의 `whale://extensions`에서 개발자 모드를 켜고 이 폴더를 압축 해제된 확장으로 로드합니다. Native Messaging 등록은 Installer가 처리하므로 사용자가 레지스트리를 직접 수정하지 않습니다.

개발 clone에서는 `src\DownloadRouter.Extension\dist`를 로드합니다. 어느 쪽이든 앱의 브라우저 연결 화면이 실행 위치를 기준으로 실제 폴더를 찾아 보여 주므로 경로를 직접 입력할 필요는 없습니다. 자세한 브라우저별 주소와 절차는 [docs/MANUAL_EXTENSION_INSTALL.md](docs/MANUAL_EXTENSION_INSTALL.md)를 따릅니다.

## PC별로 다시 지정할 항목

- 규칙의 저장 root 또는 `{Downloads}`, `{Documents}` 같은 토큰 경로
- 브라우저별 개발자 모드 확장 로드
- 회사 정책의 Native Messaging과 개발자 모드 허용 여부

사용자명, 절대 저장 경로, DB, 로그, 브라우저 프로필과 인증정보는 저장소에 포함하지 않습니다.

## 변경 작업 절차

1. `main` 최신 상태에서 별도 기능 브랜치를 만듭니다.
2. Core, Infrastructure, Agent, App 또는 Extension 순서로 필요한 최소 범위만 변경합니다.
3. Debug와 Release build, 전체 테스트와 관련 실제 브라우저 시나리오를 수행합니다.
4. `PROJECT_STATE.md`, `CHANGELOG.md`, `TESTING.md`와 브라우저 기록을 갱신합니다.
5. `git diff --check`, `git diff`, `git status`를 검토합니다.
6. PR과 GitHub Actions가 성공한 뒤 main에 병합합니다.
7. 제품 버전을 바꾸는 경우 최종 main commit에서 Installer를 다시 생성하고 Release asset과 SHA-256을 검증합니다.

## 로컬 전용 항목

다음 항목은 의도적으로 Git에서 제외합니다.

- `artifacts` 빌드, 테스트와 Installer 산출물
- `node_modules`
- `%LOCALAPPDATA%\eslee\DownloadRouter` 사용자 DB, 설정, 로그와 Native Host manifest
- 실제 다운로드 fixture와 브라우저 프로필
