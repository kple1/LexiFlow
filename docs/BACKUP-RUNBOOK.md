# 자동 암호화 백업 운영

2026-09-18 기존 Tokyo VM에 설치. 일일 백업과 상태 지표 전송은 PC/Codex 실행 여부와 무관하게 동작합니다.

## 일정과 범위

- 매일 **03:00 KST**, 최대 2분 분산 지연. VM이 꺼져 놓친 일정은 부팅 후 실행합니다.
- 같은 UTC 날짜에는 검증된 백업 하나만 생성하며 기존 객체를 덮어쓰지 않습니다.
- PostgreSQL `worddb`의 일관된 스냅샷을 덤프하고 별도 UUID 임시 DB에 실제 복원해 테이블 행 수와 제약조건을 검증합니다. 운영 DB에는 복원하지 않습니다.
- 검증한 덤프와 manifest만 AES-256-GCM CMS로 암호화합니다. RSA-3072/OAEP-SHA256 공개 인증서만 서버에 있습니다. `.env`는 이 일일 백업에 포함하지 않습니다.
- 비공개 OCI Standard 버킷 `lexiflow-encrypted-backups`의 `daily/`에 업로드한 뒤 다시 내려받아 SHA-256을 비교합니다.
- 클라우드: 해당 prefix 객체를 30일 후 삭제하는 lifecycle 활성화. 삭제 시각은 OCI lifecycle 처리 주기에 따릅니다.
- 서버: `/home/ubuntu/lexiflow-daily-backups`의 정확한 일일 파일명만 7일 후 정리합니다. 새 원격 백업 검증 성공 후에만 정리합니다.
- 기존 `/home/ubuntu/lexiflow-security-backups` 수동·배포 백업은 삭제 대상이 아닙니다. 별도 보존 정책이 필요합니다.
- 파일당 8 MiB 안전 제한, 디스크 여유 256 MiB 이상 필요. 초과 시 백업 실패로 처리하며 기존 백업을 삭제하지 않습니다.

## 접근 권한

- PAR은 `daily/` 읽기/쓰기, 목록/삭제 불가, **2026-12-17 13:00 KST 만료**. 14일 전부터 비정상 상태 지표를 보냅니다.
- PAR은 비밀번호와 같은 비밀값입니다. 소스/Git/로그/이메일에 넣지 마세요. 유출 시 재발급·폐기해야 합니다.
- 서버 설정: `/home/ubuntu/.config/lexiflow-backup/config.json`, 모드 0600. 디렉터리 0700.
- lifecycle 서비스 정책은 해당 버킷의 `BUCKET_INSPECT`, `BUCKET_READ`, `OBJECT_INSPECT`, `OBJECT_DELETE`만 허용합니다.
- 현재 VM 하나만 동적 그룹 `lexiflow-backup-monitor`에 포함됩니다. Instance Principal로 `lexiflow_backup` namespace의 `METRIC_WRITE`만 허용합니다. API 키를 추가하지 않습니다.

## 점검 및 경보

서버의 `lexiflow-backup-monitor.timer`는 5분마다 원격 백업 해시, 최신 백업 나이, 마지막 실행 결과, 타이머 상태, PAR 만료, 복구 인증서 검증 여부를 확인합니다. OCI에는 `BackupHealthy` 0/1과 고정 application 이름만 전송합니다. DB 내용/행 수/비밀번호/PAR은 전송하지 않습니다.

```sh
systemctl list-timers lexiflow-backup.timer lexiflow-backup-monitor.timer
systemctl status lexiflow-backup.service lexiflow-backup-monitor.service
python3 /home/ubuntu/LexiFlow/Server/backup/backup.py --check
journalctl -u lexiflow-backup.service -u lexiflow-backup-monitor.service --since today
```

클라우드 알림 토픽: `lexiflow-backup-alerts`. 이메일 구독은 사용자가 확인했고 OCI에서도 Active를 확인했습니다. 정상 시 주기적인 이메일은 보내지 않습니다.

- `lexiflow-backup-unhealthy`: `BackupHealthy[10m]{application = "lexiflow"}.groupBy(application).min() < 1`, trigger delay 1분. 백업 30시간 초과 지연 등 감지.
- `lexiflow-backup-heartbeat-missing`: `BackupHealthy[1h]{application = "lexiflow"}.groupBy(application).absent(72h)`, trigger delay 1분. 1시간 지표가 없으면 OCI가 감지합니다. 72시간은 최초 경보 대기 시간이 아니라 연속 부재 감지 기간입니다. 그 이후 자동 상태 해제만으로 복구됐다고 판단하지 말고 실제 최신 지표를 확인하세요.
- 백업 실패 시 원인 확인 후 `sudo systemctl start lexiflow-backup.service`. 같은 날 검증된 백업이 있으면 재생성하지 않고 원격 해시만 확인합니다.
- 비정상 종료 후 `.work-*` 또는 `lexiflow_verify_*`가 남았는지 점검하세요. 이름만 보고 광범위 삭제하지 말고 해당 실행이 만든 임시 자료인지 확인해야 합니다.
- 경보는 백업 감시 전용입니다. 애플리케이션 전체 가용성/보안 사건 감시를 대신하지 않습니다.

## 복구 키와 실제 복원

로컬 보호 디렉터리:
`C:\Users\Leepl\Documents\Codex\2026-09-17\dldj\artifacts\private-backups\oci-recovery`

`recovery-key.pkcs8.dpapi`는 **현재 Windows 사용자 프로필**에 종속됩니다. 이 파일만 다른 PC로 복사해서는 복구할 수 없습니다. 해당 PC/프로필을 잃으면 일일 암호화 백업을 복구하지 못할 수 있습니다.

따라서 사용자가 직접 PowerShell 7에서 다음을 실행하고, 암호화된 키와 공개 인증서를 별도 오프라인 매체/보호 저장소에 보관하세요. 암호는 채팅/명령 인수에 입력하지 않고 스크립트의 숨김 입력에서 설정하며 키와 별도로 보관합니다. 아직 이 휴대 가능한 키 보관은 완료되지 않았습니다.

```powershell
./Server/backup/export-recovery-key.ps1 -KeyDirectory 'C:/Users/Leepl/Documents/Codex/2026-09-17/dldj/artifacts/private-backups/oci-recovery' -OutputKey 'E:/private-recovery/lexiflow-recovery-key.encrypted.pem'
```

`E:/private-recovery`는 예시입니다. 실제 보호된 외부 저장 경로를 먼저 준비하세요. 스크립트는 기존 키 파일을 덮어쓰지 않고 암호화 후 재수입·공개키 일치를 검증합니다. 원래 DPAPI 키를 삭제하거나 인증서를 임의로 교체하지 마세요.

같은 Windows 프로필에서 암호화 백업 복호화:

```powershell
./Server/backup/decrypt-backup.ps1 -EncryptedBackup '<protected-path>/worddb-YYYY-MM-DD.tar.cms' -KeyDirectory '<protected-key-directory>' -OutputArchive '<protected-path>/recovery.tar.gz'
```

휴대용 키 사용 시 OpenSSL이 암호를 대화형으로 입력받게 합니다:

```sh
openssl cms -decrypt -binary -inform DER -in worddb-YYYY-MM-DD.tar.cms -recip recovery-cert.pem -inkey lexiflow-recovery-key.encrypted.pem -out recovery.tar.gz
```

복호화 결과는 개인정보입니다. 보호 경로에만 보관하고 검증 후 해당 임시 파일만 삭제하세요. 서버에서 별도 복원 검증을 하려면 파일을 보호된 `/home/ubuntu/lexiflow-daily-backups/recovery-check.tar.gz`에 전송하고 `verify-recovery.py`를 실행합니다. 이 검증은 운영 DB를 덮어쓰지 않으며 사용한 임시 파일과 임시 DB만 정리합니다.

실제 재해 복구는 새 PostgreSQL 환경에 복원 후 사용자/진도/인증 동작을 검증하고 승인된 전환 절차를 따릅니다. DB 비밀번호, 배포 설정, DNS/TLS, 관리자 비밀값은 별도 복구 자료가 필요합니다. 실행 중인 운영 DB에 자동 덮어쓰는 스크립트는 제공하지 않습니다.

## 설치 및 검증 기록

`Server/backup/lexiflow-backup*.service`와 `.timer`를 `/etc/systemd/system`에 설치했습니다. 모니터는 별도 venv의 `requirements-monitor.txt`에 고정된 OCI SDK를 사용합니다. 앱 컨테이너를 재시작하지 않았습니다.

- 첫 일일 백업: `worddb-2026-09-18.tar.cms`, 35,621 bytes.
- SHA-256: `3119be3fe1565b59298113b98e14fa7cf4eaed5731362dd8572821c08d447a5f`.
- 원격 업로드/다운로드 해시 일치, PC의 개인 키로 복호화, 별도 PostgreSQL DB 재복원 검증 통과.
- 검증 데이터: Users 4, WordProgresses 209, GrammarProgresses 8, Words 225, Grammars 33. 테스트 DB/평문 임시 파일 정리.
- 일일 백업 및 5분 상태 전송 systemd 활성화, 최초 상태 지표 게시 성공.
- 배포/백업 테스트 18개 통과. HTTPS `/health` 정상.
- 실패/수신 중단 경보 2개 활성화, 초기 상태 모두 OK. 이메일 구독 Active 확인, 테스트 메시지 발송 후 사용자가 메일 수신까지 확인했습니다(2026-09-18).
- 실제 운영 백업 중단이나 실패 지표 주입은 하지 않았습니다. 테스트 메일은 Notifications 전달 경로 검증이며, 경보 발화부터 이메일까지의 장애 모의시험은 별도입니다.

요금제 업그레이드는 하지 않았습니다. 현재 파일 크기와 30일 보관량은 매우 작지만 무료 한도는 계정 전체 사용량과 서비스 조건에 좌우되므로 비용 0을 보장하지 않습니다.

## 참고

- [OCI lifecycle](https://docs.oracle.com/en-us/iaas/Content/Object/Tasks/usinglifecyclepolicies.htm)
- [OCI 지표 최소 권한](https://docs.oracle.com/en-us/iaas/Content/Identity/policyreference/monitoringpolicyreference.htm)
- [OCI 지표 수신 중단 경보](https://docs.oracle.com/en-us/iaas/Content/Monitoring/Tasks/create-alarm-absence.htm)
