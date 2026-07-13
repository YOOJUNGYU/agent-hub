# 원격 질문/답변 "따라잡기" 창 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** AgentHub 앱이 꺼져 있을 때 `AskUserQuestion`이 와도, 대기창(≈600초) 안에 앱을 켜면 그 질문에 원격으로 답할 수 있게 한다.

**Architecture:** 세 가지 인메모리 변경이 맞물린다 — (1) 대기창 상수를 단일 소스(`RemoteAnswerConfig`)로 두고 600초로 확대, (2) 훅 JS가 서버가 뜰 때까지 `endpoint.txt`를 재읽으며 재접속(폴링)한 뒤 답변 대기, (3) 서버 `HookElicit`이 클라이언트 미연결이어도 항상 pending 등록·대기(붙는 즉시 기존 watch 재전송 경로로 질문 전달). 답은 원래 tool call에 구조화된 형태 그대로 반환. `resume`·디스크 영속·외부망 답변은 하지 않는다.

**Tech Stack:** C# (.NET Framework 4.8, WinForms + EmbedIO), Node.js 훅(`agenthub-hook.js`), Newtonsoft.Json, xUnit(테스트). 빌드: MSBuild. 

## Global Constraints

- 사용자 응답 언어는 **항상 한글** (CLAUDE.md 최우선 규칙).
- 자체 코드 루트 네임스페이스는 **`AgentHub`**. `EmbedIO/`(서드파티)는 **수정 금지**.
- C# 소스·리소스에 **한글(UTF-8)** 포함 — 문자열 편집 시 인코딩 훼손 금지. **Edit 도구로 편집**하고, 다른 코드페이지로 재인코딩 저장 금지.
- `agenthub-hook.js`의 `/^﻿/`(선행 BOM 제거) 리터럴에는 **원시 U+FEFF 문자가 들어 있다** — 재타이핑 말고 실제 파일에서 매칭할 것. 새로 쓰는 코드에서는 `﻿` 이스케이프를 쓴다.
- 기능/동작 변경 시 **`docs/index.html` 사용 가이드도 같은 작업에서 갱신** (Task 5, 누락 시 미완성).
- 대기창 기본값 **600초**(문서상 command 훅 기본 timeout = 안전값). 카스케이드는 **서버 대기 < 훅 JS 예산 < Claude 훅 timeout(=600)** 순으로 600초 이내에 완전히 중첩.
- 대상은 **`AskUserQuestion`(PermissionRequest 경로)만**. `PreToolUse` 권한 흐름·기타 브랜치는 손대지 않는다(surgical).
- 모든 대기 상태는 **인메모리**. `resume`/디스크 영속/외부망 답변/앱 자동시작은 **비목표**.
- 빌드 검증:
  ```powershell
  msbuild AgentHub.sln /t:Restore
  msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
  ```
- 테스트 실행:
  ```powershell
  dotnet test AgentHub.Tests/AgentHub.Tests.csproj -v minimal
  ```
  (`dotnet`이 없으면 Visual Studio 테스트 탐색기 또는 `vstest.console.exe AgentHub.Tests\bin\Debug\net48\AgentHub.Tests.dll`.)

---

## 참고: 관련 현재 코드 (구현 전 반드시 Read)

- `AgentHub/hook/agenthub-hook.js` — 훅. `readPort()`/`post()`/이벤트 브랜치.
- `AgentHub/Server/Hook/HookInstaller.cs` — `Install()`이 `PermissionRequest` 항목을 `timeout:120`, `args:[ScriptPath]`로 설치(60-72, 101행).
- `AgentHub/Server/Controller/ApiController.cs` — `HookElicit()`(269-307), 특히 `HasApprovedClient()` 게이트(290)와 `AwaitAnswer(...,110000)`(295).
- `AgentHub/Server/Hook/AskRegistry.cs` — `AwaitAnswer(id, sessionId, questionsJson, timeoutMs)`/`Resolve`/`TryGetPendingForSession`(변경 없음, 그대로 사용).
- `AgentHub/Server/Socket/AgentMonitorModule.cs` — watch 시 `TryGetPendingForSession`으로 미답 elicit 재전송(71-80), `elicitAnswer`→`Resolve`(91-101). **변경 없음(재사용)**.
- `AgentHub.Tests/AgentHub.Tests.csproj` — 소스를 `<Compile Include Link>`로 개별 링크. xUnit.

---

## Task 1: RemoteAnswerConfig (대기창 단일 소스 + 카스케이드 불변식)

순수 상수 클래스. 훅 설치(Task 2)·서버(Task 4)·훅 JS(Task 3, argv 경유)가 공유하는 단일 진실 원천. 유일한 실제 단위테스트 대상.

**Files:**
- Create: `AgentHub/Server/Hook/RemoteAnswerConfig.cs`
- Modify: `AgentHub.Tests/AgentHub.Tests.csproj` (링크 추가)
- Test: `AgentHub.Tests/RemoteAnswerConfigTests.cs`

**Interfaces:**
- Produces:
  - `AgentHub.Server.Hook.RemoteAnswerConfig.WindowSeconds : int` = 600 (settings.json에 쓸 Claude 훅 timeout, 가장 바깥)
  - `.HookBudgetMs : int` = (WindowSeconds-5)*1000 = 595000 (훅 JS 총 대기 예산)
  - `.ServerWindowMs : int` = (WindowSeconds-7)*1000 = 593000 (서버 AwaitAnswer 상한)
  - `.ServerMarginMs : int` = 2000 (서버가 훅 HTTP보다 먼저 응답하도록 빼는 여유)

- [ ] **Step 1: 실패하는 테스트 작성**

Create `AgentHub.Tests/RemoteAnswerConfigTests.cs`:

```csharp
using AgentHub.Server.Hook;
using Xunit;

namespace AgentHub.Tests
{
    public class RemoteAnswerConfigTests
    {
        [Fact]
        public void Default_window_is_documented_safe_600()
        {
            Assert.Equal(600, RemoteAnswerConfig.WindowSeconds);
        }

        [Fact]
        public void Cascade_is_strictly_nested_within_the_window()
        {
            // 서버 대기 < 훅 JS 예산 < Claude 훅 timeout(=WindowSeconds). 모두 600초 이내.
            Assert.True(RemoteAnswerConfig.ServerWindowMs < RemoteAnswerConfig.HookBudgetMs);
            Assert.True(RemoteAnswerConfig.HookBudgetMs < RemoteAnswerConfig.WindowSeconds * 1000);
            Assert.True(RemoteAnswerConfig.ServerMarginMs > 0);
        }

        [Fact]
        public void Window_is_wider_than_the_old_120s()
        {
            Assert.True(RemoteAnswerConfig.WindowSeconds > 120);
        }
    }
}
```

- [ ] **Step 2: 링크 추가 후 실패 확인**

Modify `AgentHub.Tests/AgentHub.Tests.csproj` — `<ItemGroup>`의 링크 목록(현재 `PermissionRegistry.cs` 링크 다음)에 추가:

```xml
    <Compile Include="..\AgentHub\Server\Hook\RemoteAnswerConfig.cs" Link="Linked\RemoteAnswerConfig.cs" />
```

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj -v minimal`
Expected: 컴파일 실패 — `RemoteAnswerConfig`(형식 없음) CS0246. (아직 클래스 없음)

- [ ] **Step 3: 최소 구현 작성**

Create `AgentHub/Server/Hook/RemoteAnswerConfig.cs`:

```csharp
namespace AgentHub.Server.Hook
{
    /// <summary>
    /// 원격 AskUserQuestion 답변 대기창의 단일 진실 원천(계단식 타임아웃).
    /// 앱이 꺼져 있어도 이 창(초) 안에 앱을 켜면 훅이 서버를 기다렸다가 답을 받는다.
    /// 문서상 command 훅 기본 timeout이 600초라 WindowSeconds를 600으로 두어, 카스케이드
    /// 전체를 600초 이내에 중첩시킨다(>600초 존중 불확실성 회피).
    /// 순서(안쪽이 먼저 만료): ServerWindow < HookBudget < WindowSeconds(=Claude 훅 timeout).
    /// </summary>
    public static class RemoteAnswerConfig
    {
        /// <summary>settings.json에 기록할 Claude 훅 timeout(초). 가장 바깥(가장 큼). 문서상 안전값 600.</summary>
        public const int WindowSeconds = 600;

        /// <summary>훅(JS)의 총 대기 예산(ms) — 폴링+답변 대기 합. Claude timeout보다 5초 짧게.</summary>
        public const int HookBudgetMs = (WindowSeconds - 5) * 1000;

        /// <summary>서버 AwaitAnswer 최대 대기(ms). 훅 예산보다 짧게(2초 더).</summary>
        public const int ServerWindowMs = (WindowSeconds - 7) * 1000;

        /// <summary>서버가 훅 HTTP 타임아웃보다 먼저 응답하도록 빼는 여유(ms).</summary>
        public const int ServerMarginMs = 2000;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj -v minimal`
Expected: PASS (RemoteAnswerConfigTests 3개 포함 전체 통과).

- [ ] **Step 5: 커밋**

```bash
git add AgentHub/Server/Hook/RemoteAnswerConfig.cs AgentHub.Tests/RemoteAnswerConfigTests.cs AgentHub.Tests/AgentHub.Tests.csproj
git commit -m "feat: 원격 답변 대기창 단일 소스 RemoteAnswerConfig(기본 600초) 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: HookInstaller — PermissionRequest 훅 timeout 확대 + 창 인자 전달 + 강제 갱신

`PermissionRequest`(AskUserQuestion) 훅의 `timeout`을 600으로 올리고, 창(초)을 훅에 argv로 넘긴다. 기존 설치본(timeout:120)이 멱등 스킵으로 안 바뀌는 문제를 막기 위해 이 항목만 remove→add로 강제 갱신한다.

**Files:**
- Modify: `AgentHub/Server/Hook/HookInstaller.cs` (62-72행 permReqEntry, 101행 병합)

**Interfaces:**
- Consumes: `RemoteAnswerConfig.WindowSeconds`(Task 1). 같은 네임스페이스라 `using` 불필요.

- [ ] **Step 1: permReqEntry의 args·timeout 변경**

`AgentHub/Server/Hook/HookInstaller.cs`에서 `permReqEntry`의 hooks 배열(현재 args `{ ScriptPath }`, timeout `120`)을 수정.

Old:
```csharp
                var permReqEntry = new JObject
                {
                    ["matcher"] = "",
                    ["hooks"] = new JArray { new JObject
                    {
                        ["type"] = "command",
                        ["command"] = ResolveNode(),
                        ["args"] = new JArray { ScriptPath },
                        ["timeout"] = 120
                    }}
                };
```
New:
```csharp
                var permReqEntry = new JObject
                {
                    ["matcher"] = "",
                    ["hooks"] = new JArray { new JObject
                    {
                        ["type"] = "command",
                        ["command"] = ResolveNode(),
                        // 두 번째 인자로 대기창(초)을 훅에 전달 → 훅이 서버를 기다리는 폴링 deadline로 사용.
                        ["args"] = new JArray { ScriptPath, RemoteAnswerConfig.WindowSeconds.ToString() },
                        ["timeout"] = RemoteAnswerConfig.WindowSeconds
                    }}
                };
```

- [ ] **Step 2: PermissionRequest 항목 강제 갱신(remove→add)**

같은 파일 `Install()`의 병합부에서 PermissionRequest 추가 한 줄을 remove+add로 교체(기존 timeout:120 설치본을 새 값으로 갱신하기 위함).

Old:
```csharp
                merged = HookConfigMerger.AddHook(merged, "PermissionRequest", permReqEntry, Marker);
```
New:
```csharp
                // 기존 설치본(옛 timeout/args)이 멱등 스킵으로 안 바뀌므로, 우리 항목만 제거 후 재추가해 강제 갱신.
                merged = HookConfigMerger.RemoveHook(merged, "PermissionRequest", Marker);
                merged = HookConfigMerger.AddHook(merged, "PermissionRequest", permReqEntry, Marker);
```

- [ ] **Step 3: 빌드 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: 빌드 성공(0 Error). 

- [ ] **Step 4: 커밋**

```bash
git add AgentHub/Server/Hook/HookInstaller.cs
git commit -m "feat: PermissionRequest 훅 timeout 600초로 확대 + 창 인자 전달·강제 갱신

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

> 동작 검증(생성된 `~/.claude/settings.json`의 PermissionRequest 항목 timeout=600·args 확인)은 Task 6 통합 검증에서 앱 기동 후 수행한다. HookInstaller는 사용자 전역 경로에 파일 I/O를 하므로 단위테스트하지 않는다(리포 컨벤션: 순수 `HookConfigMerger`만 테스트).

---

## Task 3: agenthub-hook.js — AskUserQuestion 브랜치를 deadline 폴링으로 재작성

앱이 꺼져 있어도 창 안에 앱을 켜면 답을 받도록, 서버 미기동/연결끊김이면 `deadline`까지 `endpoint.txt`를 재읽으며 재접속한다. **PreToolUse·Stop·Notification·SessionStart 브랜치는 손대지 않는다.**

**Files:**
- Modify: `AgentHub/hook/agenthub-hook.js`

**Interfaces:**
- Consumes: `process.argv[2]` = 창(초) (Task 2가 설치 시 전달; 없으면 600 기본). 서버 `/api/hook/elicit`가 body의 `waitMs`(잔여 ms)를 존중(Task 4).
- Produces: 답변 시 stdout에 `{hookSpecificOutput:{hookEventName:'PermissionRequest',decision:{behavior:'allow',updatedInput}}}` (기존 형식 그대로).

> **구현 전 반드시 `agenthub-hook.js`를 Read**한 뒤 아래 4개 편집을 적용한다. Edit 4의 대상 블록에는 원시 BOM(`/^﻿/`)이 들어 있으니 실제 파일에서 매칭할 것(재타이핑 금지).

- [ ] **Step 1: awaitElicit 함수 추가 (Edit 1)**

`post(...)` 함수 정의 바로 뒤(빈 줄 있는 `let raw = '';` 앞)에 삽입.

Old (매칭 앵커):
```javascript
let raw = '';
process.stdin.on('data', d => (raw += d));
```
New:
```javascript
// PermissionRequest(AskUserQuestion) 전용: deadline까지 서버 접속을 폴링하며 답변을 대기한다.
// 서버 미기동/연결거부/연결끊김(서버 재시작)은 deadline 전이면 재시도한다(앱을 창 안에 켜면 붙는다).
function awaitElicit(p) {
  const windowSec = Number(process.argv[2]) || 600;
  const budgetMs = (windowSec - 5) * 1000;     // Claude 훅 timeout보다 짧게(먼저 스스로 종료)
  const deadline = Date.now() + budgetMs;
  const safety = setTimeout(() => process.exit(0), budgetMs); // 절대 안전망

  function finish(data) {
    clearTimeout(safety);
    try {
      const r = JSON.parse((data || '{}').replace(/^﻿/, '')); // 선행 BOM 제거
      if (r.updatedInput) {
        process.stdout.write(JSON.stringify({
          hookSpecificOutput: {
            hookEventName: 'PermissionRequest',
            decision: { behavior: 'allow', updatedInput: r.updatedInput }
          }
        }));
      }
      // updatedInput 없음(무응답/타임아웃) → 출력 없음 = 기존 흐름(PC 프롬프트)으로 폴백.
    } catch (e) {}
    process.exit(0);
  }

  function attempt() {
    const now = Date.now();
    if (now >= deadline) { finish(null); return; }
    const port = readPort();                    // 매 시도 재읽기(앱이 켜지며 기록/변경될 수 있음)
    if (!port) { setTimeout(attempt, 700); return; }
    const remaining = deadline - now;
    const body = JSON.stringify({
      session_id: p.session_id, cwd: p.cwd, tool_input: p.tool_input, waitMs: remaining
    });
    let settled = false;
    const req = https.request({
      host: '127.0.0.1', port: Number(port), path: '/api/hook/elicit', method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
      rejectUnauthorized: false, timeout: remaining + 2000
    }, res => {
      let data = '';
      res.on('data', d => (data += d));
      res.on('end', () => { if (!settled) { settled = true; finish(data); } });
    });
    req.on('error', e => {
      if (settled) return; settled = true;
      // 접속 불가/연결 끊김(서버 미기동·재시작)은 deadline 전이면 재시도.
      const retryable = e && (e.code === 'ECONNREFUSED' || e.code === 'ECONNRESET'
        || e.code === 'ENOENT' || e.code === 'ECONNABORTED');
      if (retryable && Date.now() < deadline) setTimeout(attempt, 700);
      else finish(null);
    });
    req.on('timeout', () => { try { req.destroy(); } catch (e) {} if (!settled) { settled = true; finish(null); } });
    req.write(body); req.end();
  }
  attempt();
}

let raw = '';
process.stdin.on('data', d => (raw += d));
```

- [ ] **Step 2: port 가드 + isElicit 판별 (Edit 2)**

Old:
```javascript
  const port = readPort();
  if (!port) process.exit(0);
```
New:
```javascript
  // AskUserQuestion 원격 답변은 앱이 꺼져 있어도 창 안에 앱을 켜면 받도록 폴링한다(포트 없어도 진행).
  const isElicit = p.hook_event_name === 'PermissionRequest' && p.tool_name === 'AskUserQuestion';
  const port = readPort();
  if (!port && !isElicit) process.exit(0);
```

- [ ] **Step 3: 무포트 시 session-pid 생략 (Edit 3)**

Old:
```javascript
  post(port, '/api/hook/session-pid', { session_id: p.session_id, pid: process.ppid }, 2000, () => {}); // fire-and-forget
```
New:
```javascript
  if (port) post(port, '/api/hook/session-pid', { session_id: p.session_id, pid: process.ppid }, 2000, () => {}); // fire-and-forget(포트 없으면 생략)
```

- [ ] **Step 4: PermissionRequest 브랜치를 awaitElicit 호출로 교체 (Edit 4)**

`agenthub-hook.js`의 `if (p.hook_event_name === 'PermissionRequest') { ... }` 블록 전체(현재 elicit POST/118000/119000/BOM 처리 포함, `PreToolUse` 브랜치 시작 직전까지)를 아래로 교체. **원본 블록에 U+FEFF가 있으니 실제 파일에서 블록을 선택해 매칭할 것.**

New:
```javascript
  if (p.hook_event_name === 'PermissionRequest') {
    // AskUserQuestion만 폰으로 넘겨 원격 답변받는다. 그 외 권한요청은 출력 없이 통과.
    if (!isElicit) { process.exit(0); return; }
    awaitElicit(p);
    return;
  }
```

- [ ] **Step 5: 빌드(구문) + 서버 없는 폴백 타이밍 수동 검증**

먼저 구문 확인:
```powershell
node --check AgentHub/hook/agenthub-hook.js
```
Expected: 출력 없음(구문 OK).

폴백 타이밍(서버 다운 시 창까지 폴링 후 무출력 종료) — **AgentHub 앱을 종료한 상태**에서(엔드포인트가 죽은 포트를 가리키거나 없음), 짧은 창 3초로:
```bash
time (printf '%s' '{"hook_event_name":"PermissionRequest","tool_name":"AskUserQuestion","session_id":"s1","cwd":"c","tool_input":{"questions":[{"question":"q","options":[{"label":"A"}]}]}}' | node AgentHub/hook/agenthub-hook.js 3)
```
Expected: **약 3초** 뒤 종료(폴링), **stdout 출력 없음**(폴백). (연결 성공 경로는 Task 6 통합 시나리오에서 검증.)

- [ ] **Step 6: 커밋**

```bash
git add AgentHub/hook/agenthub-hook.js
git commit -m "feat: AskUserQuestion 훅을 deadline 폴링으로 재작성(앱 오프 시 서버 대기·재접속)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: ApiController.HookElicit — 게이트 제거, 항상 등록·대기, waitMs 존중

클라이언트 미연결이어도 항상 pending 등록·대기하도록 `HasApprovedClient()` 게이트를 제거하고, 훅이 준 `waitMs` 잔여시간 내에서 대기한다(서버가 훅 HTTP보다 먼저 응답하도록 여유를 뺌).

**Files:**
- Modify: `AgentHub/Server/Controller/ApiController.cs` (`HookElicit`, 290-302행)

**Interfaces:**
- Consumes: `RemoteAnswerConfig.ServerWindowMs`/`.ServerMarginMs`(Task 1), 요청 body의 `waitMs`(Task 3). `AskRegistry.AwaitAnswer`(기존), `AgentMonitorService.BroadcastElicit`(기존).
- `System.Math`는 이미 `System` 사용 중이라 추가 using 불필요.

- [ ] **Step 1: 게이트 제거 + waitMs 반영**

`HookElicit()`에서 `NotifyDisconnected(qmsg, sessionId);` 다음의 `if (AgentMonitorService.HasApprovedClient()) { ... }` 블록을 교체.

Old:
```csharp
                    AgentHub.Server.Push.PushService.NotifyDisconnected(qmsg, sessionId);
                    if (AgentMonitorService.HasApprovedClient())
                    {
                        var id = Guid.NewGuid().ToString("N");
                        AgentMonitorService.BroadcastElicit(id, project, questions, sessionId);
                        // 폰이 답변 화면을 닫아도 세션을 다시 열면(watch) 재전송할 수 있도록 sessionId·questions 보관.
                        var answersJson = await AgentHub.Server.Hook.AskRegistry.AwaitAnswer(id, sessionId, questions.ToString(), 110000);
                        if (!string.IsNullOrEmpty(answersJson))
                        {
                            var updated = (JObject)toolInput.DeepClone();
                            updated["answers"] = JToken.Parse(answersJson);
                            updatedInput = updated;
                        }
                    }
```
New:
```csharp
                    AgentHub.Server.Push.PushService.NotifyDisconnected(qmsg, sessionId);
                    // 클라이언트가 아직 안 붙었어도(앱을 방금 켠 경우) 항상 등록·대기한다.
                    // 지금 연결된 승인 클라이언트엔 즉시 broadcast하고, 대기 중 새로 연결되는
                    // 클라이언트에는 watch 시 재전송(AskRegistry.TryGetPendingForSession)으로 전달된다.
                    var id = Guid.NewGuid().ToString("N");
                    AgentMonitorService.BroadcastElicit(id, project, questions, sessionId);
                    // 훅이 준 잔여시간(waitMs) 내에서 대기. 서버가 훅 HTTP 타임아웃보다 먼저 응답하도록 여유를 뺀다.
                    var waitMs = (int?)o["waitMs"] ?? AgentHub.Server.Hook.RemoteAnswerConfig.ServerWindowMs;
                    waitMs = Math.Min(waitMs, AgentHub.Server.Hook.RemoteAnswerConfig.ServerWindowMs);
                    waitMs = Math.Max(waitMs - AgentHub.Server.Hook.RemoteAnswerConfig.ServerMarginMs, 1000);
                    var answersJson = await AgentHub.Server.Hook.AskRegistry.AwaitAnswer(id, sessionId, questions.ToString(), waitMs);
                    if (!string.IsNullOrEmpty(answersJson))
                    {
                        var updated = (JObject)toolInput.DeepClone();
                        updated["answers"] = JToken.Parse(answersJson);
                        updatedInput = updated;
                    }
```

- [ ] **Step 2: 빌드 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: 빌드 성공(0 Error).

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/Server/Controller/ApiController.cs
git commit -m "feat: HookElicit 클라이언트 미연결에도 항상 등록·대기 + waitMs 존중

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

> 동작(게이트 제거·대기·재전송)은 EmbedIO+WS 통합이라 단위테스트하지 않고 Task 6 시나리오로 검증한다(리포 컨벤션).

---

## Task 5: docs/index.html 사용 가이드 갱신 (필수)

앱이 꺼져 있었어도 창 안에 켜면 답할 수 있다는 점과, 창 길이(≈2분 → ≈10분)를 반영한다. **한글·영문 둘 다** 갱신(파일이 ko/en 병기).

**Files:**
- Modify: `AgentHub/../docs/index.html` (417행, 453행)

- [ ] **Step 1: "질문 원격 답변" 항목(417행) 갱신 — 한글**

Old:
```html
새 터미널이 필요 없습니다(폰이 연결돼 있을 때만 가로챔). 답을 안 하면 잠시 뒤 PC의 평소 프롬프트로 넘어갑니다.
```
New:
```html
새 터미널이 필요 없습니다. <b>앱이 꺼져 있었더라도</b> 답변 대기 창(기본 약 10분) 안에 앱을 켜면 그 질문에 답할 수 있습니다. 창이 지나도록 답을 안 하면 PC의 평소 프롬프트로 넘어갑니다.
```

- [ ] **Step 2: "질문 원격 답변" 항목(417행) 갱신 — 영문**

Old:
```html
with no new terminal (only intercepted while a phone is connected). If unanswered, it falls back to the normal PC prompt shortly after.
```
New:
```html
with no new terminal. <b>Even if the app was closed</b>, you can still answer within the wait window (about 10 minutes by default) by launching the app in time. If left unanswered past the window, it falls back to the normal PC prompt.
```

- [ ] **Step 3: 제한사항 "시간 창"(453행) 갱신 — 한글**

Old:
```html
Claude가 질문(선택지)에 답을 기다리는 <b>약 2분</b> 안에서만 폰으로 답할 수 있습니다.
```
New:
```html
Claude가 질문(선택지)에 답을 기다리는 <b>기본 약 10분(600초)</b> 안에서만 폰으로 답할 수 있습니다. <b>앱이 꺼져 있었더라도</b> 이 창 안에 앱을 켜면 그 질문에 답할 수 있습니다.
```

- [ ] **Step 4: 제한사항 "시간 창"(453행) 갱신 — 영문**

Old:
```html
only within the <b>~2 minutes</b> Claude waits.
```
New:
```html
only within the <b>~10 minutes (600s by default)</b> Claude waits — and <b>even if the app was closed</b>, launching it within that window lets you still answer.
```

- [ ] **Step 5: 커밋**

```bash
git add docs/index.html
git commit -m "docs: 앱 오프 시 창 안에 켜면 답변 가능 + 대기창 약 10분으로 가이드 갱신

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: 전체 빌드 + 통합 검증 (spec §7)

실제 앱을 띄워 시나리오를 재현하고 설치 결과·동작을 확인한다.

**Files:** (없음 — 검증 전용)

- [ ] **Step 1: 클린 빌드 + 테스트**

Run:
```powershell
msbuild AgentHub.sln /t:Restore
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
dotnet test AgentHub.Tests/AgentHub.Tests.csproj -v minimal
```
Expected: 빌드 0 Error, 테스트 전체 PASS.

- [ ] **Step 2: 설치 결과 확인**

`install/Debug/AgentHub.exe` 실행(서버가 뜨며 `HookInstaller.Install()` 수행) 후 `%USERPROFILE%\.claude\settings.json`을 열어 확인:
- `hooks.PermissionRequest`의 우리 항목: `"timeout": 600`, `"args": [ "...agenthub-hook.js", "600" ]`.
- `hooks.PreToolUse`의 우리 항목: `"timeout": 120` **그대로**(미변경).

- [ ] **Step 3: 시나리오 검증 (spec §7)**

각 항목을 재현하고 결과를 기록:
1. **회귀(앱 켜짐·폰 연결):** AskUserQuestion → 폰 즉답 → tool call에 답 반영. (기존과 동일)
2. **자리비움:** 앱 켜짐, 폰 미연결 → 질문 발생 → 창 안에 폰 연결 후 세션 열기(watch)에서 답변 카드 뜸 → 답 → 반영.
3. **앱 오프 → 창 안 실행(핵심):** AgentHub 종료 → AskUserQuestion 발생(Claude가 멈춰 대기) → 창 안에 `AgentHub.exe` 실행 → 콘솔/폰에서 세션 열어 답 → 반영.
4. **창 초과:** 아무도 답 안 함 → 창(≈600초) 후 훅 폴백 → PC 터미널 정상 프롬프트.
5. **서버 재시작 중:** 대기 중 앱 종료→재기동 → 훅이 재접속·질문 재전송 → 답 → 반영.
6. **R-A(블로킹 유지):** 시나리오 3에서 Claude가 옛 120초를 넘겨(2분 이상) 계속 대기하는지 확인 — 600초는 문서상 command 훅 기본값이라 존중 기대(잔여 위험 낮음).

- [ ] **Step 4: verify 스킬로 마무리(선택)**

`/verify`로 변경이 실제 앱에서 동작함을 한 번 더 관찰(시나리오 3 중심).

---

## Self-Review (계획 작성자 체크)

- **Spec 커버리지:** §3 변경1→Task1·2, 변경2→Task3, 변경3→Task4. §5 카스케이드→Task1(불변식 테스트). §6 R-A→Task6 Step3-6(+600=안전값으로 위험 제거), R-B/R-C→동작상 수용(가이드에 반영). §7 검증→Task6. §8 문서→Task5. §9 영향파일 전부 태스크에 매핑. **AskRegistry.cs는 변경 없음**(AwaitAnswer가 이미 timeoutMs 파라미터라 호출부만 변경) — 의도적, 갭 아님.
- **Placeholder 스캔:** TBD/TODO/모호 지시 없음. 모든 코드·명령·기대출력 명시. (마진 값은 Task1에서 상수로 확정.)
- **타입 일관성:** `RemoteAnswerConfig.{WindowSeconds,HookBudgetMs,ServerWindowMs,ServerMarginMs}` 이름이 Task1 정의와 Task2·3·4 사용에서 동일. 훅 body 키 `waitMs`가 Task3(전송)·Task4(수신)에서 동일. stdout 반환 스키마는 기존과 동일.
- **비목표 준수:** resume·디스크 영속·외부망 답변·앱 자동시작 없음. PreToolUse/기타 브랜치 불변.
