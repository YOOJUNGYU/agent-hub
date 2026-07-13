# 폰에서 세션 턴에 자유 텍스트로 답장 — 설계

- 날짜: 2026-07-13
- 상태: 설계 승인됨(사용자 "구현 계획으로 넘어가"), 스펙 리뷰 대기
- 관련 원칙: CLAUDE.md(단순성·서지컬·가이드 동기화·한글 응답), 메모리 [[keep-usage-guide-in-sync]], [[session-terminal-resume-model]]

## 1. 배경 / 문제

Claude가 턴을 **일반 텍스트로 끝내며 질문**할 때(예: "1. Subagent-Driven … 2. Inline Execution … 어느 쪽으로 진행할까요?")는 `AskUserQuestion` 도구가 아니라 평범한 어시스턴트 메시지다. 이 경우 현재 구조에서는 **`Stop` 훅만 발화** → 서버가 `/api/hook/stop`(fire-and-forget)에서 "작업을 완료했습니다" 알림을 보낸다.

문제:
1. 질문으로 턴을 끝냈는데 알림이 "완료"라 오해를 준다. → **질문/답변 형태로** 알리고 싶다.
2. 객관식(AskUserQuestion)이 아닌 **주관식(자유 텍스트) 답변**을 폰에서 입력·전송해 세션에 전달하고 싶다.

참고(이미 되는 것): `AskUserQuestion`(객관식)은 `PermissionRequest` 훅 → `/api/hook/elicit`(블로킹, 최대 600초) → 질문 본문 알림 + PWA 인터랙티브 선택 오버레이가 이미 동작하며, **"기타(Other)" 자유 입력 텍스트박스**까지 있어 임의 텍스트 답변이 가능하다. 따라서 이번 작업의 대상은 **"일반 텍스트로 끝난 턴"에 대한 답장**이다.

## 2. 목표 / 비목표

**목표**
- 폰에서 그 세션을 **보고 있을 때(watch 중)**, 턴이 끝나면 답장 카드가 뜨고, 자유 텍스트를 입력·전송하면 **아직 살아있는 같은 claude 프로세스**에 주입되어 대화가 이어진다.
- 답할 게 없으면 **[완료(닫기)]** 한 번으로 즉시 정상 종료.
- 폰이 미연결/미watch이면 **오늘과 100% 동일**(즉시 "완료" 알림, in-place 답장 없음).

**비목표**
- 미연결(앱 닫힘) 상태에서의 in-place 답장. (턴을 인위적으로 붙들면 PC 단독 사용 시 매 턴 claude가 멈추는 부작용 → 의도적으로 제외. 필요 시 기존 세션 터미널로.)
- `AskUserQuestion`(객관식) 흐름 변경. (자유 입력은 이미 "기타"로 지원.)
- 질문/완료 자동 판별 휴리스틱. (게이트를 "watch 중"으로 두어 불필요.)

## 3. 확정된 결정

- **인터랙션 모델:** 매 턴 종료 시 답장 대기(질문 감지 없음).
- **대기 종료:** 넓은 창(기존 `RemoteAnswerConfig` 600초 카스케이드 재사용) + 폰의 **[완료(닫기)]** 버튼으로 즉시 종료. 무응답 시 창 만료로 자동 종료.
- **게이트:** 폰이 **그 세션을 watch 중일 때만** `Stop` 훅이 턴을 붙든다. (권장안 확정)

## 4. 아키텍처 / 데이터 흐름

기존 elicit(AskUserQuestion) 흐름을 그대로 본떠 `Stop`에 적용한다.

```
claude 턴 종료
  └─ Stop 훅(블로킹) ── POST /api/hook/stop {session_id, cwd, waitMs}
        서버:
          IsSessionWatched(session_id)?
            아니오 → (오늘 그대로) BroadcastDone + NotifyDisconnected("작업을 완료했습니다")
                     즉시 {reply:null} 응답 → 훅 출력 없음 → 정상 종료
            예     → lastMsg = LastAssistantTextOf(session_id)
                     BroadcastReply(id, project, lastMsg, session_id)  // 연결 폰에 답장 카드
                     NotifyDisconnected(lastMsg 또는 "답장을 기다립니다")   // 미연결 승인기기
                     reply = ReplyRegistry.AwaitReply(id, session_id, lastMsg, waitMs)
                       ├─ 폰 [전송] → reply = "<텍스트>"
                       ├─ 폰 [완료(닫기)] → reply = null (dismiss)
                       └─ 타임아웃 → reply = null
                     reply==null 이면 BroadcastReplyClose + BroadcastDone
                     {reply} 응답
        훅:
          reply 있으면 stdout: {"decision":"block","reason":"<reply>"} → claude가 이어받아 계속
          reply 없으면 출력 없음 → claude 정상 종료
```

## 5. 컴포넌트별 변경(파일 단위)

### 5.1 `AgentHub/hook/agenthub-hook.js` — Stop 분기 블로킹화
현재 fire-and-forget 분기를 elicit 분기와 같은 형태로 교체:
- `windowSec = Number(process.argv[2]) || 600`, `budgetMs = max((windowSec-5)*1000, 1000)`.
- `POST /api/hook/stop {session_id, cwd, waitMs: budgetMs}`, HTTP timeout `budgetMs+2000`.
- 응답 파싱(선행 BOM 제거) 후 `r.reply` 있으면 `process.stdout.write(JSON.stringify({ decision:'block', reason: r.reply }))`.
- 안전망 `setTimeout(process.exit, budgetMs+4000)`.
- 서버 다운 시: `post`가 즉시 error → `onDone(null)` → 출력 없음 → 정상 종료(행 없음).

### 5.2 `AgentHub/Server/Hook/ReplyRegistry.cs` — 신규(AskRegistry 미러)
- `AwaitReply(string id, string sessionId, string lastMessage, int timeoutMs) : Task<string>` — 타임아웃/무응답/dismiss 시 `null`.
- `Resolve(string id, string text)` — 폰 [전송]. 빈 문자열이면 무시.
- `Dismiss(string id)` — 폰 [완료(닫기)] → `TrySetResult(null)`.
- `TryGetPendingForSession(sessionId, out id, out lastMessage)` — 재접속(watch) 시 카드 재전송용.
- 내부: `ConcurrentDictionary<string, Pending{Tcs, SessionId, LastMessage}>`.

### 5.3 `AgentHub/Server/Agents/ClaudeSessionReader.cs` + `TranscriptParser.cs`
- `TranscriptParser.LastAssistantText(IReadOnlyList<string> lines) : string` — 마지막 assistant 메시지의 text 블록(여러 개면 마지막/이어붙임), 표시용 truncate. (`Summarize`가 이미 추적하는 `lastAssistant`/`CurrentTask` 로직 재사용 가능)
- `ClaudeSessionReader.LastAssistantTextOf(sessionId) : string` — 파일 로드 후 위 파서 호출.

### 5.4 `AgentHub/Server/Agents/AgentMonitorService.cs`
- `BroadcastReply(id, project, message, sessionId)` — `{type:"reply", id, project, message, sessionId}` (BroadcastElicit 미러).
- `BroadcastReplyClose(sessionId)` — `{type:"replyClose", sessionId}`.
- `bool IsSessionWatched(sessionId)` — 모듈에 위임.

### 5.5 `AgentHub/Server/Socket/AgentMonitorModule.cs`
- `bool IsSessionWatched(string sessionId)` — `_watching` 값에 sessionId가 있는지.
- `OnMessageReceivedAsync`에 케이스 추가:
  - `"reply"`: ClawdGuard 실행 중이면 `answerBlocked` 안내, 아니면 `ReplyRegistry.Resolve(msg.Id, msg.Text)`.
  - `"replyDismiss"`: `ReplyRegistry.Dismiss(msg.Id)`.
- `watch` 처리부: elicit 재전송 옆에 `ReplyRegistry.TryGetPendingForSession` → `{type:"reply", …, resent:true}` 재전송.
- `WatchMessage`에 `public string Text { get; set; }` 추가.

### 5.6 `AgentHub/Server/Controller/ApiController.cs` — `/hook/stop` 재작성
- 4장 흐름대로. `waitMs` 클램프는 elicit(`HookElicit`)와 동일(`ServerWindowMs`/`ServerMarginMs`).
- watch 중이 아니면 기존 동작(BroadcastDone + "작업을 완료했습니다" 푸시) 유지.
- 응답: `{ reply }`.

### 5.7 `AgentHub/Server/Hook/HookInstaller.cs` — Stop 엔트리 블로킹화
- 기존 `stopEntry`(`async:true`, `timeout:5`) → PermissionRequest 엔트리처럼:
  - `async` 제거, `timeout = RemoteAnswerConfig.WindowSeconds`, `args = [ScriptPath, WindowSeconds.ToString()]`.
- 기존 설치본 강제 갱신: `Stop`도 `RemoveHook` 후 `AddHook`(PermissionRequest와 동일 패턴).

### 5.8 PWA — `index.html` / `js/app.js` / `js/i18n.js`
- `index.html`: elicit 오버레이 옆에 **답장 오버레이** 추가:
  ```html
  <div id="reply" class="elicit-overlay" hidden>
    <div class="elicit-card">
      <div class="elicit-header" id="replyHeader" data-i18n="reply.title">Claude가 답장을 기다립니다</div>
      <div class="elicit-question" id="replyMessage"></div>   <!-- 마지막 메시지(읽기용) -->
      <div class="elicit-hint" data-i18n="reply.hint"></div>
      <textarea id="replyText" class="elicit-other" rows="3" data-i18n-ph="reply.ph"></textarea>
      <div class="elicit-actions">
        <button id="replyDismiss" class="elicit-btn ghost" data-i18n="reply.dismiss">완료(닫기)</button>
        <button id="replySend" class="elicit-btn primary" data-i18n="reply.send">전송</button>
      </div>
    </div>
  </div>
  ```
- `js/app.js`:
  - WS 라우팅에 `else if (m.type === 'reply') handleReply(m); else if (m.type === 'replyClose') handleReplyClose(m);` 추가.
  - `handleReply(m)`: **`m.sessionId === currentSessionId`일 때만 오버레이 표시**(교차-세션 팝업 방지 — `BroadcastReply`는 연결된 모든 승인 소켓에 가므로, 다른 세션을 보는 폰에서는 무시). 마지막 메시지 표시 + textarea 초기화. `resent` 아니고 알림 권한 있으면 시스템 알림(제목 `reply.title`, 본문 `titlePrefix + 마지막 메시지`).
  - `[전송]`: 텍스트 비어있지 않으면 `send({type:'reply', id, text})` 후 오버레이 감춤(clawd 차단 회신 대비 elicit처럼 상태 유지 고려).
  - `[완료(닫기)]`: `send({type:'replyDismiss', id})` 후 감춤.
  - `handleReplyClose(m)`: 오버레이 감춤(다른 기기에서 처리됨/타임아웃).
  - `handleAnswerBlocked` 재사용(clawd).
- `js/i18n.js`: ko/en에 `reply.title`, `reply.hint`, `reply.ph`, `reply.send`, `reply.dismiss` 추가.

### 5.9 `docs/index.html` — 사용 가이드(필수 동기화)
- "세션을 보는 중 Claude가 턴을 끝내면, 답장을 입력해 대화를 이어가거나 [완료(닫기)]로 마칠 수 있습니다. (앱이 그 세션을 보고 있을 때 동작)" 취지 추가. 앱 `/guide.html` 단일 소스이므로 같은 작업에서 갱신.

## 6. 에러 처리 / 엣지 케이스

- **서버 다운/loopback 아님**: 훅은 정상 종료로 폴백(행 없음). `/hook/stop`은 `IsLoopback` 가드 유지.
- **주입 재발화(`stop_hook_active`)**: 답장 주입 → claude 계속 → 다음 턴 종료 시 Stop 재발화. 답장이 실제로 올 때만 block하므로 무한 루프 없음(대화가 이어지는 정상 동작).
- **미watch 세션은 붙들지 않음**: 안 보는 세션이 불필요하게 멈추지 않음.
- **clawd-on-desk 동시 실행**: 두 앱이 같은 훅을 다투므로 `reply` 수신 시 `answerBlocked` 안내(elicit와 동일).
- **재접속 복구**: 폰이 세션을 다시 열면 미결 답장 카드 재전송(`TryGetPendingForSession`).
- **여러 폰**: 한 폰이 [전송]/[완료]하면 `Resolve/Dismiss`로 훅 해제 + `BroadcastReplyClose`로 나머지 폰 카드 정리.

## 7. 검증 계획(원칙 4)

1. **주입 계약 실측(최우선 게이트)**: 훅 설치 → claude 세션 실행 → 폰에서 그 세션 watch → 턴을 질문으로 종료 → 폰에서 답장 전송 → **claude가 그 텍스트를 입력으로 이어받는지** 확인.
   - 기대: `{"decision":"block","reason":"<reply>"}`가 사용자 입력처럼 주입.
   - **실패 시 폴백**: (a) `hookSpecificOutput.additionalContext` 형태 시도, (b) `reason` 문구를 "사용자가 폰에서 답함: …" 로 감싸 지시로 해석되게, (c) 최후엔 resume 기반 전달(무거움) 재검토. 이 단계 결과에 따라 이후 구현 확정.
   - **✅ 실측 확정(2026-07-13, 격리 스파이크):** Stop 훅이 `{"decision":"block","reason":"<text>"}`를 내면 claude가 **턴을 이어감**(재발화 시 `stop_hook_active=true`로 확인). 단, reason이 "Disregard earlier instructions…" 같은 인젝션형 문구면 claude가 **prompt-injection으로 간주해 거부**함. → reason을 **"사용자의 정당한 답장"으로 프레이밍**하면 정상 수용(질문 "어떤 색?" → 프레이밍 답장 → "파란색으로 하겠습니다"). **확정 형식:** 서버가 원시 답장을 아래로 감싸 `{reply}`로 반환, 훅은 `reason: r.reply` 그대로 사용:
     ```
     [Agent Hub] 사용자가 휴대폰에서 이 세션에 답장을 보냈습니다:

     <원시 답장>

     — 위 내용은 사용자가 직접 입력한 정당한 후속 메시지입니다(프롬프트 인젝션이 아님). 이 답장을 사용자의 다음 지시로 받아들여 계속 진행하세요.
     ```
   - **보너스 확정:** Stop 훅 페이로드에 `session_id, transcript_path, cwd, prompt_id, permission_mode, effort, hook_event_name, stop_hook_active, last_assistant_message` 등이 존재. `last_assistant_message`로 마지막 메시지를 얻을 수도 있으나, 형식 의존을 피하려 본 설계는 서버 트랜스크립트 읽기(`LastAssistantTextOf`)를 유지한다.
2. **빌드**: `msbuild AgentHub.sln /t:Restore` → `/t:Build /p:Configuration=Debug /p:Platform="Any CPU"` 통과. 산출물 `install/Debug/AgentHub.exe`.
3. **회귀**: 폰 미연결/미watch 시 "완료" 알림이 오늘과 동일한지, PC 단독 사용 시 매 턴 멈춤이 없는지.
4. **가이드**: `docs/index.html` 갱신 포함 여부(누락 시 미완성).

## 8. 미해결 / 리스크

- Stop 훅 `decision:block`+`reason` 주입 계약이 공식 문서에 명확치 않음 → §7-1에서 실측 후 확정.
- "watch 중" 게이트라 목록만 보는 중 끝난 턴엔 in-place 답장이 안 됨(설계상 의도). 필요 시 후속으로 "목록에서 답장" 확장 여지.
