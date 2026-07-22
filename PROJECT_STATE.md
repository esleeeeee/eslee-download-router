# 프로젝트 상태

기준일: 2026-07-23 (Asia/Seoul)

## 요약

- 현재 단계: Phase 0 완료, Phase 1 기술 스파이크 완료, 핵심 기능 개발 중
- 공식 저장소: https://github.com/esleeeeee/eslee-Download-Router
- 게시 브랜치: `main`, `develop`, `feature/initial-spike` — 세 브랜치 모두 공식 원격에 생성 완료
- 작업 브랜치: `feature/initial-spike`, 기존 Draft PR #1에서 후속 변경 추적

## 구현 완료

- 역할별 .NET/TypeScript 모노레포와 고정 SDK·패키지·잠금 파일
- Extension -> Native Host -> current-user Named Pipe -> Agent 프로토콜
- Native Host 입력 크기/JSON/버전/request ID/명령 allowlist/origin 검증
- Agent 단일 인스턴스, 자동 시작 경로, 다중 pipe 연결 처리
- SQLite 마이그레이션과 규칙·작업·이벤트·설정·브라우저 연결 테이블
- 도메인+하위 도메인, 정확한 호스트, URL 포함 규칙
- 시작 페이지/파일 URL/둘 중 하나 매칭과 IDN 처리
- 규칙 미매칭 및 IPC 실패 fail-open
- 경로 토큰과 루트 경계/reparse point 검증
- 다운로드 완료 파일 안정화와 동일 볼륨 move
- 교차 볼륨 copy -> 크기/SHA-256 검증 -> rename -> 원본 삭제
- 중복 파일 번호 보존과 재시도 상태
- 브라우저 전송 상태와 라우팅 상태를 분리한 작업 모델과 v1 → v2 SQLite 마이그레이션
- 파일별 SelectSubfolder 카드, 명시적으로 체크한 항목만 일괄 적용, 건너뛰기와 세션 단위 나중에 선택
- 생성/마지막 브라우저 이벤트 기준 30분 자동 팝업 정책, 이전 세션 대기 표시, 현재 큐 `모두 나중에 선택`
- 고정 DIP 전용 선택 창, lazy 계층형 FolderTreePicker, 단일 FIFO, 숨김/최소화 상태 직접 표시, 취소 시 자동 닫기
- `onCreated`/`filename` delta/완료 검색을 통한 같은 Job 파일명 갱신과 임시 이름 차단
- `USER_CANCELED` 즉시 전송, interrupted 오류 우선순위, onErased 비취소 처리와 팝업 연령을 갱신하지 않는 service worker 시작 재조정
- 이력의 Job별 저장 위치 선택·변경, 완료 파일 명시적 재이동, 이동 안 함/나중에 선택
- 취소선·상태 필터·개별/선택/취소 이력 삭제 UI와 실제 파일 비삭제 보장
- 현재 필터 전체 선택/해제와 선택 수 표시
- NavigationView Pending InfoBadge와 대시보드 공통 집계
- 한 페이지의 규칙 생성/편집 섹션과 저장된 규칙 카드, 활성 토글, soft delete, HWND FolderPicker
- App 단일 인스턴스 트레이 호스트, `--background`, HKCU 로그인 자동 시작, X 숨김과 명시적 종료
- WinUI 3 필수 내비게이션 화면 골격과 Agent 진단
- 브라우저 설치/관리 주소 adapter와 HKCU Native Host 등록 스크립트
- Windows PowerShell 5.1 절대 경로/HKCU 등록 호환성과 BOM 없는 UTF-8 Native Host manifest
- 실패할 때만 분류 코드를 남기는 Extension Native Messaging 진단 로그
- Per-Monitor V2 WinUI 렌더링, 뷰포트 실폭 제한, 32/48 DIP 공통 상단 여백, 1초 상태 변경 감지
- 저장된 System/Light/Dark를 열린 모든 Window와 이후 생성 Window에 적용하는 공통 ThemeManager
- assembly informational version을 표시하는 정보 화면과 `Directory.Build.props` 기반 App/Agent/Native Host/Installer 단일 0.3.0 버전
- self-contained win-x64 publish와 설치/업그레이드/제거가 검증된 per-user Inno Setup 0.3.0

## 빌드와 테스트

| 항목 | 결과 |
|---|---|
| `.NET Debug build` | 성공, 경고 0, 오류 0 |
| `.NET tests` | 68/68 통과(Core 41, Infrastructure 7, Integration 20) |
| Extension ESLint/TypeScript build | 성공 |
| Extension Node tests | 13/13 통과 |
| Agent Named Pipe ping | 성공 |
| Native Host self-test/길이 접두사 ping/Agent 자동 시작 | 성공 |
| Native Host Agent 장애 fail-open | `agent.unavailable`, Host 종료 코드 0, 브라우저 비차단 응답 확인 |
| Release win-x64 self-contained publish | App/Agent/Native Host 성공 |
| WinUI QHD 125% | 수정 전 DPI Unaware/96 → 수정 후 Per-Monitor V2/120 확인 |
| WinUI 반응형 폭 | 900px 창에서 우측 넘침 0, 2400px 창에서 1100 DIP 폼 중앙 정렬 확인 |
| clean clone | 공식 `feature/initial-spike@9975c47`에서 bootstrap/build/test 성공 |
| Installer compile/install/upgrade/uninstall | 성공, 사용자 DB 해시·행 수 보존, 자동 시작/6개 Native Host/바로가기 정리 확인 |

## 브라우저 검증

| 브라우저 | 설치 탐지 | 확장 로드 | 다운로드 이벤트 | Native Messaging | 자동 저장 | 직접 선택 |
|---|---|---|---|---|---|---|
| Whale | 성공 | 성공 | 성공 | 성공 | 성공 | 성공(계층형 SelectSubfolder) |
| Edge | 성공 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Chrome | 성공 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Brave | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Vivaldi | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Opera | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |

Whale 에서 로컬 HTTP fixture로 Automatic과 SelectSubfolder를 실제 검증했습니다. Automatic은 C: 기본 다운로드 위치에서 D: 테스트 규칙 루트로 교차 볼륨 이동했고, SelectSubfolder는 테스트 루트에서 `kr → 모야지`만 확장·선택해 이동했습니다. 100자 이상 노드를 표시하고 복귀해도 선택 창 폭은 700px(125%의 560 DIP)로 동일했습니다. 메인 창 숨김/최소화 중에도 독립 선택 창이 직접 나타났습니다. 2026-07-23에는 아무 선택 없음/선택 완료/나중에 선택/이동 안 함 네 실제 Whale 취소가 모두 약 0.24초 안에 `Cancelled`로 반영되고 팝업·Pending·이동 0건과 이력 취소선을 확인했습니다.

## 알려진 문제와 제한

- downloads API만으로 다운로드 시작 탭 URL을 신뢰성 있게 얻을 수 없어 활성 탭을 추측하지 않습니다. 현재 referrer와 파일 URL을 분리해 사용합니다.
- App 실행 파일을 찾을 수 있으면 Agent가 선택 UI를 시작하고, 실행 중이면 1초 폴링과 독립 창으로 FIFO를 표시합니다. 설치 손상으로 App을 찾지 못해도 다운로드는 원래 위치에서 완료되고 Pending에 남습니다.
- “나중에 선택”과 “모두 나중에 선택” 팝업 억제는 현재 App 세션 동안만 유지됩니다. 30분이 지난 작업은 대기 탭에서 수동 처리하며 트리의 새 폴더 생성은 미구현입니다.
- Extension 시작 시 Agent의 진행 중 ID를 브라우저 다운로드 기록과 대조해 완료·취소·중단을 재전송합니다. 이 재조정은 실제 브라우저 이벤트 시각을 갱신하지 않으므로 오래된 Pending을 자동 팝업 대상으로 되살리지 않으며, 브라우저 기록에서 사라진 작업도 근거 없이 완료·삭제하지 않습니다.
- Whale registry adapter는 이 PC에서 검증했습니다. Brave/Vivaldi/Opera adapter는 실제 PC 검증이 필요합니다.
- 브라우저 자체 “다운로드 전에 저장 위치 확인” 설정 감지와 안내는 문서만 있고 UI 자동 감지는 미구현입니다.
- QHD 125% 실제 실행과 좁은/넓은 창 시각·경계 검증은 완료했습니다. Windows 100%/150%, FHD/4K 실기기와 키보드/스크린리더 접근성 검증은 남아 있습니다.
- 전역 테마의 매핑·저장·fallback은 자동 검증했습니다. 실제 QHD 125%의 다크/라이트 전환, 선택 창, 트레이 복원, 재실행 검증 결과는 아래 최신 설치본 검증 기록을 기준으로 하며 100%/150% 실기기 전환은 남아 있습니다.
- HKCU 자동 시작 명령과 백그라운드 실행은 확인했지만 실제 로그아웃/로그인 또는 재부팅은 수행하지 않았습니다.
- 설치 파일은 코드 서명되지 않았습니다.

## 보류된 결정

- 배포 코드 서명 인증서와 업데이트 채널
- 안정 배포 확장 ID/스토어 배포 방식
- 로그 보존 기간과 사용자 삭제 UI
- 로그아웃을 포함한 장기 “나중에 선택” 알림 정책

## 다음 작업

1. Windows 100%/150% 및 FHD/4K에서 UI 실배율·테마 시각 검증
2. Native Host 장애 중 Whale 실다운로드 유지 검증
3. Edge에서 개발자 모드 확장 로드와 Native Messaging 왕복을 실제 검증
4. FolderTreePicker 새 폴더 만들기와 영구 알림 설정 추가
5. 브라우저 기록에서 사라진 오래된 비종료 작업의 사용자 확인 복구 흐름 설계
6. 설치 코드 서명과 업데이트 채널 결정

## 실행 명령

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\smoke-agent.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-dev.ps1 -SkipBuild
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

## 로컬 전용 설정

- 사용자 DB, 로그, 진단: `%LOCALAPPDATA%\eslee\DownloadRouter`
- Native Host PC별 manifest: 위 경로의 `native-host` 하위
- 실제 저장 루트는 PC마다 규칙에서 다시 확인
- 확장 로드 경로: clone 내부 `src\DownloadRouter.Extension\dist`
- 필수 소스나 설정이 현재 개발 PC에만 남아 있지 않도록 모든 재현 정보는 저장소에 기록
