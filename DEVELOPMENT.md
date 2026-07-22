# 개발 가이드

## 기준 환경

`global.json`은 .NET SDK `10.0.302`를 정확히 요구하고 roll-forward를 비활성화합니다. 패키지 버전은 `Directory.Packages.props`, 확장 도구 버전은 `package.json`과 `package-lock.json`에서 고정합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

bootstrap은 버전 검사, NuGet restore, `npm ci`, 로컬 데이터 디렉터리와 예제 설정 생성을 수행합니다. SDK, Node.js, npm, Inno Setup을 자동 설치하지 않습니다.

## 브랜치

- `main`: 빌드와 자동 테스트가 통과한 기준점
- `develop`: 다음 기능을 통합하는 지점
- `feature/*`: 기능 단위 작업
- 강제 push 금지

작업 시작 시 `git fetch --prune`, `git status`, `git branch --show-current`, `git remote -v`를 확인합니다. 공식 origin은 `https://github.com/esleeeeee/eslee-Download-Router.git`입니다.

## 코딩 원칙

- 핵심 로직은 UI나 브라우저 API에서 분리합니다.
- 외부 입력은 명령 allowlist, 메시지 크기, JSON 구조, 절대경로, 루트 경계를 검증합니다.
- I/O는 가능한 한 비동기로 처리하고 취소 토큰을 전달합니다.
- 로그에 query, fragment, 자격 증명, 실제 다운로드 파일 내용이 들어가지 않게 합니다.
- 새 패키지는 중앙 버전 파일 또는 잠금 파일에 정확한 버전을 기록합니다.
- 사용자명, 회사 경로, 브라우저 프로필, PC별 Native Host manifest를 커밋하지 않습니다.

## WinUI와 DPI

- unpackaged App manifest의 `PerMonitorV2, PerMonitor` 선언을 유지합니다. 제거하면 Windows가 앱을 96 DPI 비트맵으로 확대할 수 있습니다.
- XAML 크기와 간격은 DIP 기준이며 물리 픽셀이나 모니터 해상도별 고정 폭을 사용하지 않습니다.
- 새 화면은 공통 `ContentPanel` 안에서 가로 stretch하고, 폼 전체는 `min(1100 DIP, ScrollViewer viewport - padding)`을 넘지 않습니다.
- Compact/Wide의 공통 상단 여백 32/48 DIP를 화면별 Spacer로 중복 구현하지 않습니다.
- 좁은 창은 가로 스크롤 대신 세로 스크롤을 사용하고, 최소 폭을 지정할 때는 실제 720 DIP 창에서 우측 경계를 확인합니다.
- 시각 검증 시 `GetProcessDpiAwareness`, `GetWindowDpiAwarenessContext`, `GetDpiForWindow`와 진단 화면의 `XamlRoot.RasterizationScale`을 함께 확인합니다.

## 반복 작업

```powershell
# 개발 실행
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-dev.ps1

# Native Host와 Agent를 self-contained로 게시
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -PublishNativeHost

# Agent 파이프 왕복 확인
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\smoke-agent.ps1 -Configuration Debug

# SelectSubfolder 완료 전 선택/취소용 로컬 지연 다운로드
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\manual-download-server.ps1 `
  -Root .\artifacts\manual-whale\server -Port 8765

# 설치 페이로드 준비
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Configuration Release

# 사용자 단위 Inno Setup 설치 파일
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

`build-installer.ps1`은 publish 결과에 `App.xbf`, `MainWindow.xbf`, `DownloadRouter.App.pri`가 있는지 먼저 검사합니다. unpackaged WinUI 3 게시에서 이 세 파일이 빠지면 설치본이 `XamlParseException`으로 시작하지 못하므로 해당 검사를 제거하지 않습니다. 설치 파일은 아직 코드 서명되지 않았습니다.

제품 버전은 `Directory.Build.props`의 `VersionPrefix` 한 곳에서만 변경합니다. build/publish는 가능한 경우 Git HEAD를 `SourceRevisionId`로 전달하고 정보 화면은 informational version을 읽습니다. `build-installer.ps1`은 App/Agent/Native Host ProductVersion이 VersionPrefix와 일치하지 않으면 Inno Setup 실행 전에 실패합니다. `DownloadRouter.iss`에 버전을 직접 하드코딩하지 마세요.

## 트레이, 자동 시작, 선택 창

- App은 단일 인스턴스이며 `--background`에서 메인 창을 숨긴 채 트레이와 Agent만 준비합니다.
- 로그인 자동 시작은 관리자 권한이 필요 없는 HKCU Run을 사용합니다. 값은 반드시 따옴표로 감싼 절대 App 경로와 `--background`여야 합니다.
- 기본 X 동작은 메인 AppWindow 숨김입니다. `종료` 명령과 설치 업그레이드의 `--shutdown`만 완전 종료를 요청합니다.
- 폴더 선택은 메인 창의 ContentDialog가 아니라 별도 Window입니다. 창 크기는 DIP를 실제 모니터 DPI로 변환한 뒤 작업 영역 안으로 제한합니다.
- 트리는 전체 재귀 열거를 금지하고 확장한 노드의 직계 자식만 비동기로 읽습니다. UI와 Agent 양쪽에서 루트 경계를 검사합니다.
- 이력의 경로 변경은 규칙을 수정하지 않고 Job의 상대 경로만 변경합니다. 이미 이동된 파일은 사용자 확인 뒤 기존 안전 이동 서비스를 다시 사용합니다.
- 자동 팝업은 `SelectionPromptPolicy.AutoPromptWindow`(30분)를 통과한 Job만 사용합니다. 대기 탭과 InfoBadge는 모든 실제 Pending을 사용하므로 두 목록을 다시 합치지 마세요.
- 테마는 Window별 임시 코드 대신 App의 단일 ThemeManager에 등록합니다. 새 Window를 추가하면 Content 설정 직후 등록하고 UI 설정은 기존 `config.local.json`에 보존합니다.

## 설치/제거 검증

업그레이드 전 App의 `--shutdown` 완료를 기다리고 설치합니다. 설치 뒤에는 시작 메뉴, HKCU Run, 6개 브라우저 Native Host, 백그라운드 단일 인스턴스, X 후 프로세스 유지와 트레이 메뉴를 확인합니다. 업그레이드 전후에는 사용자 DB의 행 수·SHA-256과 `config.local.json`의 테마 값을 비교합니다. 제거 스크립트는 `%LOCALAPPDATA%\eslee\DownloadRouter`를 삭제하면 안 됩니다.

## 기능 추가 순서

1. `DownloadRouter.Core`에 모델과 순수 로직 추가
2. Core 단위 테스트 추가
3. Infrastructure 어댑터와 기능 테스트 추가
4. Agent 명령 allowlist와 처리기 연결
5. App 또는 Extension 어댑터 연결
6. 통합 테스트와 수동 브라우저 시나리오 갱신
7. `PROJECT_STATE.md`, `HANDOFF.md`, 필요 시 `CHANGELOG.md` 갱신

## 작업 종료 체크리스트

- [ ] 모든 변경 저장
- [ ] Debug/Release 빌드
- [ ] .NET 및 TypeScript 테스트
- [ ] 가능한 브라우저 수동 검증
- [ ] `git diff --check`와 `git status` 확인
- [ ] `PROJECT_STATE.md`, `HANDOFF.md`, `CHANGELOG.md` 검토
- [ ] 의미 있는 커밋 생성
- [ ] 공식 origin으로 push
- [ ] 원격 커밋 반영 확인
- [ ] 로컬 전용 필수 파일이 없는지 확인
- [ ] Notion 전체이력과 프로젝트 상세 기록 동기화
