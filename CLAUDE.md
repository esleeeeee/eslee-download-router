# CLAUDE.md

이 저장소에서 작업할 때 지킬 규칙입니다. 빌드·브랜치·코딩 원칙은 `DEVELOPMENT.md`, 현재 상태와 알려진 제한은 `PROJECT_STATE.md`를 따릅니다.

## 구조

Chromium 확장(`src/DownloadRouter.Extension`) → Native Messaging Host(`src/DownloadRouter.NativeHost`) → 사용자별 Named Pipe → 백그라운드 Agent(`src/DownloadRouter.Agent`, SQLite는 `src/DownloadRouter.Infrastructure`) ↔ WinUI 3 앱(`src/DownloadRouter.App`). 공통 로직은 `src/DownloadRouter.Core`.

- 앱은 DB를 직접 열지 않고 모든 변경을 Agent 명령으로 요청합니다.
- 규칙에 맞지 않거나 구성 요소가 응답하지 않으면 브라우저 다운로드를 그대로 둡니다(fail-open). 이 원칙을 깨지 마세요.
- 앱 전용 명령은 `ProtocolConstants`의 app-only 목록에 넣고 Native Host가 브라우저에서 전달하지 않게 합니다.

## UI (v2.0.0부터, eslee 앱 시리즈 2.0 디자인)

- WinUI 3입니다. WPF용 `RenderMode.SoftwareOnly`는 해당하지 않습니다(2026-10-14 이 PC 원격 환경에서 정상 표시 확인).
- 색은 `src/DownloadRouter.App/Themes/Palette.xaml` 토큰만 씁니다. XAML은 `{ThemeResource 키}`, 코드에서는 `Themes/Controls.xaml`의 스타일을 적용합니다. 창 XAML이나 코드에 색 값을 직접 쓰거나, 코드에서 브러시를 꺼내 고정하지 마세요(소스 검사 테스트가 막습니다).
- 강조색은 Windows 기본 파랑으로 고정합니다(`SystemAccentColor*` 재정의). 사용자 강조색을 따르게 바꾸지 마세요.
- 대화상자는 `Services/AppDialog`만 씁니다. 설정 화면은 바꾸면 바로 저장하며 저장 버튼을 두지 않습니다. 스위치는 `Toggled`에서 저장합니다.
- 아이콘·글자 조합 버튼에는 `AutomationProperties.Name`, 목록 항목과 콤보 옵션은 읽을 수 있는 `ToString()`을 줍니다.

## 언어

- 한 빌드에서 한국어/영어를 고릅니다. 표시 문자열은 반드시 `AppText.Pick("한국어", "English")`(코드) 또는 `{c:L Ko='…', En='…'}`(XAML)로 두 언어를 함께 씁니다.
- 언어는 `config.local.json`의 `language`(System/Korean/English)이며 앱을 다시 시작하면 적용됩니다. 다시 시작할 때는 `AppInstance.ForwardedArguments`(데이터 위치)를 그대로 넘깁니다.
- XAML 마크업 확장 값 안에서는 작은따옴표를 쓰지 말고 “ ” 같은 문자를 쓰세요.

## 테스트용 인스턴스

- `--data-root <폴더>`로 띄운 사본은 Agent 파이프·뮤텍스·이벤트 이름에 접미사가 붙어 설치된 앱과 분리됩니다.
- 이런 사본은 HKCU Run 자동 실행, Native Messaging 등록, Tray Folder 등록, Windows 알림 등록을 **절대 바꾸지 않아야** 합니다(`AppInstance.IsDefault` 검사). 새 등록 기능을 넣을 때도 같은 검사를 하세요.
- UI 확인은 테스트 사본 + UI Automation + PrintWindow 캡처로 합니다. 이 PC는 125% 배율이므로 캡처 도구는 DPI 인식 모드로 실행해야 잘리지 않습니다.

## 데이터와 호환성

- 데이터 폴더 `%LOCALAPPDATA%\eslee\DownloadRouter`, 실행 파일 이름, 확장 ID와 key, Native Messaging 이름, Tray Folder 앱 id `eslee.downloadrouter`와 기존 메뉴 액션 id(`open-app`, `open-pending`, `open-settings`, `exit-app`)를 바꾸지 마세요. 바꾸면 Tray Folder 카탈로그도 고쳐야 합니다.
- DB 스키마 변경은 멱등 마이그레이션과 회귀 테스트를 함께 넣습니다. 기록 삭제는 실제 파일을 지우지 않습니다.
- 확장 소스나 아이콘을 바꾸면 `manifest.json` 버전을 올립니다.
- 제품 버전은 `Directory.Build.props` 한 곳에만 둡니다.

## 금지

- 사용자의 명시적 허락 없이 push, 병합, 태그, 릴리스, 설치, UAC가 필요한 작업을 하지 않습니다.
- 커밋·릴리스 문서에 개인 경로, 사용자 이름, SID, 이메일을 넣지 않습니다(`scripts/verify-release-privacy.ps1`).
- PowerShell 5.1의 `Set-Content -Encoding UTF8`은 BOM을 붙입니다. 커밋 메시지 파일은 BOM 없는 UTF-8로 쓰고, 한글이 들어간 `.ps1`은 UTF-8 BOM으로 저장합니다.
