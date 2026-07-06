# 기기 등록·인증 시스템 설계

> 작성일: 2026-07-06
> 대상: Agent Hub (WinForms 트레이 앱 + EmbedIO 내장 웹서버)

## 배경 / 문제

현재 서버는 `https://+:{port}`(모든 인터페이스)에 바인딩되어 LAN의 **모든 기기가 인증 없이** 모니터 화면(`/`)과 에이전트 데이터에 접속할 수 있다. 승인된 기기만 접속하도록 기기 등록·인증 체계를 도입한다.

요구사항 요약:
1. 모바일이 최초 접속 시 "인증 요청" 화면을 보여준다.
2. 요청 시 PC의 AgentHub가 **실시간으로** 인증 요청을 받는다.
3. 승인된 기기만 접속(에이전트 데이터 수신)이 가능하다.
4. 승인 해제 또는 등록 삭제 시 해당 기기의 접속이 다시 차단된다.
5. 별도 DB 없음 → 저장은 파일 기반으로 처리한다.

## 결정 사항 (Q&A로 확정)

- **인증 방식**: 클라이언트 생성 토큰(UUID). 브라우저/웹서버로는 MAC·하드웨어 고유 ID를 신뢰성 있게 얻을 수 없음(브라우저 프라이버시 정책, MAC 랜덤화, DHCP, EmbedIO 미지원)이 확인되어 토큰 방식 채택.
- **승인 권한**: PC 전용(loopback). `/host` 콘솔과 승인·해제·삭제 API는 127.0.0.1 요청만 허용.
- **요청 알림**: 호스트 콘솔의 실시간 목록(`/ws/host`) + 트레이 풍선알림(`ShowBalloonTip`).
- **기기 이름**: 모바일이 요청 시 별칭 입력(선택). 비우면 IP·User-Agent로 표시.
- **저장소**: JSON 파일 (아래 참조) — 사용자 위임 결정.

## 인증 모델

- 모바일 최초 접속 시 브라우저에서 랜덤 토큰(UUID)을 생성해 `localStorage`(키: `agenthub.deviceToken`)에 저장. 이 토큰이 기기의 신원(bearer credential)이다.
- 서버는 토큰 **원문을 저장하지 않고 SHA-256 해시**와 상태만 저장한다. 파일에 재사용 가능한 비밀이 남지 않는다.
- 토큰 전송 경로:
  - REST: 헤더 `X-Device-Token`
  - WebSocket: 쿼리 파라미터 `?token=` (브라우저 WS는 커스텀 헤더를 설정할 수 없으므로)
- 원문 토큰·해시는 **어떤 클라이언트에도 전송하지 않는다.** 호스트 콘솔에는 공개 식별자 `Id`만 노출한다.

## 저장소

- 파일: `%LOCALAPPDATA%\AgentHub\devices.json`
  - user.config와 같은 사용자 영역이라 설치 위치(예: Program Files)와 무관하게 항상 쓰기 가능. 가장 범용적·안정적.
- 런타임 소스: `ConcurrentDictionary`(thread-safe, 기존 `MonitorClientRegistry` 패턴과 동일).
- 영속화: 변경 시마다 **원자적 쓰기**(임시 파일에 기록 후 `File.Replace`/이동). 앱 시작 시 파일 로드. 파일이 없거나 손상 시 빈 목록으로 시작.

### 기기 레코드 (`Device`)

| 필드 | 설명 |
|------|------|
| `Id` | 공개 GUID. 승인/해제/삭제 API에서 대상 지정에 사용. |
| `TokenHash` | 토큰 SHA-256 해시(비밀). 조회 키. 클라이언트에 미전송. |
| `Name` | 사용자가 입력한 별칭(선택). |
| `Ip` | 요청 시점의 원격 IP. |
| `UserAgent` | 요청 시점의 User-Agent. |
| `Status` | `pending` / `approved` / `revoked`. |
| `RequestedAt` | 최초 요청 시각(ISO 8601 UTC). |
| `ApprovedAt` | 승인 시각. |
| `LastSeenAt` | 마지막 접속 시각. |

## 승인 권한: PC 전용 (loopback)

- 데스크톱 WebView2를 `https://127.0.0.1:{port}/host`로 로드하도록 변경. 인증서 SAN에 loopback이 이미 포함됨. 서버 인증서 오류 핸들러는 `AlwaysAllow`로 설정되어 있음.
- 표시·모바일용 `CurrentUrl`(LAN IP)은 그대로 두고, loopback용 `LocalUrl`을 추가.
- `/host` 페이지 및 승인·해제·삭제 API, 기기 목록 API는 **loopback이 아닌 요청을 403으로 거부**(`IPAddress.IsLoopback`로 판별). LAN 기기가 스스로 승인하는 것을 방지.

## 데이터 흐름

```
모바일 최초 접속:
  GET / (index.html·app.js는 게이트 없음 — 항상 로드)
  → app.js가 /ws/agents?token=XXX 연결
  → 서버가 토큰 상태 판별 후 push
     · 미등록/revoked → {type:"auth", status:"none"}    → [요청 화면] (별칭 입력 + "인증 요청 보내기")
     · pending        → {type:"auth", status:"pending"}  → [대기 화면]
     · approved       → {type:"auth", status:"approved"} + {type:"agents", ...} → [모니터 화면]

요청:
  "인증 요청 보내기" → POST /api/devices/request {name} (헤더 X-Device-Token)
  → 서버: pending 레코드 생성·영속화 → DeviceRegistry.Changed 발생
     · /ws/host로 기기 목록 broadcast (콘솔 실시간 갱신)
     · FormMain이 트레이 풍선알림(ShowBalloonTip) 표시

승인 (PC 콘솔):
  POST /api/devices/{id}/approve (loopback 전용)
  → status=approved 영속화 → 해당 토큰의 /ws/agents 소켓으로 {type:"auth",status:"approved"} + 스냅샷 push
  → 모바일 즉시 모니터 화면 전환

해제/삭제 (PC 콘솔):
  POST /api/devices/{id}/revoke  또는  DELETE /api/devices/{id} (loopback 전용)
  → 영속화 → 해당 소켓으로 {type:"auth",status:"revoked"} push 후 모니터 차단
  → 모바일 즉시 요청/대기 화면으로 복귀
```

## 게이트 지점

- **게이트함**: `/ws/agents`(승인된 토큰만 에이전트 데이터 수신 및 `MonitorClientRegistry` 등록), `GET /api/agents`(승인만).
- **게이트 안 함**: 정적 자산(`/`, `/css`, `/js`) — 요청 화면이 떠야 하므로.
- **loopback 전용**: `/host`, `GET /api/devices`(목록), approve/revoke/delete.

## 엔드포인트

### 모바일용
- `WS /ws/agents?token=` — 인증 상태 + 에이전트 데이터 실시간(주 채널). 승인된 경우에만 `MonitorClientRegistry`에 등록.
- `POST /api/devices/request` (헤더 `X-Device-Token`, body `{ name }`) — pending 레코드 생성. 이미 있으면 정보 갱신. 반환 `{ status: "pending" }`.
- `GET /api/agents` (헤더 `X-Device-Token`) — 승인된 기기용 스냅샷 폴백. 미승인 시 401/pending.

### 호스트용 (loopback 전용)
- `GET /api/devices` — 전체 기기 목록(토큰/해시 제외). 콘솔 폴백.
- `POST /api/devices/{id}/approve` — 승인.
- `POST /api/devices/{id}/revoke` — 승인 해제.
- `DELETE /api/devices/{id}` — 등록 삭제.

### WebSocket (호스트)
- `WS /ws/host` — 기존 `clients`(라이브 접속) 유지 + `{type:"devices", devices:[...]}` broadcast 추가. `DeviceRegistry.Changed` 구독.

## 신규/변경 컴포넌트

### 신규
- `Common/Models/Device.cs` — 기기 레코드 모델.
- `Server/Devices/DeviceRegistry.cs` — 영속 저장 + 상태 관리 + `Changed` 이벤트 + 토큰 해시 조회 + Id 조회. JSON 로드/저장 포함(원자적 쓰기).

### 변경
- `Server/Controller/ApiController.cs` — 신규 엔드포인트 + loopback 가드.
- `Server/Socket/AgentMonitorModule.cs` — 토큰 검사, auth 상태 push, 토큰↔소켓 매핑(승인 시 대상 소켓에 push).
- `Server/Socket/HostMonitorModule.cs` — devices 목록 broadcast.
- `Server/EmbedIOServer.cs` — loopback용 `LocalUrl` 추가, `/host` 라우트 loopback 가드.
- `View/Forms/FormMain.cs` — WebView2를 `127.0.0.1`로 로드, 신규 요청 시 풍선알림.
- 프론트: `View/Htmls/index.html` + `js/app.js`(요청/대기/모니터 상태 화면), `View/Htmls/host.html` + `js/host.js`(인증 대기·등록 기기 관리 UI + 승인/해제/삭제), `css/app.css`(추가 스타일).

### 불변
- 서드파티 `EmbedIO/`는 수정하지 않는다.

## 테스트 / 검증

- 빌드 통과:
  ```powershell
  msbuild AgentHub.sln /t:Restore
  msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
  ```
- 수동 시나리오:
  1. 미등록 기기 접속 → 요청 화면 표시.
  2. 별칭 입력 후 요청 → PC 콘솔 대기 목록 실시간 표시 + 트레이 풍선알림.
  3. 승인 → 모바일이 즉시 모니터 화면으로 전환.
  4. 해제 → 모바일 즉시 차단(요청/대기 화면 복귀).
  5. 재승인 → 복원.
  6. 삭제 → 차단, 재접속 시 요청 화면.
  7. LAN 기기에서 `/host` 접근 및 approve API 호출 → 403.
  8. 앱 재시작 후 승인 상태가 파일에서 복원되는지 확인.

## 설계 근거 / 기각한 대안

- **서버 발급 비밀 토큰**: 대기 중 기기에 토큰을 전달할 별도 채널이 필요해 복잡. LAN 환경에서 클라이언트 토큰으로 충분.
- **MAC / IP 기반 식별**: MAC 랜덤화·서브넷 한정·DHCP로 불안정. 브라우저는 하드웨어 ID 미노출.
- **DB / SQLite**: 요구사항상 DB 없음. 소규모 기기 목록에는 JSON 파일이 더 단순·범용.
