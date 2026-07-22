# 변경 이력

모든 주요 변경은 이 파일에 누적합니다. 버전은 Semantic Versioning을 따를 예정입니다.

## [Unreleased]

### Added

- .NET 10/WinUI 3 모노레포와 재현 가능한 bootstrap/build/test/publish 스크립트
- Manifest V3 TypeScript 확장, Native Messaging 브리지, Named Pipe Agent
- SQLite 스키마와 마이그레이션, 규칙/작업/이벤트 저장
- 도메인·정확한 호스트·URL 포함 규칙과 출처 대상 선택
- 경로 토큰, 루트 경계와 reparse point 방어
- 동일/교차 볼륨 안전 이동, 안정화 확인, SHA-256 검증, 중복 이름 보존
- WinUI 대시보드, 규칙, 파일별 선택 대기, 상태 필터·삭제 이력, 브라우저, 진단 화면
- 53개 .NET 테스트, 10개 TypeScript 테스트, Agent/Native Host 스모크 테스트
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

- WinUI 앱을 Per-Monitor V2로 선언하고 공통 콘텐츠를 세로 ScrollViewer, stretch viewport, 1100 DIP 반응형 폼으로 재구성
- 다운로드 이력이 규칙에 매칭된 작업만 표시함을 명시하고 데이터 변경 시 3초 간격으로 자동 갱신
- Whale 설치 탐지에 Program Files와 Program Files (x86) 후보를 추가
- 대시보드가 다운로드 중, 선택 대기, 완료, 취소/중단, 재시도/실패를 별도로 집계
- 취소 이력을 자동 삭제하지 않고 취소선과 낮은 opacity로 유지하며 DB 이력만 사용자 삭제
- 취소 시 당시 RoutingState를 보존하되 표시, Pending과 이동 가능 여부는 BrowserTransferState를 우선
- 사이트 규칙 화면을 규칙 편집 Card와 저장된 규칙 Card 섹션으로 구분

### Fixed

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
