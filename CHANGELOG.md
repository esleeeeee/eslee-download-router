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
- WinUI 대시보드, 규칙, 이력, 선택 대기 그룹, 브라우저, 진단 화면
- 27개 .NET 테스트, 4개 TypeScript 테스트, Agent/Native Host 스모크 테스트
- per-user Native Host 등록 및 Inno Setup 설치 골격
- Native Host 등록의 Windows PowerShell 5.1 회귀 테스트와 개인정보 로그 회귀 테스트
- 매칭 규칙의 작업 생성·실제 파일 이동을 검증하는 Agent 통합 테스트

### Changed

- WinUI 앱을 Per-Monitor V2로 선언하고 공통 콘텐츠를 세로 ScrollViewer, stretch viewport, 1100 DIP 반응형 폼으로 재구성
- 다운로드 이력이 규칙에 매칭된 작업만 표시함을 명시하고 데이터 변경 시 3초 간격으로 자동 갱신
- Whale 설치 탐지에 Program Files와 Program Files (x86) 후보를 추가

### Fixed

- DPI awareness 누락으로 QHD 125%에서 앱 전체가 96 DPI 비트맵으로 확대되던 흐릿한 렌더링
- NavigationView 콘텐츠 폭/중복 패딩과 고정 MinWidth 때문에 좁은 창에서 오른쪽이 잘리던 레이아웃
- PowerShell 5.1에 없는 경로 API와 RegistryKey 직접 쓰기로 실패하던 HKCU Native Host 등록
- PowerShell 5.1이 Native Host manifest에 UTF-8 BOM을 붙이던 문제
- Extension이 Native Messaging 실패를 아무 진단 없이 무시하던 문제
- Native Host가 자동 시작한 Agent에 브라우저 표준 스트림 핸들을 상속하던 문제

### Security

- 명령 allowlist, 1 MiB 메시지 제한, current-user IPC, URL 민감정보 정제
- native SQLite bundle을 advisory 경고가 없는 고정 버전으로 명시
- Extension 오류 로그를 bounded error code로 제한하고 파일 로그에서 URL, 로컬 경로, 토큰, 예외 메시지를 제거

### Known limitations

- Whale의 매칭 규칙 자동 이동/직접 선택은 실제 브라우저 검증 전
- Windows 100%/150% 및 FHD/4K 실제 디스플레이 시각 검증 전
- 선택 대기 자동 알림/팝업, 새 폴더 생성/새로 고침 미구현
- 시작 시 실행 토글과 미완료 작업 자동 복구 미연결
