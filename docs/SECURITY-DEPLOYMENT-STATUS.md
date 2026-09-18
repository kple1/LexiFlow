# 보안 전환 완료 — 2026-09-18

사용자 승인 후 기존 Oracle Cloud VM에 HTTPS·토큰 인증 서버를 반영했습니다.
**구버전 HTTP 앱은 접속 불가입니다. 1.1 보안 빌드로 업데이트하고 재로그인해야 합니다.**

## 실제 적용 및 검증

- `https://lexiflow.duckdns.org/health` GET 200, 공개 CA 인증서 검증 성공.
- HTTP 80은 HTTPS로 308 이동. 인증 정보를 HTTP로 보내면 안 됩니다.
- Oracle 보안 목록: TCP 80·443 추가, 기존 TCP 5276 공개 규칙 제거. SSH 22/ICMP는 유지.
- 호스트 방화벽도 80·443 허용, 5276 허용 제거 후 영구 저장.
- 외부 5276 접속 실패 확인. API는 컨테이너 내부 5276만 사용하고 DB 5432도 외부 비공개.
- Caddy `172.30.250.2`, API `172.30.250.3`으로 명시하여 IP 자동 할당 충돌 수정.
- API 비루트 사용자, 읽기 전용 파일시스템, 권한 최소화 적용.
- 복원 DB에서 23개, 운영 HTTPS에서 23개 API 검사 통과:
  가입/로그인, 무인증 차단, 교차 계정 조회·쓰기 차단, 비밀번호 변경 시 세션 폐기, 탈퇴.
- 테스트용으로 생성한 계정 2개는 각 환경에서 정리. 기존 사용자 비밀번호/계정은 변경하지 않음.
- 관리자 토큰 교체 후 기존 토큰 401·신규 토큰 200 확인. GitHub Actions `ADMIN_TOKEN`도 동기화.
- 운영 DB 마이그레이션 2개 적용. 자동 마이그레이션은 다시 꺼짐.

기존 `server_dbdata` 볼륨을 그대로 유지했습니다. 전환 전후 사용자 4명, 단어 진도 209건,
문법 진도 8건, 단어 225개, 문법 33개가 일치했습니다. 숙어/숙어 진도는 각각 0건입니다.

## 백업 및 복구 자료

- 첫 리허설: `/home/ubuntu/lexiflow-security-backups/20260918T024119Z/`.
- 실제 전환 직전 API를 멈춘 상태의 최신 백업:
  `/home/ubuntu/lexiflow-security-backups/20260918T025350Z/worddb.dump` (43,926 bytes).
- 두 번 모두 별도 DB에 실제 복원 후 모든 테이블 행 수가 일치함을 확인.
- `preflight.json`, `migration-rehearsal.json`, `cutover.json`에 검증 결과 보관.
- 테스트 컨테이너 2개와 리허설 DB 2개는 검증 후 정리. 원본 DB·백업·인증서 볼륨은 유지.
- PC의 별도 사본:
  `C:\Users\Leepl\Documents\Codex\2026-09-17\dldj\artifacts\private-backups\worddb-20260918T025350Z.dpapi`.
  Windows DPAPI CurrentUser 암호화 및 현재 사용자/SYSTEM만 파일 접근 허용.
  암호화/복호화 왕복 검증 및 서버 원본 SHA-256 일치 확인:
  `53f94f4bcb170891501f1c15a9279109ec366eec02d690e76ba9250f95104b69`.

DPAPI 백업 복구에는 같은 Windows 사용자 프로필의 복호화 키가 필요합니다.
서버 백업도 별도로 유지하세요. 이후 일일 암호화 백업(03:00 KST), OCI 30일/서버 7일 보관,
이메일 경보를 추가했습니다. [운영·복구 절차와 남은 키 보관 작업](BACKUP-RUNBOOK.md).
운영 DB 덮어쓰기 복원은 별도 승인과 복구 계획 없이는 실행하지 않습니다.

## 초도 전환 이미지 기록

- 로컬에서 Linux x64로 게시한 서버 파일을 기존 VM에서 이미지로 패키징.
- 이미지 태그: `lexiflow-security:20260918`.
- 실행 이미지: `sha256:a3564c0890403a857e3da3575d73ca5c7b7062029bbdebd23fcbbd7d07e1bc16`.
- 서버 게시 아카이브 SHA-256:
  `125b19d4d9fc8705bdfadc0424f17b91fe071558f65df80f402d1991526891a9`.
- 기존 이미지: `sha256:86f2fbcd47ea6d15c4b1959e331f52073f4cf4f3d02c39b8e3be6032014114cf`.
- `.env`의 API_IMAGE를 새 로컬 이미지로 고정. `:latest`를 무심코 당겨 구버전으로 돌아가지 않게 함.
- 초도 전환 시에는 자동 배포를 끄고 로컬 이미지를 사용했으며, 아래 후속 정리에서 GitHub 배포로 일치시킵니다.
- 신규 클라우드 인스턴스, 유료 요금제, 유료 서비스는 생성하지 않음.

## GitHub 배포 정리

- 보안·학습 기능 소스: `ab23becfe7f659634c6898831274e332187f79c5`, main에 커밋·푸시.
- 검증: 서버 70개, 클라이언트 17개, 학습 22개, 배포 설정 4개(총 113개).
- [GitHub CI 실행 기록](https://github.com/kple1/LexiFlow/actions).
- `DEPLOY_ENABLED=true`와 `SECURITY_DEPLOY_READY=true`를 설정하여 자동 배포 재개.
  main push 또는 수동 실행 시 검사를 통과한 커밋 이미지로 배포.
- 새 `Server/deploy-image.py`는 기존 서버 비밀값을 보존하고, DB·설정 백업 후 커밋 SHA 이미지로 교체.
  DB 컨테이너 재생성·자동 스키마 변경 없이 HTTPS 200·익명 계정 접근 401을 검증.
  실패 시 직전 앱 이미지/설정만 복구하며 DB 백업을 자동으로 덮어쓰지 않음.
- 현재 이미지의 정확한 SHA는 운영 `.env`의 `API_IMAGE`와 백업 디렉터리의 `deployment.json`에 기록.
- 자동 배포용 동시 실행을 직렬화했고, 운영 구성 파일은 임시 경로에서 검증한 후 적용.
- 저장소에 원래 추적되던 Android `obj` 생성 파일 3개의 변경은 커밋에서 제외하고 로컬에 보존.

## 새 앱 파일

`C:\Users\Leepl\Documents\Codex\2026-09-17\dldj\artifacts\` 아래:

- `LexiFlow-Windows-Secure-1.1\LexiFlow.exe`: Windows x64 자체 포함 Release 빌드.
- `LexiFlow-Windows-Secure-1.1.zip`: 전체 폴더 배포용. 압축을 모두 풀고 실행.
- `LexiFlow-Android-Secure-1.1.apk`: Android arm64, versionCode 2, versionName 1.1.

APK 서명 검증 성공(v1/v2/v3), 기존 APK와 서명 인증서 일치 확인.
기존 설치 위에 업데이트 가능하도록 같은 개발용 서명을 유지했습니다.
**Android APK는 내부 테스트용이며 상용 스토어 출시용 키 서명은 별도 준비가 필요합니다.**
Windows Release 앱의 실제 실행·로그인 화면 렌더링을 확인했습니다.
로그인 입력은 computer-use 자동화 대상에서 제외되어 사용자가 직접 완료해야 합니다.
후속 확인에서 Windows 로그인·홈·학습 진입·Archive·재시작 세션 유지가 통과했습니다.
실제 학습 정답 제출과 Android 실기기 업데이트는 별도 확인이 남아 있습니다.

남은 상용화 보안: 휴대 가능한 암호화 복구 키의 별도 보관, 정기 재해 복구 훈련,
비밀번호 복구, 관리자 개별 계정/MFA, 보안 감사 로그·알림,
개인정보/기존 수동 백업 보존 정책, 외부 보안 점검 및 정식 앱 서명.
