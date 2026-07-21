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

### Security

- 명령 allowlist, 1 MiB 메시지 제한, current-user IPC, URL 민감정보 정제
- native SQLite bundle을 advisory 경고가 없는 고정 버전으로 명시

### Known limitations

- 실제 브라우저 검증 전
- 선택 대기 자동 알림/팝업, 새 폴더 생성/새로 고침 미구현
- 시작 시 실행 토글과 미완료 작업 자동 복구 미연결
