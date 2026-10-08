# 이메일 인증·비밀번호 복구 — 운영 반영 전

2026-09-18 로컬 구현. **운영 서버에는 아직 배포하지 않았고 실제 인증메일도 발송하지 않았습니다.**
사용자가 선택한 방향은 전용 소유 도메인 준비 후 Oracle Email Delivery 연결입니다.
도메인 이름/등록, DNS 인증, 전용 발송 자격증명, 실제 수신 검증이 필요합니다.
기존 OCI Notifications 백업 경보와 별도 서비스입니다.
도메인·등록업체·발신 주소 결정안은 [인증메일 도메인 계획](EMAIL-DOMAIN-PLAN.md)에 정리했습니다. 구매는 아직 하지 않았습니다.

## 사용자 흐름

- 신규 가입: 앱의 ‘새 계정 만들기’ → 브라우저에서 이메일 입력 → 메일 링크 → 아이디·비밀번호 설정 → 앱 로그인.
  이메일을 확인하기 전에는 계정이나 비밀번호를 미리 생성하지 않습니다.
- 기존 계정: 현재 ID/비밀번호로 계속 로그인 → ‘나 / 계정 보안’에서 복구 이메일과 현재 비밀번호 입력 → 메일에서 직접 확인 → 다시 로그인.
- 비밀번호 찾기: 로그인 화면 → 인증된 복구 이메일 → 메일 링크에서 새 비밀번호 입력 → 앱 로그인.
- 복구 이메일 변경: 기존 이메일은 새 이메일을 확인할 때까지 유지됩니다. 확인 후 이전 이메일에 변경 알림을 시도합니다.
- 이메일 등록 전 비밀번호를 잊은 기존 계정은 이 기능으로 복구할 수 없습니다. 운영자가 이메일을 임의 배정하거나 인증을 우회하지 마세요.

## 보안 경계

- 256-bit 난수 토큰, DB에는 SHA-256 해시만 저장. 가입/이메일 연결 30분, 비밀번호 재설정 15분 만료.
- 용도·대상 계정·이메일·SecurityStamp 바인딩. 조건부 상태 갱신과 토큰 DELETE를 같은 트랜잭션으로 묶어 한 번만 사용합니다.
- 이메일 연결/재설정은 계정 행을 먼저 갱신하고 토큰을 소비해 다른 비밀번호 변경과 잠금 순서를 일치시킵니다. 토큰 만료/소비 실패 시 계정 갱신도 롤백합니다.
- 가입 후 다른 가입 링크는 이메일 유일성 제약으로 사용할 수 없으며 만료 정리 시 제거됩니다. 동시에 처리 중인 다른 가입 링크를 삭제하다 서로 대기하지 않도록 가입 완료 때 다른 토큰을 일괄 삭제하지 않습니다.
- 인증/재설정 완료와 일반 비밀번호 변경 시 다른 미사용 링크 및 모든 기기 세션을 폐기합니다.
- GET 요청/메일 링크를 열기만 하는 것으로 상태가 바뀌지 않습니다. 사용자가 확인 버튼을 눌러 POST 해야 합니다.
- 토큰은 URL 쿼리가 아닌 fragment에 넣고 페이지 로드 즉시 주소창에서 제거합니다. 브라우저 저장소나 로그에 쓰지 않습니다.
- 메일 링크 출처는 서버 설정의 고정 HTTPS origin만 사용하며 요청 Host/return URL을 신뢰하지 않습니다.
- 비밀번호 확인은 앱의 마스킹 입력을 사용하며 서버에서는 BCrypt 정책(12자 이상, UTF-8 72바이트 이하)을 유지합니다.
- 인증메일 등록/변경 요청은 인증된 세션과 현재 비밀번호가 필요합니다. 요청 본문의 사용자 ID는 대상 선택에 사용하지 않습니다.
- 익명 메일 요청은 DB 조회/SMTP 전송을 백그라운드에서 처리하고, 존재·미존재·발송 제한 여부와 무관하게 동일한 202 응답을 보냅니다.
- 새 계정은 메일 소유 증명 후에만 생성됩니다. 기존 `/users` 가입 API는 410으로 차단하여 구버전 앱에서 인증을 우회하지 못하게 합니다.
- 이메일 정책: ASCII 주소, 대소문자 무시 유일성, 원래 주소 표기로 발송. 점/plus 태그를 제거하거나 별칭을 추정하지 않습니다. 국제화 주소는 현재 지원하지 않습니다.
- SMTP는 MailKit, 인증 필수, 587 STARTTLS 또는 465 TLS만 사용. 인증서 검증 우회/프로토콜 로그 없음.

## 발송 제한과 운영상 한계

- 계정 이메일 API: IP당 분당 6회, 기존 API 전역 제한도 적용.
- 메일 요청: 수신 주소별 UTC 분당 1회, 시간당 3회, 일당 5회.
- 서비스 전체: UTC 일당 50회, 월당 1,000회 **발송 시도** 상한. 실패도 소모합니다.
  사용자 요청으로 발생한 비밀번호/이메일 변경 안내는 수신자 요청 cooldown에서는 제외하되 전체 한도에는 포함됩니다.
- DB 원자적 카운터로 재시작 후에도 상한 유지. 메일 발송 계정의 다른 앱 사용량이나 공급자 과금 전체를 제한하는 기능은 아닙니다.
- 128개 상한의 메모리 작업 큐. 재시작/큐 포화 시 요청은 유실될 수 있어 잠시 후 재요청해야 합니다. 원문 토큰을 디스크 outbox에 저장하지 않습니다.
- SMTP 실패 시 해당 토큰을 폐기합니다. SMTP 응답이 불확실한 경우 도착한 메일도 무효일 수 있으므로 재요청해야 합니다.
- 만료된 토큰/발송 카운터는 활성 메일 작업 처리 시 정리합니다. 개인정보 보존 정책 및 주기적 정리 작업은 공개 출시 전 추가 검토합니다.
- 발송 실패/한도 초과는 주소나 토큰을 제외한 서버 로그로 남깁니다. 전용 메일 장애 경보, 발송 실패 내역 UI, CAPTCHA, 보안 감사 대시보드는 아직 없습니다.
- 변경 안내는 best-effort입니다. 재시작/발송 한도/SMTP 장애로 누락될 수 있으므로 전달 보장으로 간주하지 않습니다.
- 로컬 PostgreSQL 17에서 마이그레이션/동시 요청을 검증했습니다. 직접 메일함 수신, 실제 운영 환경에서의 점검 및 앱→메일→재로그인 실기기 검증은 배포 전에 별도로 해야 합니다.

## 설정

기본값 `ACCOUNT_EMAIL_ENABLED=false`. 아래 항목을 서버의 보호된 `.env`에 설정합니다. SMTP 비밀번호를 채팅/소스/GitHub 로그에 넣지 마세요.

```dotenv
ACCOUNT_EMAIL_ENABLED=false
ACCOUNT_EMAIL_HOST=<Oracle 콘솔의 해당 리전 SMTP endpoint>
ACCOUNT_EMAIL_PORT=587
ACCOUNT_EMAIL_USERNAME=<전용 최소권한 발송 자격증명>
ACCOUNT_EMAIL_PASSWORD=<보호된 위치에서 직접 입력>
ACCOUNT_EMAIL_FROM=<소유 도메인의 승인된 발신 주소>
```

메일의 링크 origin은 `https://${API_DOMAIN}/`입니다. 발신 도메인을 준비하더라도 현재 API 도메인은 그대로 유지할 수 있습니다.
올바른 SMTP 설정이 없는 상태에서 Enabled를 true로 켜면 서버가 시작되지 않습니다.
테스트 이메일 송신 대체물은 테스트 프로젝트에만 있으며 운영에서 토큰을 로그로 보여 주는 우회 모드는 없습니다.

## 배포 순서 — 자동 배포 주의

1. 소유 도메인 등록 및 DNS 접근 준비. 사용자가 구매 조건을 승인하기 전 구매/유료 전환 금지.
2. OCI Email Delivery 도메인/DKIM/SPF 및 승인된 발신 주소 구성, 최소 권한 SMTP 자격증명 보호 저장.
3. 별도 PostgreSQL DB에서 새 마이그레이션과 인증·재설정/동시 제출 검증(로컬 완료, CI에도 추가). 전환 직전 현재 운영 DB의 새 백업과 복원 확인은 별도로 수행.
4. 새 앱 배포와 서버 전환을 조율. 기존 앱 로그인은 호환되지만 신규 가입에는 새 앱이 필요합니다.
5. 새 서버 코드에는 `20260918050320_AddVerifiedRecoveryEmail` 마이그레이션이 필요합니다.
   기존 사용자 값은 nullable로 추가하여 유지하며 사용자 삭제/비밀번호 교체는 하지 않습니다.
   자동 배포는 마이그레이션을 실행하지 않습니다. 이번 변경에는 기존 두 조건에 더해 `ACCOUNT_RECOVERY_DEPLOY_READY == 'true'` 배포 게이트를 추가했습니다.
   이 저장소 변수는 아직 설정하지 않았습니다. 미설정/false이면 빌드·테스트·이미지 게시만 가능하고 운영 deploy 작업은 건너뜁니다. 준비 없이 true로 설정하지 마세요.
6. 승인된 점검 창에서 마이그레이션을 명시적으로 적용하고 이메일 설정 후 새 이미지/앱 반영. 자동 마이그레이션은 다시 꺼 둡니다.
7. SMTP 수신·스키마·새 앱 준비와 전환 절차를 확인한 뒤 별도 승인으로 배포 게이트를 true로 설정합니다. 실제 테스트 주소로 가입·이메일 연결·비밀번호 찾기·세션 폐기를 검증하고 테스트 계정만 정리합니다.
8. 실패 시 신규 기능 활성화를 중단하고 원인을 확인합니다. DB 다운 마이그레이션/운영 덮어쓰기 복원은 별도 승인 없이 실행하지 않습니다.

## 검증 명령

```powershell
dotnet run --project Tests/SecurityChecks -c Release
dotnet run --project Tests/ClientSecurityChecks -c Release
dotnet run --project Tests/LearningChecks -c Release
python -m unittest discover -s Tests/DeploymentChecks -p 'test_*.py'
dotnet ef migrations has-pending-model-changes --project Server/WordApp/WordApp.csproj
dotnet list Server/WordApp/WordApp.csproj package --vulnerable --include-transitive
```

기본 보안 테스트는 격리 SQLite와 메모리 송신 대체물을 사용합니다. CI는 별도의 PostgreSQL 17 서비스에서도 동일 검사를 실행합니다.
브라우저 UI 시험도 로컬 mock이며 실제 SMTP 전달이나 운영 환경 시험을 대체하지 않습니다.

PostgreSQL 재현용으로 **운영과 무관한 폐기 가능한 로컬 클러스터**를 준비한 뒤 아래처럼 실행합니다.
접속 정보는 `127.0.0.1`, 기본 포트가 아닌 포트, `postgres` 관리 DB, `lexiflow_test` 전용 역할만 허용합니다.
각 fixture가 임의 이름의 `lexiflow_recovery_test_*` DB를 만들고 종료 시 자신이 만든 DB만 삭제합니다. 기존 DB를 초기화하는 코드는 없습니다.

```powershell
$env:LEXIFLOW_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=lexiflow_test'
dotnet run --project Tests/SecurityChecks -c Release
# 마이그레이션/동시성 부분만 재실행
dotnet run --project Tests/SecurityChecks -c Release -- --postgres-recovery-only
Remove-Item Env:LEXIFLOW_TEST_POSTGRES
```

인증을 사용하는 시험 DB의 비밀번호는 보호된 환경 설정으로 제공합니다. 위 무비밀번호 예제는 이 PC의 일회성 loopback 전용 시험 클러스터에서만 사용했습니다.
CI 서비스의 `disposable-ci-test-only` 값도 격리 테스트 전용이며 실제 서버/SMTP에 재사용하지 않습니다.

### 2026-09-18 로컬 검증 결과

- 기본 자동 검사 193개 통과: SQLite 서버 보안 132, 클라이언트 보안 19, 학습 22, 배포 20.
- PostgreSQL 17.11에서도 서버 보안 153개 통과(같은 132개 + PostgreSQL 전용 21개). 전용 검사는 별도로 재실행해 통과했습니다.
- 기존 스키마 → 새 스키마 변경 시 계정/비밀번호/세션/학습 기록 보존, 마이그레이션 재실행, 중복·동시 복구/가입/이메일 연결, 앱 내 변경과 복구의 경합, 동시 발송 상한을 확인했습니다.
- 서로 다른 복구 토큰을 동시에 소비할 때 PostgreSQL deadlock이 재현되어, 계정 → 토큰 순으로 잠금을 통일했습니다. 수정 후 전체 PostgreSQL 검사를 통과했습니다.
- CI에 같은 PostgreSQL 검사를 추가하고, 배포 준비 변수 미설정 시 운영 배포를 막는 조건을 추가했습니다. 이 CI 변경 자체는 아직 push/원격 실행하지 않았습니다.
- 서버 Release 빌드: 오류 0, 경고 0. Windows/Android Release 빌드도 오류 없이 완료했으며 기존 nullable/MVVM 경고는 남아 있습니다.
- EF 모델과 마이그레이션 snapshot 일치. NuGet 취약점 검사에서 보고된 취약 패키지 없음.
- 로컬 브라우저 mock에서 가입 요청·비밀번호 불일치·가입 완료·이메일 확인·재설정 오류·메뉴 전환을 확인했습니다. 링크 토큰의 주소창 제거와 비밀번호 입력 초기화도 확인했습니다.
- 실제 메일 발송, 운영 배포 및 새 앱 배포는 아직 하지 않았습니다. 이 결과는 상용 보안 감사를 대신하지 않습니다.

## 참고

- [OWASP 비밀번호 복구](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html)
- [OWASP 이메일 확인](https://cheatsheetseries.owasp.org/cheatsheets/Email_Validation_and_Verification_Cheat_Sheet.html)
- [OCI 이메일 도메인 설정](https://docs.oracle.com/en-us/iaas/Content/Email/Tasks/configureemaildomains.htm)
