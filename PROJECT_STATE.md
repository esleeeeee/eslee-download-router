# 프로젝트 상태

기준일: 2026-07-21 (Asia/Seoul)

## 요약

- 현재 단계: Phase 0 완료, Phase 1 기술 스파이크 완료, 핵심 기능 개발 중
- 공식 저장소: https://github.com/esleeeeee/eslee-Download-Router
- 게시 브랜치: `main`, `develop`, `feature/initial-spike` — 세 브랜치 모두 공식 원격에 생성 완료
- 구현 기준 커밋: `9975c47bd0ac64e26fa651dcd8162aac068f73d8` (`feat: bootstrap download router spike`)
- 원격 검증: GitHub 플러그인에서 공개 저장소, 세 브랜치, 위 커밋을 직접 확인

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
- 규칙별 선택 대기 작업 묶음 처리 API와 App 화면
- WinUI 3 필수 내비게이션 화면 골격과 Agent 진단
- 브라우저 설치/관리 주소 adapter와 HKCU Native Host 등록 스크립트
- self-contained win-x64 publish와 per-user Inno Setup 골격

## 빌드와 테스트

| 항목 | 결과 |
|---|---|
| `.NET Debug build` | 성공, 경고 0, 오류 0 |
| `.NET tests` | 27/27 통과(Core 21, Infrastructure 4, Integration 2) |
| Extension ESLint/TypeScript build | 성공 |
| Extension Node tests | 4/4 통과 |
| Agent Named Pipe ping | 성공 |
| Native Host self-test | 성공 |
| Release win-x64 self-contained publish | App/Agent/Native Host 성공 |
| clean clone | 공식 `feature/initial-spike@9975c47`에서 bootstrap/build/test 성공 |
| Installer compile/install/uninstall | 미검증 |

## 브라우저 검증

| 브라우저 | 설치 탐지 | 확장 로드 | 다운로드 이벤트 | Native Messaging | 자동 저장 | 직접 선택 |
|---|---|---|---|---|---|---|
| Whale | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Edge | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Chrome | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Brave | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Vivaldi | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Opera | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |

실제 브라우저에서 수행하지 않은 결과를 성공으로 표시하지 않았습니다.

## 알려진 문제와 제한

- downloads API만으로 다운로드 시작 탭 URL을 신뢰성 있게 얻을 수 없어 활성 탭을 추측하지 않습니다. 현재 referrer와 파일 URL을 분리해 사용합니다.
- 직접 선택은 App의 대기 화면에서 규칙별 묶음으로 처리합니다. Agent의 자동 창 활성화/트레이 알림, 새 폴더 생성, 새로 고침은 미구현입니다.
- 시작 시 실행 UI는 실제 Windows 등록과 연결되지 않았습니다.
- Agent 시작 시 미완료 이동 복구 워커는 미구현입니다. DB에는 작업 상태가 유지됩니다.
- 브라우저별 Native Messaging registry adapter, 특히 Whale/Opera는 실제 PC 검증이 필요합니다.
- 브라우저 자체 “다운로드 전에 저장 위치 확인” 설정 감지와 안내는 문서만 있고 UI 자동 감지는 미구현입니다.
- 앱 UI의 실제 실행/시각·접근성 검증과 installer 동작 검증이 필요합니다.

## 보류된 결정

- 배포 코드 서명 인증서와 업데이트 채널
- 안정 배포 확장 ID/스토어 배포 방식
- 로그 보존 기간과 사용자 삭제 UI
- Agent 선택 알림을 App activation, tray, 별도 picker 중 어떤 방식으로 구현할지

## 다음 작업

1. Edge에서 개발자 모드 확장 로드와 Native Messaging 왕복을 실제 검증
2. Edge 자동 저장과 규칙 미매칭 fail-open 실다운로드 검증
3. 규칙별 전용 폴더 트리 picker에 새 폴더/새로 고침 추가 및 Agent 알림 연결
4. Whale registry adapter와 다운로드/Native Messaging 검증
5. Chrome 동일 시나리오 검증
6. Agent 시작 시 미완료 작업 복구와 시작 시 실행 옵션 구현
7. Inno Setup 설치/제거와 앱 UI 검증

## 실행 명령

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\smoke-agent.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-dev.ps1 -SkipBuild
```

## 로컬 전용 설정

- 사용자 DB, 로그, 진단: `%LOCALAPPDATA%\eslee\DownloadRouter`
- Native Host PC별 manifest: 위 경로의 `native-host` 하위
- 실제 저장 루트는 PC마다 규칙에서 다시 확인
- 확장 로드 경로: clone 내부 `src\DownloadRouter.Extension\dist`
- 필수 소스나 설정이 현재 개발 PC에만 남아 있지 않도록 모든 재현 정보는 저장소에 기록
