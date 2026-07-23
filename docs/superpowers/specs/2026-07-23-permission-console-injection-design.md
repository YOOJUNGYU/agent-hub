# 권한 요청 콘솔 주입 폴백 — 설계

- 날짜: 2026-07-23
- 상태: 설계 승인 대기
- 관련: [`2026-07-16-mobile-direct-console-input-injection-design.md`](./2026-07-16-mobile-direct-console-input-injection-design.md), [`2026-07-22-mobile-session-detail-answer-ux-design.md`](./2026-07-22-mobile-session-detail-answer-ux-design.md)

## 배경 / 문제

폰 원격 응답에서 **권한 요청(PreToolUse)**에 두 가지 문제가 있다.

1. **알림이 안 옴** — `HookPermission`은 `default 모드 + 승인 기기 WS 연결`일 때만 원격 승인 흐름을 태운다. 그 순간 폰이 연결돼 있지 않으면 즉시 `"ask"`를 반환하고 Claude가 PC 터미널 프롬프트로 넘어가 버려, 폰엔 아무 기록/알림도 남지 않는다.
2. **시간 지나면 답변 불가** — 라이브 배너가 떠도 훅 대기창(약 110초)이 지나면 `"ask"`로 만료되고, 그 뒤엔 세션 상세에 남는 게 없어 원격으로 답할 방법이 없다.

반면 **AskUserQuestion**은 이미 만료 후에도 `pendingAsk`를 세션 스냅샷에 남겨 두고, 폰에서 터미널 picker에 **번호를 직접 콘솔 주입**(`InjectPickerAnswer`)해 답할 수 있다.

## 목표

- 현행 라이브 UI(권한 배너 / elicit 오버레이)는 **그대로 유지**.
- 권한 요청도 AskUserQuestion처럼 **만료 후 세션 상세에 남겨 두고 콘솔 주입으로 허용/거부**할 수 있게 한다.
- 부수 효과로 "권한 알림이 안 옴"을 완화한다(만료/폴백 전환 시 push 재발송).

비목표(YAGNI): 알림 트리거 전면 재설계, 라이브 배너 UX 변경, 디스크 영속화, ConPTY 주입 지원.

## 핵심 원리

Claude Code의 PreToolUse 훅이 `allow/deny`가 아닌 결과(=출력 없음)를 내면, Claude는 **정상 권한 흐름 = 터미널에 번호 메뉴를 띄우고 stdin을 대기**한다. 서버는 `HookPermission`이 최종적으로 `"ask"`를 반환하는 시점을 정확히 안다. **그 시점이 곧 "콘솔 주입이 유효해지는 시점"**이다.

- 라이브 배너로 `allow/deny`가 확정되면 → 훅이 그 결정을 반환 → 터미널 프롬프트가 뜨지 않음 → 주입 대상 없음 → 기록하지 않는다.
- `"ask"`로 반환되면(폰 미연결 즉시, 또는 배너 무응답 타임아웃) → 터미널 프롬프트가 뜬다 → **대기 권한을 기록**하고 폰에 노출한다.

> 참고: 트랜스크립트만으로는 "권한 대기 중"과 "도구 실행 중"(둘 다 tool_use에 tool_result 없음)을 구분할 수 없다. 그래서 **훅의 `"ask"` 신호**를 권위 있는 트리거로 쓴다(트랜스크립트 추론 방식은 오탐으로 기각).

## 컴포넌트

### 1. `PendingPermissionRegistry` (신규, 서버 메모리)

`AgentHub/Server/Hook/PendingPermissionRegistry.cs`. `AskRegistry`와 대칭인 정적 클래스. 세션당 하나의 대기 권한.

```
record PendingPermission { string Tool; string Detail; DateTime CreatedAt; }
ConcurrentDictionary<string /*sessionId*/, PendingPermission> _pending

Set(sessionId, tool, detail)        // "ask" 전환 시 기록(기존 있으면 덮어씀)
TryGet(sessionId, out tool, detail) // 스냅샷/재전송용
Clear(sessionId)                    // 주입 성공 / 새 PreToolUse / 세션 종료
PruneExpired(ttl)                   // TTL(기본 15분) 지난 기록 제거
```

메모리 보관(디스크 영속 없음) — `AskRegistry`/`PermissionRegistry`와 동일. agent-hub는 상시 실행 전제라 재시작 중 만료는 드물고, 재시작 후엔 트랜스크립트로 복구 불가(위 참고). **알려진 한계**로 문서화.

### 2. `SessionSummary.PendingPermission` (신규 필드)

```csharp
public class PendingPermission { public string Tool { get; set; } public string Detail { get; set; } }
// SessionSummary에 추가:
public PendingPermission PendingPermission { get; set; }
```

`AgentMonitorService.CurrentSessions()`에서 `Injectable` 세팅 루프와 같은 자리에서:
```csharp
if (PendingPermissionRegistry.TryGet(s.Id, out var tool, out var detail))
    s.PendingPermission = new PendingPermission { Tool = tool, Detail = detail };
```

### 3. `HookPermission` 수정 (`ApiController.cs`)

최종 `decision`이 결정된 뒤:
```csharp
var sessionId = (string)o["session_id"];
if (decision == "ask")
{
    // 터미널 프롬프트가 뜨는 경우에만 콘솔-대기로 승격.
    var tool = (string)o["tool_name"] ?? "";
    var detail = ToolDetail(tool, o["tool_input"] as JObject);
    PendingPermissionRegistry.Set(sessionId, tool, detail);
    if (!IsDuplicateNotify("perm", sessionId, detail))
        PushService.NotifyDisconnected("권한 대기: " + (detail ?? tool), sessionId);
    OnChanged();  // 세션 스냅샷 즉시 재브로드캐스트(pendingPermission 노출)
}
else
{
    PendingPermissionRegistry.Clear(sessionId); // allow/deny 확정 → 대기 없음
}
```
> 새 PreToolUse가 도착하면 위 `Set`이 이전 기록을 덮어쓰므로 supersede도 자연 처리된다. `OnChanged`는 서비스의 재브로드캐스트를 재사용(외부 노출 시 얇은 래퍼 추가).

### 4. `ConsoleInputInjector.InjectPermissionAnswer` (신규)

```csharp
public static Result InjectPermissionAnswer(int pid, string choice)
// choice: "allow"→"1", "allowAlways"→"2", "deny"→"3"
// 단일 숫자키 = picker 단일선택과 동일하게 즉시 제출(Enter 불필요).
```
`MapChar`에 `'\x1b'(Esc)=VK_ESCAPE(0x1B)` 매핑을 추가(거부 폴백 대비). 기본 매핑은 숫자 `1/2/3`.

**검증 리스크(구현 시 실제 conhost로 확인):** 실제 Claude 권한 프롬프트의 옵션 순서/개수는 도구·버전에 따라 다를 수 있다. 매핑은 **한 곳(이 메서드)에 상수로 격리**해 확인 후 손쉽게 조정한다. 옵션이 2개뿐인 프롬프트에서 `2/3`이 어긋나면 거부는 `Esc`로 대체.

### 5. WS 핸들러 `permissionInject` (`AgentMonitorModule.cs`)

`pickerAnswer` 케이스와 동형:
```
msg.Type == "permissionInject": engine!=claude→"engine" / no pid→"nopid" /
  else Task.Run(InjectPermissionAnswer(pid, msg.Choice));
  성공 시 PendingPermissionRegistry.Clear(sessionId);
  회신 permissionInjectResult { sessionId, ok, reason }
```
`WatchMessage`에 `string Choice` 추가. `watch` 시 대기 권한 재전송은 스냅샷(`pendingPermission`)으로 이미 전달되므로 별도 재전송 불필요.

### 6. 프론트 (`app.js` / `index.html`)

- 세션 상세(`#detail`)에 대기 권한 카드 `#permPending`(신규 DOM): tool·detail 표시 + 3버튼(`허용` / `허용+다음 안 물음` / `거부`).
- `syncPendingForm`/스냅샷 처리에서: `s.pendingPermission && s.injectable`이면 카드 표시, 아니면 숨김. `pendingAsk` 폼과 상호 배타(질문 폼 우선순위는 기존 유지).
- 버튼 → `send({type:'permissionInject', sessionId, choice})`, 전송중 로딩 → `permissionInjectResult`로 해제(`handleInjectResult` 패턴 재사용).
- codex / 비주입가능(ConPTY 등)이면 버튼 대신 안내(`inject.hintNotShell` 재사용).
- 라이브 배너(`#permBanner`) 흐름은 손대지 않음.

### 7. 사용 가이드 (`docs/index.html`)

권한 응답 절: "라이브 배너로 허용/거부 → 시간이 지나 배너가 사라지면, 세션 상세의 권한 카드에서 콘솔 주입으로 허용/거부" 흐름 추가. (앱 `/guide.html` 단일 소스)

## 데이터 흐름

```
PreToolUse 훅 ─► /api/hook/permission
   │
   ├─ default+폰연결 ─► BroadcastPermission(라이브 배너) ─► 폰 allow/deny
   │      └─ resolve ─► decision=allow/deny ─► Clear ─► (터미널 프롬프트 없음)  ✔ 기존 유지
   │      └─ 110s 무응답 ─► decision="ask" ─┐
   ├─ 폰 미연결 ─────────► decision="ask" ──┤
   │                                        ▼
   │                    PendingPermissionRegistry.Set + push + OnChanged
   │                                        ▼
   │          세션 스냅샷 pendingPermission ─► 폰 세션 상세 3버튼 카드
   │                                        ▼
   │                    permissionInject{choice} ─► InjectPermissionAnswer(pid,"1|2|3")
   │                                        ▼
   │                    Claude 터미널 프롬프트에 번호 주입 → 진행 → Clear
```

## 생명주기 / 정리

대기 권한 기록 제거 시점:
1. **주입 성공**(`permissionInject` Ok) — 낙관적 즉시 Clear. 실패(NoConsole/failed)면 유지.
2. **새 PreToolUse 도착** — `Set`이 덮어씀(supersede).
3. **TTL 15분** — `CurrentSessions()` 진입 시 `PruneExpired`.
4. **세션 종료** — 별도 정리 없이 스냅샷에서 자연 소멸(세션이 목록에서 빠짐). 필요 시 ended 감지로 Clear.

## 테스트 (goal-driven)

- `PendingPermissionRegistry`: Set/TryGet/Clear/덮어쓰기/PruneExpired 단위 테스트.
- `ConsoleInputInjector.MapChar`: `'\x1b'→0x1B`, 숫자키 매핑 검증(콘솔 없이 가능한 순수 매핑).
- 매핑→키 시퀀스: `InjectPermissionAnswer`의 choice→payload 결정 로직을 추출/검증(실제 주입은 conhost 수동 검증).
- 빌드: `msbuild AgentHub.sln /t:Restore` → `/t:Build`(Debug, Any CPU) 통과, 산출물 `install/Debug/AgentHub.exe`.
- 수동: 실제 conhost claude 세션에서 위험 도구 트리거 → 배너 무시 → 세션 상세 카드 → 허용/거부 주입이 터미널 프롬프트를 정확히 조작하는지 확인(매핑 확정).

## 알려진 한계

- **ConPTY 미지원**: Windows Terminal 등에서는 주입 불가(`Injectable=false`) → 카드 대신 안내.
- **재시작 취약**: 대기 권한은 메모리 보관 → agent-hub 재시작 시 소실(트랜스크립트로 복구 불가).
- **매핑 버전 의존**: 권한 프롬프트 레이아웃이 바뀌면 매핑 상수 조정 필요(한 곳에 격리).
- **두 자리 번호 없음**: 권한 옵션은 3개 이하 전제(AskUserQuestion의 두 자리 한계와 동종).
