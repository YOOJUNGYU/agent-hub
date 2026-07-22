# 콘솔 상단 접속 경로 다중 표시 (LAN + VPN) — 설계

날짜: 2026-07-22

## 배경 / 문제

PC 콘솔 상단은 현재 접속 주소를 **하나(사설망 LAN IPv4)** 만 표시한다
(예: `https://192.168.246.201:47600`). 하지만 백엔드는 이미 NetBird·Tailscale
같은 오버레이 VPN의 CGNAT 대역(`100.64.0.0/10`) IP도 수집해 인증서 SAN에 넣고
있다. 사용자가 다른 네트워크에서 붙으려면 이 VPN 주소가 필요한데 콘솔에 드러나지
않아, 어떤 주소로 접속해야 하는지 알기 어렵다.

## 목표

콘솔 상단에 접속 가능한 경로를 **각 주소별 설명 label과 함께 여러 줄로** 표시한다.

```
🟢 서버 활성
  https://192.168.0.10:47600   [사설망(LAN)]
  https://100.79.56.13:47600   [NetBird]
```

## 결정 사항

1. **Label 식별**: 네트워크 어댑터 이름(`NetworkInterface.Name`+`Description`)으로
   `NetBird`/`Tailscale`을 실제 식별한다. 식별 실패 시 일반 `VPN`. LAN은 `사설망(LAN)`.
   (NetBird·Tailscale 모두 `100.64.0.0/10`을 써 IP만으로는 구분 불가하므로 어댑터명 사용.)
2. **표시 범위**: 실제 LAN + VPN만. VirtualBox·VMware·Hyper-V·WSL·Docker 등
   가상 어댑터의 사설 IP는 폰에서 닿지 않으므로 어댑터명 키워드로 제외한다.
   (VPN 어댑터는 CGNAT 분기에서 처리되므로 이 제외 대상에 걸리지 않는다.)
3. **label 문자열은 클라이언트 i18n에 둔다**: 서버는 `kind`(`lan`/`netbird`/`tailscale`/`vpn`)만
   내려주고, ko/en label은 `i18n.js` 단일 소스에서 매핑한다.
4. **하위호환**: 기존 `Url`(대표=첫 LAN)은 그대로 두고 `Endpoints[]`를 추가한다.
   `endpoints`가 비었거나 서버가 옛 필드만 주면 host.js가 `url` 한 줄로 폴백한다.

## 변경 범위

- `Common/Models/ServerStatusInfo.cs` — `List<EndpointInfo> Endpoints` 추가,
  `EndpointInfo { Url, Kind }` 신규.
- `Server/EmbedIOServer.cs`
  - `GetLocalEndpoints()` 신규 — `(ip, kind)` 목록 반환(LAN 먼저, VPN 뒤).
    LAN은 가상 어댑터 키워드 제외, CGNAT는 어댑터명으로 vendor 식별.
  - `GetPrivateIPv4List()`는 `GetLocalEndpoints().Select(ip)`로 재정의(인증서 SAN·CurrentHost 그대로).
  - `CurrentEndpoints` 정적 속성(시작 시 `CurrentPort`로 URL 조립해 저장) 추가.
- `Server/Controller/ApiController.cs` — `/server/status`에 `Endpoints = CurrentEndpoints` 채움.
- `View/Htmls/host.html` — `#serverUrl`(단일 앵커) → `#serverUrls`(목록 컨테이너).
- `View/Htmls/js/host.js` — `refreshStatus()`가 `endpoints`를 주소+label 배지로 렌더, 폴백 포함.
- `View/Htmls/js/i18n.js` — `endpoint.lan/netbird/tailscale/vpn` (ko/en).
- `View/Htmls/css/app.css` — `.server-urls`/`.ep`/`.ep-label` 스타일.
- `docs/index.html` — 콘솔이 LAN·VPN 주소를 label과 함께 함께 보여준다는 점 반영(가이드 동기화).

## 비목표 / 한계

- 실행 중 VPN이 새로 올라와도 즉시 반영하지 않는다(시작 시 1회 계산 — `CurrentHost`·인증서와
  동일 정책, 가이드도 "VPN 나중에 깔면 재시작" 안내). 
- 가상 어댑터 제외·vendor 식별은 어댑터명 휴리스틱이라 드물게 오분류 가능. 실 LAN이
  실수로 빠지지 않도록 제외 키워드는 보수적으로 유지.
- IPv6는 표시하지 않는다(기존과 동일, IPv4만).
