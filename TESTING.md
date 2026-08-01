# 테스트

테스트 기록에는 개인 파일 경로, 브라우저 프로필, 실제 사용자 DB 내용, 행 번호, 해시 또는 원본 로그를 저장하지 않습니다. 수동 검증은 격리된 테스트 프로필과 임시 데이터 디렉터리에서 수행합니다.

## 자동 검증

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -PublishNativeHost
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\smoke-agent.ps1
```

자동 검증 범위:

- Core 규칙, 경로 정책, 상태 전이와 개인정보 제거
- SQLite 마이그레이션, 원자성, 중복 이름 및 파일 이동
- App/Agent/Native Host 명령과 장애 시 fail-open
- 브라우저 확장 lint, TypeScript 컴파일 및 단위 테스트
- 릴리스 빌드의 버전 일치와 필수 WinUI 리소스

## 수동 검증 원칙

- 실제 사용자 프로필과 데이터베이스를 사용하지 않습니다.
- 테스트마다 고유한 임시 데이터 디렉터리와 브라우저 프로필을 사용합니다.
- 테스트가 만든 파일과 DB만 정리하며 사용자 파일을 삭제하지 않습니다.
- 결과는 `성공`, `실패`, `미검증`으로만 기록하고 로컬 경로·ID·해시·행 수·원본 로그를 커밋하지 않습니다.

## 브라우저 수동 검증

1. Release 구성으로 App, Agent와 Native Host를 빌드합니다.
2. 격리된 브라우저 프로필에 unpacked 확장을 로드합니다.
3. Native Host 등록 상태와 Agent ping을 확인합니다.
4. 규칙이 없는 다운로드가 원래 위치에 남는지 확인합니다.
5. 자동 규칙으로 테스트 파일이 지정한 임시 폴더로 이동하는지 확인합니다.
6. 직접 선택 규칙에서 완료 전·후 선택, 나중에 선택, 이동 안 함과 취소를 확인합니다.
7. 동일 이름 파일이 덮어쓰이지 않고 보존되는지 확인합니다.
8. 루트 외부, `..`, junction/reparse point 경로가 거부되는지 확인합니다.
9. Agent 또는 Native Host가 중단되어도 브라우저 다운로드가 차단되지 않는지 확인합니다.
10. 앱 재시작 후 terminal 작업이 다시 표시되지 않는지 확인합니다.

결과는 [브라우저 호환성 문서](docs/BROWSER_COMPATIBILITY.md)의 표에 민감정보 없이 반영합니다.

## 설치·업그레이드 검증

- 초기 설치, 업그레이드와 제거 종료 코드를 확인합니다.
- App, Agent와 Native Host의 제품 버전이 일치하는지 확인합니다.
- 테스트용 설정과 DB가 업그레이드 후 보존되는지 기능적으로 확인합니다.
- Native Host 등록, 자동 시작과 제거 동작을 확인합니다.
- 배포 폴더와 설치 파일에 PDB 또는 개발 PC 절대 경로가 없는지 검사합니다.

## 트레이 아이콘 검증

아이콘 또는 `TrayIconHost` 알림 옵션을 변경한 뒤에는 다음을 확인합니다.

- 트레이 아이콘에 마우스를 올리면 `eslee Download Router` 제품명 툴팁이 표시됩니다.
- 일반 실행 직후 툴팁이 표시됩니다.
- X 버튼으로 트레이에 숨긴 뒤에도 툴팁이 표시됩니다.
- 앱을 종료하고 다시 실행한 뒤에도 툴팁이 표시됩니다.
- `--background` 자동 시작 경로로 실행한 뒤에도 툴팁이 표시됩니다.
- 트레이 아이콘 더블 클릭으로 메인 창이 열립니다.
- 트레이 아이콘 오른쪽 클릭 메뉴가 열리고 각 항목이 동작합니다.
- Explorer 또는 작업 표시줄이 다시 시작된 뒤 트레이 아이콘이 복구됩니다.
- 트레이에 같은 아이콘이 중복 표시되지 않습니다.

결과는 통과 여부만 기록하고 화면 캡처, 좌표, 사용자 환경 정보는 저장소에 남기지 않습니다.

## UI·DPI 검증

- Per-Monitor V2 DPI awareness
- 좁은 창의 수평 넘침과 세로 스크롤
- 넓은 창의 최대 폭과 중앙 정렬
- System/Light/Dark 테마 전환
- 메인 창 최소화 상태의 독립 선택 창 표시
- 긴 폴더 이름과 다중 대기 작업의 레이아웃

물리 디스플레이별 검증은 장치 정보나 사용자 환경을 기록하지 않고 통과 여부만 남깁니다.
