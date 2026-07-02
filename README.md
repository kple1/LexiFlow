# LexiFlow

> **Lexicon + Flow** — 머릿속 단어들이 자연스럽게 흘러나오는 상태.

Notion을 데이터 원본으로 사용하는 풀스택 영어 단어 학습 앱입니다. Notion에서 단어를 관리하면, 서버가 자동으로 동기화하여 모바일 앱에 표시합니다.

<p align="left">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="MAUI" src="https://img.shields.io/badge/.NET_MAUI-Client-512BD4">
  <img alt="ASP.NET Core" src="https://img.shields.io/badge/ASP.NET_Core-Web_API-512BD4">
  <img alt="SQL Server" src="https://img.shields.io/badge/SQL_Server-2022-CC2927?logo=microsoftsqlserver&logoColor=white">
  <img alt="Docker" src="https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white">
  <img alt="Google Cloud" src="https://img.shields.io/badge/Google_Cloud-Deployed-4285F4?logo=googlecloud&logoColor=white">
</p>

---

## ✨ 주요 특징

- **Notion을 단어장으로** — 익숙한 Notion에서 단어를 추가/편집하면 앱에 자동 반영
- **자동 동기화** — 서버가 Notion 변경사항을 주기적으로 감지하여 DB에 반영
- **크로스플랫폼 앱** — .NET MAUI로 Android / Windows 지원
- **컨테이너 배포** — Docker Compose로 API와 DB를 한 번에 실행
- **클라우드 호스팅** — Google Cloud에 배포되어 어디서든 접속 가능
- **REST API** — 표준 HTTP 인터페이스로 클라이언트와 통신

---

## 🏗️ 아키텍처

데이터는 한 방향으로 흐릅니다. 각 계층은 자신의 역할만 수행합니다. 서버와 데이터베이스는 **Google Cloud**에 컨테이너로 배포되어 있습니다.

```
                        ┌─────────────────────────────────────────────┐
                        │                Google Cloud                 │
                        │                                             │
  ┌─────────────┐       │   ┌──────────────────┐      ┌─────────────┐ │
  │   Notion    │ ──────┼──▶│  ASP.NET Core    │─────▶│   MSSQL     │ │
  │ (단어 원본) │polling │   │  Web API (서버)  │EFCore│  (WordDb)   │ │
  └─────────────┘(10초)  │   └──────────────────┘upsert└─────────────┘ │
                        │            │  [Docker Compose]               │
                        └────────────┼────────────────────────────────┘
                                     │  GET /words (REST / JSON)
                                     │  ▲ 인터넷을 통해 어디서든 접속
                                     ▼
                            ┌──────────────────┐
                            │   .NET MAUI 앱   │
                            │   (클라이언트)   │
                            └──────────────────┘
```

| 계층 | 역할 | 위치 |
| --- | --- | --- |
| **Notion** | 데이터 원본. 사용자가 단어를 관리하는 곳 | 외부 SaaS |
| **서버 (Web API)** | Notion을 읽어 DB에 저장하고, 앱에 REST API 제공 | Google Cloud |
| **MSSQL** | 서버가 읽고 쓰는 저장소 | Google Cloud |
| **MAUI 앱** | 서버 API를 호출해 단어를 화면에 표시 | 사용자 기기 |

> **설계 원칙:** 앱은 오직 서버의 REST API만 호출합니다. 앱이 DB나 Notion에 직접 접근하지 않아, 데이터 흐름이 단순하고 각 계층이 독립적입니다.

> **클라우드 배포의 이점:** 서버가 클라우드에서 24시간 실행되므로, 개인 PC를 켜두지 않아도 됩니다. 앱은 와이파이·LTE 등 네트워크 환경과 무관하게 언제 어디서든 서버에 접속할 수 있습니다.

### 동기화 방식

Notion API는 변경 알림(webhook)을 제공하지 않습니다. 따라서 서버가 `BackgroundService`로 **주기적으로 Notion을 조회(polling)** 하고, `last_edited_time`을 기준으로 변경된 단어를 감지하여 DB에 반영합니다. Notion에서 삭제된 단어는 DB에서도 제거됩니다.

---

## 🛠️ 기술 스택

**클라이언트**
- .NET MAUI 10.0 — 크로스플랫폼 UI
- HttpClient — REST API 통신

**서버**
- ASP.NET Core Web API — REST 엔드포인트
- Entity Framework Core — ORM
- BackgroundService — Notion 동기화 워커
- Notion API — 데이터 소스 연동

**데이터 / 인프라**
- SQL Server 2022 — 데이터 저장
- Docker & Docker Compose — 컨테이너 배포

---

## 📁 프로젝트 구조

```
LexiFlow/
├── Server/                      # ASP.NET Core Web API
│   ├── WordApp/
│   │   ├── Controllers/
│   │   │   └── WordsController.cs    # GET /words 엔드포인트
│   │   ├── Services/
│   │   │   ├── NotionService.cs      # Notion API 호출 + 파싱
│   │   │   └── WordSyncService.cs    # 주기적 동기화 (BackgroundService)
│   │   ├── Data/
│   │   │   └── AppDbContext.cs       # EF Core DbContext
│   │   ├── Models/
│   │   │   └── Word.cs               # 단어 엔티티
│   │   └── Program.cs                # 앱 시작점 (DI, 미들웨어)
│   ├── Dockerfile                    # 멀티스테이지 빌드
│   └── docker-compose.yml            # API + SQL Server
│
└── Application/                 # .NET MAUI 클라이언트
    └── LexiFlow/
        ├── Services/
        │   └── ApiService.cs         # 서버 API 호출
        ├── Views/                    # UI 페이지
        ├── Models/
        └── Platforms/Android/
            └── AndroidManifest.xml   # 권한, 네트워크 설정
```

---

## 🔌 API

| 메서드 | 엔드포인트 | 설명 |
| --- | --- | --- |
| `GET` | `/words` | 전체 단어 목록 조회 |
| `GET` | `/words?status={status}` | 상태별 필터링 (외움 / 애매 / 모름 / 미분류) |

**응답 예시**

```json
[
  {
    "id": "39021ce8-5244-...",
    "english": "allocate",
    "meaning": "할당하다, 배분하다",
    "status": "미분류",
    "example": "The manager will allocate resources to each team. (관리자가 각 팀에 자원을 할당할 것이다.)"
  }
]
```

---

## 🚀 시작하기

### 사전 요구사항

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (컨테이너 실행 시)
- Notion API 토큰 및 데이터베이스

### 1. 서버 실행 (Docker — 권장)

Docker Compose를 사용하면 API 서버와 SQL Server를 한 번에 실행합니다. 대상 머신에 SQL Server를 설치할 필요가 없습니다.

```bash
cd Server
docker compose up --build
```

- API: `http://localhost:5276`
- SQL Server: `localhost:1433`

동작 확인:

```bash
curl http://localhost:5276/words
```

> 환경 변수(연결 문자열, Notion 토큰 등)는 `docker-compose.yml`에서 설정합니다.

### 2. 서버 실행 (로컬 — Docker 없이)

로컬에 SQL Server가 설치되어 있다면:

```bash
cd Server/WordApp
dotnet run --urls "http://0.0.0.0:5276"
```

> 외부 기기(모바일)에서 접근하려면 `localhost`가 아닌 `0.0.0.0`에 바인딩해야 합니다.

### 3. 클라이언트 실행

`ApiService.cs`에서 실행 환경에 맞게 서버 주소를 설정합니다.

```csharp
string baseUrl =
#if ANDROID
    "http://10.0.2.2:5276/";   // Android 에뮬레이터 → 호스트 PC
#else
    "http://localhost:5276/";  // Windows 데스크톱
#endif
```

| 실행 환경 | 서버 주소 |
| --- | --- |
| Windows 데스크톱 | `localhost` |
| Android 에뮬레이터 | `10.0.2.2` |
| 실기기 / 원격 접속 | PC의 LAN IP 또는 배포된 서버 IP |

```bash
cd Application/LexiFlow

# Windows
dotnet build -t:Run -f net10.0-windows10.0.19041.0

# Android (에뮬레이터 또는 연결된 기기)
dotnet build -t:Run -f net10.0-android
```

### 4. Android APK 빌드 (배포용)

```bash
dotnet publish -f net10.0-android -c Release
```

서명된 APK가 생성됩니다:

```
bin/Release/net10.0-android/publish/io.leeple.lexiflow-Signed.apk
```

> 실기기에 사이드로딩 시, 삼성 기기는 **설정 → 보안 → 자동 차단**을 해제해야 설치됩니다.

### 5. Windows 배포용 빌드

Windows는 실행 파일과 의존 DLL을 담은 **폴더 형태**로 배포합니다.

```bash
dotnet publish -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None
```

결과물 위치:

```
bin/Release/net10.0-windows10.0.19041.0/publish/
```

이 폴더 안의 `LexiFlow.exe`가 실행 파일입니다. **폴더 전체를 압축(zip)하여 배포**하고, 받는 쪽에서 압축을 푼 뒤 `LexiFlow.exe`를 실행합니다. (`.exe` 단독으로는 의존 DLL이 없어 실행되지 않습니다.)

> **참고**
> - 실행하려면 대상 PC에 **.NET 10 데스크톱 런타임**이 필요합니다.
> - MAUI Windows는 단일 파일 배포(`PublishSingleFile`)가 불안정하므로, 폴더 배포를 권장합니다.

---

## ⚙️ 설정

`docker-compose.yml` 또는 `appsettings.json`에서 다음을 설정합니다.

| 항목 | 설명 |
| --- | --- |
| `ConnectionStrings:Default` | SQL Server 연결 문자열 |
| Notion API 토큰 | Notion 통합(integration) 토큰 |
| Notion Data Source ID | 단어 데이터베이스 식별자 |
| 동기화 주기 | 기본 10초 |

> Docker 환경에서는 연결 문자열의 서버 주소로 서비스 이름(`Server=db`)을 사용합니다. 컨테이너 간에는 서비스 이름으로 통신합니다.

---

## ☁️ 배포 (Google Cloud)

이 프로젝트는 Docker 컨테이너로 패키징되어 **Google Cloud**에 배포됩니다. 서버와 데이터베이스가 클라우드에서 상시 실행되므로, 클라이언트 앱은 네트워크 환경과 무관하게 언제든 접속할 수 있습니다.

**배포 흐름**

```
로컬 개발  →  docker compose 검증  →  이미지 빌드  →  Google Cloud 배포  →  앱이 클라우드 서버에 접속
```

**로컬에서 먼저 검증하는 이유**

로컬에서 `docker compose up`으로 완전히 동작하는 것을 확인한 뒤 클라우드에 올립니다. 로컬에서 검증된 동일한 컨테이너 이미지가 클라우드에서도 그대로 실행되므로, "로컬에선 되는데 서버에선 안 되는" 문제를 최소화합니다.

**클라이언트 설정**

배포 후, 앱의 `ApiService.cs`에서 `BaseAddress`를 배포된 서버 주소로 지정합니다.

```csharp
_http = new HttpClient(handler)
{
    BaseAddress = new Uri("http://<배포된-서버-IP>:<포트>/")
};
```

> **네트워크 확인 팁:** 앱을 다시 빌드하기 전에, 기기의 브라우저에서 `http://<서버-IP>:<포트>/words`에 접속해 JSON이 반환되는지 먼저 확인하세요. 이 한 번의 테스트로 문제가 네트워크에 있는지 앱 코드에 있는지 빠르게 구분할 수 있습니다.

---

## 📄 라이선스

이 프로젝트는 학습 목적으로 제작되었습니다.

---

<p align="center">
  <sub>Built with .NET MAUI · ASP.NET Core · SQL Server · Docker · Google Cloud</sub>
</p>