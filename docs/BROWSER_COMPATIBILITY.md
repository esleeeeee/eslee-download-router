# 브라우저 호환성

자동 테스트와 격리된 수동 테스트 결과만 기록합니다. 개인 브라우저 프로필, 로컬 경로, 다운로드 기록, DB 내용, 식별자, 해시와 원본 로그는 이 문서에 저장하지 않습니다.

| 브라우저 | 지원 수준 | 설치 탐지 | 확장 로드 | 다운로드 이벤트 | Native Messaging | 자동 저장 | 직접 선택 |
|---|---|---|---|---|---|---|---|
| Naver Whale | 정식 목표 | 성공 | 성공 | 성공 | 성공 | 성공 | 성공 |
| Microsoft Edge | 정식 목표 | 성공 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Google Chrome | 정식 목표 | 성공 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Brave | 호환 목표 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Vivaldi | 호환 목표 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |
| Opera | 호환 목표 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 |

## 브라우저별 관리 주소

- Whale: `whale://extensions`
- Edge: `edge://extensions`
- Chrome: `chrome://extensions`
- Brave: `brave://extensions`
- Vivaldi: `vivaldi://extensions`
- Opera: `opera://extensions`

설치 탐지는 확장 또는 Native Messaging 연결 성공을 의미하지 않습니다. 각 항목은 별도로 검증합니다.

## 검증 기록 양식

```text
날짜:
브라우저:
설치 탐지:
확장 로드:
다운로드 이벤트:
Native Messaging:
자동 저장:
직접 선택:
미매칭 fail-open:
알려진 문제:
```

버전이 꼭 필요한 호환성 문제는 최소한의 제품 버전만 기록합니다. OS 빌드, 화면 해상도, 배율, 확장 ID, 사용자 폴더, 프로필명과 로그 위치는 기록하지 않습니다.
