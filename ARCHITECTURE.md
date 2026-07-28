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

Manifest V3 service worker이며 권한은 `downloads`, `nativeMessaging`뿐입니다. `onCreated`에서 referrer, 최초/최종 파일 URL을 별도 필드로 전달하고, `onChanged`의 `filename` delta를 같은 Job의 메타데이터 갱신으로 전송합니다. `USER_CANCELED` error delta는 state delta를 기다리지 않고 즉시 idempotent `download.cancelled`로 전송합니다. interrupted는 `downloads.search({ id })` 후 delta error → item error → 마지막 error 순서로 원인을 결정합니다. `onErased`는 기록 삭제 진단일 뿐 취소가 아닙니다. 시작 재조정은 complete/cancelled/interrupted/in_progress/stale을 구분하고 item 누락을 취소로 추측하지 않습니다. 연결 로그에는 정제된 ID/state/error/전송 결과만 기록합니다.

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

Agent와 동일한 Core 라이브러리를 참조하지만 DB나 파일 이동 구현을 직접 호출하지 않고 Named Pipe 명령으로 통신합니다. App은 사용자 범위 mutex로 단일 인스턴스를 유지하는 트레이 호스트이며 `--background`에서는 메인 창을 표시하지 않습니다. 500ms 간격으로 Pending을 읽되 자동 팝업 정책을 통과한 항목만 중복 없는 FIFO `FolderSelectionWindow`로 표시합니다. 선택창은 owner 없는 독립 top-level HWND입니다. 표시 때 선택창 HWND만 `SW_SHOWNORMAL`, XAML activate, topmost/bring-to-top, foreground 순으로 처리하고, Windows foreground 제한이 직접 호출을 거부하면 현재 foreground thread와 입력 큐를 잠시 연결해 선택창을 다시 활성화합니다. MainWindow에는 restore를 호출하지 않으며 FlashWindowEx는 모든 활성화 시도가 실패했을 때만 사용합니다.

선택창 전면 진단은 MainWindow와 선택창의 HWND, visible, iconic, owner, foreground 및 Show/Bring/SetForeground 결과만 기록합니다. 다운로드 URL, query, token, 전체 로컬 경로는 기록하지 않습니다.

공통 `FolderTreePicker`는 루트 하나만 먼저 만들고 노드 확장 시 해당 단계의 자식만 비동기로 읽습니다. 로드된 노드를 중복 조회하지 않고, 접근 불가 항목은 노드 단위 오류로 제한하며 reparse point는 선택 경계에서 제외합니다. 팝업, 선택 대기, 이력 경로 변경이 같은 컴포넌트와 Agent 명령을 사용합니다. 규칙의 저장 루트는 경계가 아직 정해지지 않은 선택이므로 HWND로 초기화한 Windows `FolderPicker`를 사용합니다.

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

두 축의 상태 전이는 `DownloadJobStateMachine`이 각각 검사합니다. 완료 전에 선택하면 `InProgress / SelectionReady`만 저장하고 파일을 이동하지 않습니다. `Complete / SelectionReady`가 된 뒤에만 Moving으로 전이합니다. 사용자 취소는 BrowserTransferState를 `Cancelled`로 바꾸고 WaitingForSelection/SelectionReady는 `NotRequired`로 종료합니다. Skipped/Failed 같은 이미 확정된 진단 라우팅은 보존할 수 있으며, 표시·Pending·이동 가능 여부는 항상 BrowserTransferState를 우선합니다. 다른 중단은 일반적으로 `Interrupted / Failed`입니다. 기존 단일 `Status` 열은 마이그레이션과 호환 표시용 파생 값으로 유지합니다.

## 저장 위치 선택 경계

저장 루트를 정규화한 뒤 상대 경로를 결합하고 다시 루트 내부인지 확인합니다. `..`, 루트 경로 자체 변경, reparse point 통과를 거부합니다. App은 reparse point 디렉터리를 열거하지 않습니다. 보안 결정은 UI가 아니라 Agent에서도 다시 검증합니다.

## 팝업 수명과 테마

자동 선택 팝업은 `SelectionPromptPolicy.AutoPromptWindow`의 30분 안에 생성되었거나 실제 브라우저 이벤트가 갱신된 SelectSubfolder 대기 Job만 대상으로 합니다. 오래되거나 브라우저 record가 stale인 Job은 DB/대기 탭/InfoBadge에 남고 자동 큐에는 들어가지 않습니다. 재연결의 중복 start와 값이 바뀌지 않은 metadata는 실제 이벤트 시각을 갱신하지 않습니다.

`이번 파일은 이동하지 않기`는 `selection.skip`, `모두 선택 안 함`은 현재 자동 FIFO의 ID 목록을 `selection.skip-many`로 Agent에 보냅니다. Repository는 허용된 `WaitingForSelection`/`SelectionReady`만 한 transaction에서 `RoutingState=Skipped`, `Status=Skipped`, `CompletedAt`으로 바꾸고 같은 transaction에 `selection.skipped` 이벤트를 기록합니다. UI는 commit 성공 뒤에만 큐를 비웁니다. `Skipped`는 popup/startup/reconnect 복구의 terminal 상태이고 `BrowserTransferState.Cancelled`와는 별개입니다. 개별 `나중에 선택`만 메모리의 세션 숨김 집합을 사용합니다.

`ThemeManager`는 App 시작 때 `config.local.json`의 System/Light/Dark를 정규화하고 모든 Window 루트 FrameworkElement를 등록합니다. 설정 변경은 등록된 열린 Window 전체에 적용되고 새 FolderSelectionWindow는 Content 지정 직후 등록됩니다. System은 `ElementTheme.Default`입니다. UI 설정 저장은 SQLite 이력 스키마와 분리합니다.

## 배포

- 앱, Agent, Native Host: Windows x64 self-contained publish
- 확장: `npm ci` 후 TypeScript compile, manifest 복사, ZIP 생성
- Native Host: HKCU 브라우저별 registry adapter와 `%LOCALAPPDATA%` manifest
- Installer: per-user, `PrivilegesRequired=lowest`
- 버전: 정식 1.0.0부터 `Directory.Build.props` VersionPrefix를 App/Agent/Native Host assembly와 Installer AppVersion의 단일 원본으로 사용
- Extension identity: manifest key와 고정 ID는 제품 버전과 독립적으로 유지하며 Installer가 같은 Extension 파일과 Native Messaging origin을 배포
- 자동 시작: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`에 설치 App의 정확한 따옴표 경로와 `--background`
- X 버튼: 기본은 AppWindow 숨김, 트레이 `종료` 또는 `--shutdown`만 App/Agent 정상 종료
- 제거: 설치 파일·자동 시작·Native Host만 제거하고 사용자 DB와 규칙은 보존
- Release: 최종 main commit에서 Installer를 다시 빌드하고 같은 commit을 가리키는 Git tag와 GitHub Release에 단일 설치 asset으로 게시

설치 페이로드와 PC별 manifest는 커밋하지 않습니다.

## 후속 설계 항목

- 세션을 넘는 “나중에 선택” 알림 정책과 새 폴더 만들기
- 중단된 작업의 시작 시 복구 워커
- Whale 이외 브라우저 adapter의 실제 레지스트리/실행 경로 검증
- 설치 코드 서명과 업데이트 채널
