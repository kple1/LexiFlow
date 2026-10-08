# Windows 업데이트 배포 검증 — 2026-10-08

## 공개 배포

- 최초 부트스트랩 앱: 1.6.0 / build 25. 기존 1.5 이하 앱을 한 번 교체해야 합니다.
- 공개 최신 버전: 1.6.1 / build 26. 1.6.0과 기능은 같으며 첫 인앱 업데이트 경로 검증용 버전입니다.
- 두 버전 모두 Windows x64 self-contained 단일 EXE publish 성공. 기존 nullable/MVVM 분석 경고가 있으며 오류는 없습니다.
- EXE 크기: 310,768,851 bytes (약 296 MiB).
- 1.6.0 SHA-256: `2C1FD20F4C48CD12F43D5840C13CAB84F30F23230F7BAA51EB96D7E15C6050B9`.
- 1.6.1 SHA-256: `A232A011B29248D0C6DB61E48EEA31019B2DD071A3433E6EDB830DB827725C7B`.
- 피드 발행: 2026-10-08 03:34:26 UTC. 만료: 2027-04-06 03:34:26 UTC.
- 개인 서명키는 제작 PC에 DPAPI로 보호 저장했습니다. Oracle에는 EXE·서명 피드·Caddy 설정·배포 스크립트만 전송했습니다.

피드: `https://lexiflow.duckdns.org/updates/windows-x64/latest.json`.
아티팩트: `https://lexiflow.duckdns.org/updates/windows-x64/1.6.1/LexiFlow.exe`.
상세 설계·사용법·후속 배포: [앱 업데이트 운영 안내](APPLICATION-UPDATES.md).

## 서버 검증

12:37 KST경 프록시만 재생성하여 `/updates/windows-x64/` 정적 파일 읽기 전용 경로를 연결했습니다. 기존 Caddy 데이터/설정 볼륨과 TLS 인증서를 유지했습니다.

- HTTPS `/health`: 200.
- 익명 `/ranking`: 401 — 기존 인증 정책 유지.
- 업데이트 피드: 200, 게시한 서명 envelope와 바이트 동일.
- EXE HEAD: 200, Content-Length 310,768,851.
- 제작 PC에서 피드 및 **전체 EXE를 실제 HTTPS로 다시 다운로드**하고 고정 공개키 서명·길이·SHA-256·EXE 표식 검증 성공.
- API 컨테이너 ID `90695d5c98cd` 불변. 이미지 `lexiflow-ranking:20261008-loginid-527602d9dbc5` 유지.
- PostgreSQL 컨테이너 ID `5d60433a776c` 불변·healthy. 계정 수 읽기 전용 조회 결과 1.
- DB·API·스키마·환경 비밀값·IAM·백업 설정은 변경하지 않았습니다. 미배포 이메일 가입/복구 코드도 반영하지 않았습니다.
- `lexiflow-backup.timer`: active. `backup.py --check`: healthy=true, issues=[]; 최근 일일 백업의 복원/클라우드 왕복 검증도 true. 이 최근 일일 백업은 당일 계정 삭제 **이전** 스냅샷이며 현재 DB 계정 수의 증거가 아닙니다.

서버 staging 및 원본 프록시 설정 백업:
`/home/ubuntu/lexiflow-update-deploy/release-20261008-161/`.
검증 보고서는 해당 디렉터리의 `deployment-result.json`에 보관합니다.

## 자동·격리 검사

- 업데이트 프로토콜/다운로드/원자적 교체/복구: 53/53.
- 실제 Windows helper 정상 교체 및 시작 실패 복구: 14/14. 테스트 전용 바이너리·임시 별도 서명키 사용; 프로덕션 키·계정 저장소·DB 사용 없음.
- 업데이트 배포 보호·프록시 복구 검사: 7/7.
- 전체 기존 배포 검사 포함: 32/32.
- 클라이언트 보안/랭킹/세션 검사: 46/46.
- 학습/코스/별점/계정 분리 검사: 56/56.

기본 업데이트 검사는 기존 Linux CI에 연결했습니다. 실제 Windows helper 검사는 별도 Windows 명령으로 실행하며, GitHub CI의 실제 실행이나 저장소 push를 한 것은 아닙니다.

## 실제 앱 확인

고정 실행 경로:
`C:\Users\Leepl\Documents\Codex\Apps\LexiFlow\LexiFlow.exe`.

1.6.0 앱에서 기존 로그인 세션을 복원하고 2,456 XP·레벨 13·마스터 107개를 표시했습니다. `나 → 앱 업데이트`에서 1.6.1 사용 가능 표시, 다운로드 진행률/취소 버튼, 전체 파일 수신 후 ‘다운로드 검증 완료’와 ‘재시작해 적용’ 버튼을 확인했습니다.

다운로드까지 검증했으며 실제 프로덕션 앱의 마지막 재시작 적용은 사용자 실행 확인 대기 상태입니다. 이 기록을 쓴 시점에 고정 경로의 EXE는 1.6.0입니다. 실제 재시작 완료를 확인하지 않은 상태를 완료로 보고하지 않습니다. 자동 복구는 격리 바이너리로 검증했으며 실제 MAUI 시작 실패를 강제로 유발하지는 않았습니다.
