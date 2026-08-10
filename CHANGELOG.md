# 변경 이력

모든 주요 변경은 이 파일에 누적합니다. 버전은 Semantic Versioning을 따릅니다.

## [Unreleased]

### 1.1.4 후보

아직 GitHub Release로 게시하지 않은 개발 중 버전입니다.

#### Added

- 정보 화면에 업데이트 확인을 추가. 현재 버전 옆에서 GitHub 최신 정식 Release와 비교한 결과를 보여주고, `지금 확인`과 `Release 페이지 열기`를 제공합니다. draft와 prerelease는 제외합니다.
- 시작 시 비동기 자동 확인을 추가. 하루 1회로 제한되고 결과는 로컬 설정에 저장되므로 매 실행마다 요청하지 않습니다. 확인 실패는 다운로드 감시와 분류에 영향을 주지 않습니다.

#### Changed

- 네트워크를 쓰지 않는다는 개인정보 설명을 실제에 맞게 갱신. 네트워크 요청은 업데이트 확인 하나뿐이며 GitHub의 공개 배포 정보만 읽고 개인 정보를 보내지 않는다는 점을 정보 화면, README, `SECURITY.md`에 명시했습니다. Agent, Native Host와 확장은 여전히 네트워크 요청을 하지 않으며 이를 테스트로 고정했습니다.
- App, Agent, Native Host와 Installer의 단일 제품 버전을 1.1.4로 갱신

## [1.1.3] - 2026-08-10

### Added

- Tray Folder 호스트 연동을 추가. Tray Folder가 실행 중이면 트레이 아이콘을 Tray Folder 안으로 옮기고(hosted 모드), 파이프로 메뉴를 제공하며 연결이 끊기면 자체 트레이 아이콘을 복원합니다. 다운로드 감시와 분류는 hosted 모드에서도 그대로 동작합니다.

## [1.1.2] - 2026-08-05

### Fixed

- 브라우저 연결 화면이 개발용 소스 경로를 안내하던 문제를 수정. 실행 중인 프로그램 위치를 기준으로 실제 확장 폴더를 찾아 그대로 보여주고, 경로 복사와 폴더 열기를 제공합니다.
- `SECURITY.md`의 로그 설명이 실제 동작과 달랐던 문제를 수정. 로그 파일별로 무엇이 기록되는지, 파일 이름과 전체 경로가 남을 수 있는지, issue에 첨부하기 전에 무엇을 확인해야 하는지를 실제 코드에 맞춰 다시 작성했습니다.

### Changed

- 지원 브라우저 목록과 문구를 `BrowserSupportCatalog` 한 곳에서 관리하도록 정리. 정보 화면의 지원 범위 문장이 이 목록에서 생성되며, 등록 스크립트와 어긋나면 테스트가 실패합니다.
- 고정 Extension ID를 `ProtocolConstants.ExtensionId` 한 곳에 선언하고 Native Host와 앱이 같은 값을 사용하도록 정리. ID 값 자체는 변경하지 않았습니다.
- App, Agent, Native Host와 Installer의 단일 제품 버전을 1.1.2로 갱신
- 개발 전용 transitive 의존성 `brace-expansion`의 advisory를 해소 (lock 파일만 변경, 배포물에는 포함되지 않는 패키지)

## [1.1.1] - 2026-08-02

### Fixed

- 브라우저를 시작할 때 과거 다운로드 기록이 새 작업으로 다시 등록돼 이미 처리한 파일의 저장 위치 선택 창이 다시 나타나던 문제를 수정
- Chromium은 브라우저 시작 시 다운로드 기록 전체에 대해 `downloads.onCreated`를 다시 발생시킵니다. 확장은 이제 진행 중이면서 이번 세션에서 시작한 다운로드만 Agent에 전달합니다.
- 브라우저가 프로그램 업데이트 뒤에도 이전 확장 코드를 계속 사용할 수 있다는 점을 확인했습니다. 이 경우 확장 필터가 동작하지 않으므로 Agent의 작업 생성 정책을 fail-closed로 바꿨습니다.
- 확장 빌드 식별자나 전송 상태가 없는 `download.started` 요청으로는 새 작업을 만들지 않습니다. 기존 작업 갱신과 브라우저 다운로드 자체는 그대로 동작합니다.
- 인식할 수 없는 확장 빌드를 감지하면 대시보드와 진단 화면에서 확장 새로고침을 안내합니다.
- 이력을 삭제한 뒤에도 브라우저의 과거 기록을 다시 가져와 복원하지 않습니다.

### Added

- 확장이 시작할 때 빌드를 알리는 `extension.hello` 핸드셰이크를 추가해, 다운로드를 시도하기 전에 오래된 확장을 감지합니다.

### Changed

- 확장 `manifest.json` 버전을 `0.2.0`으로 올려 브라우저가 갱신된 확장을 다시 읽도록 했습니다. 고정 Extension ID와 key는 변경하지 않았습니다.
- 확장 소스를 바꿀 때 `manifest.json` 버전을 반드시 올리는 정책을 `DEVELOPMENT.md`에 문서화했습니다.
- App, Agent, Native Host와 Installer의 단일 제품 버전을 1.1.1로 갱신

## [1.1.0] - 2026-08-01

### Fixed

- 이미 처리하거나 나중에 처리하기로 한 저장 위치 선택 창이 컴퓨터, 브라우저 또는 앱을 다시 시작한 뒤 다시 나타나던 문제를 수정
- 자동 팝업 여부를 브라우저 활동 시각이 아니라 SQLite에 저장한 사용자 결정으로 판단하도록 변경해, 브라우저 재연결이 사용자의 결정을 되돌리지 못하게 함

### Added

- 다운로드 이력에 `처리 대기` 필터를 추가하고 기존 저장 위치 선택 대기 탭의 기능을 모두 이전
- 처리 대기 목록의 규칙별 그룹, 같은 규칙 일괄 폴더 적용, 선택 항목 저장 위치 지정과 일괄 이동하지 않기
- 대시보드의 저장 위치 선택 대기 카드에서 처리 대기 목록으로 바로 이동
- 사이트 규칙 편집기를 기본 정보, 적용 대상, 확인할 주소, 저장 위치, 규칙 요약의 5단계로 재구성하고 자연어 요약을 표시

### Changed

- 저장 위치 선택 창의 `나중에 선택`을 `대기 목록에 남기기`로 변경하고, 창을 닫아도 같은 의미로 저장
- 사이트 규칙 화면에서 내부 enum 이름 대신 사용자 문구와 예시를 표시
- Light 테마를 흰색 기반으로 정리하고 파랑을 주요 동작과 선택 상태에만 사용. Dark 테마와 기존 사용자 테마 설정은 그대로 유지
- 별도 `저장 위치 선택 대기` 탐색 항목을 제거하고 대기 개수 배지를 다운로드 이력 항목으로 이동
- App, Agent, Native Host와 Installer의 단일 제품 버전을 1.1.0으로 갱신
- 공개 저장소 이름을 `eslee-download-router`로 통일

## [1.0.3] - 2026-08-01

### Fixed

- 표준 툴팁을 억제하던 NotifyIcon 버전 협상을 제거해 hover 시 `eslee Download Router` 제품명 툴팁이 표시되도록 수정

### Changed

- 새 흰색 기반 제품 아이콘을 App, Window, taskbar, tray, Installer, shortcut, uninstall entry와 Extension에 공통 적용
- 트레이 아이콘에서 메인 창을 열 때는 더블 클릭 또는 오른쪽 클릭 메뉴를 사용합니다.
- 다운로드 라우팅 기능 로직은 변경하지 않았습니다.

## [1.0.2] - 2026-07-31

### Added

- 릴리스 산출물에서 개발 환경 정보를 검사하는 `scripts/verify-release-privacy.ps1`
- 태그 push 시 빌드, 테스트, Installer 생성, 개인정보 검사와 Release 발행을 수행하는 `Build Release` 워크플로
- 개인정보를 남기지 않는 검증 기준 문서 `TESTING.md`와 `docs/BROWSER_COMPATIBILITY.md`

### Changed

- 소스와 릴리스 산출물에서 로컬 개발 환경 정보를 제거

## [1.0.1] - 2026-07-29

### Fixed

- Windows 작업표시줄과 시스템 트레이에서 아이콘이 eslee Auto Power보다 약 15% 작게 보이던 문제
- ICO 각 프레임을 master 디자인과 종횡비는 그대로 유지한 채 중앙 기준 1.18배로 렌더링하고, 트레이용 16/20px은 안전 여백과 실제 표시 크기가 맞는 1.15배로 별도 생성
- 16px 프레임에서도 안티앨리어싱이 캔버스 가장자리에 닿지 않도록 최소 1px 안전 여백 유지

### Changed

- App, Agent, Native Host와 Installer의 단일 제품 버전을 1.0.1로 갱신
- Extension 16/32/48/128px 아이콘도 같은 canvas occupancy 보정을 적용하되 Extension key와 고정 ID는 유지

## [1.0.0] - 2026-07-28

0.3.x Release Candidate에서 검증한 기능, 상태 복구, 브랜딩과 설치 흐름을 정식 공개 버전으로 승격했습니다. App, Agent, Native Host와 Installer는 `Directory.Build.props`의 단일 `1.0.0` 버전을 사용하며 README, 지원 범위, 설치 안내와 배포 문서를 공개 제품 수준으로 정리했습니다.

### Added

- 1024px master PNG에서 재현 가능하게 생성하는 9-size Windows ICO와 Chromium 16/32/48/128px 아이콘
- App/창/작업표시줄/트레이, Inno Setup/바로가기/제거 항목, Whale Extension에 통일된 eslee Download Router 전용 branding
- 선택창 전면 표시 전후의 MainWindow/FolderSelectionWindow HWND, visible, iconic, owner, foreground와 native 호출 결과를 남기는 경로·URL 비포함 진단
- 숨김/최소화 MainWindow, iconic 선택창 복원, foreground fallback, FIFO 취소 제거를 검증하는 Core 회귀 테스트
- 30분 자동 팝업 정책, 이전 세션 대기 작업 안내와 선택 창의 영구 `모두 선택 안 함`
- 현재 FIFO의 1~1000개 Job을 한 SQLite transaction에서 terminal `Skipped`로 저장하는 `selection.skip-many`와 규칙별 stale Pending 정리 UI
- Extension `download.cancelled`/`download.interrupted`, 시작 시 complete/interrupted/in_progress/stale 재조정과 정제된 연결 진단
- 전역 `ThemeManager`, System/Light/Dark 사용자 설정 저장과 열린/새 Window 즉시 적용
- 정보 화면 제품명, 1.0.0 버전, commit, 설치형/개발 빌드 표시, 데이터 폴더와 GitHub 열기
- `Directory.Build.props` 단일 버전 원본과 App/Agent/Native Host/Installer 일치 검증

- .NET 10/WinUI 3 모노레포와 재현 가능한 bootstrap/build/test/publish 스크립트
- Manifest V3 TypeScript 확장, Native Messaging 브리지, Named Pipe Agent
- SQLite 스키마와 마이그레이션, 규칙/작업/이벤트 저장
- 도메인·정확한 호스트·URL 포함 규칙과 출처 대상 선택
- 경로 토큰, 루트 경계와 reparse point 방어
- 동일/교차 볼륨 안전 이동, 안정화 확인, SHA-256 검증, 중복 이름 보존
- WinUI 대시보드, 규칙, 파일별 선택 대기, 상태 필터·삭제 이력, 브라우저, 진단 화면
- 80개 .NET 테스트, 13개 TypeScript 테스트, Agent/Native Host 스모크 테스트
- per-user Native Host 등록 및 Inno Setup 설치 골격
- Native Host 등록의 Windows PowerShell 5.1 회귀 테스트와 개인정보 로그 회귀 테스트
- 매칭 규칙의 작업 생성·실제 파일 이동을 검증하는 Agent 통합 테스트
- BrowserTransferState/RoutingState 분리와 기존 DB v2 마이그레이션
- 단일 FIFO 선택 팝업, App 자동 시작 요청, 파일별/선택 항목 폴더 적용과 이동 건너뛰기
- Extension 시작 시 진행 중 브라우저 다운로드 상태 재조정
- 계층형 lazy `FolderTreePicker`와 고정 크기 독립 `FolderSelectionWindow`
- 이력의 Job별 경로 선택·변경, 완료 파일 확인 후 재이동, 현재 필터 전체 선택
- NavigationView Pending `InfoBadge`와 공통 대시보드 집계
- 규칙 편집·활성 토글·soft delete·저장 루트 Windows FolderPicker
- 단일 인스턴스 트레이 호스트, `--background`/`--shutdown`, HKCU 로그인 자동 시작
- per-user Inno Setup 설치/업그레이드/제거 및 설치 파일 빌드 스크립트

### Changed

- App, Agent, Native Host와 Installer 단일 제품 버전을 정식 배포판 1.0.0으로 갱신
- 트레이가 Windows 기본 아이콘 대신 App EXE의 embedded small icon을 추출해 사용하고 종료 시 HICON을 해제
- 취소 상태는 선택 여부와 관계없이 `Cancelled`가 되고 Waiting/SelectionReady 라우팅은 `NotRequired`로 종료
- UI 상태 갱신 주기를 500ms로 줄여 취소 후 팝업·Pending·이력을 1초 이내 반영
- 브라우저 기록에서 찾지 못한 진행 작업은 취소로 추측하지 않고 stale 진단 상태로 보존하며 자동 팝업에서 제외
- service worker 시작 재조정은 Job 상태만 맞추고 생성/마지막 실시간 브라우저 이벤트 기반의 30분 팝업 연령은 유지
- 개별 이동 안 함은 idempotent `selection.skip`, 현재 FIFO 일괄 이동 안 함은 `selection.skip-many`로 DB commit 성공을 확인한 뒤에만 UI 큐를 비움
- 중복 `download.started`와 실제 파일명이 바뀌지 않은 `download.metadata`는 `LastBrowserEventAt`을 갱신하지 않아 재연결 replay가 stale Pending을 최근 작업으로 되살리지 않음

- WinUI 앱을 Per-Monitor V2로 선언하고 공통 콘텐츠를 세로 ScrollViewer, stretch viewport, 1100 DIP 반응형 폼으로 재구성
- 다운로드 이력이 규칙에 매칭된 작업만 표시함을 명시하고 데이터 변경 시 3초 간격으로 자동 갱신
- Whale 설치 탐지에 Program Files와 Program Files (x86) 후보를 추가
- 대시보드가 다운로드 중, 선택 대기, 완료, 취소/중단, 재시도/실패를 별도로 집계
- 취소 이력을 자동 삭제하지 않고 취소선과 낮은 opacity로 유지하며 DB 이력만 사용자 삭제
- 취소 시 당시 RoutingState를 보존하되 표시, Pending과 이동 가능 여부는 BrowserTransferState를 우선
- 사이트 규칙 화면을 규칙 편집 Card와 저장된 규칙 Card 섹션으로 구분

### Fixed

- MainWindow가 최소화된 상태에서 선택창이 foreground를 얻지 못하고 작업표시줄 강조로만 끝나던 문제
- foreground 제한 시 바로 Flash fallback으로 종료하던 순서를 선택창 HWND의 normal 표시, 활성화, topmost, 입력 스레드 연결 재시도로 보강
- 앱 시작 때 오래된 `WaitingForSelection` 전체를 FIFO에 재삽입해 20개 이상 팝업이 연속 표시되던 문제
- service worker 시작 검색 결과가 오래된 Job의 마지막 브라우저 이벤트 시각을 현재로 덮어 다시 자동 팝업 대상으로 만들던 문제
- 기존 `모두 나중에 선택`이 메모리 큐만 비우고 DB의 `WaitingForSelection`을 남겨 Whale·Agent 재시작 뒤 처리한 파일들이 다시 FIFO에 들어오던 문제
- 동일 다운로드의 replayed start/unchanged metadata가 실제 사용자 이벤트처럼 저장되어 30분 stale 방어를 반복 무효화하던 문제
- Whale가 `state` delta 없이 `error.current=USER_CANCELED`만 보낼 때 Extension이 조기 반환해 취소가 누락되던 문제
- `downloads.onErased`와 실제 사용자 취소를 혼동할 수 있는 시작 재조정 공백
- 일반 설정의 테마 ComboBox가 저장·적용 로직에 연결되지 않아 화면이 바뀌지 않던 문제
- 다크 테마에서 코드로 만든 카드와 기본 창 제목 표시줄이 라이트 리소스를 사용하던 대비 문제
- 정보 화면에 실행 버전이 없고 설치 프로그램 버전만 별도 하드코딩되어 구성 요소가 불일치할 수 있던 문제

- DPI awareness 누락으로 QHD 125%에서 앱 전체가 96 DPI 비트맵으로 확대되던 흐릿한 렌더링
- NavigationView 콘텐츠 폭/중복 패딩과 고정 MinWidth 때문에 좁은 창에서 오른쪽이 잘리던 레이아웃
- PowerShell 5.1에 없는 경로 API와 RegistryKey 직접 쓰기로 실패하던 HKCU Native Host 등록
- PowerShell 5.1이 Native Host manifest에 UTF-8 BOM을 붙이던 문제
- Extension이 Native Messaging 실패를 아무 진단 없이 무시하던 문제
- Native Host가 자동 시작한 Agent에 브라우저 표준 스트림 핸들을 상속하던 문제
- `WaitingForSelection` 하나로 전송 중/완료/취소를 동시에 표현해 취소 항목이 Pending에 남던 문제
- ScrollViewer가 좁은 창에서도 1100 DIP 폼을 측정해 우측 콘텐츠가 창 밖으로 나가던 문제
- 모든 페이지 제목이 TitleBar 바로 아래에 붙어 보이던 공통 상단 여백
- 다운로드 완료로 파일명이 바뀐 동안 열린 선택 팝업이 이전 이름과 상태를 유지하던 문제
- `download`, `*.crdownload` 등 임시 이름을 확정 이름처럼 표시하던 문제와 같은 다운로드의 중복 Job 가능성
- 긴 트리 노드가 선택 창 DesiredSize와 폭을 계속 키우던 문제
- 최소화·트레이 상태에서 ContentDialog가 사용자 화면에 직접 나타나지 않던 문제
- RoutingState에 따라 취소선이 누락되던 문제
- 선택 대기 수가 내비게이션에서 보이지 않던 문제
- 이력의 3상태 전체 선택이 한 번의 클릭으로 해제되지 않던 문제
- `dotnet publish` 결과에서 unpackaged WinUI XBF/PRI가 빠져 설치 App이 시작하지 못하던 문제

### Security

- 명령 allowlist, 1 MiB 메시지 제한, current-user IPC, URL 민감정보 정제
- native SQLite bundle을 advisory 경고가 없는 고정 버전으로 명시
- Extension 오류 로그를 bounded error code로 제한하고 파일 로그에서 URL, 로컬 경로, 토큰, 예외 메시지를 제거

### Known limitations

- Whale Automatic/SelectSubfolder는 실제 이동 검증 완료, 다른 Chromium 브라우저는 검증 전
- Windows 100%/150% 및 FHD/4K 실제 디스플레이 시각 검증 전
- 새 폴더 생성과 세션 간 “나중에 선택” 알림 미구현
- 실제 로그아웃/로그인 자동 시작과 미완료 작업 자동 복구 미검증
- 설치 파일 코드 서명 미구현

## [0.3.3] - 2026-07-28

- eslee Download Router 전용 master PNG, 9-size Windows ICO와 Extension 아이콘을 추가했습니다.
- App EXE, WinUI Window, taskbar, 트레이, Installer, shortcut와 제거 항목에 같은 브랜딩을 적용했습니다.
- 트레이가 App EXE의 small icon을 사용하고 HICON을 종료 시 해제하도록 수정했습니다.
- npm 개발 의존성 감사를 정리해 high 이상 취약점 0건을 확인했습니다.

## [0.3.2] - 2026-07-28

- `이번 파일은 이동하지 않기`와 `모두 선택 안 함`을 SQLite terminal `Skipped`로 영구 저장했습니다.
- Agent, Native Messaging과 Whale 재연결에서 terminal Job이 Pending으로 돌아가지 않도록 단조 상태 전이를 보강했습니다.
- 실제 이벤트가 아닌 시작 재조정과 중복 metadata가 30분 자동 팝업 연령을 갱신하지 않도록 수정했습니다.

## [0.3.1] - 2026-07-23

- MainWindow 최소화 상태에서도 독립 FolderSelectionWindow만 전면 활성화하도록 HWND 처리 순서를 수정했습니다.
- Whale `USER_CANCELED`, 기타 interrupted와 브라우저 기록 삭제를 구분했습니다.
- System, Light, Dark 공통 ThemeManager와 assembly 기반 정보 화면 버전을 추가했습니다.

## [0.3.0] - 2026-07-23

- 사용자 단위 App, Agent, Native Host와 Inno Setup 설치 흐름을 통합했습니다.
- 30분 자동 팝업 정책, FIFO 위치 표시와 이전 세션 Pending 관리를 추가했습니다.
- Whale 다운로드 취소와 상태 재조정 진단을 구현했습니다.
