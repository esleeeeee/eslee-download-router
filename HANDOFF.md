# 다른 PC 인수인계

이 문서와 `PROJECT_STATE.md`를 먼저 읽으면 회사 PC의 빌드 산출물, DB, 사용자 경로 없이 개발을 이어갈 수 있습니다.

## 기준점

- 저장소: `https://github.com/esleeeeee/eslee-Download-Router.git`
- 이어서 사용할 브랜치: 초기 push 후 `feature/initial-spike`(기준 통합 브랜치는 `develop`)
- 마지막 정상 커밋: 초기 커밋 전 — 이 세션의 push 후 실제 해시로 갱신
- .NET SDK: `10.0.302`
- Node.js: 24 이상
- npm: 11 이상
- 운영체제: Windows 11 x64

## 새 PC 절차

```powershell
git clone https://github.com/esleeeeee/eslee-Download-Router.git
cd eslee-Download-Router
git switch feature/initial-spike
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -PublishNativeHost
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-native-host.ps1 -Action Register -Browser Edge
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-dev.ps1 -SkipBuild
```

Edge에서 `edge://extensions`를 열고 개발자 모드를 켠 뒤 “압축 풀린 확장 로드”에서 `src\DownloadRouter.Extension\dist`를 선택합니다. 다른 브라우저 주소와 절차는 `docs/MANUAL_EXTENSION_INSTALL.md`를 따릅니다.

## 현재 정상 기준

- Debug solution build 성공, 경고 0/오류 0
- .NET tests 27/27 통과
- Extension lint/build와 Node tests 4/4 통과
- Agent Named Pipe ping 성공
- Native Host self-test 성공
- App/Agent/Native Host self-contained Release publish 성공

브라우저 수동 검증, installer, UI 시각 검증은 아직 정상 기준에 포함되지 않습니다.

## 가장 먼저 할 작업

Edge에서 실제 확장 -> Native Host -> Agent ping과 다운로드 이벤트를 확인하세요. 성공/실패 로그를 `docs/BROWSER_COMPATIBILITY.md`에 기록하고, 실패하면 `scripts\diagnose.ps1`과 Native Host `-Action Status` 결과부터 확인합니다.

그 다음 자동 규칙, 미매칭 fail-open, 직접 선택 묶음, 중복 이름, 취소/중단 순서로 검증합니다.

## PC별로 다시 지정할 항목

- 규칙의 저장 루트 또는 `{Downloads}`, `{Documents}` 같은 토큰 경로
- 브라우저별 개발자 모드 확장 로드
- Native Host HKCU 등록
- 필요 시 기업 정책 허용 여부

회사 PC의 사용자명, 절대 저장 경로, DB, 로그, 브라우저 프로필, 인증정보는 저장소에 없으며 가져올 필요가 없습니다.

## 작업 종료 절차

1. build, test, 가능한 브라우저 시나리오 실행
2. `PROJECT_STATE.md`, 이 문서, `CHANGELOG.md`, 브라우저 표 갱신
3. `git diff --check`, `git diff`, `git status` 검토
4. 의미 있는 커밋 후 공식 origin으로 push
5. GitHub에서 원격 브랜치 커밋 확인
6. Notion의 전체이력 요약과 프로젝트 상세 이력 갱신
7. 로컬에만 남은 필수 파일이 없는지 확인

## 로컬 설정 잔존 여부

현재 필수 구현 파일은 모두 저장소 안에 있습니다. 테스트·게시 산출물, `node_modules`, LocalAppData DB/로그와 Native Host manifest는 의도적으로 제외됩니다.
