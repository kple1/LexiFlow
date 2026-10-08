# LexiFlow DB 접속 안내

현재 저장소의 배포 설정은 Oracle VM의 Docker Compose, PostgreSQL 17, DB/역할 이름 `worddb`입니다. 앱은 HTTPS API만 사용하고 DB에 직접 접속하지 않습니다. `db:5432`는 컨테이너 네트워크 내부 주소이며 **호스트·인터넷에 5432 포트를 공개하지 않습니다**. 아래 경로는 CI 배포 경로 기준이며, 서버에서 실제 위치를 먼저 확인하세요.

## 가장 간단한 방법: SSH + 컨테이너 psql

내 PC의 터미널에서 본인이 보관한 Oracle SSH 키와 공인 IP를 사용합니다. `<…>`는 실제 값으로 바꿔야 하는 설명용 자리 표시자입니다. 키·DB 비밀번호를 채팅이나 Git에 올리지 마세요.

```powershell
ssh -i "<SSH 개인키의 절대 경로>" ubuntu@<Oracle VM 공인 IP>
```

접속 후 서버의 Linux 셸에서:

```bash
cd ~/LexiFlow/Server
docker compose ps db
docker compose exec -e PGOPTIONS='-c default_transaction_read_only=on -c statement_timeout=10000 -c lock_timeout=5000' db psql -X -U worddb -d worddb
```

Docker 권한이 없다면 서버 운영 권한이 있는 계정으로 실행해야 합니다. 임의로 Docker 소켓을 누구나 쓰게 변경하지 마세요. 배포가 별도 env 파일이나 Compose 프로젝트명을 쓰는 경우, 기존 배포 명령과 동일한 비밀 env 파일·프로젝트 옵션을 사용합니다. DB 컨테이너 내부의 로컬 소켓 접속입니다. 비밀번호가 요구되면 보호된 운영 비밀번호를 직접 입력하고 명령행에 붙이지 않습니다.

psql 안에서 확인:

```sql
\conninfo
SHOW default_transaction_read_only;
\dt
SELECT COUNT(*) AS word_count FROM "Words";
SELECT "Id", "English", "Meaning" FROM "Words" ORDER BY "English" LIMIT 10;
SELECT COUNT(*) AS account_count FROM "Users";
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
\q
```

테이블·컬럼은 EF의 대문자 이름이라 큰따옴표가 필요합니다. 랭킹 테이블은 신규 마이그레이션을 적용한 뒤에만 존재합니다. `Users` 전체 조회, 비밀번호 해시, 세션 토큰 해시, 인증메일 토큰·이메일 출력은 피하세요. 콘텐츠 수정은 관리자 패널을 사용해 API 검증을 유지하는 것이 좋습니다.

읽기 전용 기본 트랜잭션과 10초 제한은 실수 방지 장치입니다. **`worddb` 운영 역할 자체를 읽기 전용으로 제한하는 보안 경계는 아닙니다.** 해당 역할은 설정을 바꾸거나 DB를 수정할 수 있습니다. 상시 GUI 조회·다른 사람에게 접근 제공 시에는 승인된 테이블/컬럼에만 SELECT 가능한 전용 역할을 따로 설계해야 합니다. 이번 작업에서 역할·비밀번호·접근 권한은 변경하지 않았습니다. [PostgreSQL 읽기 전용·시간 제한 설정](https://www.postgresql.org/docs/17/runtime-config-client.html).

## GUI 도구를 쓰려면: SSH 터널

현재 구성에는 호스트의 `localhost:5432` 리스너가 없으므로 `ssh -L …:localhost:5432`를 그대로 쓰면 연결되지 않습니다. 서버에서 **현재 DB 컨테이너의 브리지 IP**를 확인해야 합니다.

```bash
docker compose ps -q db
docker inspect --format '{{range $name, $net := .NetworkSettings.Networks}}{{$name}} {{$net.IPAddress}}{{println}}{{end}}' <위에서 확인한 DB 컨테이너 ID>
```

API와 DB가 함께 사용하는 Compose 기본 네트워크의 IPv4를 선택합니다. 일반 `docker inspect` 전체 출력이나 `docker compose config`는 환경변수 비밀값을 노출할 수 있으므로 공유하지 마세요.

내 PC의 별도 터미널에서 (명령 실행 중 터미널을 유지):

```powershell
ssh -N -o ExitOnForwardFailure=yes -L 127.0.0.1:15432:<DB 컨테이너의 현재 IPv4>:5432 -i "<SSH 개인키의 절대 경로>" ubuntu@<Oracle VM 공인 IP>
```

GUI의 PostgreSQL 접속 대상은 호스트 `127.0.0.1`, 포트 `15432`, DB `worddb`입니다. 로그인은 승인된 DB 역할과 비밀번호를 사용합니다. 이 문서는 새 GUI 도구 설치나 새 역할 생성을 요구하지 않습니다. 전용 조회 역할을 만드는 것이 권장되며, 기존 운영 역할을 쓰면 수정 가능한 권한이 있다는 점을 기억하세요. 사용자 PC의 다른 프로세스도 해당 로컬 포트에 접근할 수 있으므로 작업이 끝나면 Ctrl+C로 터널을 닫습니다.

컨테이너를 재생성하면 IP가 바뀔 수 있어 다시 확인해야 합니다. SSH 서버의 TCP forwarding 허용 여부와 컨테이너의 실제 네트워크도 확인하세요. 이 구성을 위해 Oracle 보안 목록·방화벽에서 DB 포트를 열거나 Compose에 `5432:5432`를 추가하지 않습니다. [PostgreSQL 공식 SSH 터널 안내](https://www.postgresql.org/docs/17/ssh-tunnels.html).

## 주의

2026-10-08 랭킹 배포에서 기존 SSH 개인키 `C:/Users/Leepl/.ssh/id_ed25519`로 운영 VM에 접속하고 `/home/ubuntu/LexiFlow/Server`, `server-db-1`, `worddb` 및 마이그레이션 이력을 확인했습니다. 개인키 내용은 출력하지 않았습니다. 실제 IP는 변경될 수 있으니 현재 Oracle 인스턴스 정보를 확인하세요. 접속 오류가 나면 비밀번호·키·토큰을 제외한 오류 메시지를 확인합니다.

운영 DB를 개발/테스트 연결로 사용하지 말고 별도 테스트 DB를 사용하세요. `docker compose down -v`, 데이터 볼륨 삭제, 전체 테이블 삭제·초기화, 승인 없는 마이그레이션은 금지합니다. 변경이 필요하면 먼저 암호화 백업과 별도 DB 복원 검증을 진행합니다. [백업 운영 안내](BACKUP-RUNBOOK.md).
