# 개발자 모드 확장 및 Native Host 연결

이 절차는 사용자 단위 개발 검증용입니다. 브라우저 정책을 수정하거나 확장을 강제 설치하지 않습니다.

## 1. 빌드

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -PublishNativeHost
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-extension.ps1
```

압축 해제 로드 경로는 `src\DownloadRouter.Extension\dist`입니다. ZIP은 배포 검토용이며 개발자 모드 “압축 풀린 확장 로드”에는 폴더를 사용합니다.

## 2. Native Host 등록

브라우저별로 하나씩 등록하고 상태를 확인합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-native-host.ps1 -Action Register -Browser Edge
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-native-host.ps1 -Action Status -Browser Edge
```

등록은 HKCU와 `%LOCALAPPDATA%\eslee\DownloadRouter\native-host`만 사용하며 관리자 권한을 요구하지 않습니다.

## 3. 확장 로드

1. 브라우저의 확장 관리 주소를 엽니다.
2. 개발자 모드를 켭니다.
3. “압축 풀린 확장 로드”를 선택합니다.
4. 저장소의 `src\DownloadRouter.Extension\dist`를 선택합니다.
5. 표시된 ID가 `gilicenlclaemgiijcjjejilikbooggj`인지 확인합니다.
6. 다르면 Native Host manifest의 origin과 일치하지 않으므로 연결 테스트를 진행하지 말고 원인을 기록합니다.

## 4. Agent와 앱

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-dev.ps1 -SkipBuild
```

앱의 “진단 및 문제 해결”에서 Agent 연결을 테스트합니다. 이 성공은 앱과 Agent 사이만 확인합니다. 브라우저 Native Messaging 성공은 확장 service worker 오류와 Agent 작업 이력을 함께 확인해야 합니다.

## 5. 테스트 규칙

샘플은 `samples\rule.naver.example.json`에 있습니다. 앱에서 동일한 값으로 규칙을 만들고 개인 PC에 맞는 저장 루트를 지정합니다. 실제 개인 파일 대신 공개 테스트 파일을 사용하세요.

## 6. 제거

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\unregister-native-host.ps1 -Browser Edge
```

브라우저 확장 관리 화면에서 개발자 확장을 제거합니다. 사용자 DB와 규칙은 자동 삭제하지 않습니다.

## 문제 해결

- `scripts\diagnose.ps1` 실행
- 등록 `-Action Status` 확인
- Host 경로가 self-contained publish 산출물인지 확인
- 확장 ID와 `allowed_origins` 일치 확인
- 기업 정책의 Native Messaging/개발자 모드 차단 확인
- 브라우저를 완전히 종료 후 다시 시작

URL query, 인증 토큰, 실제 사용자 경로가 포함된 로그를 issue나 문서에 붙이지 마세요.
