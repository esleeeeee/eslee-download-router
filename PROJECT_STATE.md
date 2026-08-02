# 프로젝트 상태

기준일: 2026-08-02 (Asia/Seoul)

## 요약

- 현재 단계: 1.1.1 긴급 수정 후보 개발 중. 브라우저 시작 시 과거 다운로드가 새 작업으로 재등록되던 문제를 확장 필터와 Agent fail-closed 정책으로 수정하고 로컬 검증 중이며 아직 Release로 게시하지 않았습니다.
- 직전 정식 버전: 1.1.0 (자동 팝업 영속 상태, 처리 대기 통합, 사이트 규칙 UI 개선)
- 공식 저장소: https://github.com/esleeeeee/eslee-download-router
- 기본 브랜치: `main`
- 최종 개발 PR: [#1 Fix pending prompts, Whale cancellation, version, and themes](https://github.com/esleeeeee/eslee-download-router/pull/1)
- 최신 정식 Release: [GitHub Releases](https://github.com/esleeeeee/eslee-download-router/releases/latest)
- 지원 운영체제: Windows 11 x64
- 격리 환경 수동 검증 브라우저: Naver Whale

## 정식 버전 구성

- App, Agent, Native Host와 Installer는 `Directory.Build.props`의 단일 `1.1.1` 제품 버전을 사용합니다.
- assembly informational version에는 빌드한 Git commit metadata가 포함됩니다.
- Extension manifest 버전은 기존 정책에 따라 제품 assembly와 독립 관리합니다.
- 고정 Extension ID `gilicenlclaemgiijcjjejilikbooggj`와 Native Messaging identity를 유지합니다.
- 설치 프로그램은 현재 사용자 단위이며 App, Agent, Native Host와 Extension 파일을 함께 설치합니다.
- v1.0.3부터 흰색 기반 master PNG와 9-size ICO를 App, Window, taskbar, 트레이, Installer, shortcut, 제거 항목과 Extension에 공통 적용합니다. eslee 로고, 폴더, cyan 다운로드와 분기 경로, gold node 구성은 유지합니다.
- 트레이 아이콘은 별도 NotifyIcon 버전 협상 없이 셸 기본 툴팁 경로를 사용해 hover 시 제품명을 표시합니다.

## 구현 완료

- Chromium Manifest V3 Extension에서 Native Messaging Host, current-user Named Pipe, 단일 Agent로 이어지는 로컬 프로토콜
- 도메인과 하위 도메인, 정확한 호스트, URL 포함 사이트 규칙
- Automatic 완료 파일 이동과 SelectSubfolder 파일별 선택
- root 경계 안에서만 탐색하는 lazy-loading FolderTreePicker
- 브라우저 완료 전 선택, 실제 최종 파일명 갱신과 단일 FIFO
- 다운로드 이력, 경로 변경, 완료 파일 재이동과 실제 파일 비삭제 이력 정리
- SQLite에 영구 저장하는 자동 팝업 상태(`NeverShown`, `Shown`, `Deferred`, `Resolved`)와 재시작 뒤 팝업 비재현
- 실시간 `downloads.onCreated`만 새 작업을 만들고, 브라우저 시작 시 재생되는 과거 기록은 확장 필터와 Agent의 fail-closed 등록 정책에서 모두 거부. 인식할 수 없는 확장 빌드는 작업을 만들지 못하며 진단 화면이 확장 새로고침을 안내
- 다운로드 이력의 `처리 대기` 필터로 통합한 저장 위치 선택 대기 목록과 규칙별 일괄 처리
- 사용자 문구 기반 사이트 규칙 편집기와 자연어 규칙 요약
- 개별 `selection.skip`과 일괄 `selection.skip-many`의 영구 terminal `Skipped`
- Whale, Agent, App과 Native Messaging 재연결 뒤 terminal Job 비회귀
- `USER_CANCELED`, 기타 interrupted와 브라우저 기록 삭제의 분리
- MainWindow 최소화 또는 트레이 숨김 상태에서 독립 FolderSelectionWindow 활성화
- X 버튼 트레이 숨김, HKCU Windows 로그인 자동 시작과 명시적 종료
- System, Light, Dark 전역 테마와 실행 assembly 버전 정보 화면
- Per-Monitor V2와 DIP 기반 반응형 공통 레이아웃 검증
- 규칙 미매칭과 구성 요소 장애에서 브라우저 다운로드를 유지하는 fail-open
- 동일 볼륨 move, 교차 볼륨 copy, 크기와 SHA-256 검증, 확정 뒤 원본 삭제
- 기존 대상 덮어쓰기 금지와 중복 이름 보존
- 사용자 DB, 규칙, 이력과 설정을 지우지 않는 설치, 업그레이드와 제거 정책

## 최종 검증 기준

| 항목 | 결과 |
|---|---|
| .NET Debug build | 성공, 경고 0, 오류 0 |
| .NET Release build | 성공, 경고 0, 오류 0 |
| .NET tests | 131/131 통과, Core 82, Infrastructure 10, Integration 39 |
| Extension | ESLint, TypeScript, dist build 성공, Node tests 27/27 |
| npm audit | 취약점 0건 |
| Release publish | App, Agent, Native Host win-x64 self-contained 성공 |
| 릴리스 개인정보 검사 | `verify-release-privacy.ps1` 통과 |
| Installer | Inno Setup compile과 기존 1.1.0 설치 위 1.1.1 업그레이드 성공 |
| 사용자 데이터 | SQLite, 규칙, 이력, Pending, 설정과 테마 보존 |
| 자동 시작 | 기존 HKCU Run 값 보존, 자동 시작 경로 실행 성공 |
| Native Messaging | 등록 보존, 설치 Native Host와 Agent ping 성공 |
| 브랜딩 | 흰색 master 기준으로 App, Window, taskbar, 트레이, Installer, 시작 메뉴, 제거 항목과 Extension 아이콘 갱신 확인 |
| 트레이 툴팁 | 실행 직후, 트레이 숨김 뒤, 재실행과 자동 시작 경로에서 제품명 툴팁 표시 확인 |
| 트레이 동작 | 더블 클릭 창 열기, 오른쪽 클릭 메뉴, 아이콘 중복 없음 확인 |
| CI | PR과 main의 최종 GitHub Actions 성공 |

## 실제 사용자 검증

- Whale 실제 사용자 프로필에서 브라우저 완전 종료와 재실행
- 이미 처리한 SelectSubfolder 팝업 재등장 0건
- 실제 Windows 재부팅
- 로그인 후 eslee Download Router 자동 시작
- 재부팅 이후 정상 동작
- 1.0.3 업그레이드 설치 뒤 흰색 아이콘, 트레이 툴팁, 트레이 더블 클릭과 오른쪽 클릭 메뉴 정상 동작

별도 격리 Whale 프로필에서는 Automatic 교차 볼륨 이동, SelectSubfolder 계층형 선택, 긴 폴더명, 네 취소 흐름, 개별과 일괄 `Skipped`, FIFO, 최소화 상태 선택창과 재연결 상태 보존을 검증했습니다.

## 배포

- 설치 파일: GitHub 최신 Release의 `eslee-download-router-setup.exe`
- 설치 범위: 현재 사용자
- 기본 경로: `%LOCALAPPDATA%\Programs\eslee\DownloadRouter`
- 사용자 데이터: `%LOCALAPPDATA%\eslee\DownloadRouter`
- Extension 로드 경로: 설치 폴더의 `extension`
- 제거 시 사용자 데이터는 보존합니다.

## 알려진 제한

- Installer는 코드 서명되지 않았습니다.
- Chromium Web Store 배포와 확장 자동 설치는 제공하지 않습니다.
- Edge, Chrome, Brave, Vivaldi와 Opera 실제 다운로드는 검증하지 않았습니다.
- FHD, 4K, Windows 100%, 150% 물리 디스플레이는 직접 검증하지 않았습니다.
- FolderTreePicker에서 새 폴더 만들기는 제공하지 않습니다.
- 자동 저장 위치 선택 팝업은 작업당 한 번만 표시합니다. 이후에는 다운로드 이력의 `처리 대기` 목록에서 처리합니다.
- 트레이 아이콘은 단일 왼쪽 클릭으로 창을 열지 않습니다. 더블 클릭 또는 오른쪽 클릭 메뉴를 사용합니다.
- 다운로드 시작 탭 URL은 Chromium API가 신뢰할 수 있는 필드로 제공하지 않으므로 활성 탭을 추측하지 않습니다.

## 유지보수 기준

- 안정 기준은 `main`과 최신 GitHub Release입니다.
- 기능 변경은 별도 브랜치와 PR에서 진행합니다.
- `Directory.Build.props` 외의 파일에 제품 버전을 하드코딩하지 않습니다.
- Extension key, 고정 ID와 Native Messaging identity를 변경하지 않습니다.
- 사용자 DB 스키마와 terminal 상태 전이는 migration과 회귀 테스트 없이 수정하지 않습니다.
- 실제 검증하지 않은 브라우저와 디스플레이 환경을 지원 완료로 기록하지 않습니다.
