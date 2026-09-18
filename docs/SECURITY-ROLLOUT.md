# 보안 전환·백업 절차

운영 HTTPS·인증 전환은 2026-09-18 완료했습니다. [실제 검증·배포·백업 기록](SECURITY-DEPLOYMENT-STATUS.md).
아래는 신규 환경 및 향후 전환에도 사용할 수 있는 절차입니다.

이 문서는 로컬 보안 개편의 배포 절차입니다. **코드 수정·테스트만으로 운영 서버가 보호되지는 않습니다.**
실행 당시의 변경 사항과 남은 작업은 위 진행 기록을 확인하세요.
새 앱은 HTTPS와 서버 세션을 요구하므로 구버전 HTTP 서버와 로그인 호환되지 않습니다.

## 구현한 경계

- 로그인: 암호학적 난수 256비트 bearer 토큰, 7일 절대 만료. 서버에는 SHA-256 해시만 저장.
- 앱: SecureStorage에 세션 저장. 사용자 ID만 저장했던 구버전 세션은 자동 로그인 불가.
- 서버: 기본 인증 요구. 공개 콘텐츠·가입·로그인·상태 확인만 예외. 관리자 API는 독립된 X-Admin-Token 필터 적용.
- 사용자 조회·비밀번호 변경·탈퇴·세 종류 학습 기록에 본인 확인. 변경/탈퇴에는 현재 비밀번호 재확인.
- 로그아웃은 해당 토큰 폐기, 비밀번호 변경은 모든 토큰 폐기. 계정 삭제는 서버 기록과 세션도 삭제.
- 로그인 5회 실패 시 계정 15분 잠금. 가입/로그인/계정 변경은 IP당 10회/분, API는 IP당 120회/분, 동시 요청 16개 제한.
  IP 제한은 단일 프로세스 메모리 기준이며 재시작 시 초기화됨. 계정 실패/잠금은 DB에 저장.
- 신규 비밀번호: 12자 이상, BCrypt 제한에 맞춰 UTF-8 72바이트 이하. 기존 짧은 비밀번호는 로그인 가능.
  기존 계정도 외부 공개 전 강한 비밀번호로 변경 권장. 72바이트를 넘는 기존 비밀번호는 별도 복구 절차 필요.
- 앱의 인증서 검증 우회·HTTP·자동 리디렉션 제거. Android 평문 통신과 자동 백업 비활성화.
- 관리자 토큰은 32자 이상 필요하며 고정 시간 비교. 브라우저 영구/세션 저장 대신 페이지 메모리에만 보관.
- 계정별 로컬 데이터는 서버 숫자 ID 기준. 대소문자 변형·같은 이름 재가입으로 데이터 공유되지 않음.
  구버전 SecureStorage에 남은 마지막 사용자와 **정확히 동일한 계정이 로그인할 때만** 기존 Archive/코스/XP를 가져옴.
  나머지 예전 로컬 데이터는 자동으로 다른 계정에 붙이지 않고 그대로 보존. 구버전 전역 스트릭은 마지막 사용자에게만 이관.
- 탈퇴 UI는 서버와 현재 기기의 해당 계정 데이터를 삭제. 다른 기기의 오프라인 사본 및 운영 백업은 별도 삭제/보존 정책 필요.

## 로컬 검증

```powershell
dotnet run --project Tests/SecurityChecks
dotnet run --project Tests/ClientSecurityChecks
dotnet run --project Tests/LearningChecks
dotnet ef migrations has-pending-model-changes --project Server/WordApp/WordApp.csproj
dotnet list Server/WordApp/WordApp.csproj package --vulnerable --include-transitive
```

SecurityChecks는 실제 인증 미들웨어/컨트롤러를 TestServer와 격리 SQLite로 실행합니다.
ClientSecurityChecks는 HTTP·SecureStorage·Preferences의 메모리 대체 구현을 사용합니다.
실제 계정이나 운영 API에 요청하지 않습니다. PostgreSQL 마이그레이션·Caddy 인증서 발급·모바일 실기기 검증을 대체하지 않습니다.
로컬 PC에는 Docker가 없어, 실제 PostgreSQL 복원·마이그레이션과 Compose·HTTPS 검증은 운영 VM의 격리 리허설 환경에서 별도로 수행했습니다.

## 전환 전 확인

1. 운영 반영 승인을 받고 점검 시간을 정합니다. 기존 앱/서버와 새 버전을 혼용하지 않습니다.
2. GitHub 변수 SECURITY_DEPLOY_READY를 비활성 상태로 유지합니다. 자동 배포는
   DEPLOY_ENABLED와 SECURITY_DEPLOY_READY가 모두 true일 때만 실행됩니다.
3. 기존 DB 비밀번호와 Compose 프로젝트 이름/볼륨을 유지합니다. **docker compose down -v 금지.**
4. 보안 변경 이전에 노출됐을 가능성이 있는 관리자 토큰은 교체합니다. 실제 토큰을 Git/로그/문서에 넣지 않습니다.
5. 도메인이 대상 서버를 가리키는지 확인하고 80/443 공개, 기존 5276 직접 접속 차단을 준비합니다.
   PostgreSQL 5432는 계속 외부 비공개입니다. 이는 운영 방화벽 변경이므로 별도 적용합니다.
6. Compose의 edge 네트워크 172.30.250.0/24가 호스트의 기존 네트워크와 겹치지 않는지 확인합니다.
   변경 시 proxy 고정 주소(.2), API 고정 주소(.3), Security__TrustedProxy를 함께 변경합니다. 임의 클라이언트의 전달 헤더를 신뢰하지 않습니다.
7. API_DOMAIN이 앱의 HTTPS 호스트와 동일한지 확인합니다. 사용자 정의 도메인 사용 시 CI에서도 유지해야 합니다.

## 백업과 데이터 사전 점검

기존 Server 디렉터리에서, 접근 제한된 별도 백업 디렉터리로 실행합니다.

```sh
sh backup-db.sh /absolute/protected/lexiflow-backups
```

스크립트는 pg_dump custom 파일을 만들고 pg_restore --list로 아카이브를 확인합니다.
**빈 파일/명령 실패가 있으면 배포를 중단**하고, 별도 임시 DB에 복원해 행 개수와 로그인을 검증하세요.
백업은 개인정보이므로 외부 보호 저장소에 암호화해 복사하고, 보존 기간/삭제 책임자를 정합니다.
수동 복원 리허설에 이어 일일 자동 암호화 백업·보관 정책·이메일 경보도 구성했습니다.
현재 운영 환경은 [자동 백업 운영 절차](BACKUP-RUNBOOK.md)를 확인하세요.

다음 읽기 전용 SQL로 중복 로그인 ID가 없는지 확인합니다.

```sql
SELECT "UserId", COUNT(*) FROM "Users" GROUP BY "UserId" HAVING COUNT(*) > 1;
SELECT 'word' AS kind, p."UserId" FROM "WordProgresses" p LEFT JOIN "Users" u ON u."UserId" = p."UserId" WHERE u."Id" IS NULL
UNION ALL SELECT 'grammar', p."UserId" FROM "GrammarProgresses" p LEFT JOIN "Users" u ON u."UserId" = p."UserId" WHERE u."Id" IS NULL
UNION ALL SELECT 'idiom', p."UserId" FROM "IdiomProgresses" p LEFT JOIN "Users" u ON u."UserId" = p."UserId" WHERE u."Id" IS NULL;
```

신규 마이그레이션은 Users.UserId 고유 인덱스, 로그인 제한 필드, 세션 테이블, 진행도→사용자 외래 키를 추가합니다.
외래 키는 탈퇴와 동시에 도착한 기록 쓰기가 삭제된 계정의 데이터를 다시 만들지 못하게 합니다.
소유 계정이 없는 기존 진행도가 있으면 마이그레이션이 실패하므로, 운영자의 별도 보존/귀속 판단 전에는 적용하지 않습니다.
중복 ID가 있으면 실패하도록 두었으며 **계정을 자동 삭제하거나 병합하지 않습니다.**
중복이 발견되면 사용자의 데이터 귀속을 확인한 뒤 별도 계획으로 해결합니다.

## 승인 후 실제 전환

1. 빌드/테스트가 통과한 커밋 SHA 태그의 이미지를 준비하고 기존 이미지 태그/설정을 기록합니다.
2. docker-compose.yml과 Caddyfile을 함께 배치합니다. 기존 .env는 유지하고 권한을 600으로 제한합니다.
   DB_PASSWORD, NOTION_TOKEN, NOTION_WORDS_DATA_SOURCE_ID, ADMIN_TOKEN을 확인합니다.
3. 백업 검증 후 첫 전환에 한해 스키마 변경을 명시적으로 허용합니다.

```sh
export API_IMAGE=ghcr.io/kple1/lexiflow/wordapp:VERIFIED_COMMIT_SHA
docker compose pull
APPLY_MIGRATIONS=true docker compose up -d
```

4. Caddy가 HTTPS 인증서를 발급하고 /health가 200인지 확인합니다. API 컨테이너에는 공개 포트가 없어야 합니다.
5. /users/me 무토큰 요청은 401, 테스트 계정의 다른 계정 접근은 403인지 확인합니다.
   테스트 계정만 사용해 로그인·로그아웃·변경·삭제를 점검합니다. 운영 사용자 비밀번호를 테스트에 사용하지 않습니다.
6. 스키마 확인 후 자동 마이그레이션을 다시 끕니다.

```sh
APPLY_MIGRATIONS=false docker compose up -d api
```

7. 새 앱을 배포하고 재로그인을 안내합니다. HTTP 로그인 또는 인증서 검증 무시로 우회하지 않습니다.
8. 전환과 복원 리허설이 끝난 뒤에만 SECURITY_DEPLOY_READY=true를 설정합니다.
   이후 CI는 커밋 SHA 이미지와 HTTPS 구성을 배포하며 스키마 변경은 별도 승인합니다.

## 실패 시

- 인증서/프록시 실패: 신규 공개를 중단하고 도메인·80/443·볼륨 권한을 확인합니다.
- 중복 ID/마이그레이션 실패: DB 변경을 반복 강행하거나 데이터를 삭제하지 말고 사전 점검 결과를 확인합니다.
- 구버전으로 롤백하면 인증 없는 API가 돌아올 수 있습니다. 외부 접근을 차단한 상태에서만 복구합니다.
- 추가 컬럼/세션 테이블을 성급하게 다운 마이그레이션하지 않습니다. DB 복원은 현재 DB를 덮으므로 별도 승인 및 복원 계획이 필요합니다.

## 남은 상용화 보안 작업

운영 TLS/방화벽 검증, PostgreSQL 전환 리허설, 일일 암호화 백업과 초도 복원 검증은 완료했습니다.
휴대용 복구 키 별도 보관·정기 재해 복구 훈련, 비밀번호 분실 복구,
관리자 다중요소 인증/개별 계정, 보안 이벤트 감사·알림, 분산 환경 속도 제한,
개인정보/백업 보존 정책, 외부 보안 점검은 별도 작업입니다. 이 변경이 전체 보안 감사를 대신하지 않습니다.

## 참고

- [OWASP 세션 관리](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)
- [ASP.NET Core 프록시 신뢰 설정](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- [ASP.NET Core 요청 제한](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Caddy 자동 HTTPS](https://caddyserver.com/docs/automatic-https)
- [Microsoft.OpenApi 취약점과 수정 버전](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)
