# 인증메일 도메인 결정안 — 구매·연결 전

2026-09-18 사용자의 도메인/서비스 선택 위임에 따라 정한 구성입니다.
**아직 도메인을 소유하지 않으며 구매, DNS 변경, OCI 발송 자격증명 생성, 실제 발송은 하지 않았습니다.**

## 선택한 구성

| 항목 | 결정 |
| --- | --- |
| 등록할 도메인 | `getlexiflow.com` |
| 등록업체/DNS | Porkbun 기본 DNS — 추가 유료 DNS/호스팅 상품 제외 |
| 등록 기간 | 우선 1년; 결제 및 자동 갱신 조건은 사용자 확인 필요 |
| 인증메일 전용 도메인 | `mail.getlexiflow.com` |
| 발신 주소/표시명 | `security@mail.getlexiflow.com` / LexiFlow |
| 발송 서비스 | 기존 OCI 계정의 도쿄 Email Delivery, SMTP 587 STARTTLS |
| 앱/API/메일 링크 | 기존 `https://lexiflow.duckdns.org` 유지 |
| 발송 상한 | 앱 전체 일 50회 / 월 1,000회 시도, 수신자별 제한도 유지 |

`get` 접두사로 앱 이름을 유지하고, 메일 전용 하위 도메인을 사용해 향후 웹사이트나 고객지원 메일 설정과 분리합니다.
이는 운영 구성 선택이며 상표 권리 확보를 뜻하지 않습니다. 공개 상용 출시 전 이름 충돌 검토가 별도로 필요합니다.

## 구매 전 확인

- 2026-09-18 Verisign 공식 RDAP 조회에서 `getlexiflow.com`은 HTTP 404였습니다. 조회 시점에 등록 레코드가 없다는 뜻이며, 구매 가능·예약 가능·비프리미엄 가격을 보장하지 않습니다.
- Porkbun의 일반 `.com` 공개 기본 가격은 조회 시 연 US$11.08입니다. 해당 도메인의 실제 장바구니 가격, 갱신 가격, 결제 통화/수수료는 결제 직전에 다시 확인합니다.
- 계획 예산은 **첫 1년 총 US$15 이내**로 제안합니다. 아직 지출 승인은 받지 않았으며, 프리미엄 도메인·부가상품·다년 등록·자동 갱신은 임의로 결제하지 않습니다.
- 사용자 소유 계정으로 등록하고 MFA/복구 정보를 비밀번호 관리자에 보관합니다. 카드·계정 비밀번호는 채팅에 입력하지 않습니다.

## 구매 후 연결 순서

1. 도메인 소유권과 DNS 관리 접근 확인. 현재 DuckDNS/API 주소는 변경하지 않습니다.
2. 도쿄 OCI Email Delivery에 `mail.getlexiflow.com` 이메일 도메인 생성.
3. OCI가 제공하는 **실제 DKIM selector/CNAME 값**을 DNS에 등록하고 Active 상태 확인. 값을 추정하거나 예제 값을 그대로 등록하지 않습니다.
4. 콘솔의 도쿄 리전 안내에 맞춰 SPF 구성. 기존 SPF와 중복 TXT를 만들지 않습니다.
5. `_dmarc.mail.getlexiflow.com`은 수신처가 없는 보고 주소를 임의로 넣지 않고, 초기 `p=none`으로 전달·정렬 결과를 검증한 후 강화합니다.
6. 승인된 발신자는 `security@mail.getlexiflow.com` 한 주소로 제한. 전용 SMTP 발송 자격증명을 최소 권한으로 만들고 보호된 서버 환경파일에만 저장합니다.
7. 계정의 실제 Email Delivery 할당량/과금 조건 확인. 앱 한도는 OCI 계정 전체 과금을 차단하는 장치가 아닙니다. 유료 플랜 전환이나 한도 상향은 하지 않습니다.
8. 테스트 주소로 인증메일 수신, SPF/DKIM/DMARC 결과, 링크 가입/복구, 재사용 차단을 검증한 후 배포 게이트를 엽니다.

OCI Email Delivery는 수신함 서비스가 아닙니다. 위 발신 주소가 답장을 받는다고 안내하지 않습니다.
공개 출시 전 고객지원 수신 경로와 개인정보 처리 정책도 별도로 준비해야 합니다.
기존 Gmail 백업 경보 수신 주소를 고객용 발신/공개 연락처로 자동 전용하지 않습니다.

배포·DB 검증은 [계정 복구 운영 절차](ACCOUNT-RECOVERY.md)를 따릅니다.

## 확인한 공식 자료

- [Porkbun .com 가격](https://porkbun.com/tld/com)
- [Porkbun 등록·갱신 가격표](https://porkbun.com/products/domains)
- [Verisign RDAP 조회](https://rdap.verisign.com/com/v1/domain/getlexiflow.com)
- [OCI 이메일 도메인과 DNS 인증](https://docs.oracle.com/en-us/iaas/Content/Email/Reference/gettingstarted_topic-create-email-domain.htm)
- [OCI SMTP 연결](https://docs.oracle.com/en-us/iaas/Content/Email/Reference/gettingstarted_topic-Configure_the_SMTP_connection.htm)
