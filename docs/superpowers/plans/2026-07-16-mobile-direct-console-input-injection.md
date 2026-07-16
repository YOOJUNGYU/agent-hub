# 모바일 자유 텍스트 → 원본 세션 콘솔 직접 주입 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 모바일 세션 상세 화면 하단 입력창에서 자유 텍스트를 보내면, agent-hub이 해당 Claude 세션의 원본 프로세스를 종료하지 않고 그 콘솔 입력 버퍼에 텍스트(+Enter)를 직접 주입한다.

**Architecture:** 모바일 detail → 기존 `/ws/agents` 소켓으로 `{type:'inject', sessionId, text}` → `AgentMonitorModule`이 `SessionPidRegistry`로 원본 PID 조회 → 신규 `ConsoleInputInjector`가 `AttachConsole`+`WriteConsoleInputW`로 주입 → `injectResult` 회신. 기존 resume-PTY 경로(`/ws/session`)와 완전히 분리.

**Tech Stack:** C# .NET Framework 4.8 (WinExe, 구식 csproj), Win32 P/Invoke(kernel32/user32), EmbedIO WebSocket, 바닐라 JS 프론트(index.html/app.js/i18n.js/app.css).

**Spec:** `docs/superpowers/specs/2026-07-16-mobile-direct-console-input-injection-design.md`

## Global Constraints

- 응답/문구 언어: 사용자에게 보이는 문구는 한글 기본, i18n은 ko/en 양쪽 추가.
- 범위: **Claude 세션 전용.** Codex(데스크톱 앱=콘솔 없음)는 입력 바를 숨기고 대체 방법 안내만 표시.
- 전송 = **텍스트 + Enter(제출)**. 백엔드가 `appendEnter: true`로 처리.
- 유니코드 필수: 구조체/`WriteConsoleInput`은 반드시 `CharSet.Unicode`(=`WriteConsoleInputW`, 2바이트 `char`). 아니면 한글이 깨진다.
- `AttachConsole`은 프로세스 전역 상태 → 주입 전 구간을 `static` lock으로 직렬화.
- 기존 resume-PTY 경로(`SessionTerminalModule`, `/ws/session`)는 건드리지 않는다.
- 서드파티 `EmbedIO/`는 수정 금지. 자체 코드 네임스페이스 `AgentHub.*`.
- 신규 .cs 파일은 구식 `AgentHub/AgentHub.csproj`의 `<Compile Include>` 목록에 반드시 등록(자동 포함 아님).
- 기능 변경이므로 `docs/index.html` 사용 가이드를 같은 작업에서 갱신(CLAUDE.md 필수 규칙).
- 빌드 검증:
  ```powershell
  msbuild AgentHub.sln /t:Restore
  msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
  ```
  산출물: `install/Debug/AgentHub.exe`. (View/Htmls 자산은 빌드 시 출력으로 복사됨. `/sw.js`의 `{{VER}}`는 서버가 자산 해시로 치환해 캐시 자동 무효화 → SW 수동 버전업 불필요.)

## File Structure

- `AgentHub/Server/Terminal/ConsoleInputInjector.cs` — **신규.** 콘솔 입력 주입기(P/Invoke). 단일 책임: pid+text → 주입 결과.
- `AgentHub/AgentHub.csproj` — 위 신규 파일 `<Compile Include>` 등록.
- `AgentHub/Server/Socket/AgentMonitorModule.cs` — `WatchMessage.Text` 필드 + `inject` 케이스 + `injectResult` 회신.
- `AgentHub/View/Htmls/index.html` — detail 화면에 입력 바(`#injectBar`/`#injectHint`/`#injectRow`/`#injectInput`/`#injectSend`) 추가.
- `AgentHub/View/Htmls/css/app.css` — 입력 바 스타일.
- `AgentHub/View/Htmls/js/app.js` — 전송·결과 처리·엔진 가드·안내 렌더.
- `AgentHub/View/Htmls/js/i18n.js` — `inject.*` 키(ko/en).
- `docs/index.html` — 사용 가이드에 신규 기능·제약·대체 방법 문서화.

---

### Task 1: ConsoleInputInjector (콘솔 입력 주입기)

**Files:**
- Create: `AgentHub/Server/Terminal/ConsoleInputInjector.cs`
- Modify: `AgentHub/AgentHub.csproj` (Compile Include 등록, 현재 `Server\Terminal\*` 항목은 159–162행)

**Interfaces:**
- Produces:
  - `AgentHub.Server.Terminal.ConsoleInputInjector.Result` — enum `{ Ok, NoConsole, Failed }`
  - `ConsoleInputInjector.Result Inject(int pid, string text, bool appendEnter)`
  - `ConsoleInputInjector.KeyStroke MapChar(char c)` (pure, `struct { ushort Vk; char Ch; }`)

> **테스트 참고:** 이 클래스는 Win32 P/Invoke이며 `LogService`(NLog 의존)를 쓴다. `AgentHub.Tests`(net48, 소스 링크 방식)에 링크하면 NLog까지 끌려와 부적합하고, 실제 주입은 살아있는 콘솔이 필요해 단위 테스트 대상이 아니다(스펙 §9). 이 태스크의 검증은 **빌드 통과 + 통합 스파이크(고전 conhost cmd에 마커 명령 주입 → 파일 생성)**로 한다. 주입 메커니즘 자체는 2026-07-16 스파이크에서 ASCII·한글·raw-mode claude 모두 실증됨(스펙 §4).

- [ ] **Step 1: 주입기 파일 생성**

Create `AgentHub/Server/Terminal/ConsoleInputInjector.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using AgentHub.Common.Util;

namespace AgentHub.Server.Terminal
{
    /// <summary>
    /// 실행 중인 콘솔 프로세스(claude 세션)의 입력 버퍼에 텍스트/키를 직접 주입한다.
    /// 원본 프로세스를 종료하지 않는다(=/ws/session의 kill→resume과 별개).
    /// AttachConsole+WriteConsoleInput 기반 → 고전 conhost에서만 동작하고,
    /// ConPTY(Windows Terminal 등)에서는 AttachConsole 실패로 NoConsole을 반환한다.
    /// </summary>
    public static class ConsoleInputInjector
    {
        public enum Result { Ok, NoConsole, Failed }

        public struct KeyStroke { public ushort Vk; public char Ch; }

        /// <summary>문자 → (가상키코드, 유니코드문자). '\r'=VK_RETURN,
        /// 매핑 불가 문자(한글 등)는 vk 0 + UnicodeChar 유지(유니코드로 그대로 주입).</summary>
        public static KeyStroke MapChar(char c)
        {
            if (c == '\r') return new KeyStroke { Vk = 0x0D, Ch = c };
            short sc = VkKeyScan(c);
            ushort vk = sc == -1 ? (ushort)0 : (ushort)(sc & 0xFF);
            return new KeyStroke { Vk = vk, Ch = c };
        }

        private static readonly object _gate = new object();

        public static Result Inject(int pid, string text, bool appendEnter)
        {
            if (pid <= 0) return Result.Failed;
            string payload = (text ?? "") + (appendEnter ? "\r" : "");
            if (payload.Length == 0) return Result.Ok;

            lock (_gate)
            {
                bool attached = false;
                try
                {
                    FreeConsole(); // 우리(WinExe, 콘솔 없음)를 방어적으로 분리
                    if (!AttachConsole((uint)pid))
                        return Result.NoConsole; // err 6 등 — ConPTY이거나 대상 프로세스 종료
                    attached = true;

                    IntPtr hIn = GetStdHandle(STD_INPUT_HANDLE);
                    if (hIn == IntPtr.Zero || hIn == new IntPtr(-1)) return Result.Failed;

                    var records = new INPUT_RECORD[payload.Length * 2];
                    int i = 0;
                    foreach (char c in payload)
                    {
                        var k = MapChar(c);
                        records[i++] = KeyRecord(k, true);
                        records[i++] = KeyRecord(k, false);
                    }
                    bool ok = WriteConsoleInput(hIn, records, (uint)records.Length, out _);
                    return ok ? Result.Ok : Result.Failed;
                }
                catch (Exception ex) { LogService.Instance.Error(ex); return Result.Failed; }
                finally { if (attached) FreeConsole(); }
            }
        }

        private static INPUT_RECORD KeyRecord(KeyStroke k, bool down) => new INPUT_RECORD
        {
            EventType = KEY_EVENT,
            KeyEvent = new KEY_EVENT_RECORD
            {
                bKeyDown = down ? 1 : 0,
                wRepeatCount = 1,
                wVirtualKeyCode = k.Vk,
                wVirtualScanCode = 0,
                UnicodeChar = k.Ch,
                dwControlKeyState = 0
            }
        };

        private const int STD_INPUT_HANDLE = -10;
        private const ushort KEY_EVENT = 0x0001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct INPUT_RECORD { public ushort EventType; public KEY_EVENT_RECORD KeyEvent; }

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        private struct KEY_EVENT_RECORD
        {
            [FieldOffset(0)] public int bKeyDown;
            [FieldOffset(4)] public ushort wRepeatCount;
            [FieldOffset(6)] public ushort wVirtualKeyCode;
            [FieldOffset(8)] public ushort wVirtualScanCode;
            [FieldOffset(10)] public char UnicodeChar;
            [FieldOffset(12)] public uint dwControlKeyState;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WriteConsoleInputW")]
        private static extern bool WriteConsoleInput(IntPtr hConsoleInput, INPUT_RECORD[] lpBuffer, uint nLength, out uint written);
        [DllImport("user32.dll")] private static extern short VkKeyScan(char ch);
    }
}
```

- [ ] **Step 2: csproj에 등록**

`AgentHub/AgentHub.csproj`의 162행(`<Compile Include="Server\Terminal\TerminalGate.cs" />`) 바로 뒤에 추가:

```xml
    <Compile Include="Server\Terminal\ConsoleInputInjector.cs" />
```

- [ ] **Step 3: 빌드 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: 빌드 성공(0 Error), `install/Debug/AgentHub.exe` 갱신.

- [ ] **Step 4: 통합 검증(선택, 고전 conhost 스파이크)**

주입 메커니즘은 스펙 §4에서 이미 실증됨. 코드 변경(파라미터/유니코드) 재확인이 필요하면 스크래치패드의 `InjectSpike.exe`(고전 conhost cmd에 `echo ...> marker`·한글 주입 → 파일 생성)로 재현한다. 저장소에는 커밋하지 않는다.

- [ ] **Step 5: 커밋**

```powershell
git add AgentHub/Server/Terminal/ConsoleInputInjector.cs AgentHub/AgentHub.csproj
git commit -m "feat: 콘솔 입력 주입기 ConsoleInputInjector 추가(AttachConsole+WriteConsoleInputW)"
```

---

### Task 2: `/ws/agents` inject 핸들러 + injectResult 회신

**Files:**
- Modify: `AgentHub/Server/Socket/AgentMonitorModule.cs` (`OnMessageReceivedAsync` 60–105행 dispatch 체인, `WatchMessage` 213–220행)

**Interfaces:**
- Consumes: `ConsoleInputInjector.Inject` / `.Result` (Task 1), `SessionPidRegistry.TryGet(string, out int)` (`AgentHub.Server.Hook`), `AgentMonitorService.EngineOf(string)` (`AgentHub.Server.Agents`).
- Produces (프론트가 소비): 서버→클라 메시지 `{ type: "injectResult", sessionId: string, ok: bool, reason: string|null }`. `reason` ∈ `"engine" | "nopid" | "noconsole" | "failed"`(성공 시 null).

- [ ] **Step 1: WatchMessage에 Text 필드 추가**

`AgentMonitorModule.cs`의 `WatchMessage` 클래스(220행 `Answers` 속성 아래)에 추가:

```csharp
        public string Text { get; set; }      // inject: 세션 콘솔에 주입할 자유 텍스트
```

- [ ] **Step 2: inject 케이스 추가**

`OnMessageReceivedAsync`의 dispatch 체인에서 `elicitAnswer` 블록(91–101행) 바로 뒤, `// 세션 제어...` 주석(102행) 앞에 추가:

```csharp
                else if (msg.Type == "inject" && !string.IsNullOrEmpty(msg.SessionId) && msg.Text != null)
                {
                    // 원본 kill 없이 세션 콘솔에 직접 주입(Claude 전용). /ws/session의 resume 경로와 별개.
                    bool ok = false; string reason;
                    if (AgentMonitorService.EngineOf(msg.SessionId) != "claude")
                        reason = "engine"; // Codex 등: 콘솔 없음 → 미지원
                    else if (!AgentHub.Server.Hook.SessionPidRegistry.TryGet(msg.SessionId, out var pid))
                        reason = "nopid";  // PID 미보고(세션 종료/훅 미실행)
                    else
                    {
                        var r = AgentHub.Server.Terminal.ConsoleInputInjector.Inject(pid, msg.Text, appendEnter: true);
                        ok = r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.Ok;
                        reason = ok ? null
                            : (r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.NoConsole ? "noconsole" : "failed");
                    }
                    await SendSafe(context, Json.Serialize(new
                    {
                        type = "injectResult", sessionId = msg.SessionId, ok, reason
                    }));
                }
```

- [ ] **Step 3: 빌드 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: 빌드 성공(0 Error).

- [ ] **Step 4: 수동 e2e 검증(프론트 완성 후 Task 3 검증과 함께 수행 가능)**

`install/Debug/AgentHub.exe` 실행 → 고전 cmd/PowerShell 창에서 `claude` 세션 하나 실행(입력 대기 상태) → 모바일 상세에서 텍스트 전송 시 그 세션에 입력·제출되고 `injectResult{ok:true}`가 오는지 확인. (프론트가 아직이면 이 단계는 Task 3에서 함께 확인.)

- [ ] **Step 5: 커밋**

```powershell
git add AgentHub/Server/Socket/AgentMonitorModule.cs
git commit -m "feat: /ws/agents inject 메시지로 세션 콘솔 직접 주입 + injectResult 회신"
```

---

### Task 3: 모바일 detail 입력 UI + 전송/결과/엔진 가드/안내

**Files:**
- Modify: `AgentHub/View/Htmls/index.html` (detail 섹션 98–106행)
- Modify: `AgentHub/View/Htmls/css/app.css` (파일 끝에 스타일 추가)
- Modify: `AgentHub/View/Htmls/js/app.js` (dispatch 114–128행, `openDetail` 240–253행, `backToList` 256–262행)
- Modify: `AgentHub/View/Htmls/js/i18n.js` (ko 블록 9행~, en 블록 145행~)

**Interfaces:**
- Consumes: 서버 메시지 `injectResult`(Task 2). 세션 요약 `sessionsById[id].engine`("claude"|"codex").
- Produces: 클라→서버 메시지 `send({ type:'inject', sessionId, text })`.

- [ ] **Step 1: detail 화면에 입력 바 추가**

`index.html`에서 `<div class="activity-feed" id="activityFeed"></div>`(105행) 바로 뒤, `</section>`(106행) 앞에 추가:

```html
      <div class="inject-bar" id="injectBar" hidden>
        <div class="inject-hint" id="injectHint" hidden></div>
        <div class="inject-row" id="injectRow">
          <input id="injectInput" class="inject-input" type="text" autocomplete="off"
                 data-i18n-ph="inject.placeholder" placeholder="답변 입력…">
          <button id="injectSend" class="inject-send" data-i18n="inject.send">전송</button>
        </div>
      </div>
```

- [ ] **Step 2: i18n 키 추가(ko/en)**

`i18n.js`의 `ko:` 블록(9행 이후 적절한 위치, 예: `term.*` 근처)에 추가:

```javascript
      'inject.placeholder': '답변 입력…',
      'inject.send': '전송',
      'inject.hintCodex': 'Codex(데스크톱 앱) 세션은 모바일 직접 입력을 지원하지 않습니다. PC의 Codex 앱에서 답하거나 “터미널 열기”(이어받기)로 진행하세요.',
      'inject.hintNoConsole': '이 세션은 직접 입력이 안 되는 터미널(Windows Terminal 등)에서 실행 중입니다. cmd.exe 또는 PowerShell 창에서 claude를 실행하면 모바일에서 바로 답할 수 있어요. (또는 “터미널 열기”로 이어받아 진행)',
      'inject.hintNoPid': '실행 중인 세션 프로세스를 찾을 수 없습니다(종료됐거나 훅 미보고). PC에서 직접 답하거나 세션을 다시 시작하세요.',
      'inject.hintFailed': '전송에 실패했습니다. 잠시 후 다시 시도해 주세요.',
```

`en:` 블록(145행 이후 대응 위치)에 추가:

```javascript
      'inject.placeholder': 'Type a reply…',
      'inject.send': 'Send',
      'inject.hintCodex': 'Codex (desktop app) sessions do not support direct input from mobile. Answer in the Codex app on your PC, or use “Open terminal” (resume).',
      'inject.hintNoConsole': 'This session runs in a terminal that does not accept direct input (e.g. Windows Terminal). Run claude in a cmd.exe or PowerShell window and you can answer from mobile. (Or use “Open terminal” to resume.)',
      'inject.hintNoPid': 'Could not find the running session process (ended or hook not reported). Answer on your PC or restart the session.',
      'inject.hintFailed': 'Send failed. Please try again in a moment.',
```

- [ ] **Step 3: app.js — 전송/결과/엔진 가드 로직 추가**

`app.js`의 `send`/`esc` 헬퍼(288행) 근처(예: 296행 `rel` 함수 뒤)에 함수 추가:

```javascript
// ---- 세션 콘솔 직접 주입(자유 텍스트 답변) ----
function showInjectHint(key) {
  const hint = document.getElementById('injectHint');
  if (!hint) return;
  hint.textContent = key ? t(key) : '';
  hint.hidden = !key;
}
// 상세 진입/세션 전환 시 입력 바 상태 초기화. codex면 입력줄 숨기고 안내만.
function updateInjectBar(id) {
  const bar = document.getElementById('injectBar');
  const row = document.getElementById('injectRow');
  const input = document.getElementById('injectInput');
  if (!bar) return;
  bar.hidden = false;
  if (input) input.value = '';
  const isCodex = sessionsById[id] && sessionsById[id].engine === 'codex';
  if (row) row.hidden = !!isCodex;
  showInjectHint(isCodex ? 'inject.hintCodex' : null);
}
function sendInject() {
  const input = document.getElementById('injectInput');
  if (!input || !currentSessionId) return;
  const v = input.value;
  if (!v) return;
  send({ type: 'inject', sessionId: currentSessionId, text: v });
  // 회신(injectResult)에서 성공 시 비운다.
}
function handleInjectResult(m) {
  if (m.sessionId !== currentSessionId) return;
  const input = document.getElementById('injectInput');
  if (m.ok) { if (input) input.value = ''; showInjectHint(null); return; }
  const key = m.reason === 'noconsole' ? 'inject.hintNoConsole'
    : m.reason === 'nopid' ? 'inject.hintNoPid'
    : m.reason === 'engine' ? 'inject.hintCodex'
    : 'inject.hintFailed';
  showInjectHint(key);
}
document.getElementById('injectSend') && document.getElementById('injectSend').addEventListener('click', sendInject);
document.getElementById('injectInput') && document.getElementById('injectInput').addEventListener('keydown', e => {
  if (e.key === 'Enter') { e.preventDefault(); sendInject(); }
});
```

- [ ] **Step 4: app.js — dispatch에 injectResult 연결**

`ws.onmessage`의 dispatch 체인(127행 `answerBlocked` 뒤)에 추가:

```javascript
      else if (m.type === 'injectResult') { handleInjectResult(m); }
```

- [ ] **Step 5: app.js — openDetail에서 입력 바 초기화**

`openDetail(id)`의 `updateDetailRun();`(248행) 바로 뒤에 추가:

```javascript
  updateInjectBar(id); // 입력 바 상태(codex 숨김/안내) 초기화
```

- [ ] **Step 6: app.js — backToList에서 입력 바 숨김**

`backToList()`의 `showScreen('monitor');`(260행) 앞에 추가:

```javascript
  { const bar = document.getElementById('injectBar'); if (bar) bar.hidden = true; }
```

- [ ] **Step 7: app.css — 입력 바 스타일**

`AgentHub/View/Htmls/css/app.css` 파일 끝에 추가(기존 변수/톤에 맞춘 최소 스타일):

```css
/* 세션 상세 하단 자유 텍스트 주입 입력 바 */
.inject-bar { padding: 8px 12px calc(8px + env(safe-area-inset-bottom)); border-top: 1px solid var(--line, #2a2f42); background: var(--panel, #181c2a); }
.inject-hint { font-size: 12px; line-height: 1.4; color: var(--muted, #9aa3bd); margin-bottom: 6px; }
.inject-row { display: flex; gap: 8px; }
.inject-input { flex: 1; min-width: 0; padding: 10px 12px; border-radius: 10px; border: 1px solid var(--line, #2a2f42); background: var(--bg, #12151f); color: var(--fg, #e7ecf8); font-size: 15px; }
.inject-send { flex: none; padding: 10px 16px; border-radius: 10px; border: none; background: var(--accent, #5b7cfa); color: #fff; font-size: 15px; font-weight: 600; }
.inject-send:active { opacity: .85; }
```
> CSS 변수명(`--line`/`--panel`/`--muted`/`--accent` 등)이 app.css의 실제 정의와 다르면 파일 상단의 `:root` 변수에 맞춰 교체한다(폴백값이 있어 미정의여도 동작).

- [ ] **Step 8: 빌드 + 실행 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
그 후 `install/Debug/AgentHub.exe` 실행.

- [ ] **Step 9: 수동 e2e 검증**

1. **정상(Claude/conhost)**: cmd 또는 PowerShell 창에서 `claude` 세션 실행(입력 대기) → 모바일에서 그 세션 상세 열기 → 하단 입력창에 한글 답변 입력 후 전송/Enter → PC 세션에 그대로 입력·제출됨, 입력창 비워짐.
2. **Codex**: Codex 세션 상세 → 입력줄 숨김 + `inject.hintCodex` 안내 표시.
3. **noconsole(ConPTY)**: Windows Terminal에서 실행한 claude 세션에 전송 → `inject.hintNoConsole` 안내(“cmd/PowerShell로 실행하세요”) 표시.
4. 목록으로 나갔다 다른 세션 진입 시 입력창/안내가 올바르게 초기화되는지.

- [ ] **Step 10: 커밋**

```powershell
git add AgentHub/View/Htmls/index.html AgentHub/View/Htmls/css/app.css AgentHub/View/Htmls/js/app.js AgentHub/View/Htmls/js/i18n.js
git commit -m "feat: 모바일 세션 상세 하단 자유 텍스트 입력창(직접 주입) + 불가 세션 대체 방법 안내"
```

---

### Task 4: 사용 가이드 동기화 (`docs/index.html`)

**Files:**
- Modify: `docs/index.html`

**Interfaces:** 없음(문서).

- [ ] **Step 1: 가이드에 신규 기능 문서화**

`docs/index.html`의 모바일 사용 흐름/세션 상세 관련 섹션에, 기존 문서 톤·구조를 따라 다음 내용을 추가한다:
- 세션 상세 화면 하단 **입력창으로 자유 텍스트 답변을 원본 세션에 직접 전송**(전송 시 Enter까지 제출)한다는 설명.
- 제약: **Claude 세션 + 고전 콘솔(cmd.exe/PowerShell 창)**에서만 동작. Windows Terminal 등 ConPTY에서 실행한 세션은 직접 입력이 안 되며, 이때는 cmd/PowerShell 창에서 `claude`를 실행하라는 **대체 방법 안내**가 표시됨.
- **Codex(데스크톱 앱)** 세션은 콘솔이 없어 미지원(입력창 숨김, 안내 표시).

> 정확한 문구/삽입 위치는 `docs/index.html`의 기존 "모바일에서 답변하기"류 섹션을 열어 그 서식에 맞춰 작성한다. GitHub Pages(홈페이지) 겸 앱 `/guide.html`의 단일 소스이므로 과장 없이 사실대로.

- [ ] **Step 2: 확인**

브라우저로 `docs/index.html`를 열어 추가 섹션이 기존 스타일과 어울리고, 제약(conhost 전용·Codex 미지원)이 명확한지 확인.

- [ ] **Step 3: 커밋**

```powershell
git add docs/index.html
git commit -m "docs: 사용 가이드에 모바일 세션 직접 입력(콘솔 주입) 기능·제약·대체 방법 추가"
```

---

## Self-Review

**Spec coverage:**
- §6A ConsoleInputInjector → Task 1. §6B inject 핸들러/WatchMessage.Text/injectResult → Task 2. §6C 프론트(입력 바·엔진 가드·안내·전송·결과) → Task 3. §6D 가이드 → Task 4. §6C "안내 문구" 표 → Task 3 Step 2 i18n. §7 reason 표 → Task 2(생성)·Task 3(표시). §8 동시성 lock/§Global Constraints 유니코드 → Task 1 코드. ✅ 누락 없음.

**Placeholder scan:** 모든 코드 스텝에 실제 코드 포함. "TBD/TODO/적절히 처리" 없음. Task 4만 문서라 정확 문구를 기존 파일 서식에 맞추도록 지시(코드 아님).

**Type consistency:** `ConsoleInputInjector.Inject`/`.Result{Ok,NoConsole,Failed}`/`MapChar`는 Task 1 정의와 Task 2 사용이 일치. `injectResult{type,sessionId,ok,reason}`는 Task 2 생성과 Task 3 소비가 일치. `reason` 값(`engine|nopid|noconsole|failed`)과 프론트 매핑 키(`hintCodex|hintNoPid|hintNoConsole|hintFailed`) 일치. `SessionPidRegistry.TryGet(string,out int)`·`AgentMonitorService.EngineOf(string)` 실제 시그니처와 일치. ✅

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-16-mobile-direct-console-input-injection.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — 태스크마다 새 subagent 파견 + 태스크 간 리뷰, 빠른 반복.

**2. Inline Execution** — 이 세션에서 executing-plans로 배치 실행 + 체크포인트 리뷰.

**어느 방식으로 진행할까요?**
