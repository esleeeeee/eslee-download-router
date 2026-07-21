# eslee Download Router

Chromium 기반 브라우저에서 완료된 다운로드를 사이트 규칙에 따라 Windows 폴더로 분류하는 로컬 전용 애플리케이션입니다. 규칙이 없거나 로컬 구성 요소가 응답하지 않으면 브라우저의 원래 다운로드를 그대로 유지하는 fail-open 방식을 사용합니다.

> 현재 상태: Phase 0과 Phase 1 기술 스파이크가 구현되었습니다. 자동 빌드·테스트와 로컬 IPC는 검증했지만 실제 브라우저의 확장 로드, Native Messaging, 다운로드 라우팅은 아직 수동 검증 전입니다. 자세한 내용은 [PROJECT_STATE.md](PROJECT_STATE.md)를 확인하세요.

## 구성 요소

```text
Chromium MV3 Extension
  -> Native Messaging (길이 접두사 JSON)
  -> DownloadRouter.NativeHost
  -> 현재 사용자 전용 Named Pipe
  -> 단일 인스턴스 DownloadRouter.Agent
  -> SQLite / 규칙 엔진 / 안전한 파일 이동
  -> WinUI 3 설정 앱
```

- 정식 지원 목표: Naver Whale, Microsoft Edge, Google Chrome
- 호환 지원 목표: Brave, Vivaldi, Opera
- 제외: Firefox, Safari, 모바일, Windows 이외 운영체제
- 데이터 위치: `%LOCALAPPDATA%\eslee\DownloadRouter`
- 서버, 텔레메트리, 광고 SDK, 전체 방문 기록 수집 없음

## 개발 요구사항

- Windows 11 x64
- [.NET SDK 10.0.302](https://dotnet.microsoft.com/download/dotnet/10.0) — `global.json`에 고정
- Node.js 24 이상과 npm 11 이상
- PowerShell 5.1 이상
- 선택: Inno Setup 6(설치 파일 생성 시)

스크립트는 도구를 자동 설치하지 않습니다. 누락된 항목을 확인하고 설치 안내만 표시합니다.

## 빠른 시작

```powershell
git clone https://github.com/esleeeeee/eslee-Download-Router.git
cd eslee-Download-Router
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-dev.ps1 -SkipBuild
```

개발 환경 실행은 Agent를 백그라운드로 시작하고 설정 앱을 엽니다. 앱과 Agent를 종료한 뒤 Native Host 게시 및 등록을 진행하세요.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -PublishNativeHost
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-native-host.ps1 -Action Register -Browser Edge,Whale,Chrome
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-extension.ps1
```

브라우저 개발자 모드에서 압축 해제된 확장 경로 `src\DownloadRouter.Extension\dist`를 로드합니다. 고정 공개 키로 계산되는 개발 확장 ID는 `gilicenlclaemgiijcjjejilikbooggj`입니다. 개인 키는 저장소에 포함하지 않습니다. 단계별 절차는 [docs/MANUAL_EXTENSION_INSTALL.md](docs/MANUAL_EXTENSION_INSTALL.md)에 있습니다.

## 주요 명령

| 목적 | 명령 |
|---|---|
| 환경 확인 및 복원 | `scripts\bootstrap.ps1` |
| 전체 빌드 | `scripts\build.ps1` |
| 전체 자동 테스트 | `scripts\test.ps1` |
| Agent IPC 스모크 테스트 | `scripts\smoke-agent.ps1` |
| 확장 패키지 생성 | `scripts\package-extension.ps1` |
| self-contained 설치 페이로드 | `scripts\publish.ps1` |
| Native Host 상태 확인 | `scripts\register-native-host.ps1 -Action Status` |
| 진단 | `scripts\diagnose.ps1` |

PowerShell 실행 정책이 로컬 스크립트를 막는 경우 예시처럼 `powershell -ExecutionPolicy Bypass -File ...` 형식으로 실행합니다.

## 저장소 구조

- `src/DownloadRouter.Core`: 도메인 모델, 규칙, 경로, IPC, 상태 머신
- `src/DownloadRouter.Infrastructure`: SQLite, 파일 이동, JSONL 로깅
- `src/DownloadRouter.Agent`: 단일 인스턴스 중앙 처리기와 Named Pipe 서버
- `src/DownloadRouter.NativeHost`: 최소 권한 Native Messaging 브리지
- `src/DownloadRouter.App`: WinUI 3 설정 및 대기 작업 UI
- `src/DownloadRouter.Extension`: TypeScript Manifest V3 확장
- `tests`: .NET 단위·기능·통합 테스트
- `installer`: 사용자 단위 Inno Setup 정의
- `scripts`: 복원, 빌드, 테스트, 게시, 등록, 진단 자동화

## 안전 원칙

- 규칙이 없는 다운로드는 추적하거나 이동하지 않습니다.
- 브라우저가 완료를 보고하고 파일이 안정화되기 전에는 이동하지 않습니다.
- 다른 볼륨은 임시 파일 복사, 크기와 SHA-256 검증, 최종 이름 변경 후에만 원본을 삭제합니다.
- 기존 파일을 덮어쓰지 않고 `파일 (1).ext` 형식으로 보존합니다.
- 직접 선택 경로는 지정 루트 내부로 제한하고 `..`, 심볼릭 링크, junction, reparse point 탈출을 거부합니다.
- URL 자격 증명, query, fragment는 저장 전에 제거합니다.

설계와 위협 모델은 [ARCHITECTURE.md](ARCHITECTURE.md), [SECURITY.md](SECURITY.md)를 참고하세요.

## 현재 제한

- Chromium downloads API가 다운로드 시작 탭 URL을 직접 제공하지 않으므로, 신뢰할 수 없는 활성 탭 추측은 하지 않습니다. 제공되는 referrer, 최초 URL, 최종 URL을 분리해 사용합니다.
- 폴더 선택 대기 작업은 앱의 전용 화면에서 규칙별로 묶어 처리하지만, 자동 팝업·새 폴더 만들기·새로 고침은 후속 구현입니다.
- 시작 시 실행 토글은 UI 골격만 있으며 실제 Windows 등록은 아직 연결하지 않았습니다.
- 모든 브라우저 수동 검증 결과는 현재 `미검증`입니다.

## 문서

- [DEVELOPMENT.md](DEVELOPMENT.md): 개발 규칙과 반복 작업
- [TESTING.md](TESTING.md): 자동·수동 검증
- [PROJECT_STATE.md](PROJECT_STATE.md): 현재 기준 상태
- [HANDOFF.md](HANDOFF.md): 다른 PC 인수인계
- [docs/BROWSER_COMPATIBILITY.md](docs/BROWSER_COMPATIBILITY.md): 브라우저별 검증표
- [docs/TECHNICAL_SPIKE.md](docs/TECHNICAL_SPIKE.md): Phase 1 결과

## 라이선스

라이선스는 아직 결정되지 않았습니다. 공개 저장소라는 사실만으로 재사용 권한이 부여되지는 않습니다.
