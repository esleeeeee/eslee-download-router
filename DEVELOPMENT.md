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

- `main`: 정식 배포와 최신 Release의 기준점
- `develop`: 다음 기능을 통합하는 지점
- `feature/*`: 기능 단위 작업
- 강제 push 금지

작업 시작 시 `git fetch --prune`, `git status`, `git branch --show-current`, `git remote -v`를 확인합니다. 공식 origin은 `https://github.com/esleeeeee/eslee-download-router.git`입니다.

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

## 색상과 사용자 문구

- 색은 `App.xaml`의 theme dictionary에서만 정의하고 화면 코드에 hex 값을 하드코딩하지 않습니다. Light와 Dark 양쪽에 같은 키를 정의합니다.
- Light는 흰색 카드와 연한 청회색 경계를 사용하고 파랑은 primary action, 선택 상태와 badge에만 적용합니다. 넓은 파란 배경은 사용하지 않습니다.
- 위험 작업은 `DangerTextBrush`, 경고 상태는 기존 `WarningStatusSurfaceBrush`를 사용합니다.
- Dark theme와 사용자의 기존 theme 설정은 유지하며 새 설치의 기본값을 임의로 바꾸지 않습니다.
- 내부 enum 이름을 화면에 노출하지 않습니다. 규칙 화면의 문구, 설명, 예시와 자연어 요약은 `RulePresentation`에서 관리합니다.
- 오류 메시지는 원인과 함께 사용자가 취할 다음 행동을 알려 줍니다.

## 자동 선택 팝업 상태

- 자동 팝업 자격은 `DownloadJobs.SelectionPromptState` 한 축으로만 판단합니다. 시간 기반 조건이나 브라우저 활동 시각을 다시 도입하지 않습니다.
- 상태는 `NeverShown → Shown → Deferred → Resolved` 방향으로만 전진합니다. 되돌리는 갱신은 Repository에서 거부합니다.
- 팝업을 여는 코드는 창을 표시하기 전에 `Shown`을 커밋해야 합니다.
- 새 상태나 명령을 추가할 때는 SQLite migration 버전을 올리고 재실행이 안전한지 확인합니다.

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

제품 버전은 `Directory.Build.props`의 `VersionPrefix` 한 곳에서만 변경합니다. 정식 v1.0.0부터 같은 정책을 App, Agent, Native Host와 Installer에 적용합니다. build/publish는 가능한 경우 Git HEAD를 `SourceRevisionId`로 전달하고 정보 화면은 informational version을 읽습니다. `build-installer.ps1`은 App/Agent/Native Host ProductVersion이 VersionPrefix와 일치하지 않으면 Inno Setup 실행 전에 실패합니다. `DownloadRouter.iss`에 버전을 직접 하드코딩하지 마세요.

Extension `manifest.json`의 버전은 Chromium 패키지 수명 주기용이며 제품 assembly 버전과 독립 관리합니다. 제품 버전 승격만을 이유로 Extension key, manifest version 또는 identity를 수정하지 마세요.

## 브랜딩 자산

- 현재 제품용 white master 원본은 `assets/branding/eslee-download-router.png`이며 생성된 `eslee-download-router.ico`와 함께 소스 관리합니다.
- `scripts/generate-branding-assets.ps1`은 master PNG를 변경하지 않고 16/20/24/32/40/48/64/128/256px ICO와 Extension 16/32/48/128px PNG를 재생성합니다. v1.0.1부터 24px 이상은 canvas 내부 artwork를 중앙 기준 1.18배로 렌더링하고, 트레이용 16/20px은 실제 Auto Power 비교와 1px 안전 여백을 만족하는 1.15배를 사용합니다. 종횡비, 색, 그림자와 구성 요소는 변경하지 않습니다.
- App EXE는 `ApplicationIcon`, MainWindow/FolderSelectionWindow는 embedded group icon ID, Installer는 `SetupIconFile`을 통해 같은 ICO를 사용합니다. 트레이는 현재 App EXE의 small icon을 추출하며 HICON lifetime을 직접 관리하고, 별도 버전 협상 없이 셸 기본 제품명 툴팁을 사용합니다.
- Extension `key`와 고정 ID `gilicenlclaemgiijcjjejilikbooggj`는 branding 변경과 무관하게 유지합니다. `manifest.json`의 별도 확장 버전은 제품 assembly 버전과 자동 동기화하지 않습니다.
- 아이콘을 갱신한 뒤에는 App/Installer embedded resource, Start Menu shortcut target, 실행 창·작업표시줄·트레이, Extension `dist/icons`를 실제 산출물에서 확인합니다. 작은 프레임은 알파 경계와 알파 32 이상 핵심 시각 경계를 모두 측정하며 Windows icon cache 파일을 직접 삭제하지 않습니다.

## 트레이, 자동 시작, 선택 창

- App은 단일 인스턴스이며 `--background`에서 메인 창을 숨긴 채 트레이와 Agent만 준비합니다.
- 로그인 자동 시작은 관리자 권한이 필요 없는 HKCU Run을 사용합니다. 값은 반드시 따옴표로 감싼 절대 App 경로와 `--background`여야 합니다.
- 기본 X 동작은 메인 AppWindow 숨김입니다. `종료` 명령과 설치 업그레이드의 `--shutdown`만 완전 종료를 요청합니다.
- 폴더 선택은 메인 창의 ContentDialog가 아니라 별도 Window입니다. 창 크기는 DIP를 실제 모니터 DPI로 변환한 뒤 작업 영역 안으로 제한합니다.
- 선택창은 MainWindow owner를 설정하지 않는 독립 HWND입니다. 최소화 회귀를 막기 위해 MainWindow가 아니라 선택창 HWND에만 normal 표시/activate/foreground를 적용하고, foreground 제한 시 입력 스레드 연결 재시도 뒤에만 Flash fallback을 사용합니다.
- 선택창 전면 표시 코드를 바꿀 때는 MainWindow hidden/minimized, selection iconic, 팝업 종료 뒤 MainWindow 상태, FIFO 중복과 취소 제거 회귀 테스트를 함께 실행합니다.
- 트리는 전체 재귀 열거를 금지하고 확장한 노드의 직계 자식만 비동기로 읽습니다. UI와 Agent 양쪽에서 루트 경계를 검사합니다.
- 이력의 경로 변경은 규칙을 수정하지 않고 Job의 상대 경로만 변경합니다. 이미 이동된 파일은 사용자 확인 뒤 기존 안전 이동 서비스를 다시 사용합니다.
- 자동 팝업은 `SelectionPromptPolicy.AutoPromptWindow`(30분)를 통과한 Job만 사용합니다. 대기 탭과 InfoBadge는 모든 실제 Pending을 사용하므로 두 목록을 다시 합치지 마세요.
- `Skipped`, `Cancelled`, `Interrupted`, `Completed`, `Failed` 같은 terminal Job을 startup/reconnect/중복 브라우저 이벤트에서 Pending으로 되돌리지 마세요. 중복 start와 값이 같은 metadata는 `LastBrowserEventAt`도 갱신하지 않습니다.
- 큐 일괄 이동 안 함은 UI collection만 비우지 말고 `selection.skip-many`의 단일 SQLite transaction이 성공한 뒤 큐를 비웁니다. 과거 stale Pending은 행별 사용자 결정 근거가 없으면 자동 migration하지 않습니다.
- 테마는 Window별 임시 코드 대신 App의 단일 ThemeManager에 등록합니다. 새 Window를 추가하면 Content 설정 직후 등록하고 UI 설정은 기존 `config.local.json`에 보존합니다.

## 설치/제거 검증

업그레이드 전 App의 `--shutdown` 완료를 기다리고 설치합니다. 설치 뒤에는 시작 메뉴, HKCU Run, 브라우저 Native Host, 백그라운드 단일 인스턴스, X 후 프로세스 유지와 트레이 메뉴를 확인합니다. 업그레이드 전후에는 격리된 테스트 DB와 설정이 보존되는지 기능적으로 확인합니다. 제거 스크립트는 `%LOCALAPPDATA%\eslee\DownloadRouter`를 삭제하면 안 됩니다.

## 정식 Release 절차

1. 버전과 문서를 기능 브랜치에서 확정하고 전체 build, test, audit와 Installer 사전 검증을 수행합니다.
2. PR의 최종 feature commit과 CI가 일치하는지 확인한 뒤 main에 병합합니다.
3. main을 `--ff-only`로 동기화하고 최종 main commit에서 Installer를 다시 생성합니다.
4. 생성된 App, Agent, Native Host와 Installer의 버전, commit metadata, icon, 크기와 SHA-256을 확인합니다.
5. 기존 설치 위에 같은 Installer를 적용해 사용자 데이터와 Native Messaging 등록을 다시 검증합니다.
6. main CI 성공 뒤 main commit에 `vX.Y.Z` tag와 GitHub Release를 생성합니다.
7. Release에는 최종 Installer만 첨부하고 민감정보를 제외한 검증 범위를 기록합니다.
8. GitHub에서 tag target, latest, draft false, prerelease false, asset 크기와 UTF-8 본문을 재조회합니다.

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
