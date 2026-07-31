# 보안 및 개인정보 보호

## 범위와 위협 모델

Extension 입력, Native Messaging stdin, Named Pipe 요청, 규칙 DB의 경로, 다운로드 파일 경로를 신뢰하지 않습니다. 로컬 사용자의 계정 자체가 침해된 상황이나 브라우저/운영체제 취약점은 이 프로젝트의 방어 범위를 벗어나지만, 다운로드 경로를 이용한 임의 파일 덮어쓰기와 루트 탈출은 방지합니다.

## 적용된 통제

- Native Host origin을 고정 확장 ID로 제한
- 프로토콜 버전, request ID, 명령 allowlist 검증
- Native Messaging과 Named Pipe 메시지 모두 최대 1 MiB
- `PipeOptions.CurrentUserOnly`와 사용자 단위 mutex/HKCU 등록
- Native Host에서 셸 실행, 임의 실행 파일 이름, 상대 Agent 경로 금지
- 모든 다운로드 경로를 완전한 경로로 정규화
- 직접 선택 상대 경로의 `..`, rooted path, reparse point, junction 탈출 거부
- 기존 목적지 자동 덮어쓰기 금지
- 교차 볼륨 복사 검증 성공 전 원본 삭제 금지
- URL 저장 시 사용자 정보, query, fragment 제거
- JSONL 로그에서 URL과 경로 민감값 정제
- 서버 전송, 텔레메트리, 광고 SDK, 전체 방문 기록 권한 없음

## 데이터 보존

규칙, 정제된 작업 메타데이터, 상태 이력, 설정과 로그는 현재 사용자 LocalAppData에만 저장합니다. 실제 파일 내용은 읽어서 업로드하지 않습니다. SHA-256은 교차 볼륨 복사의 로컬 무결성 비교에만 사용합니다.

## 종속성

NuGet과 npm 버전은 고정합니다. Microsoft.Data.Sqlite의 초기 transitive native SQLite 패키지에서 알려진 고위험 advisory 경고가 발생하여 `SQLitePCLRaw.bundle_e_sqlite3`을 `3.0.4`로 직접 고정했고, 이후 restore는 경고 없이 완료되었습니다. 새 버전 반영 시 restore/build/test와 advisory 검토를 함께 수행합니다.

## 확장 ID와 키

manifest의 `key`는 확장 ID를 재현하기 위한 공개 키입니다. 대응하는 private key는 생성·보관·커밋하지 않습니다. 배포 서명 키, `.crx`, 인증서, 토큰은 `.gitignore` 대상이며 별도 보안 저장소가 결정되기 전에는 저장소 밖에도 만들지 않습니다.

## 보고

공개 issue에 토큰, 개인 URL, 실제 다운로드 경로, 회사 정보, Native Host manifest 원본을 첨부하지 마세요. 보안 연락 채널은 아직 정해지지 않았으므로 민감한 취약점 공개 전에 저장소 소유자와 비공개 채널을 합의해야 합니다.

## 알려진 위험

- 브라우저별 Native Host 검색 위치는 격리된 환경에서 검증해야 합니다.
- 기업 정책이 Native Messaging 또는 개발자 모드 확장을 차단할 수 있습니다.
- 브라우저가 제공하지 않는 시작 탭 URL을 정확히 복원할 수 없습니다.
- Agent 시작 시 미완료 작업 자동 복구와 로그 보존 정책은 후속 구현입니다.
- 설치 파일 코드 서명과 자동 업데이트 정책은 미결정입니다.
