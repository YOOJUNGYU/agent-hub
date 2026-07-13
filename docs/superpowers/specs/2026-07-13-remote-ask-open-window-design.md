# 원격 질문/답변 "따라잡기" 창 설계 (앱 오프 상태에서도 창 안에 켜면 답변)

- 날짜: 2026-07-13
- 상태: 설계 승인 대기 → 승인 시 구현 계획(writing-plans)으로 이관
- 대상 기능: `AskUserQuestion` 원격 답변의 대기창 확대 + 앱 오프 상태 대응

## 1. 배경 / 문제

Claude Code가 `AskUserQuestion`을 던지면 `PermissionRequest` 훅(`agenthub-hook.js`)이 동기(blocking)로 로컬 서버(`/api/hook/elicit`)에 POST하고, 서버는 승인 기기에 질문을 push한 뒤 답변을 기다렸다가 그 답을 tool call에 되돌린다.

현재 구조의 한계(코드 분석으로 확인):

- **앱이 꺼져 있으면 답변 자체가 불가.** 훅이 `127.0.0.1:{port}`에 붙지 못하면(connection refused) 즉시 포기(`onDone(null)`)하고 출력 없이 종료 → Claude Code는 PC 터미널로 폴백. 이미 훅이 반환됐으므로, 사용자가 **직후에 앱을 켜도** 그 질문에는 답할 수 없다. (`agenthub-hook.js` `readPort`/`post`, `if(!port) process.exit(0)`)
- **대기창이 짧음.** 서버 `AskRegistry.AwaitAnswer`는 약 110초, Node 훅 HTTP 118초/안전망 119초, Claude 훅 `timeout` 120초의 계단식. 자리를 잠깐만 비워도 창을 놓친다.
- **POST 시점에 클라이언트가 붙어 있어야만 대기.** 서버 `HookElicit`은 `HasApprovedClient()`가 참일 때만 등록·블로킹한다. 앱을 막 켜서 콘솔/폰이 아직 WS로 안 붙은 순간에 POST가 도달하면 즉시 반환되어 폴백된다.

사용자 요구: **앱이 꺼져 있는 상태에서 `AskUserQuestion`이 와도, (기본 600초) 창 안에 앱을 실행하면 그 질문에 답할 수 있게 한다.**

## 2. 목표 / 비목표

### 목표
- 앱이 꺼져 있을 때 온 `AskUserQuestion`을, 사용자가 창(기본 600초) 안에 앱을 실행하면 답할 수 있다.
- 대기창을 설정값으로 확대(기본 600초). 답은 원래 tool call에 **구조화된 원본 형태 그대로** 되돌아간다(기존 즉답 경로 유지).
- 서버 재시작 등으로 연결이 잠깐 끊겨도 창 안이면 복구된다.

### 비목표 (이번에 하지 않는 것)
- `claude --resume` 재주입 / 멈춘 세션 이어받기 (위험 부담으로 제외).
- 대기 질문의 디스크 영속 큐 (불필요 — 훅이 창 동안 요청을 붙들고 있으므로 인메모리로 충분).
- 외부망(LTE 등)에서의 답변. 외부는 **push 알림만**(기존 동작 유지), 답변은 같은 LAN에서만.
- `PreToolUse` 위험 도구 권한승인의 창 확대/앱-오프 대응 (기존 120초·즉시 폴백 유지).
- 앱 자동시작/상주화, VPN·터널·포트포워딩 등 외부 도달성.

## 3. 설계 개요

세 가지 변경이 맞물려 동작한다. 모두 인메모리이며 서드파티/외부 의존 없음.

```
[변경2] 훅이 앱을 기다림  →  [변경3] 서버가 클라이언트를 기다림  →  [변경1] 넉넉한 창 안에 답변
   (endpoint.txt 폴링·재접속)     (POST 즉시 pending 등록·블로킹,        (deadline 기반, 원본 답변을
                                   연결되는 클라이언트에 재전송)            tool call에 반환)
```

### 변경 1 — 대기창을 설정값으로 확대 (기본 600초)
- 새 설정: `Properties.Settings`에 `RemoteAnswerWindowSeconds`(기본 **600**) 추가. 사용자가 조정 가능(예: 최대 1800). UI 노출 여부는 최소화 — 설정 파일/기존 설정 화면에 필드 추가 수준.
- **단일 창 W = 총 예산(deadline)** 모델. "앱을 기다리는 시간"과 "답을 기다리는 시간"을 합쳐 W 안에 둔다(둘의 합이 W를 넘지 않음). 앱을 늦게 켤수록 남는 답변 시간이 줄어드는 자연스러운 모델.
- 계단식 순서(안쪽이 먼저 만료)를 그대로 유지하되 W 기준으로 재계산:
  - 서버 `AwaitAnswer` = 훅이 넘겨준 잔여시간(`remaining`)과 설정 W 중 작은 값 (가장 먼저 만료 → 훅에 폴백 응답을 돌려줄 여유 확보)
  - Node 훅 HTTP 요청 timeout = 잔여시간 + 소폭 마진
  - Node 훅 안전망 `setTimeout` = 위보다 소폭 큼
  - Claude `PermissionRequest` 훅 `timeout`(settings.json) = W + 바깥 마진 (훅이 스스로 마무리하기 전에 Claude가 먼저 죽이지 않도록)
- **`PreToolUse` 훅 `timeout`은 120초 그대로.** AskUserQuestion(=PermissionRequest 경로)만 길게. (PermissionRequest 훅은 AskUserQuestion이 아니면 즉시 통과하므로 확대해도 다른 권한 다이얼로그엔 영향 없음.)

### 변경 2 — 훅이 서버가 뜰 때까지 기다림 (`agenthub-hook.js`, AskUserQuestion 분기 한정)
- 시작 시 `deadline = now + W`.
- 서버 접속 실패(connection refused) 또는 `endpoint.txt` 부재를 **즉시 포기 사유로 보지 않고**, `deadline`까지 폴링:
  - 매 시도마다 `endpoint.txt`를 **재읽기**(앱이 켜지며 포트가 바뀌거나 새로 기록될 수 있음).
  - 짧은 간격(예: 0.5~1초)으로 재접속 시도.
- 서버에 연결되면 `/api/hook/elicit`에 POST하고, **잔여시간(`remaining = deadline - now - margin`)을 함께 전달**. 서버 응답(답변/타임아웃)을 받아 기존과 동일하게 stdout으로 결정 반환.
- **연결이 도중에 끊기면**(서버 재시작 등) 이를 "아직 안 뜬 상태"와 동일하게 취급해 `deadline` 전까지 재접속 루프로 복귀 → 재시작 견딤. 재접속 후에는 질문을 다시 POST(새 id로 재등록)한다.
- `deadline` 초과 시: 출력 없이 종료 → Claude Code는 기존대로 PC 터미널로 폴백(무손실).
- **AskUserQuestion 외 이벤트, 그리고 `PreToolUse` 분기는 기존 동작 유지**(서버 없으면 즉시 폴백).

### 변경 3 — 서버가 클라이언트 없이도 등록·대기 (`ApiController.HookElicit` + `AskRegistry`)
- `HookElicit`을 **`HasApprovedClient()` 게이트 없이** 다음으로 변경:
  1. 항상 `id` 생성 + `AskRegistry`에 pending 등록.
  2. 현재 연결된 승인 클라이언트에는 즉시 `BroadcastElicit`.
  3. 미연결 승인 기기에는 기존대로 `PushService.NotifyDisconnected`(앱이 켜져 있을 때만 유효).
  4. `AskRegistry.AwaitAnswer(id, sessionId, questions, min(W, remaining))`로 블로킹.
- **대기 중 새 클라이언트가 붙는 경우**는 이미 있는 재전송 경로를 활용: 클라이언트가 세션을 watch하기 시작하면 `AskRegistry.TryGetPendingForSession`으로 미답 질문을 재전송(`AgentMonitorModule` watch 핸들러, 기존 코드). → 앱을 막 켜서 콘솔/폰이 붙는 데 몇 초 걸려도 질문이 전달됨.
- 답변 도착 → `AskRegistry.Resolve` → TCS 완료 → `HookElicit` 반환(기존 `updatedInput` 병합/반환 그대로).
- 타임아웃 시 `null` 반환 → 훅 폴백(기존 그대로). `finally`에서 pending 제거(기존 그대로).

## 4. 통합 흐름 (앱 오프 상태 시나리오)

1. 앱 꺼짐. Claude Code가 `AskUserQuestion` 던짐 → `PermissionRequest` 훅 실행.
2. 훅: `endpoint.txt`/서버 접속 실패 → `deadline`까지 폴링 시작(변경2). Claude Code는 훅 대기로 멈춤.
3. 사용자가 창 안에 AgentHub 앱 실행 → 서버 기동, `endpoint.txt` 기록, 리스닝 시작. 호스트 콘솔(WebView)·폰 PWA가 WS로 연결.
4. 훅이 폴링 중 접속 성공 → 질문 POST(+`remaining`).
5. 서버 `HookElicit`: pending 등록 + 연결 클라이언트에 broadcast(변경3). (막 붙은 클라이언트는 watch 재전송으로 수신.)
6. 사용자가 호스트 콘솔 또는 폰에서 답 선택 → `elicitAnswer` → `Resolve`.
7. 훅이 답을 받아 `updatedInput`으로 반환 → Claude Code가 **원본 tool call에 구조화된 답변**을 받아 진행.
8. (창 안에 아무도 답 안 하면) `deadline` 만료 → 훅 폴백 → PC 터미널 정상 흐름.

## 5. 설정 / 계단식 타임아웃 (기본 W=600초)

| 계층 | 값(기본) | 비고 |
|---|---|---|
| 서버 `AwaitAnswer` | `min(W, remaining)` | 가장 먼저 만료 |
| Node 훅 HTTP timeout | `remaining + 소폭` | |
| Node 훅 안전망 `setTimeout` | 위 + 소폭 | 프로세스 강제 종료 안전망 |
| Claude `PermissionRequest` `timeout` | `W + 바깥 마진` (예 630s) | 가장 나중, 훅을 미리 죽이지 않음 |

- `W`는 총 예산(폴링 + 답변 대기 합). 정확한 마진 값은 구현 계획에서 확정.
- 설정 상한: 문서상 안전값은 600초. 그 이상(예 1800)은 **Claude Code가 존중하는지 실측 확인 필요**(§6 R-A).

## 6. 리스크 / 검증 포인트

- **R-A (실측 필수):** 훅 `timeout`의 최대 허용값이 공식 문서에 없음. 기본 W=600초는 문서상 command 훅 기본값이라 안전하다고 판단하나, 구현 중 **실제로 600초 blocking이 존중되는지** 스파이크로 확인한다. (600 초과 설정 시 실효 창 = `min(설정, Claude 상한)`.)
- **R-B (트레이드오프):** 창 동안 Claude Code는 이 질문에 멈춰 대기한다. AskUserQuestion은 답 없이는 진행 불가하므로 실질 문제는 없으나, **이 창 동안 답변 창구는 AgentHub(호스트 콘솔/폰)뿐**이고 순수 PC 터미널 선택 UI는 창을 넘겨야 나타난다.
- **R-C (한계 명시):** 앱이 꺼진 동안엔 서버가 push를 못 보낸다(발신 주체=서버). 이 기능은 "사용자가 창 안에 앱을 직접 켠다"는 전제에서 성립. 앱이 켜져 있으면 기존 push는 정상.
- **R-D:** 훅 폴링 간격/재접속 로직이 서버 정상 부재 시 CPU를 낭비하지 않도록 sleep 간격 확보(예 0.5~1초).
- **반환 스키마:** 이번 변경은 훅의 결정 반환 형식을 바꾸지 않는다(타임아웃·접속대기·서버등록만 변경). 기존 동작 형식 유지.

## 7. 검증 계획 (goal-driven)

1. **앱 켜진 상태(회귀):** AskUserQuestion → 폰/콘솔 즉답 → tool call에 답 반영. (기존과 동일해야 함) → verify: 정상.
2. **자리비움:** 앱 켜짐, 클라이언트 미연결 상태로 질문 → 창 안에 폰 연결 후 답 → 반영. → verify: 창 확대 + 변경3 동작.
3. **앱 오프 → 창 안 실행(핵심):** 앱 종료 → AskUserQuestion 발생 → 창 안에 앱 실행 → 콘솔/폰에서 답 → 반영. → verify: 변경2+3.
4. **창 초과:** 아무도 답 안 함 → `deadline` 후 훅 폴백 → PC 터미널 정상. → verify: 무손실 폴백.
5. **서버 재시작 중:** 대기 중 서버 재기동 → 훅 재접속 → 질문 재전송 → 답 → 반영. → verify: 재시작 견딤.
6. **R-A 실측:** 600초 blocking이 Claude Code에서 실제로 유지되는지 확인.

## 8. 문서 동기화 (CLAUDE.md 필수)

`docs/index.html` 사용 가이드에 다음을 추가:
- 앱을 잠깐 꺼놨더라도 (기본 600초) 창 안에 앱을 켜면 밀린 질문에 답할 수 있다는 설명.
- 대기창 길이 설정(`RemoteAnswerWindowSeconds`, 기본 600초)과 그 의미(앱 대기+답변 대기 합).
- 외부망(LTE 등)에서는 push 알림만 오고 답변은 같은 Wi‑Fi 복귀 후 가능하다는 안내.

## 9. 영향 파일 (예상)

- `AgentHub/hook/agenthub-hook.js` — 변경2(폴링·재접속·deadline), 변경1(타임아웃 값).
- `AgentHub/Server/Hook/HookInstaller.cs` — 변경1(PermissionRequest `timeout` 상향; PreToolUse는 유지).
- `AgentHub/Server/Controller/ApiController.cs` — 변경3(`HookElicit`에서 `HasApprovedClient` 게이트 제거, 항상 등록·대기, `remaining` 반영).
- `AgentHub/Server/Hook/AskRegistry.cs` — 대기 시간 파라미터화(설정/`remaining`).
- `AgentHub/`(Properties.Settings) — `RemoteAnswerWindowSeconds` 설정 추가.
- `docs/index.html` — 사용 가이드 갱신.
- (기존 재전송 경로 `AgentMonitorModule` watch 핸들러는 재사용, 변경 최소.)
