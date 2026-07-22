# 모바일 세션 상세 답변 UX 개선 + 세션연결(재실행) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 모바일 세션 상세 화면에서 내 답변을 시각 구분하고, 여러 줄 textarea·전송 확인 기반 활성화(중복 전송 방지)·로딩을 도입하며, 직접입력이 안 되는 claude 세션은 ‘세션연결’ 버튼으로 PC에서 `claude --resume`을 고전 콘솔에 재실행해 주입 가능 상태로 되돌린다.

**Architecture:** 대부분 클라이언트(HTML/CSS/JS) 변경. 서버는 세션 요약에 `Injectable` 플래그 추가 + `/ws/agents`에 `reopen` 케이스 + `SessionReopener`(conhost 강제 resume 실행) 신규. 재개된 세션이 같은 sessionId로 PID를 재보고하면 스냅샷의 `injectable`이 true가 되어 UI가 자동으로 일반 입력창으로 복귀한다.

**Tech Stack:** C#/.NET Framework(EmbedIO, Newtonsoft.Json, xUnit), 정적 HTML/CSS/Vanilla JS PWA. 빌드: MSBuild.

## Global Constraints

- 응답/주석 등 한글 문자열은 UTF-8 유지. 문자열 치환 시 인코딩 훼손 금지(Edit 도구 사용).
- 자체 코드 네임스페이스 `AgentHub.*`. `EmbedIO/`는 수정 금지.
- JSON 직렬화는 camelCase(`Common/Util/Json.cs`) → C# `Injectable` 프로퍼티는 클라이언트에서 `s.injectable`로 읽힌다.
- 기능/UI 변경은 **같은 작업에서 `docs/index.html` 가이드 갱신 필수**(누락 시 미완성).
- 세션연결은 **claude 전용 · LAN 승인기기 한정**. 임의 셸 실행 아님(sessionId 하나로 고정 커맨드 구성). 라이브 attach/스트리밍 없음.
- 빌드 검증: `msbuild AgentHub.sln /t:Restore` → `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`, 산출물 `install/Debug/AgentHub.exe`.
- 테스트: `dotnet test` 또는 `msbuild` 후 xUnit 실행(기존 `AgentHub.Tests`).
- JS/CSS/HTML은 자동화 테스트 러너가 없다 → 검증은 빌드 통과 + 수동 브라우저 확인.

## File Structure

- `AgentHub/View/Htmls/index.html` — `#injectInput` textarea 교체, 세션연결 블록 마크업, 전송 스피너.
- `AgentHub/View/Htmls/css/app.css` — 내 답변 카드, textarea 6줄, 전송 로딩/비활성, 세션연결 버튼.
- `AgentHub/View/Htmls/js/app.js` — textarea auto-grow/키 규칙, 전송 상태머신, injectable 기반 UI 분기, `reopen`/`reopenResult` 처리.
- `AgentHub/View/Htmls/js/i18n.js` — `session.*` 키(ko/en) + 힌트 문구 조정.
- `AgentHub/View/Htmls/sw.js` — 프리캐시 버전 bump(변경 자산 강제 갱신).
- `AgentHub/Common/Models/SessionSummary.cs` — `Injectable` 필드.
- `AgentHub/Server/Agents/AgentMonitorService.cs` — `IsInjectable` 규칙 + 요약 채우기.
- `AgentHub/Server/Terminal/SessionReopener.cs` — 신규(conhost 강제 resume 실행).
- `AgentHub/Server/Socket/AgentMonitorModule.cs` — `reopen`/`reopenResult` 케이스.
- `AgentHub.Tests/SessionInjectableTests.cs` — 신규(`IsInjectable`, `SessionReopener.IsValidSessionId` 단위 테스트).
- `docs/index.html` — 사용 가이드 갱신.

---

### Task 1: 내가 작성한 답변 카드 시각 구분 (CSS)

활동 피드는 `display:flex; flex-direction:column`이므로 `.ev-user_prompt`에 `align-self:flex-end`만 주면 오른쪽 말풍선이 된다. JS/DOM 변경 없음(수술적).

**Files:**
- Modify: `AgentHub/View/Htmls/css/app.css` (`.ev-user_prompt` 근처 218행, 라이트 오버라이드 389행)

**Interfaces:**
- Consumes: 기존 `evHtml`가 붙이는 `.ev.ev-user_prompt` 클래스, CSS 변수 `--accent`.
- Produces: 없음(스타일만).

- [ ] **Step 1: `.ev-user_prompt` 다크 스타일 확장**

`css/app.css`의 `.ev-user_prompt { border-left-color: #fbbf24; }`(218행 부근)를 아래로 교체:

```css
.ev-user_prompt {
  align-self: flex-end;
  max-width: 85%;
  background: #1c2740;
  border-left: 3px solid #7aa2ff;
  border-radius: 10px 10px 4px 10px;
}
.ev-user_prompt .ev-summary { color: #cdd8f2; }
```

- [ ] **Step 2: 라이트 테마 오버라이드 확장**

`[data-theme="light"] .ev-user_prompt{ border-left-color:#d98a1a; }`(389행 부근)를 아래로 교체:

```css
[data-theme="light"] .ev-user_prompt {
  background: #e8effc;
  border-left-color: #3b6fd4;
}
[data-theme="light"] .ev-user_prompt .ev-summary { color: #1f2b45; }
```

- [ ] **Step 3: 빌드 + 수동 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공. 앱 실행 후 세션 상세에서 내 답변(`user_prompt`)이 오른쪽 파란 말풍선으로, assistant/도구 이벤트는 기존 왼쪽 정렬로 보인다(다크·라이트 모두).

- [ ] **Step 4: Commit**

```bash
git add AgentHub/View/Htmls/css/app.css
git commit -m "feat: 세션 상세에서 내 답변 카드를 오른쪽 말풍선으로 구분"
```

---

### Task 2: 답변 입력 영역 textarea 교체 (최대 6줄 + Enter 규칙)

**Files:**
- Modify: `AgentHub/View/Htmls/index.html:108-109` (`#injectInput`)
- Modify: `AgentHub/View/Htmls/css/app.css` (`.inject-input` 440행 부근)
- Modify: `AgentHub/View/Htmls/js/app.js` (keydown 핸들러 396-398행, auto-grow 신규)

**Interfaces:**
- Consumes: 기존 `sendInject()`(`input.value` 읽음 — textarea도 `.value` 동일).
- Produces: `autoGrowInject()` 함수(입력 시 높이 재계산). Task 3·9에서 값 변경 후 호출.

- [ ] **Step 1: input → textarea 교체 (index.html)**

108-109행의 `<input …>`을 아래로 교체(id/class/placeholder 유지, `rows="1"`):

```html
          <textarea id="injectInput" class="inject-input" rows="1" autocomplete="off"
                 data-i18n-ph="inject.placeholder" placeholder="답변 입력…"></textarea>
```

- [ ] **Step 2: textarea 6줄 CSS (app.css)**

`.inject-input { … }`(440행 부근)을 아래로 교체:

```css
.inject-input {
  flex: 1; min-width: 0; padding: 9px 0; border: none; background: transparent;
  color: var(--fore); font-size: 16px; line-height: 1.35;
  resize: none; overflow-y: auto;
  max-height: calc(6 * 1.35 * 16px + 18px); /* 6줄 + 상하 패딩 */
  font-family: inherit;
}
```

- [ ] **Step 3: auto-grow + Enter/Shift+Enter (app.js)**

`sendInject`/`handleInjectResult` 근처(368행 부근)에 auto-grow 함수를 추가:

```js
// textarea 높이를 내용에 맞춰 재계산(최대 높이는 CSS max-height가 clamp, 초과 시 스크롤).
function autoGrowInject() {
  const ta = document.getElementById('injectInput');
  if (!ta) return;
  ta.style.height = 'auto';
  ta.style.height = ta.scrollHeight + 'px';
}
```

396-398행의 keydown 핸들러를 아래로 교체(Shift+Enter=줄바꿈, Enter=전송) + input 리스너 추가:

```js
document.getElementById('injectInput') && document.getElementById('injectInput').addEventListener('keydown', e => {
  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendInject(); }
});
document.getElementById('injectInput') && document.getElementById('injectInput').addEventListener('input', autoGrowInject);
```

- [ ] **Step 4: 빌드 + 수동 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공. 상세 하단 입력이 여러 줄 입력되며 6줄까지 커지고 이후 세로 스크롤. Enter로 전송, Shift+Enter로 줄바꿈.

- [ ] **Step 5: Commit**

```bash
git add AgentHub/View/Htmls/index.html AgentHub/View/Htmls/css/app.css AgentHub/View/Htmls/js/app.js
git commit -m "feat: 답변 입력을 textarea(최대 6줄)로 교체, Enter 전송/Shift+Enter 줄바꿈"
```

---

### Task 3: 전송 상태머신 + 로딩 + 안전망 (중복 전송 방지)

**Files:**
- Modify: `AgentHub/View/Htmls/index.html:110-113` (전송 버튼에 스피너 추가)
- Modify: `AgentHub/View/Htmls/css/app.css` (`.inject-send` 근처 443행 부근)
- Modify: `AgentHub/View/Htmls/js/app.js` (`sendInject` 368행, `handleInjectResult` 376행)

**Interfaces:**
- Consumes: `autoGrowInject()`(Task 2), `send()`, `showInjectHint()`.
- Produces: `injectSending`(모듈 bool), `setInjectSending(on)`. Task 9의 `handleInjectResult` 확장이 재사용.

- [ ] **Step 1: 전송 버튼에 스피너 요소 추가 (index.html)**

110-112행 `<button id="injectSend" …> … </button>` 안, `</svg>` 다음에 스피너 span을 추가(버튼 닫기 전):

```html
            <span class="btn-spinner" aria-hidden="true"></span>
```

- [ ] **Step 2: 로딩/비활성 CSS (app.css)**

`.inject-send:active { opacity: .6; }`(447행 부근) 아래에 추가:

```css
.btn-spinner { display: none; width: 18px; height: 18px; border: 2px solid var(--muted);
  border-top-color: transparent; border-radius: 50%; animation: btnspin .7s linear infinite; }
@keyframes btnspin { to { transform: rotate(360deg); } }
.inject-bar.sending .inject-send > svg { display: none; }
.inject-bar.sending .inject-send > .btn-spinner { display: block; }
.inject-input:disabled { opacity: .55; }
.inject-send:disabled { cursor: default; opacity: .6; }
```

- [ ] **Step 3: 상태머신 구현 (app.js)**

`sendInject`(368-375행)를 아래로 교체:

```js
let injectSending = false;
let injectTimer = null;
function setInjectSending(on) {
  injectSending = on;
  const bar = document.getElementById('injectBar');
  const ta = document.getElementById('injectInput');
  const btn = document.getElementById('injectSend');
  if (bar) bar.classList.toggle('sending', on);
  if (ta) ta.disabled = on;
  if (btn) btn.disabled = on;
  if (!on && injectTimer) { clearTimeout(injectTimer); injectTimer = null; }
}
function sendInject() {
  const input = document.getElementById('injectInput');
  if (!input || !currentSessionId || injectSending) return; // 전송 중이면 무시(중복 차단)
  const v = input.value;
  if (!v.trim()) return;
  setInjectSending(true);
  showInjectHint(null);
  send({ type: 'inject', sessionId: currentSessionId, text: v });
  // 안전망: 회신이 없으면 무한 비활성 방지 후 재시도 안내.
  injectTimer = setTimeout(() => {
    if (injectSending) { setInjectSending(false); showInjectHint('inject.hintFailed'); }
  }, 12000);
  // 성공/실패는 injectResult 회신(handleInjectResult)에서 처리.
}
```

`handleInjectResult`(376-385행)를 아래로 교체(ok 시 비우고 재활성화; 실패는 Task 9에서 확장):

```js
function handleInjectResult(m) {
  if (m.sessionId !== currentSessionId) return;
  setInjectSending(false);
  const input = document.getElementById('injectInput');
  if (m.ok) { if (input) { input.value = ''; autoGrowInject(); } showInjectHint(null); return; }
  const key = m.reason === 'noconsole' ? 'inject.hintNoConsole'
    : m.reason === 'nopid' ? 'inject.hintNoPid'
    : m.reason === 'engine' ? 'inject.hintCodex'
    : 'inject.hintFailed';
  showInjectHint(key);
}
```

- [ ] **Step 4: 빌드 + 수동 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공. 전송하면 입력창·버튼이 비활성 + 스피너. 전송 중 Enter/클릭 연타가 무시된다. 성공 회신 시에만 입력창이 비워지고 재활성화. 회신 지연(12초) 시 재활성화 + 실패 힌트.

- [ ] **Step 5: Commit**

```bash
git add AgentHub/View/Htmls/index.html AgentHub/View/Htmls/css/app.css AgentHub/View/Htmls/js/app.js
git commit -m "feat: 답변 전송 확인 기반 활성화·로딩 표시로 중복 전송 차단"
```

---

### Task 4: i18n `session.*` 키 추가 + 힌트 문구 조정

**Files:**
- Modify: `AgentHub/View/Htmls/js/i18n.js` (ko 블록 132행 부근, en 블록 251행 부근)

**Interfaces:**
- Produces: i18n 키 `session.connect`, `session.connecting`, `session.connectDesc`, `session.connectConfirm`, `session.reopenFailed`. Task 6·9가 사용.

- [ ] **Step 1: ko 키 추가 (i18n.js)**

`'qna.sendFailed': '전송에 실패했습니다. PC에서 답해 주세요.'`(133행) 뒤에 콤마 추가 후 삽입:

```js
      'session.connect': '세션연결',
      'session.connecting': '연결 중…',
      'session.connectDesc': '이 세션은 직접 입력이 안 됩니다. ‘세션연결’을 누르면 PC에서 세션을 다시 열어 답변할 수 있어요.',
      'session.connectConfirm': 'PC에서 이 세션을 다시 열까요? (claude --resume 실행)',
      'session.reopenFailed': '세션 재실행에 실패했습니다. PC에서 직접 열어 주세요.'
```

- [ ] **Step 2: en 키 추가 (i18n.js)**

`'qna.sendFailed': 'Send failed — please answer on the PC.'`(252행) 뒤에 콤마 추가 후 삽입:

```js
      'session.connect': 'Connect session',
      'session.connecting': 'Connecting…',
      'session.connectDesc': "This session can't take direct input. Tap \"Connect session\" to reopen it on the PC and answer here.",
      'session.connectConfirm': 'Reopen this session on the PC? (runs claude --resume)',
      'session.reopenFailed': "Couldn't reopen the session. Please open it on the PC."
```

- [ ] **Step 3: 빌드 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공(정적 자산 복사). i18n.js JSON 구조 문법 오류 없음(브라우저 콘솔에 파싱 에러 없음).

- [ ] **Step 4: Commit**

```bash
git add AgentHub/View/Htmls/js/i18n.js
git commit -m "feat: 세션연결 i18n 키(ko/en) 추가"
```

---

### Task 5: `SessionSummary.Injectable` + 서버 요약 채우기 + 단위 테스트

**Files:**
- Modify: `AgentHub/Common/Models/SessionSummary.cs`
- Modify: `AgentHub/Server/Agents/AgentMonitorService.cs:20-29` (`CurrentSessions`)
- Test: `AgentHub.Tests/SessionInjectableTests.cs` (신규)

**Interfaces:**
- Consumes: `SessionPidRegistry.TryGet(id, out pid)`(`AgentHub.Server.Hook`).
- Produces: `SessionSummary.Injectable`(bool, 직렬화 시 `injectable`), `AgentMonitorService.IsInjectable(string engine, bool hasPid)`.

- [ ] **Step 1: 실패 테스트 작성 (신규 파일)**

`AgentHub.Tests/SessionInjectableTests.cs`:

```csharp
using AgentHub.Server.Agents;
using Xunit;

namespace AgentHub.Tests
{
    public class SessionInjectableTests
    {
        [Theory]
        [InlineData("claude", true, true)]   // claude + PID 있음 → 주입 가능
        [InlineData("claude", false, false)] // claude + PID 없음 → 불가(세션연결)
        [InlineData("codex", true, false)]   // codex → 콘솔 없음, 불가
        [InlineData("codex", false, false)]
        [InlineData(null, true, false)]
        public void IsInjectable_rule(string engine, bool hasPid, bool expected)
        {
            Assert.Equal(expected, AgentMonitorService.IsInjectable(engine, hasPid));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj --filter SessionInjectableTests`
Expected: 컴파일 실패(`IsInjectable` 미정의).

- [ ] **Step 3: `Injectable` 필드 추가 (SessionSummary.cs)**

`public PendingAsk PendingAsk { get; set; }`(21행) 뒤에 추가:

```csharp
        public bool Injectable { get; set; }   // claude + 살아있는 PID → 모바일 직접 주입 가능(세션연결 판별)
```

- [ ] **Step 4: `IsInjectable` + 요약 채우기 (AgentMonitorService.cs)**

`CurrentSessions()`(20-29행)를 아래로 교체:

```csharp
        public static List<SessionSummary> CurrentSessions()
        {
            var merged = new List<SessionSummary>();
            merged.AddRange(ClaudeSessionReader.ListSessions());
            if (CodexSessionReader.Available) merged.AddRange(CodexSessionReader.ListSessions());
            var list = merged
                .OrderByDescending(s => s.LastActivityAt ?? "", StringComparer.Ordinal)
                .Take(MaxSessions)
                .ToList();
            foreach (var s in list)
                s.Injectable = IsInjectable(s.Engine, Hook.SessionPidRegistry.TryGet(s.Id, out _));
            return list;
        }

        /// <summary>모바일 직접 주입 가능 여부(세션연결 판별): claude 엔진 + 살아있는 PID.</summary>
        public static bool IsInjectable(string engine, bool hasPid) => engine == "claude" && hasPid;
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj --filter SessionInjectableTests`
Expected: PASS(5 케이스).

- [ ] **Step 6: Commit**

```bash
git add AgentHub/Common/Models/SessionSummary.cs AgentHub/Server/Agents/AgentMonitorService.cs AgentHub.Tests/SessionInjectableTests.cs
git commit -m "feat: 세션 요약에 injectable 플래그(claude+PID) 추가"
```

---

### Task 6: 클라이언트 injectable 기반 UI 분기 (세션연결 버튼 노출)

이 태스크는 UI 분기까지만. `reopen` 전송/처리는 Task 9.

**Files:**
- Modify: `AgentHub/View/Htmls/index.html` (inject-bar 안에 세션연결 블록)
- Modify: `AgentHub/View/Htmls/css/app.css` (세션연결 버튼)
- Modify: `AgentHub/View/Htmls/js/app.js` (`updateInjectBar` → `refreshInjectBar`, 상태 변수)

**Interfaces:**
- Consumes: `sessionsById[id].injectable`, `sessionsById[id].engine`, i18n `session.*`.
- Produces: `refreshInjectBar(id)`, 모듈 `reopening`(bool), `injectFailedSet`(Set). Task 9가 사용.

- [ ] **Step 1: 세션연결 블록 마크업 (index.html)**

`#injectBar` 안, `#injectRow` div(107-113행)가 끝나는 `</div>` 바로 뒤(= `#injectBar` 닫기 전)에 추가:

```html
        <div class="session-connect" id="sessionConnect" hidden>
          <div class="session-connect-desc" data-i18n="session.connectDesc"></div>
          <button id="sessionConnectBtn" class="session-connect-btn" type="button" data-i18n="session.connect"></button>
        </div>
```

- [ ] **Step 2: 세션연결 버튼 CSS (app.css)**

`.inject-bar { … }`(434행 부근) 뒤에 추가:

```css
.session-connect { padding: 4px 6px 8px; }
.session-connect-desc { font-size: 12px; line-height: 1.45; color: var(--muted); margin: 0 0 8px; }
.session-connect-btn { width: 100%; padding: 11px; border: none; border-radius: 22px;
  background: var(--accent); color: #fff; font-size: 15px; font-weight: 600; cursor: pointer; }
.session-connect-btn:disabled { opacity: .6; cursor: default; }
```

- [ ] **Step 3: `refreshInjectBar` 구현 (app.js)**

`updateInjectBar`(357-367행)를 아래로 교체:

```js
let reopening = false;               // '세션연결' 진행 중(연결 중 UI)
const injectFailedSet = new Set();   // 전송이 noconsole/nopid로 실패한 세션(강제 세션연결 모드)

// 상세 진입 시: 입력값 초기화 + 상태 리셋 후 바 갱신.
function updateInjectBar(id) {
  const input = document.getElementById('injectInput');
  if (input) { input.value = ''; }
  setInjectSending(false);
  reopening = false;
  autoGrowInject();
  refreshInjectBar(id);
}

// 스냅샷/전환 시: 세션의 injectable·engine에 맞춰 입력창/세션연결/코덱스안내 중 하나를 표시.
// 사용자가 입력 중인 값·전송 중 상태는 건드리지 않는다.
function refreshInjectBar(id) {
  const bar = document.getElementById('injectBar');
  const row = document.getElementById('injectRow');
  const connect = document.getElementById('sessionConnect');
  if (!bar) return;
  bar.hidden = false;
  const s = sessionsById[id];
  const engine = s && s.engine;
  if (engine === 'codex') { // 콘솔 없음 → 안내만
    if (row) row.hidden = true; if (connect) connect.hidden = true;
    showInjectHint('inject.hintCodex');
    return;
  }
  const injectable = !!(s && s.injectable) && !injectFailedSet.has(id);
  if (injectable) {         // 일반 입력
    if (connect) connect.hidden = true;
    if (row) row.hidden = false;
    if (!injectSending) showInjectHint(null);
    reopening = false;
    return;
  }
  // claude + 주입 불가 → 세션연결
  if (row) row.hidden = true;
  if (connect) connect.hidden = false;
  const btn = document.getElementById('sessionConnectBtn');
  if (btn) { btn.disabled = reopening; btn.textContent = t(reopening ? 'session.connecting' : 'session.connect'); }
}
```

- [ ] **Step 4: 스냅샷 시 바 갱신 (app.js)**

`ws.onmessage`의 sessions 분기(140-141행) — `else if (currentSessionId) syncPendingForm(currentSessionId);`를 아래로 교체:

```js
        else if (currentSessionId) { syncPendingForm(currentSessionId); refreshInjectBar(currentSessionId); }
```

- [ ] **Step 5: 목록 복귀 시 상태 리셋 (app.js)**

`backToList`(286행) — `{ const bar = document.getElementById('injectBar'); if (bar) bar.hidden = true; }`를 아래로 교체:

```js
  { const bar = document.getElementById('injectBar'); if (bar) bar.hidden = true; }
  setInjectSending(false); reopening = false;
```

- [ ] **Step 6: 빌드 + 수동 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공. PID가 없는(종료된) claude 세션 상세 진입 시 입력창 대신 ‘세션연결’ 버튼 + 설명이 표시된다. 살아있는 claude 세션은 입력창, Codex는 안내만. (버튼은 아직 동작 안 함 — Task 9)

- [ ] **Step 7: Commit**

```bash
git add AgentHub/View/Htmls/index.html AgentHub/View/Htmls/css/app.css AgentHub/View/Htmls/js/app.js
git commit -m "feat: injectable=false claude 세션은 입력창 대신 세션연결 버튼 표시"
```

---

### Task 7: 스파이크 — conhost 주입 가능성(S1) + resume sessionId 유지(S2)

Task 8 구현 전 **반드시** 실측. 코드 산출물 없이 정확한 실행 커맨드와 세션 매핑을 확정한다.

**Files:** 없음(수동 검증, 결과를 스펙 §8에 한 줄로 기록).

- [ ] **Step 1: S1 — conhost 강제 실행 콘솔이 주입 가능한지**

임의의 claude 세션 id·cwd로 아래를 각각 실행(PowerShell)하고, 뜬 콘솔에서 `AttachConsole`이 되는지(= 기존 모바일 답변 주입이 도달하는지) 확인:

```powershell
# 후보 A (pwsh 경유)
conhost.exe pwsh -NoExit -Command "claude --resume <sessionId>"
# 후보 B (cmd 경유)
conhost.exe cmd.exe /k claude --resume <sessionId>
```

Expected: 최소 하나가 **고전 conhost**로 떠서 그 세션에 모바일 입력이 주입된다(기존 injectBar로 한 줄 테스트). 동작하는 커맨드를 기록.
판정: 둘 다 실패하면(모두 ConPTY로 승격되면) 중단하고 사용자에게 보고(설계 재검토 필요).

- [ ] **Step 2: S2 — `claude --resume`가 같은 sessionId를 유지하는지**

위에서 뜬 세션에서 훅이 보고하는 sessionId가 **원본과 동일**한지 확인(`%LOCALAPPDATA%/AgentHub/session-pids.json` 갱신 또는 앱 로그/스냅샷의 해당 카드 PID 갱신 관찰).

Expected: 같은 sessionId로 PID가 재보고되어 그 카드의 `injectable`이 true가 된다.
판정: 다르면(새 sessionId 생성) Task 9의 복귀 로직을 “새 세션 카드로 안내”로 조정해야 함 → 사용자에게 보고 후 결정.

- [ ] **Step 3: 결과 기록 + Commit**

확정된 실행 커맨드(A/B)와 S2 결과를 스펙 파일 §8에 한 줄로 추가.

```bash
git add docs/superpowers/specs/2026-07-22-mobile-session-detail-answer-ux-design.md
git commit -m "docs: 세션연결 스파이크 결과(실행 커맨드·sessionId 유지) 기록"
```

---

### Task 8: `SessionReopener` + `reopen`/`reopenResult` (서버)

**Files:**
- Create: `AgentHub/Server/Terminal/SessionReopener.cs`
- Modify: `AgentHub/Server/Socket/AgentMonitorModule.cs` (dispatch 체인, 121행 `inject` 케이스 뒤)
- Test: `AgentHub.Tests/SessionInjectableTests.cs` (추가)

**Interfaces:**
- Consumes: `AgentMonitorService.EngineOf(id)`, `AgentMonitorService.CwdOf(id)`.
- Produces: `SessionReopener.Result { Ok, NoCwd, Failed }`, `SessionReopener.Reopen(string sessionId, string cwd)`, `SessionReopener.IsValidSessionId(string)`. 클라이언트로 `{ type:"reopenResult", sessionId, ok, reason }`.

- [ ] **Step 1: `IsValidSessionId` 실패 테스트 추가 (SessionInjectableTests.cs)**

파일 끝(네임스페이스 닫기 전)에 클래스 추가:

```csharp
    public class SessionReopenerTests
    {
        [Theory]
        [InlineData("019f5f7b-1a2b-3c4d-5e6f-708192a3b4c5", true)]
        [InlineData("abcdef0123456789", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("a && calc.exe", false)]   // 명령 주입 시도 차단
        [InlineData("../../etc", false)]
        public void IsValidSessionId_rejects_unsafe(string id, bool expected)
        {
            Assert.Equal(expected, AgentHub.Server.Terminal.SessionReopener.IsValidSessionId(id));
        }
    }
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj --filter SessionReopenerTests`
Expected: 컴파일 실패(`SessionReopener` 미정의).

- [ ] **Step 3: `SessionReopener` 구현 (신규 파일)**

`AgentHub/Server/Terminal/SessionReopener.cs`. 실행 커맨드는 **Task 7에서 확정한 후보(A: pwsh / B: cmd)**를 사용 — 아래는 후보 A(pwsh) 기본. Task 7이 B를 확정했으면 `FileName`/`Arguments`만 그 형태로 교체:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using AgentHub.Common.Util;

namespace AgentHub.Server.Terminal
{
    /// <summary>
    /// 직접입력이 안 되는(ConPTY/종료된) claude 세션을, PC에서 고전 conhost 콘솔에
    /// claude --resume 로 다시 실행해 콘솔 주입이 가능한 상태로 되돌린다.
    /// 라이브 attach/스트리밍 없음. sessionId 외 임의 입력을 실행 커맨드에 넣지 않는다.
    /// </summary>
    public static class SessionReopener
    {
        public enum Result { Ok, NoCwd, Failed }

        private static readonly Regex SessionIdPattern = new Regex("^[0-9a-fA-F-]{8,64}$", RegexOptions.Compiled);

        /// <summary>커맨드 주입 방지: 세션 id는 16진수/하이픈만 허용.</summary>
        public static bool IsValidSessionId(string id) => !string.IsNullOrEmpty(id) && SessionIdPattern.IsMatch(id);

        public static Result Reopen(string sessionId, string cwd)
        {
            if (!IsValidSessionId(sessionId)) return Result.Failed;
            if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd)) return Result.NoCwd;
            try
            {
                // conhost.exe 를 앞세워 '고전 콘솔 호스트'를 강제(Windows Terminal 기본이어도 ConPTY 승격 방지).
                var psi = new ProcessStartInfo
                {
                    FileName = "conhost.exe",
                    Arguments = "pwsh -NoExit -Command \"claude --resume " + sessionId + "\"",
                    WorkingDirectory = cwd,
                    UseShellExecute = true   // 새 콘솔 창 표시
                };
                Process.Start(psi);
                return Result.Ok;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return Result.Failed; }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj --filter SessionReopenerTests`
Expected: PASS(6 케이스).

- [ ] **Step 5: `reopen` 케이스 추가 (AgentMonitorModule.cs)**

`inject` 케이스가 끝나는 121행 `}` 뒤(즉 `pickerAnswer` `else if` 앞)에 추가:

```csharp
                else if (msg.Type == "reopen" && !string.IsNullOrEmpty(msg.SessionId))
                {
                    // 직접입력 불가 claude 세션을 PC에서 claude --resume 으로 재실행(고전 콘솔).
                    bool ok = false; string reason;
                    if (AgentMonitorService.EngineOf(msg.SessionId) != "claude")
                        reason = "engine"; // Codex 등: 세션연결 미지원
                    else
                    {
                        var cwd = AgentMonitorService.CwdOf(msg.SessionId); // 미상 세션이면 null → NoCwd
                        var r = AgentHub.Server.Terminal.SessionReopener.Reopen(msg.SessionId, cwd);
                        ok = r == AgentHub.Server.Terminal.SessionReopener.Result.Ok;
                        reason = ok ? null
                            : (r == AgentHub.Server.Terminal.SessionReopener.Result.NoCwd ? "nocwd" : "failed");
                    }
                    await SendSafe(context, Json.Serialize(new
                    {
                        type = "reopenResult", sessionId = msg.SessionId, ok, reason
                    }));
                }
```

- [ ] **Step 6: 빌드 + 전체 테스트**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"` 후 `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`
Expected: 빌드 성공, 전체 테스트 PASS.

- [ ] **Step 7: Commit**

```bash
git add AgentHub/Server/Terminal/SessionReopener.cs AgentHub/Server/Socket/AgentMonitorModule.cs AgentHub.Tests/SessionInjectableTests.cs
git commit -m "feat: 세션연결 서버 — conhost로 claude --resume 재실행(reopen/reopenResult)"
```

---

### Task 9: 클라이언트 reopen 배선 + 실패 전환 + 연결중 상태

**Files:**
- Modify: `AgentHub/View/Htmls/js/app.js` (버튼 핸들러, `reopenResult` dispatch, `handleInjectResult` 실패 전환)

**Interfaces:**
- Consumes: `refreshInjectBar`, `reopening`, `injectFailedSet`, `send`, `showInjectHint`, i18n `session.*`.
- Produces: `handleReopenResult(m)`.

- [ ] **Step 1: reopen 전송 + 결과 처리 (app.js)**

`injectSend`/`injectInput` 리스너 등록부(395-398행) 뒤에 추가:

```js
let reopenTimer = null;
document.getElementById('sessionConnectBtn') && document.getElementById('sessionConnectBtn').addEventListener('click', () => {
  if (!currentSessionId || reopening) return;
  if (!confirm(t('session.connectConfirm'))) return;
  injectFailedSet.delete(currentSessionId); // 재실행으로 복구 시도 → 강제 세션연결 해제
  reopening = true;
  refreshInjectBar(currentSessionId);        // '연결 중…' 표시
  send({ type: 'reopen', sessionId: currentSessionId });
  // 안전망: reopenResult/injectable 스냅샷이 안 오면 복구.
  reopenTimer = setTimeout(() => {
    if (reopening) { reopening = false; showInjectHint('session.reopenFailed'); refreshInjectBar(currentSessionId); }
  }, 20000);
});
function handleReopenResult(m) {
  if (m.sessionId !== currentSessionId) return;
  if (m.ok) return; // 실행 성공 → injectable 스냅샷을 기다림(연결 중 유지, 안전망이 커버)
  reopening = false; if (reopenTimer) { clearTimeout(reopenTimer); reopenTimer = null; }
  showInjectHint('session.reopenFailed');
  refreshInjectBar(m.sessionId);
}
```

- [ ] **Step 2: `reopenResult` dispatch 추가 (app.js)**

`ws.onmessage`의 dispatch(150행 `pickerAnswerResult` 뒤)에 추가:

```js
      else if (m.type === 'reopenResult') { handleReopenResult(m); }
```

- [ ] **Step 3: injectable 복귀 시 연결중 타이머 해제 (app.js)**

`refreshInjectBar`의 injectable 분기에서 `reopening = false;` 직전에 타이머 해제를 추가 — injectable 분기(`reopening = false;` 줄)를 아래로 교체:

```js
    if (reopenTimer) { clearTimeout(reopenTimer); reopenTimer = null; }
    reopening = false;
```

- [ ] **Step 4: 전송 실패 → 세션연결 전환 (app.js)**

`handleInjectResult`(Task 3에서 작성)의 실패 처리에 noconsole/nopid 전환을 추가 — 함수를 아래로 교체:

```js
function handleInjectResult(m) {
  if (m.sessionId !== currentSessionId) return;
  setInjectSending(false);
  const input = document.getElementById('injectInput');
  if (m.ok) { if (input) { input.value = ''; autoGrowInject(); } showInjectHint(null); return; }
  if (m.reason === 'noconsole' || m.reason === 'nopid') {
    injectFailedSet.add(m.sessionId);     // 이 세션은 세션연결로 전환
    showInjectHint(null);
    refreshInjectBar(m.sessionId);
    return;
  }
  showInjectHint(m.reason === 'engine' ? 'inject.hintCodex' : 'inject.hintFailed');
}
```

- [ ] **Step 5: 빌드 + 수동 통합 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공. 시나리오:
- 종료된 claude 세션 상세 → ‘세션연결’ 탭 → 확인 → PC에 콘솔 창이 뜨고 세션 재개 → 같은 카드 `injectable=true` → 입력창 자동 복귀 → 답변 전송 성공.
- Windows Terminal에서 실행한 세션에 전송 → `noconsole` → 세션연결 모드로 전환.
- Codex 세션 → 세션연결 버튼 없이 안내만.

- [ ] **Step 6: Commit**

```bash
git add AgentHub/View/Htmls/js/app.js
git commit -m "feat: 세션연결 버튼 배선·전송 실패 시 세션연결 전환·injectable 복귀"
```

---

### Task 10: sw.js 프리캐시 버전 bump + 사용 가이드 갱신

**Files:**
- Modify: `AgentHub/View/Htmls/sw.js` (캐시 버전 상수)
- Modify: `docs/index.html` (모바일 답변 가이드 섹션)

**Interfaces:** 없음(문서·캐시).

- [ ] **Step 1: sw.js 캐시 버전 bump**

`sw.js`에서 캐시 버전 상수를 찾아(예: `const CACHE = 'agenthub-vN'` 또는 유사) 버전을 1 올린다. 변경된 index.html/app.js/app.css/i18n.js가 이미 프리캐시 `ASSETS`에 포함돼 있는지 확인(누락 시 추가 — 메모리 `sw-precache-render-blocking-assets` 준수).

Run: `grep -n "CACHE\|VERSION\|ASSETS" AgentHub/View/Htmls/sw.js`
Expected: 버전 상수 확인 후 bump.

- [ ] **Step 2: 사용 가이드 갱신 (docs/index.html)**

모바일 답변/직접 입력을 설명하는 섹션을 찾는다:

Run: `grep -n "답변\|직접 입력\|주입\|inject\|터미널" docs/index.html`

해당 섹션에 아래 취지의 항목을 추가(주변 마크업 스타일에 맞춰):
- 내가 보낸 답변은 오른쪽 말풍선으로 구분 표시된다.
- 답변은 여러 줄 입력 가능(Enter=전송, Shift+Enter=줄바꿈). 전송 중에는 입력창이 잠기고 로딩이 보이며, 전달이 확인돼야 다시 입력할 수 있다(중복 전송 방지).
- 직접 입력이 안 되는 세션(Windows Terminal에서 실행했거나 종료된 세션)은 ‘세션연결’ 버튼이 나오고, 누르면 PC에서 해당 세션이 다시 열려(claude --resume) 다른 세션처럼 답변할 수 있다. **claude 전용 · 같은 Wi-Fi(LAN) 승인기기 한정.** Codex 세션은 지원하지 않는다.

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/View/Htmls/sw.js docs/index.html
git commit -m "docs: 세션 상세 답변 UX·세션연결 가이드 갱신 + SW 캐시 버전 bump"
```

---

### Task 11: 최종 전체 빌드 + 회귀 확인

**Files:** 없음(검증).

- [ ] **Step 1: 전체 Restore/Build**

Run: `msbuild AgentHub.sln /t:Restore` 후 `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공, 산출물 `install/Debug/AgentHub.exe`.

- [ ] **Step 2: 전체 테스트**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`
Expected: 기존 + 신규(`SessionInjectableTests`/`SessionReopenerTests`) 모두 PASS.

- [ ] **Step 3: 회귀 수동 체크(핵심)**

- 살아있는 conhost claude 세션: 여러 줄 답변 전송 → 성공·비움·재활성화, 내 답변 오른쪽 말풍선.
- 전송 연타/중복 Enter가 무시되는지.
- 종료된 claude 세션: 세션연결 → 재개 → 입력창 복귀 → 답변.
- ConPTY 세션: 전송 실패 → 세션연결 전환.
- Codex 세션: 안내만.
- 다크/라이트 테마 모두.

- [ ] **Step 4: 최종 커밋(잔여 변경 있으면)**

```bash
git status
# 잔여 변경이 있으면:
git add -A && git commit -m "chore: 세션 상세 답변 UX 개선 마무리"
```

---

## Self-Review (작성자 체크 결과)

- **Spec coverage:** §3 내 답변 구분→Task1 / §4 textarea→Task2 / §5 상태머신→Task3 / §6 판별·세션연결→Task5,6,8,9 / §7 i18n→Task4 / §8 스파이크→Task7 / §9 문서→Task10 / 빌드→Task11. 누락 없음.
- **Placeholder scan:** 실행 커맨드는 Task7 스파이크로 확정 후 Task8에 반영(구체 코드 제시). "적절히" 류 표현 없음.
- **Type consistency:** `IsInjectable(engine, hasPid)`, `SessionReopener.Result{Ok,NoCwd,Failed}`, `IsValidSessionId`, `refreshInjectBar/updateInjectBar`, `injectSending/setInjectSending`, `reopening/injectFailedSet`, `handleInjectResult/handleReopenResult` — 태스크 간 명칭·시그니처 일치. reopenResult reason 값(`engine`/`nocwd`/`failed`)은 클라이언트에서 세분 처리하지 않고 `session.reopenFailed`로 통일(의도).
