# Phase 1 기술 스파이크

날짜: 2026-07-21

## 검증 목표

- Manifest V3 확장에서 Native Messaging 요청을 구성할 수 있는가
- 최소 Native Host가 안전하게 메시지를 검증하고 Agent로 전달할 수 있는가
- 다중 Host가 하나의 current-user Agent로 통합될 수 있는가
- 규칙, 경로, 파일 이동의 핵심 정책을 UI와 분리해 테스트할 수 있는가
- .NET 10/WinUI 3와 TypeScript 프로젝트를 고정 버전으로 재현할 수 있는가

## 구현 결과

- 고정 public key를 가진 Manifest V3 service worker를 빌드했습니다.
- 브라우저 메시지와 내부 IPC에 동일한 4-byte little-endian 길이 접두사 JSON 계약을 사용했습니다.
- Native Host는 origin, 1 MiB 크기 상한, 버전, request ID와 명령 allowlist를 검사합니다.
- Agent는 current-user mutex와 `PipeOptions.CurrentUserOnly` Named Pipe를 사용합니다.
- ping 왕복, 잘못된 명령 거부, 규칙 미매칭 fail-open을 자동 검증했습니다.
- SQLite migration과 규칙 저장, 파일 이동, 중복 이름, 루트 경계를 테스트했습니다.
- WinUI 3 설정 앱이 동일한 pipe 계약으로 규칙과 작업을 조회/갱신하도록 연결했습니다.

## 검증 증거

- solution build: 성공, 경고 0, 오류 0
- .NET tests: 27/27
- Extension tests: 4/4, lint/type/build 성공
- Agent pipe smoke: 성공
- Native Host self-test: 성공
- App/Agent/Native Host self-contained win-x64 publish: 성공

## 발견과 결정

1. Chromium downloads API의 이벤트만으로 정확한 시작 탭 URL을 얻을 수 없습니다. 활성 탭이나 CDN 소유자를 추측하면 오분류 위험이 있으므로 referrer와 파일 URL을 분리하고 근거가 없으면 `확인 불가`로 둡니다.
2. Native Messaging의 브라우저 입력 허용량과 무관하게 제품 프로토콜은 양방향 1 MiB로 더 작게 제한합니다.
3. 설정 앱과 Agent가 DB를 함께 열지 않고 Agent만 장기 상태를 소유하도록 하여 동시성과 마이그레이션 책임을 단순화했습니다.
4. 초기 transitive native SQLite 버전의 advisory 경고를 직접 패키지 고정으로 제거했습니다.
5. 폴더 선택은 UI의 값만 신뢰하지 않고 Agent가 루트 경계를 다시 검사합니다.

## 미검증

- 실제 Edge/Whale/Chrome이 manifest를 찾고 Host를 실행하는지
- 실제 다운로드 이벤트의 referrer/finalUrl 값 차이
- 브라우저가 완료를 보고한 파일의 잠금 해제 시간
- 기업 보안 정책과 브라우저 자체 저장 확인창의 영향
- App 시각 렌더링과 사용자 상호작용

## Phase 2 진입 조건

Edge에서 확장 로드, Native Messaging ping, 미매칭 fail-open, 자동 저장 실다운로드를 먼저 검증합니다. 실패 증거에 따라 registry adapter 또는 프로토콜만 조정하며 핵심 규칙/이동 계층은 유지합니다.
