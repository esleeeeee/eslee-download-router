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
- Agent JSONL 로그에서 http/https URL, 드라이브 또는 UNC로 시작하는 절대 경로, 토큰 표식 치환 (아래 「로그에 기록되는 정보」 참고)
- 텔레메트리, 광고 SDK, 전체 방문 기록 권한 없음
- 네트워크 요청은 업데이트 확인 하나뿐: 앱이 GitHub의 공개 최신 Release 정보(`api.github.com`)를 HTTPS로 읽기만 하며, 사용자·다운로드·규칙에 관한 어떤 값도 보내지 않음. 자동 확인은 하루 1회로 제한되고 실패해도 다운로드 감시와 분류에는 영향이 없음. Agent와 Native Host, 확장은 네트워크 요청을 하지 않음

## 데이터 보존

규칙, 정제된 작업 메타데이터, 상태 이력, 설정과 로그는 현재 사용자 LocalAppData에만 저장합니다. 실제 파일 내용은 읽어서 업로드하지 않습니다. SHA-256은 교차 볼륨 복사의 로컬 무결성 비교에만 사용합니다.

데이터베이스에는 로그와 달리 치환이 적용되지 않습니다. 다운로드의 원본 경로와 최종 경로가 전체 경로 그대로 저장되고, 출처 URL은 사용자 정보·query·fragment를 제거한 형태로 저장됩니다.

## 로그에 기록되는 정보

로그는 `%LOCALAPPDATA%\eslee\DownloadRouter\logs\`에만 기록되며 외부로 전송되지 않습니다. 파일마다 기록 범위가 다릅니다.

| 파일 | 생성 주체 | 기록되는 내용 |
|---|---|---|
| `download-router.jsonl` (회전본 `download-router.1.jsonl`) | Agent | 시각, 로그 수준, 카테고리(클래스 이름), 이벤트 ID, 치환된 메시지, 예외 **형식 이름만** |
| `app-unhandled.log` | 앱 | 처리되지 않은 예외의 `ToString()` 전체 — **치환 없음** |
| `app-startup.log` | 앱 | 고정된 영문 시작 진단 문장 |
| `window-activation.log` | 앱 | 창 핸들, 작업 GUID, 개수, 창 상태, 예외 형식 이름 |

### 파일 이름이 기록될 수 있는지

기록될 수 있습니다. `download-router.jsonl`의 파일 이동 성공 로그는 `File move completed for {SourceFileName}` 형식으로 **다운로드한 파일 이름을 그대로 남깁니다.** 치환 규칙은 드라이브 문자나 UNC로 시작하는 경로만 대상으로 하므로 경로 없는 파일 이름은 치환되지 않습니다. `app-unhandled.log`의 예외 메시지에도 파일 이름이 포함될 수 있습니다.

### 전체 경로가 기록되는지

`download-router.jsonl`에서는 `C:\...` 형태의 절대 경로와 `\\서버\공유` 형태의 UNC 경로가 `[PATH_REDACTED]`로 치환됩니다. 다만 이는 정규식 기반 치환이므로 완전하다고 보장할 수 없습니다.

`app-unhandled.log`에는 **치환이 적용되지 않습니다.** 처리되지 않은 예외의 메시지와 스택 트레이스가 그대로 기록되며, 파일 관련 예외 메시지에는 전체 경로가 포함되는 경우가 많습니다.

### URL이 기록되는지

의도적으로 URL을 남기는 로그 호출은 없습니다. `download-router.jsonl`에서는 `http://` 또는 `https://`로 시작하는 문자열이 `[URL_REDACTED]`로 치환됩니다. `app-unhandled.log`에는 이 치환이 적용되지 않습니다.

### 오류 메시지에 경로나 파일 이름이 포함될 가능성

`download-router.jsonl`은 예외의 **형식 이름만** 기록하고 예외 메시지와 스택 트레이스는 기록하지 않으므로, 예외를 통해 경로나 파일 이름이 이 파일에 들어가지는 않습니다. 반면 `app-unhandled.log`는 예외 전체를 기록하므로 경로와 파일 이름이 포함될 수 있습니다.

앱 화면과 Agent 응답에 표시되는 오류 문구에는 예외 메시지가 포함될 수 있습니다. 이 값은 로그 파일이 아니라 화면과 데이터베이스의 작업 오류 필드에 남습니다.

### 로그를 첨부하기 전에 확인할 것

공개 issue에 로그나 진단 화면 캡처를 첨부하기 전에 다음을 직접 확인하고 필요한 부분을 지워 주세요.

- 진단 화면의 `dataDirectory` 값 — Windows 계정 이름이 포함된 전체 경로입니다

- `download-router.jsonl`의 `File move completed for ...` 줄에 남은 다운로드 파일 이름
- `app-unhandled.log` 전체 — 경로, 파일 이름, 사용자 이름이 그대로 있을 수 있습니다
- 경로에 포함된 Windows 사용자 이름과 회사·조직 정보
- `[URL_REDACTED]`, `[PATH_REDACTED]`로 치환되지 않고 남은 값

치환은 보조 수단이며 완전한 익명화가 아닙니다. 첨부 전 확인 없이 로그를 공개하지 마세요.

## 종속성

NuGet과 npm 버전은 고정합니다. Microsoft.Data.Sqlite의 초기 transitive native SQLite 패키지에서 알려진 고위험 advisory 경고가 발생하여 `SQLitePCLRaw.bundle_e_sqlite3`을 `3.0.4`로 직접 고정했고, 이후 restore는 경고 없이 완료되었습니다. 새 버전 반영 시 restore/build/test와 advisory 검토를 함께 수행합니다.

## 확장 ID와 키

manifest의 `key`는 확장 ID를 재현하기 위한 공개 키입니다. 대응하는 private key는 생성·보관·커밋하지 않습니다. 배포 서명 키, `.crx`, 인증서, 토큰은 `.gitignore` 대상이며 별도 보안 저장소가 결정되기 전에는 저장소 밖에도 만들지 않습니다.

## 보고

공개 issue에 토큰, 개인 URL, 실제 다운로드 경로, 회사 정보, Native Host manifest 원본을 첨부하지 마세요. 로그를 첨부한다면 위 「로그를 첨부하기 전에 확인할 것」을 먼저 확인하세요. 보안 연락 채널은 아직 정해지지 않았으므로 민감한 취약점 공개 전에 저장소 소유자와 비공개 채널을 합의해야 합니다.

## 알려진 위험

- 브라우저별 Native Host 검색 위치는 격리된 환경에서 검증해야 합니다.
- 기업 정책이 Native Messaging 또는 개발자 모드 확장을 차단할 수 있습니다.
- 브라우저가 제공하지 않는 시작 탭 URL을 정확히 복원할 수 없습니다.
- Agent 시작 시 미완료 작업 자동 복구와 로그 보존 정책은 후속 구현입니다.
- 설치 파일 코드 서명과 자동 업데이트 정책은 미결정입니다.
