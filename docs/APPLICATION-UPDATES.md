# Windows 앱 업데이트

[2026-10-08 배포·검증 기록](UPDATE-DEPLOYMENT-STATUS.md)

## 사용 방법

1. 1.5 이하에는 업데이터가 없으므로 **업데이터 포함 1.6 EXE로 한 번만 교체**합니다. `LexiFlow.exe` 이름을 유지하고 본인에게 쓰기 권한이 있는 로컬 폴더에서 실행합니다. 네트워크 공유·심볼릭 링크·reparse point 경로는 지원하지 않습니다.
2. `나 → 앱 업데이트` 또는 로그인 전 화면의 업데이트 카드에서 버전을 확인합니다. 앱 시작 시에도 한 번 확인합니다. 서버에 접속하지 못하면 기존 앱을 계속 사용할 수 있습니다.
3. 새 버전이 있으면 `다운로드`를 누릅니다. 전체 EXE를 받으며 진행률과 중단 버튼을 제공합니다. 검증된 다운로드는 앱을 다시 열어도 재사용합니다(적용 요청을 이미 만든 경우 제외).
4. 학습·계정 작업을 마친 뒤 `재시작해 적용`을 누르고 확인합니다. **진행 중이지만 저장되지 않은 답안까지 보존하는 기능은 아닙니다.** 같은 경로의 앱을 여러 개 실행했다면 다른 창부터 닫습니다.

앱의 Publisher/Package ID·저장소 이름을 바꾸지 않고 EXE만 교체합니다. 업데이터는 로그인·ChatGPT 토큰·Preferences·Archive·학습 진도·서버 DB를 읽거나 삭제·재작성하지 않습니다. 새 앱은 기존 세션 복원 및 루트 페이지 생성 후 시작 완료를 알립니다. 실제 계정 작업으로 세션 갱신이 발생하는 것은 기존 인증 동작입니다.

## 검증과 복구 범위

- 배포 주소: `https://lexiflow.duckdns.org/updates/windows-x64/latest.json`. 로그인이나 인증 헤더·쿠키 없이 공개 아티팩트만 제공합니다.
- JSON envelope의 원문 payload에 ECDSA P-256/SHA-256 서명을 붙입니다. 앱에 고정된 공개키로 서명을 확인하고 제품·Windows x64·버전·UTC 발행/만료·크기·정확한 HTTPS 다운로드 경로를 검사합니다. JSON 중복·알 수 없는 필드·다운그레이드·리다이렉트는 거절합니다.
- 다운로드는 최대 512 MiB, 실제 길이·SHA-256·EXE 표식 검증을 거칩니다. 적용 직전에도 서명·만료·파일 해시를 다시 확인합니다. PC 시간이 크게 틀리면 검증이 실패할 수 있습니다.
- 현재 EXE 복사본을 보조 프로세스로 실행하며 부모 PID·시작 시간·경로·기존 EXE 해시가 모두 일치해야 준비를 알립니다. 부모가 종료될 때까지 기다리며 부모를 강제 종료하지 않습니다.
- 같은 디스크의 `File.Replace`로 원자적으로 교체하고 `LexiFlow.exe.lexiflow-previous-<transaction>`에 이전 파일을 보관합니다. 90초 안에 새 앱의 시작 완료가 확인되지 않으면 보조 프로세스가 자신이 띄운 새 앱만 종료하고 이전 파일 복구를 시도합니다.
- 이 자동 복구는 **시작 실패 대응**입니다. 시작 후에 발견되는 학습 버그, 저장소 마이그레이션 되돌리기, OS/디스크 손상, 백신 차단을 완전히 해결하는 것은 아닙니다. 잠금이나 권한 때문에 복구하지 못하면 이전 EXE를 보존하며 수동 복구가 필요합니다.
- 사용자 선택 없이 다운로드·재시작하지 않습니다. Windows Authenticode 서명·유료 인증서는 추가하지 않았습니다. **앱의 배포 서명과 Windows SmartScreen 신뢰는 별개**이며 보안 경고를 자동으로 우회하지 않습니다.

캐시: `%LOCALAPPDATA%\LexiFlow\ClientUpdates\<transaction>`. 완료된 업데이트는 최근 1개 복구본을 유지하고 다음 확인 때 더 오래된 완료 캐시·검증된 이전 파일만 정리합니다. 실패/미완료 캐시는 자동 정리 대상이 아니므로 장기 사용 시 용량을 확인합니다. 수동 정리는 모든 LexiFlow/업데이터 프로세스를 종료하고 상태·대상 경로를 확인한 뒤 해당 트랜잭션의 파일만 수행합니다. 학습 데이터 폴더를 지우지 않습니다.

전체 업데이트가 약 300 MiB라 다운로드·보조 EXE·기존 EXE·교체 준비 파일까지 추가 공간이 필요합니다. 다운로드 디스크에는 파일 크기의 4배+100 MiB, EXE 디스크에는 2배+100 MiB 이상의 여유 공간을 검사합니다. 단일 파일 자체 추출을 위한 .NET/Windows App SDK 캐시는 별도로 공간을 사용합니다. 델타 업데이트·설치 프로그램·작업 스케줄러·관리자 권한 요청은 추가하지 않았습니다.

## 릴리스 제작

공개 버전은 3자리 숫자 `major.minor.patch`로 단조 증가시킵니다. 이미 공개한 버전의 EXE를 다른 파일로 덮어쓰지 않습니다. 클라이언트 릴리스만 만들며 API 이미지·DB 마이그레이션은 별도 승인 절차입니다. 현재 미배포 이메일 가입·복구 코드를 업데이트 때문에 서버에 적용하면 안 됩니다.

```powershell
dotnet publish Application/LexiFlow/LexiFlow.csproj -c Release `
  -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 `
  -r win-x64 --self-contained true -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true `
  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
  -p:DebugType=None -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false `
  -o <fresh-public-artifact-folder>

dotnet run --project Tools/UpdateRelease -c Release -- sign <protected-key.dpapi> <LexiFlow.exe> <version> <fresh-latest.json>
dotnet run --project Tools/UpdateRelease -c Release -- verify <latest.json> <current-version>
```

서명 도구는 EXE 파일 버전이 지정한 버전과 같은지 확인하고, 서명된 파일 크기·SHA-256·180일 만료 시각을 포함합니다. 기존 출력 JSON을 덮어쓰지 않습니다. 서명 생성 후 도구에서도 **프로덕션 앱의 고정 공개키**로 검증하므로 다른 개인키를 실수로 사용한 배포는 차단합니다.

서명 개인키는 제작 PC의 Windows 사용자 DPAPI로 보호한 `release-key.dpapi`입니다. Git·Oracle 서버·공개 다운로드에 올리지 말고 채팅/로그로 출력하지 않습니다. 파일만 다른 PC에 복사해도 복호화할 수 없으며 Windows 사용자 프로필/DPAPI 복구 체계를 포함한 별도 비밀 백업이 필요합니다. 프로필 삭제 전 반드시 복구 계획을 확인합니다. 키가 유실되면 기존 클라이언트는 새 키를 신뢰하지 못하므로 수동 부트스트랩 교체가 필요합니다. 키가 유출되면 배포 중지·호스팅 차단·신뢰키 교체 및 수동 재배포가 필요하며 현재 프로토콜에 자동 키 회전은 없습니다. `keygen`은 최초 키 생성용으로만 사용합니다.

## Oracle 공개 파일 배포

새로운 `/home/ubuntu/lexiflow-update-deploy/release-<unique-label>`에 `LexiFlow.exe`, `latest.json`, `Server/Caddyfile`, `Tools/UpdateRelease/deploy.py` **4개 공개 파일만** 올립니다. 개인키·`.env`·사용자 DB·토큰은 올리지 않습니다. PC에서 서명 검증이 성공한 피드만 사용합니다.

```sh
python3 /home/ubuntu/lexiflow-update-deploy/release-<unique-label>/deploy.py <version> <verified-sha256>
```

배포 도구는 아티팩트·manifest 크기/버전/해시를 다시 검사하고 기존 피드보다 새 버전만 허용합니다. `/home/ubuntu/LexiFlow/Server/public-updates/windows-x64/<version>/LexiFlow.exe`를 생성한 뒤 `latest.json`을 원자적으로 승격합니다. 최초 연결 때만 기존 compose의 프록시 볼륨 줄에 읽기 전용 정적 경로를 추가하고 Caddy 설정을 검증한 뒤 `docker compose up -d --no-deps proxy`를 실행합니다. 다음 릴리스는 프록시 설정이 같으면 재생성하지 않습니다.

원본 compose/Caddyfile은 각 staging의 `original-config`에 보관합니다. HTTPS `/health` 200, 익명 `/ranking` 401, 피드의 바이트 일치, EXE HEAD 길이, API·DB 컨테이너 ID 불변을 확인합니다. 실패하면 프록시 설정/이전 피드를 복구하며 DB와 API는 재배포하지 않습니다. 서버는 개인키를 갖지 않으며 암호학적 검증은 제작 PC와 클라이언트가 담당합니다. 파일 게시만으로 Windows 신뢰·정상 앱 동작이 보장되는 것은 아닙니다.

마지막으로 PC에서 피드를 새로 받아 서명 검증 및 **실제 HTTPS 다운로드 해시**를 확인하고, 이전 버전 앱에서 확인/다운로드/재시작을 점검합니다.

```powershell
dotnet run --project Tools/UpdateRelease -c Release -- download-check <freshly-fetched-latest.json> <current-version> <fresh-roundtrip.exe>
```

공개 EXE는 용량과 트래픽을 사용합니다. 기존 Oracle 서버를 재사용하며 별도 유료 서비스를 추가하지 않았지만, 사용량 증가에 따른 비용이 없다고 보장하지 않습니다. 서버의 옛 공개 버전·staging은 자동 삭제하지 않습니다. 관리자에게 릴리스 보존/디스크 점검 책임이 있습니다. 서명 피드는 만료 전에 후속 버전으로 갱신해야 하며 만료 피드는 앱에서 차단합니다.

## 검사

```powershell
dotnet run --project Tests/UpdateChecks -c Release
dotnet run --project Tests/UpdateHostChecks -c Release -- --run-helper-checks <absolute-path-to-Tests/UpdateHostChecks/UpdateHostChecks.csproj>
python -m unittest discover -s Tests/DeploymentChecks -p test_update_release.py -v
```

기본 53개 검사는 임시 키·가짜 HTTP·격리 파일로 서명·엄격한 JSON·URL·만료·다운그레이드·다운로드 실패·파일 교체/복구를 검사합니다. 실제 Windows helper 14개 검사는 임시 공개키를 **테스트 전용 바이너리에만** 컴파일하여 정상 시작/시작 전 강제 실패를 재현합니다. 프로덕션 키나 사용자 저장소는 사용하지 않습니다. 배포 7개 검사는 가짜 서버 디렉터리/명령으로 프록시만 변경·기존 릴리스 보존·서버 검사 실패 시 복구를 확인합니다. 기본 검사는 Linux CI에도 연결되며 Windows helper 검사는 Windows에서 별도로 실행합니다.
