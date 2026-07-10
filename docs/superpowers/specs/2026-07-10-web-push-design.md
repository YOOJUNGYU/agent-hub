# 앱 종료 상태 알림(Web Push) 설계 — payload-less VAPID-only

작성일: 2026-07-10

## 목표 / 범위(Scope A: 알림 전용)
PWA가 **꺼져 있거나 백그라운드**여서 WebSocket이 끊긴 상태에서도, 세션이 사용자 응답을 기다릴 때 **푸시 알림**으로 알린다. 알림을 탭하면 앱이 열리고, 어느 세션인지는 앱의 카드(응답 대기중)로 확인한다. **답변 전달 자체는 기존 흐름**(창 내 재오픈 / 만료 시 터미널·PC 안내)이 담당한다. PC 단독 UX에 영향 없음(질문 훅 게이트/블로킹 로직 불변).

## 핵심 결정
- **payload-less VAPID-only**: 푸시 본문 암호화(aes128gcm) 없이 VAPID 인증(ES256 JWT)만 사용. net48 내장 `ECDsa`(P-256)로 구현 → **NuGet/BouncyCastle 불필요**(net48엔 AesGcm 부재, 외부 의존 복원 위험 회피). 알림 문구는 서비스워커가 생성하는 일반 문구.
- **미연결 기기에만 전송**: 서버는 현재 `/ws/agents` WS로 연결돼 있지 **않은** 승인 기기의 구독에만 푸시 → 연결된 기기는 기존 인앱 알림으로 처리(중복 없음).
- **태그 병합**: SW 알림 tag 고정(`agenthub-ask`, `renotify:true`) → 여러 이벤트가 하나의 알림으로 갱신(스팸 방지). payload가 없어 세션 구분 불가하므로 일반 문구 1건.

## 구성요소
- `Server/Push/Vapid.cs`: 최초 1회 P-256 키쌍 생성 → `%LocalAppData%\AgentHub\push-vapid.json`(base64url D/X/Y) 영속. `PublicKeyBase64Url`(0x04‖X‖Y), `AuthorizationHeader(endpoint)`= `vapid t=<JWT(aud=endpoint origin, exp=now+12h, sub=mailto)>, k=<pub>`.
- `Server/Push/PushSubscriptionRegistry.cs`: tokenHash → {endpoint, p256dh, auth} 저장, `push-subs.json` 영속.
- `Server/Push/PushService.cs`: `NotifyDisconnected(project, message, sessionId)` — 승인+미연결 구독에 payload-less POST(HttpWebRequest, TLS1.2, TTL 3600). 201/200=성공, 404/410=구독 삭제, 그 외 로깅. 백그라운드(fire-and-forget).
- `AgentMonitorModule.IsConnected(hash)` + `AgentMonitorService.IsDeviceConnected(hash)`: 연결 여부 판별.

## HTTP 엔드포인트(ApiController, loopback 훅 호출 + 기기 인증)
- `GET /api/push/vapid-key` → `{ key }`(공개키, 인증 불필요).
- `POST /api/push/subscribe`(승인 기기): `{ endpoint, keys:{p256dh, auth} }` 저장.
- `POST /api/push/unsubscribe`(승인 기기): 삭제.

## 트리거
- `POST /api/hook/notification`(actionable 통과분) → `PushService.NotifyDisconnected`.
- `POST /api/hook/elicit`(AskUserQuestion) → 게이트와 무관하게 `NotifyDisconnected` 호출(앱 꺼져 있어도 알림). 기존 블로킹/브로드캐스트 로직은 불변.
- `/hook/permission`은 별도 푸시 안 함(연결 없으면 즉시 PC 폴백 + notification 훅으로 커버, 중복/소음 방지).

## 클라이언트
- app.js: 승인 + 알림 권한 허용 시 `ensurePushSubscribed()` — vapid-key 조회 → `pushManager.subscribe({userVisibleOnly:true, applicationServerKey})` → `/api/push/subscribe` POST.
- sw.js: `push` 이벤트 → `showNotification('Agent Hub', {body:'응답 대기 중인 세션이 있습니다', tag:'agenthub-ask', renotify:true, data:{url:'/'}})`. `notificationclick`는 기존 핸들러(포커스/열기) 재사용.

## 제약
- PC·폰 모두 인터넷 필요, 브라우저 푸시 서비스 경유. iOS는 PWA 설치 + iOS 16.4+에서만. 자체서명 HTTPS는 인증서 설치 시 보안 컨텍스트로 인정되어 SW/Push 사용 가능.

## 검증
1. 빌드 통과.
2. 라이브: 폰 승인·알림 허용 → 앱 종료 → PC에서 AskUserQuestion 유발 → 폰에 푸시 도착 → 탭 → 앱 열림 → 해당 세션 '응답 대기중' 확인.
