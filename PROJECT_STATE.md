# 프로젝트 상태

기준일: 2026-07-21 (Asia/Seoul)

## 요약

- 현재 단계: Phase 0 완료, Phase 1 기술 스파이크 완료, 핵심 기능 개발 중
- 공식 저장소: https://github.com/esleeeeee/eslee-Download-Router
- 게시 브랜치: `main`, `develop`, `feature/initial-spike` — 세 브랜치 모두 공식 원격에 생성 완료
- 작업 브랜치/기준 HEAD: `feature/initial-spike` / `3a4e25e86f570d03a37f61f4754b3d11bd501717`
- 현재 문서는 위 HEAD에 적용한 로컬 진단·수정 결과를 포함하며 원격 push는 하지 않음

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
- Windows PowerShell 5.1 절대 경로/HKCU 등록 호환성과 BOM 없는 UTF-8 Native Host manifest
- 실패할 때만 분류 코드를 남기는 Extension Native Messaging 진단 로그
- Per-Monitor V2 WinUI 렌더링, 반응형 공통 콘텐츠/세로 스크롤, 3초 이력 변경 감지
- self-contained win-x64 publish와 per-user Inno Setup 골격

## 빌드와 테스트

| 항목 | 결과 |
|---|---|
| `.NET Debug build` | 성공, 경고 0, 오류 0 |
| `.NET tests` | 29/29 통과(Core 21, Infrastructure 5, Integration 3) |
| Extension ESLint/TypeScript build | 성공 |
| Extension Node tests | 6/6 통과 |
| Agent Named Pipe ping | 성공 |
| Native Host self-test/길이 접두사 ping/Agent 자동 시작 | 성공 |
| Native Host Agent 장애 fail-open | `agent.unavailable`, Host 종료 코드 0, 브라우저 비차단 응답 확인 |
| Release win-x64 self-contained publish | App/Agent/Native Host 성공 |
| WinUI QHD 125% | 수정 전 DPI Unaware/96 → 수정 후 Per-Monitor V2/120 확인 |
| WinUI 반응형 폭 | 900px 창에서 우측 넘침 0, 2400px 창에서 1100 DIP 폼 중앙 정렬 확인 |
| clean clone | 공식 `feature/initial-spike@9975c47`에서 bootstrap/build/test 성공 |
| Installer compile/install/uninstall | 미검증 |

## 브라우저 검증

| 브라우저 | 설치 탐지 | 확장 로드 | 다운로드 이벤트 | Native Messaging | 자동 저장 | 직접 선택 |
|---|---|---|---|---|---|---|
| Whale | 성공 | 성공 | 성공 | 성공 | 수동 검증 필요 | 수동 검증 필요 |
| Edge | 성공 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Chrome | 성공 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Brave | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Vivaldi | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Opera | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |

Whale은 설치된 , 고정 ID 확장과 `dist` 로드, HKCU 등록, 실제 미매칭 다운로드 이벤트의 Agent 도달을 확인했습니다. 규칙이 없는 다운로드는 의도적으로 작업을 만들지 않으므로 이력이 비는 것이 정상입니다. 실제 Whale에서 매칭 규칙 파일 이동과 직접 선택은 아직 성공으로 표시하지 않았습니다.

## 알려진 문제와 제한

- downloads API만으로 다운로드 시작 탭 URL을 신뢰성 있게 얻을 수 없어 활성 탭을 추측하지 않습니다. 현재 referrer와 파일 URL을 분리해 사용합니다.
- 직접 선택은 App의 대기 화면에서 규칙별 묶음으로 처리합니다. Agent의 자동 창 활성화/트레이 알림, 새 폴더 생성, 폴더 새로 고침은 미구현입니다.
- 시작 시 실행 UI는 실제 Windows 등록과 연결되지 않았습니다.
- Agent 시작 시 미완료 이동 복구 워커는 미구현입니다. DB에는 작업 상태가 유지됩니다.
- Whale registry adapter는 이 PC에서 검증했습니다. Brave/Vivaldi/Opera adapter는 실제 PC 검증이 필요합니다.
- 브라우저 자체 “다운로드 전에 저장 위치 확인” 설정 감지와 안내는 문서만 있고 UI 자동 감지는 미구현입니다.
- QHD 125% 실제 실행과 좁은/넓은 창 시각·경계 검증은 완료했습니다. Windows 100%/150%, FHD/4K 실기기, 키보드/스크린리더 접근성 및 installer 동작 검증은 남아 있습니다.

## 보류된 결정

- 배포 코드 서명 인증서와 업데이트 채널
- 안정 배포 확장 ID/스토어 배포 방식
- 로그 보존 기간과 사용자 삭제 UI
- Agent 선택 알림을 App activation, tray, 별도 picker 중 어떤 방식으로 구현할지

## 다음 작업

1. Whale에서 `example.com` 샘플 규칙으로 공개 파일 자동 이동과 이력 자동 갱신을 수동 검증
2. Whale 직접 선택 규칙과 Native Host 장애 중 실다운로드 유지 검증
3. Windows 100%/150% 및 FHD/4K에서 UI 실배율 시각 검증
4. Edge에서 개발자 모드 확장 로드와 Native Messaging 왕복을 실제 검증
5. 규칙별 전용 폴더 트리 picker에 새 폴더/새로 고침 추가 및 Agent 알림 연결
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
