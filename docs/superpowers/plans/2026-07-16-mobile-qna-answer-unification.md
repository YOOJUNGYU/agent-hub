# 모바일 Q&A 답변 통합 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 모바일 Q&A에서 "터미널 열기"를 없애고, 미답 질문은 항상 답변 폼으로 표시하며, 라이브 질문은 기존 훅(elicitAnswer)으로, 만료 질문은 콘솔 주입(pickerAnswer)으로 제출한다. 연속 Q&A가 pendingAsk 기준으로 안정 동작한다.

**Architecture:** 프론트가 라이브 elicit(id) 없으면 `pendingAsk`로 동일한 답변 폼을 구성. 제출 시 id 있으면 `elicitAnswer`(기존), 없으면 `pickerAnswer`로 `/ws/agents`에 전송 → 백엔드가 검증된 키 시퀀스(옵션 번호 / Other=번호→텍스트→별도 Enter)를 `ConsoleInputInjector.InjectPickerAnswer`로 세션 콘솔에 주입.

**Tech Stack:** C# .NET Framework 4.8 (WinExe), Win32 P/Invoke, EmbedIO WebSocket, 바닐라 JS PWA.

**Spec:** `docs/superpowers/specs/2026-07-16-mobile-qna-answer-unification-design.md`

## Global Constraints

- 사용자 문구는 한글 기본, i18n ko/en 양쪽.
- **Claude 세션 전용** 주입. Codex/ConPTY는 만료 제출 시 실패 회신 + 안내(선행 기능 `inject.hint*` 재사용).
- **유니코드 필수**(기존 `ConsoleInputInjector`가 CharSet.Unicode/WriteConsoleInputW 사용 — 유지).
- **Other 주입은 반드시 단계 분리**: `(옵션수+1)` → 지연 → 텍스트 → 지연 → **Enter는 별도**. 같은 버스트면 빈 값 제출됨(스파이크 검증).
- 나열 옵션 단일선택은 **번호만** 주입(Enter 없이 즉시 제출).
- `AttachConsole`은 프로세스 전역 → `InjectPickerAnswer`는 시퀀스 전체를 `_gate` lock으로 직렬화.
- detail 헤더의 일반 "⌨ 터미널 열기"(`openSessionTermBtn`)는 **유지**. 제거 대상은 **askExpired 패널**뿐.
- 라이브 elicit 경로(`elicitAnswer`, `AskRegistry`)는 동작 불변.
- 신규 .cs 없음(기존 파일 수정만). 서드파티 EmbedIO 수정 금지.
- 기능 변경이므로 `docs/index.html` 가이드 동기화(같은 작업).
- 빌드: msbuild PATH 없음 → `& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"` (>120s 가능, 0 Error; 기존 CS0168 무관). JS는 `node --check`. 산출물 `install/Debug/AgentHub.exe`.
- **main 직접 커밋**(저장소 관행).

## File Structure

- `AgentHub/Server/Terminal/ConsoleInputInjector.cs` — `WriteOnce` 헬퍼 추출 + `Inject` 리팩터 + 신규 `InjectPickerAnswer`.
- `AgentHub/Server/Socket/AgentMonitorModule.cs` — `WatchMessage.Indices/OptionCount` + `pickerAnswer` 케이스.
- `AgentHub/View/Htmls/js/app.js` — askExpired 제거, pendingAsk 폼, 제출 라우팅, 상태 정리, `pickerAnswerResult`.
- `AgentHub/View/Htmls/index.html` — `#askExpired` DOM 제거.
- `docs/index.html` — Q&A 가이드 갱신.

---

### Task 1: 백엔드 — `ConsoleInputInjector.InjectPickerAnswer`

**Files:**
- Modify: `AgentHub/Server/Terminal/ConsoleInputInjector.cs`

**Interfaces:**
- Produces: `ConsoleInputInjector.Result InjectPickerAnswer(int pid, int[] indices, string text, int optionCount)` (Result enum 기존 `{Ok,NoConsole,Failed}`).
- Internal refactor: private `Result WriteOnce(int pid, string payload)` (attach→write→free, lock 없음). `Inject`는 이를 lock으로 감싸도록 변경(동작 동일).

> **테스트**: P/Invoke라 단위테스트 부적합(선행 태스크와 동일). 검증=빌드 통과. 키 시퀀스는 스파이크로 실측 검증됨(스펙 §4, 메모리 `askuserquestion-picker-injection`).

- [ ] **Step 1: `WriteOnce` 추출 + `Inject` 리팩터 + `InjectPickerAnswer` 추가**

`ConsoleInputInjector.cs`에서 기존 `Inject` 메서드 본문을 아래로 교체하고, 그 아래에 `InjectPickerAnswer`와 private `WriteOnce`를 추가한다(기존 `MapChar`/`KeyRecord`/구조체/DllImport/`_gate`는 그대로 재사용):

```csharp
        public static Result Inject(int pid, string text, bool appendEnter)
        {
            if (pid <= 0) return Result.Failed;
            string payload = (text ?? "") + (appendEnter ? "\r" : "");
            if (payload.Length == 0) return Result.Ok;
            lock (_gate) { return WriteOnce(pid, payload); }
        }

        private const int PickerStepDelayMs = 500;

        /// <summary>
        /// 만료된 AskUserQuestion 터미널 picker에 답을 주입한다(스파이크 검증 시퀀스).
        /// - text 있음(Other): (optionCount+1)="Type something" → 지연 → text → 지연 → Enter(별도).
        /// - indices 1개(단일선택): 그 번호만(즉시 제출, Enter 불필요).
        /// - indices 다수(다중선택, best-effort): 각 번호 토글 → 지연 → Enter.
        /// 시퀀스 전체를 _gate로 직렬화(중간 끼어들기 방지).
        /// </summary>
        public static Result InjectPickerAnswer(int pid, int[] indices, string text, int optionCount)
        {
            if (pid <= 0) return Result.Failed;
            lock (_gate)
            {
                try
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        var r1 = WriteOnce(pid, (optionCount + 1).ToString()); if (r1 != Result.Ok) return r1;
                        System.Threading.Thread.Sleep(PickerStepDelayMs);
                        var r2 = WriteOnce(pid, text); if (r2 != Result.Ok) return r2;
                        System.Threading.Thread.Sleep(PickerStepDelayMs);
                        return WriteOnce(pid, "\r");
                    }
                    if (indices == null || indices.Length == 0) return Result.Failed;
                    if (indices.Length == 1)
                        return WriteOnce(pid, (indices[0] + 1).ToString());
                    foreach (var idx in indices)
                    {
                        var r = WriteOnce(pid, (idx + 1).ToString()); if (r != Result.Ok) return r;
                        System.Threading.Thread.Sleep(150);
                    }
                    System.Threading.Thread.Sleep(PickerStepDelayMs);
                    return WriteOnce(pid, "\r");
                }
                catch (Exception ex) { LogService.Instance.Error(ex); return Result.Failed; }
            }
        }

        // attach→write→free 원자 1회(락 없음 — 호출자가 _gate 보유). payload에 필요한 문자를 그대로(예: Enter는 "\r").
        private static Result WriteOnce(int pid, string payload)
        {
            if (string.IsNullOrEmpty(payload)) return Result.Ok;
            bool attached = false;
            try
            {
                FreeConsole();
                if (!AttachConsole((uint)pid)) return Result.NoConsole;
                attached = true;
                IntPtr hIn = GetStdHandle(STD_INPUT_HANDLE);
                if (hIn == IntPtr.Zero || hIn == new IntPtr(-1)) return Result.Failed;
                // 주의: BMP 문자 가정(한글·ASCII). 서로게이트쌍(이모지 등 보충문자)은 코드유닛 단위로 분리됨.
                var records = new INPUT_RECORD[payload.Length * 2];
                int i = 0;
                foreach (char c in payload)
                {
                    var k = MapChar(c);
                    records[i++] = KeyRecord(k, true);
                    records[i++] = KeyRecord(k, false);
                }
                return WriteConsoleInput(hIn, records, (uint)records.Length, out _) ? Result.Ok : Result.Failed;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return Result.Failed; }
            finally { if (attached) FreeConsole(); }
        }
```

> 기존 `Inject`가 갖고 있던 attach/write/free 본문·BMP 주석은 `WriteOnce`로 이동한다(중복 제거). `_gate`, `MapChar`, `KeyRecord`, 구조체들, DllImport(`AttachConsole`/`FreeConsole`/`GetStdHandle`/`WriteConsoleInput`/`VkKeyScanW`)는 그대로 둔다.

- [ ] **Step 2: 빌드**

Run: `& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: `0 Error(s)`.

- [ ] **Step 3: 커밋**

```
git add AgentHub/Server/Terminal/ConsoleInputInjector.cs
git commit -m "feat: InjectPickerAnswer 추가(옵션 번호 / Other 텍스트+별도 Enter) + WriteOnce 추출"
```

---

### Task 2: 백엔드 — `/ws/agents` `pickerAnswer` 케이스

**Files:**
- Modify: `AgentHub/Server/Socket/AgentMonitorModule.cs`

**Interfaces:**
- Consumes: `ConsoleInputInjector.InjectPickerAnswer` (Task 1), `SessionPidRegistry.TryGet`, `AgentMonitorService.EngineOf`.
- Produces (프론트 소비): `{ type:"pickerAnswerResult", sessionId, ok, reason }`, reason ∈ `engine|nopid|noconsole|failed|null`.
- Client→server 메시지 계약: `{ type:"pickerAnswer", sessionId, indices:[int], text:string|null, optionCount:int }`.

- [ ] **Step 1: `WatchMessage`에 필드 추가**

`WatchMessage` 클래스(기존 `Text` 필드 아래)에 추가:
```csharp
        public int[] Indices { get; set; }     // pickerAnswer: 선택한 옵션 0-based 인덱스들
        public int OptionCount { get; set; }   // pickerAnswer: 나열된 실제 옵션 수(Other 번호 계산용)
```

- [ ] **Step 2: `pickerAnswer` 케이스 추가**

`OnMessageReceivedAsync`의 dispatch 체인에서 `inject` 케이스 바로 뒤(그리고 `// 세션 제어...` 주석 앞)에 추가. 블로킹 주입은 `Task.Run`으로 오프로드(핸들러 스레드 미차단):
```csharp
                else if (msg.Type == "pickerAnswer" && !string.IsNullOrEmpty(msg.SessionId))
                {
                    bool ok = false; string reason;
                    if (AgentMonitorService.EngineOf(msg.SessionId) != "claude")
                        reason = "engine";
                    else if (!AgentHub.Server.Hook.SessionPidRegistry.TryGet(msg.SessionId, out var pid))
                        reason = "nopid";
                    else
                    {
                        var r = await System.Threading.Tasks.Task.Run(() =>
                            AgentHub.Server.Terminal.ConsoleInputInjector.InjectPickerAnswer(
                                pid, msg.Indices ?? new int[0], msg.Text, msg.OptionCount));
                        ok = r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.Ok;
                        reason = ok ? null
                            : (r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.NoConsole ? "noconsole" : "failed");
                    }
                    await SendSafe(context, Json.Serialize(new
                    {
                        type = "pickerAnswerResult", sessionId = msg.SessionId, ok, reason
                    }));
                }
```

- [ ] **Step 3: 빌드**

Run: (Task 1 Step 2와 동일 msbuild) → `0 Error(s)`.

- [ ] **Step 4: 커밋**

```
git add AgentHub/Server/Socket/AgentMonitorModule.cs
git commit -m "feat: /ws/agents pickerAnswer 케이스(만료 질문 콘솔 주입 제출) + WatchMessage.Indices/OptionCount"
```

---

### Task 3: 프론트 — Q&A 폼 통합(askExpired 제거·pendingAsk 폼·제출 라우팅·상태 정리)

**Files:**
- Modify: `AgentHub/View/Htmls/js/app.js`
- Modify: `AgentHub/View/Htmls/index.html`

**Interfaces:**
- Consumes: 서버 `pickerAnswerResult`(Task 2); 세션 요약 `sessionsById[id].pendingAsk`(`{header,question,multiSelect,options:[label]}`)와 `.engine`.
- Produces: `send({type:'pickerAnswer', sessionId, indices, text, optionCount})`; 기존 `elicitAnswer`는 유지.

- [ ] **Step 1: index.html에서 `#askExpired` DOM 제거**

`index.html`에서 `<div id="askExpired" ...> ... </div>` 오버레이 블록 전체를 삭제한다(질문 원격답변 안내 패널). (elicit 오버레이 `#elicit`은 유지.)

- [ ] **Step 2: app.js — askExpired 로직 제거**

다음을 삭제한다:
- 변수 `askExpiredTimer`, `askExpiredSession` 및 함수 `clearAskExpiredTimer`, `scheduleAskExpiredGuidance`, `showAskExpired`, `closeAskExpired`.
- `askExpiredClose` / `askExpiredOpenTerm` 버튼 `addEventListener` 등록 2건.
- `openDetail`의 `scheduleAskExpiredGuidance(id);` 호출.
- `backToList`의 `closeAskExpired();` 호출.
- `handleElicit` 안의 `clearAskExpiredTimer(); closeAskExpired();` 호출.
- `showAskExpired`를 참조하던 그 외 잔여 호출(없으면 무시).

- [ ] **Step 3: app.js — pendingAsk 폼 표시 함수 추가**

`send`/`esc` 헬퍼 근처(예: `rel` 함수 뒤)에 추가:
```javascript
// 라이브 elicit이 없을 때 pendingAsk로 답변 폼을 띄운다(만료/주입 모드). Codex는 제외.
function maybeShowPendingForm(id) {
  if (!id || elicit) return;                                   // 이미 폼(라이브/pending) 있음 → 방해 금지
  if (!document.getElementById('elicit').hidden) return;       // 폼 열려 있음
  const s = sessionsById[id];
  const pa = s && s.pendingAsk;
  if (!pa || s.engine === 'codex') return;                     // 미답 질문 없음 / Codex(주입 불가)
  elicit = { id: null, sessionId: id, fromPending: true, step: 0, answers: {},
    questions: [{ header: pa.header, question: pa.question, multiSelect: !!pa.multiSelect,
                  options: (Array.isArray(pa.options) ? pa.options : []).map(l => ({ label: l })) }] };
  renderElicitStep();
  document.getElementById('elicit').hidden = false;
}
// 만료 폼 표시 중 그 세션의 pendingAsk가 사라지면(답변됨) 닫는다.
function syncPendingForm(id) {
  if (elicit && elicit.fromPending && elicit.sessionId === id) {
    const s = sessionsById[id];
    if (!s || !s.pendingAsk) { closeElicit(); return; }
  }
  maybeShowPendingForm(id);
}
```

- [ ] **Step 4: app.js — openDetail / sessions 스냅샷에서 폼 갱신**

`openDetail(id)`에서 삭제한 `scheduleAskExpiredGuidance(id);` 자리에 추가:
```javascript
  maybeShowPendingForm(id); // 미답 질문 있으면 즉시 답변 폼(터미널 열기 없음)
```
`ws.onmessage`의 `sessions` 분기(renderSessions 뒤)에서, 상세 화면일 때 폼을 갱신하도록 추가:
```javascript
      else if (m.type === 'sessions') {
        renderSessions(m.sessions);
        if (currentSessionId === null && document.getElementById('terminal').hidden) { showScreen('monitor'); refreshNotifyBtn(); }
        else if (currentSessionId) syncPendingForm(currentSessionId); // 연속 Q&A: pendingAsk로 폼 표시/정리
      }
```

- [ ] **Step 5: app.js — `pickerAnswerResult` dispatch + 결과 처리**

`ws.onmessage` dispatch 체인에 추가(`injectResult` 인근):
```javascript
      else if (m.type === 'pickerAnswerResult') { handlePickerAnswerResult(m); }
```
그리고 함수 추가(선행 기능의 `inject.hint*` 재사용):
```javascript
function handlePickerAnswerResult(m) {
  if (m.sessionId !== currentSessionId) return;
  if (m.ok) return; // 성공: 폼은 제출 시 이미 정리됨. pendingAsk가 곧 스냅샷에서 사라짐.
  const key = m.reason === 'noconsole' ? 'inject.hintNoConsole'
    : m.reason === 'nopid' ? 'inject.hintNoPid'
    : m.reason === 'engine' ? 'inject.hintCodex'
    : 'inject.hintFailed';
  alert(t(key)); // 만료 제출 실패 안내(간단히)
}
```

- [ ] **Step 6: app.js — 만료 답 수집기 추가**

`collectElicitAnswer` 근처에 추가(옵션 인덱스/커스텀 텍스트 산출):
```javascript
// 만료(pending) 폼 제출용: 선택을 옵션 인덱스 배열 + 커스텀 텍스트로 변환.
function collectPickerAnswer() {
  const q = elicit.questions[0];
  const opts = Array.isArray(q.options) ? q.options : [];
  const checked = Array.from(document.querySelectorAll('input[name="elicitOpt"]:checked'));
  if (checked.length === 0) return null;
  const indices = []; let text = null;
  checked.forEach(c => {
    if (c.value === ELICIT_OTHER) {
      const ta = document.getElementById('elicitOther');
      const v = ta && ta.value.trim();
      if (v) text = v;
    } else {
      indices.push(Number(c.value));
    }
  });
  if (indices.length === 0 && !text) return null;
  return { indices, text, optionCount: opts.length };
}
```

- [ ] **Step 7: app.js — 제출 핸들러 라우팅(라이브=elicitAnswer / 만료=pickerAnswer)**

`elicitNext` 클릭 핸들러를 아래로 교체(라이브는 기존 다단계 유지, 만료는 단일 질문 pickerAnswer):
```javascript
document.getElementById('elicitNext') && document.getElementById('elicitNext').addEventListener('click', () => {
  if (!elicit) return;
  if (elicit.id != null) {
    // 라이브 경로(기존): 다단계 수집 후 elicitAnswer
    const ans = collectElicitAnswer();
    if (ans == null) return;
    const q = elicit.questions[elicit.step];
    elicit.answers[q.question] = ans;
    if (elicit.step < elicit.questions.length - 1) { elicit.step++; renderElicitStep(); return; }
    send({ type: 'elicitAnswer', id: elicit.id, answers: elicit.answers });
    setWaiting(elicit.sessionId, false);
    document.getElementById('elicit').hidden = true; // elicit 유지(clawd answerBlocked 재시도 대비)
  } else {
    // 만료(pending) 경로: 단일 질문 → 콘솔 주입 제출
    const pa = collectPickerAnswer();
    if (pa == null) return;
    send({ type: 'pickerAnswer', sessionId: elicit.sessionId, indices: pa.indices, text: pa.text, optionCount: pa.optionCount });
    setWaiting(elicit.sessionId, false);
    closeElicit(); // 상태 정리 → 연속 Q&A는 다음 pendingAsk로 새 폼
  }
});
```

- [ ] **Step 8: 빌드 + node --check**

Run:
- `node --check AgentHub/View/Htmls/js/app.js`
- msbuild(자산 복사 확인) → `0 Error(s)`.

- [ ] **Step 9: 커밋**

```
git add AgentHub/View/Htmls/js/app.js AgentHub/View/Htmls/index.html
git commit -m "feat: Q&A 답변 통합 — 터미널 열기 제거, pendingAsk 폼 항상 표시, 만료=pickerAnswer 제출, 연속 Q&A 상태정리"
```

---

### Task 4: 사용 가이드 동기화 (`docs/index.html`)

**Files:**
- Modify: `docs/index.html`

- [ ] **Step 1: Q&A 가이드 문구 갱신**

`docs/index.html`의 Q&A(질문에 답하기) 관련 항목에서:
- "만료/창이 지나면 세션 터미널로 답하라"류 문구가 있으면 제거.
- "미답 질문은 항상 폰에서 **답변 폼**으로 제출됩니다. 라이브 질문은 즉시 반영되고, 답변 창이 지난(만료) 질문도 **콘솔로 전달**됩니다(옵션 선택·직접 입력 모두). Claude 세션이 cmd/PowerShell(고전 콘솔)에서 실행 중일 때 동작하며, Windows Terminal·Codex에서는 안내가 표시됩니다."로 ko/en 갱신.
- 기존 서식(li/span ko·en)에 맞춰 삽입. 정확한 위치·문구는 기존 "질문에 답하기" 항목 서식을 따른다.

- [ ] **Step 2: 확인 + 커밋**

브라우저로 `docs/index.html` 확인 후:
```
git add docs/index.html
git commit -m "docs: 가이드 Q&A 답변 통합 반영(터미널 열기 제거·만료 질문 콘솔 전달)"
```

---

## Self-Review

**Spec coverage:** §6A InjectPickerAnswer→Task1. §6B pickerAnswer 케이스+WatchMessage→Task2. §6C 프론트(askExpired 제거·pendingAsk 폼·제출 라우팅·결과·상태정리)→Task3. §6E 가이드→Task4. §5 데이터흐름·§7 reason·§8 lock 직렬화 모두 반영. ✅

**Placeholder scan:** 코드 스텝은 실제 코드 포함. Task4만 문서(기존 서식 준수 지시). "TBD" 없음.

**Type consistency:** `InjectPickerAnswer(int,int[],string,int)`/`Result{Ok,NoConsole,Failed}` Task1 정의 = Task2 사용 일치. `pickerAnswer{sessionId,indices,text,optionCount}` Task3 송신 = Task2 `WatchMessage.Indices/Text/OptionCount` 수신 일치. `pickerAnswerResult{type,sessionId,ok,reason}` Task2 생성 = Task3 소비 일치. reason→`inject.hint*` 키 매핑 일치(선행 기능 키 재사용). `elicit.fromPending`/`elicit.id==null` 분기 Task3 내부 일관. ✅

**주의(리뷰 관점):** Task3는 app.js 상태머신 변경이라 회귀 위험 — 라이브 elicit 경로(다단계·answerBlocked 재시도) 불변 확인, `elicit` 우선순위(라이브>pending, 폼 열림 중 재렌더 금지) 확인 필요.
