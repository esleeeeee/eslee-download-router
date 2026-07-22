# 아키텍처

## 목표

브라우저 다운로드의 성공 여부를 로컬 프로그램 가용성과 분리하고, 신뢰 경계마다 최소 권한과 입력 검증을 적용합니다. 모든 장기 상태는 Agent의 SQLite에 모으고 Native Host는 상태를 갖지 않습니다.

## 데이터 흐름

```text
chrome.downloads.onCreated/onChanged
  -> Extension: 메타데이터 구성, fail-open 전송
  -> Native Messaging stdin/stdout: 4-byte little-endian 길이 + UTF-8 JSON
  -> Native Host: origin/버전/request ID/명령/1 MiB 상한 검증
  -> CurrentUserOnly Named Pipe: eslee.download-router.agent.v1
  -> Agent: 단일 인스턴스, 규칙 매칭, 상태 전이, SQLite
  -> FileMoveService: 안정화, 경계 검사, 중복 보존, 이동/복사 검증
  -> App: 규칙·이력·선택 대기·브라우저·진단 화면
```

## 구성 요소

### Extension

Manifest V3 service worker이며 권한은 `downloads`, `nativeMessaging`뿐입니다. `onCreated`에서 referrer, 최초/최종 파일 URL을 별도 필드로 전달하고 `onChanged`에서 완료, 사용자 취소 또는 다른 중단 상태를 전송합니다. 시작 시 Agent가 가진 진행 중 ID만 `downloads.search`로 재확인해 놓친 종료 이벤트를 재전송합니다. 연결 오류는 다운로드를 취소하거나 변경하지 않습니다. 실패할 때만 원문 대신 `host-not-found`, `host-exited`, `agent.unavailable` 같은 제한된 분류 코드를 service worker 콘솔에 기록합니다.

Chromium downloads API에는 신뢰할 수 있는 시작 탭 URL 필드가 없습니다. 따라서 활성 탭을 다운로드 출처로 추측하지 않고 `initiatingPageUrl`은 근거가 있을 때만 사용합니다. 현재 확장은 referrer를 우선 근거로 전달합니다.

### Native Host

브라우저가 실행하는 짧은 수명의 브리지입니다. 고정 개발 확장 origin, 프로토콜 버전, 비어 있지 않은 request ID, 명령 allowlist, 최대 1 MiB 메시지를 검사합니다. 임의 셸 문자열을 실행하지 않으며 장기 상태나 UI가 없습니다. Agent가 없으면 같은 설치 디렉터리의 정확한 `DownloadRouter.Agent.exe`만 숨김 프로세스로 분리 실행해 Native Messaging stdin/stdout/stderr를 상속하지 않게 합니다.

### Agent

현재 사용자 범위 mutex로 단일 인스턴스를 보장합니다. 여러 Native Host 연결을 비동기 Named Pipe 서버로 병합합니다. 명령 처리기는 DB 오류와 경계 오류를 안전한 오류 응답으로 바꾸며, 규칙이 없을 때 작업을 만들지 않습니다. SelectSubfolder 작업 생성 시 설치 폴더 또는 개발 출력의 App만 실행 요청하며 실패해도 브라우저 다운로드와 원본 위치를 바꾸지 않습니다.

### Core

- 규칙 유형: 도메인+하위 도메인, 정확한 호스트, URL 문자열 포함
- 대상: 시작 페이지, 파일 URL, 어느 한쪽
- IDN 호스트 정규화와 도메인 경계 비교
- 경로 토큰 해석과 루트 내부 검증
- URL 민감정보 제거
- 브라우저 전송과 라우팅 결정을 분리한 명시적 상태 머신

### Infrastructure

SQLite 마이그레이션은 `schema_migrations`, `rules`, `download_jobs`, `job_events`, `app_settings`, `browser_connections`를 생성합니다. DB와 로그는 `%LOCALAPPDATA%\eslee\DownloadRouter`에 저장합니다.

파일 이동은 다음 정책을 사용합니다.

- 브라우저 완료 상태, 임시 확장자, 크기 안정성, 읽기 가능 여부 확인
- 동일 볼륨: 예약된 중복 없는 목적지로 move
- 다른 볼륨: `.downloadrouter-partial-*`로 복사, 크기와 SHA-256 비교, 최종 rename, 그 뒤 원본 삭제
- 충돌: `name (1).ext`, `name (2).ext` 형태
- 재시도 가능한 잠금/I/O 오류와 영구 검증 오류 구분

### WinUI 3 App

Agent와 동일한 Core 라이브러리를 참조하지만 DB나 파일 이동 구현을 직접 호출하지 않고 Named Pipe 명령으로 통신합니다. 현재 대시보드, 규칙, 파일별 선택 대기, 상태 필터·삭제 이력, 브라우저 연결, 일반 설정, 진단, 정보 화면이 있습니다. App은 사용자 범위 mutex로 단일 인스턴스를 유지하고 1초 간격으로 Pending을 읽어 중복 없는 FIFO ContentDialog를 하나씩 표시합니다.

앱은 unpackaged WinUI 3 프로세스를 manifest에서 Per-Monitor V2로 선언합니다. 크기와 여백은 장치 독립 픽셀(DIP)을 사용하고 루트에서 layout rounding을 적용합니다. 모든 화면은 하나의 `NavigationView -> vertical ScrollViewer -> stretch viewport -> MaxWidth 1100 form` 구조를 공유합니다. 실제 폼 폭은 `ViewportWidth - 좌우 Padding`과 1100 DIP 중 작은 값이며 가로 스크롤을 만들지 않습니다. Compact는 `16,32,16,24`, Wide는 `32,48,32,32` DIP 패딩을 사용합니다.

## 작업 상태

```text
BrowserTransferState       RoutingState
InProgress                 WaitingForSelection
Complete                   SelectionReady
Cancelled                  Moving
Interrupted                RetryPending
                           Completed
                           Skipped
                           Failed
                           NotRequired
```

두 축의 상태 전이는 `DownloadJobStateMachine`이 각각 검사합니다. 완료 전에 선택하면 `InProgress / SelectionReady`만 저장하고 파일을 이동하지 않습니다. `Complete / SelectionReady`가 된 뒤에만 Moving으로 전이합니다. 취소는 `Cancelled / NotRequired`, 다른 중단은 `Interrupted / Failed`이며 둘 다 Pending과 이동 대상에서 제외됩니다. 기존 단일 `Status` 열은 마이그레이션과 호환 표시용 파생 값으로 유지합니다.

## 저장 위치 선택 경계

저장 루트를 정규화한 뒤 상대 경로를 결합하고 다시 루트 내부인지 확인합니다. `..`, 루트 경로 자체 변경, reparse point 통과를 거부합니다. App은 reparse point 디렉터리를 열거하지 않습니다. 보안 결정은 UI가 아니라 Agent에서도 다시 검증합니다.

## 배포

- 앱, Agent, Native Host: Windows x64 self-contained publish
- 확장: `npm ci` 후 TypeScript compile, manifest 복사, ZIP 생성
- Native Host: HKCU 브라우저별 registry adapter와 `%LOCALAPPDATA%` manifest
- Installer: per-user, `PrivilegesRequired=lowest`

설치 페이로드와 PC별 manifest는 커밋하지 않습니다.

## 후속 설계 항목

- 트레이/Windows 알림과 세션을 넘는 “나중에 선택” 정책
- 새 폴더 만들기와 새로 고침을 포함한 전용 트리 선택기
- Windows 시작 시 실행 설정 저장 및 등록
- 중단된 작업의 시작 시 복구 워커
- 브라우저 adapter의 실제 레지스트리/실행 경로 검증
- 설치 서명과 업데이트 전략
