# SQL Server → PostgreSQL 이관

GCP에서 Oracle Cloud로 서버를 옮기면서 DB를 SQL Server에서 PostgreSQL로 바꿨다.
이유는 메모리다. SQL Server 컨테이너 하나가 최소 2GB를 요구해서 4GB짜리 인스턴스(월 $35 수준)가 필요했는데,
Postgres는 1GB 인스턴스로도 충분하다. 단어장 앱 규모에서 SQL Server를 쓸 이유가 없었다.

## 코드 변경

| 파일 | 변경 |
| --- | --- |
| `WordApp.csproj` | `Microsoft.EntityFrameworkCore.SqlServer` → `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `Program.cs` | `UseSqlServer` → `UseNpgsql` |
| `Migrations/` | SQL Server용 6개를 지우고 Postgres용 초기 마이그레이션 1개로 재생성 |
| `docker-compose.yml` | mssql → `postgres:17-alpine`, 연결 문자열/헬스체크 변경, **1433 호스트 포트 노출 제거** |
| `.env` | `DB_SA_PASSWORD` → `DB_PASSWORD` |

EF 마이그레이션은 제공자별로 SQL이 다르게 생성되므로 그대로 쓸 수 없다.
새 DB가 빈 상태로 시작하니 초기 마이그레이션 하나로 합치는 게 맞다.

`NotionService`에서 `created_time`을 `.ToUniversalTime()` 하도록 했다.
Npgsql은 `timestamptz` 컬럼에 UTC가 아닌 `DateTime`이 들어오면 예외를 던진다.

## 데이터 이관

`Words`(225)는 **옮기지 않는다.** `WordSyncService`가 Notion에서 다시 채운다.
`Grammars`는 관리자 패널 전용 데이터라 자동 동기화되지 않으므로 반드시 옮긴다.

옮겨야 하는 데이터는 다음과 같다:

- `Users` — 계정 3건 (BCrypt 해시)
- `WordProgresses` — 학습 진도 186건
- `Grammars` — 문법 33건

`Id`는 옮기지 않고 Postgres가 새로 매기게 둔다. 진도 테이블은 `Users.Id`(int)가 아니라
`UserId`(로그인 문자열)로 사용자를 참조하므로 정수 키가 바뀌어도 관계가 깨지지 않는다.

### 1) 구 서버(SQL Server)에서 INSERT 문 추출

```bash
cd ~/LexiFlow/Server
set -a; . ./.env; set +a

Q="SET NOCOUNT ON;
SELECT 'INSERT INTO \"Users\" (\"UserId\",\"Pw\") VALUES ('''+REPLACE(UserId,'''','''''')+''','''+REPLACE(Pw,'''','''''')+''');' FROM Users;
SELECT 'INSERT INTO \"WordProgresses\" (\"UserId\",\"WordId\",\"Status\",\"CorrectCount\",\"WrongCount\",\"LastReviewed\",\"UpdatedAt\") VALUES ('''
 +REPLACE(UserId,'''','''''')+''','''+REPLACE(WordId,'''','''''')+''','''+REPLACE(Status,'''','''''')+''','
 +CAST(CorrectCount AS varchar(11))+','+CAST(WrongCount AS varchar(11))+','
 +CASE WHEN LastReviewed IS NULL THEN 'NULL' ELSE ''''+CONVERT(varchar(33),LastReviewed,126)+'Z''' END+','
 +''''+CONVERT(varchar(33),UpdatedAt,126)+'Z'');' FROM WordProgresses;
SELECT 'INSERT INTO \"Grammars\" (\"Id\",\"Title\",\"Category\",\"Example\",\"Explanation\",\"Note\",\"Status\") VALUES ('''
 +REPLACE(ISNULL(Id,''),'''','''''')+''','''+REPLACE(ISNULL(Title,''),'''','''''')+''','''
 +REPLACE(ISNULL(Category,''),'''','''''')+''','''+REPLACE(ISNULL(Example,''),'''','''''')+''','''
 +REPLACE(ISNULL(Explanation,''),'''','''''')+''','''+REPLACE(ISNULL(Note,''),'''','''''')+''','''
 +REPLACE(ISNULL(Status,''),'''','''''')+''');' FROM Grammars;"

docker exec server-db-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DB_SA_PASSWORD" -C -d WordDb \
  -y 0 -Q "$Q" | sed 's/[[:space:]]*$//' | grep '^INSERT' > data_migration.sql
```

`CONVERT(..., 126)`은 ISO8601로 뽑고, 뒤에 `Z`를 붙여 Postgres가 UTC로 읽게 한다.
붙이지 않으면 서버 로컬 타임존으로 해석되어 시각이 밀린다.

### 2) 새 서버에서 적용

API를 먼저 한 번 띄워 마이그레이션으로 스키마를 만든 뒤에 넣어야 한다.

```bash
docker compose up -d
docker compose logs -f api   # 마이그레이션 완료 확인 후 Ctrl+C

docker compose cp data_migration.sql db:/tmp/data.sql
docker compose exec db psql -U worddb -d worddb -v ON_ERROR_STOP=1 -f /tmp/data.sql
```

### 3) 검증

```bash
docker compose exec db psql -U worddb -d worddb -c \
  'SELECT (SELECT count(*) FROM "Users") AS users,
          (SELECT count(*) FROM "WordProgresses") AS progress,
          (SELECT count(*) FROM "Grammars") AS grammars;'
```

`users=3`, `progress=186`, `grammars=33`이면 성공이다. `Words`는 Notion 동기화가 돌면 채워진다.
