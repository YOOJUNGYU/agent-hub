# 폰에서 세션 턴에 자유 텍스트로 답장 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 폰이 그 세션을 보고 있을 때, Claude가 턴을 끝내면 답장 카드가 떠서 자유 텍스트를 입력·전송하면 같은 claude 프로세스에 주입되어 대화가 이어지고, [완료(닫기)]로 즉시 마칠 수 있게 한다.

**Architecture:** 기존 AskUserQuestion(elicit) 흐름(블로킹 훅 → 서버가 폰 응답 대기 → 훅 stdout 주입)을 `Stop` 훅에 그대로 재사용한다. `Stop` 훅을 fire-and-forget에서 블로킹으로 바꾸고, 서버는 "그 세션을 watch 중인 폰이 있을 때만" 턴을 붙든다. 폰 답장은 `{"decision":"block","reason":"<답장>"}`으로 훅이 출력해 세션에 주입한다.

**Tech Stack:** C# / .NET Framework 4.8 (WinForms + EmbedIO 서버), Newtonsoft.Json, xUnit(net48, 소스 링크), Node.js 훅(agenthub-hook.js), PWA(vanilla JS + WebSocket).

## Global Constraints

- 자체 코드 루트 네임스페이스는 `AgentHub.*`.
- `EmbedIO/`(서드파티)는 수정 금지.
- C# 소스·리소스의 한글(UTF-8) 문자열 인코딩 훼손 금지 — Edit 도구로 부분 수정, 재인코딩 저장 금지.
- 사용자에게 보이는 문구는 한글 + i18n(ko/en 동시).
- 대기창은 기존 `RemoteAnswerConfig`(600초 카스케이드) 재사용 — 새 설정 항목 추가 금지.
- 게이트: 폰이 **그 세션을 watch 중일 때만** 턴을 붙든다.
- 빌드: `msbuild AgentHub.sln /t:Restore` → `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`. 산출물 `install/Debug/AgentHub.exe`.
- 테스트: `dotnet test AgentHub.Tests\AgentHub.Tests.csproj`.
- 기능 변경 시 `docs/index.html` 사용 가이드 동기화 필수(누락 시 미완성).
- 스펙 단일 소스: `docs/superpowers/specs/2026-07-13-phone-reply-design.md`.

## File Structure

- `AgentHub/hook/agenthub-hook.js` (수정): `Stop` 분기를 블로킹으로. 응답의 `reply`를 `{decision:block, reason}`로 주입.
- `AgentHub/Server/Hook/ReplyRegistry.cs` (신규): 답장 대기/해제(AskRegistry 미러). `AwaitReply`/`Resolve`/`Dismiss`/`TryGetPendingForSession`.
- `AgentHub/Server/Agents/TranscriptParser.cs` (수정): `LastAssistantText(lines, max)` 추가(순수 함수, 단위 테스트).
- `AgentHub/Server/Agents/ClaudeSessionReader.cs` (수정): `LastAssistantTextOf(sessionId)` 추가(파일 I/O).
- `AgentHub/Server/Agents/AgentMonitorService.cs` (수정): `BroadcastReply`/`BroadcastReplyClose`/`IsSessionWatched`.
- `AgentHub/Server/Socket/AgentMonitorModule.cs` (수정): `IsSessionWatched`, `reply`/`replyDismiss` 수신, watch 재전송, `WatchMessage.Text`.
- `AgentHub/Server/Controller/ApiController.cs` (수정): `/hook/stop` 재작성(게이트+대기+응답).
- `AgentHub/Server/Hook/HookInstaller.cs` (수정): `Stop` 엔트리 블로킹화 + 강제 갱신.
- `AgentHub/View/Htmls/index.html` (수정): `#reply` 답장 오버레이.
- `AgentHub/View/Htmls/js/app.js` (수정): `handleReply`/`handleReplyClose` + 버튼 + WS 라우팅.
- `AgentHub/View/Htmls/js/i18n.js` (수정): `reply.*` 키(ko/en).
- `AgentHub.Tests/ReplyRegistryTests.cs` (신규), `AgentHub.Tests/TranscriptParserLastAssistantTests.cs` (신규).
- `AgentHub.Tests/AgentHub.Tests.csproj` (수정): `ReplyRegistry.cs` 소스 링크 추가.
- `docs/index.html` (수정): 사용 가이드 문구.

---

### Task 1: 주입 계약 스파이크 — Stop 훅 block/reason 실측(게이트, 수동)

전체 접근이 "Stop 훅이 `{decision:block, reason}`로 사용자 입력처럼 주입된다"에 의존한다. 코드 대량 작성 전에 이 계약만 최소로 검증한다.

**Files:**
- Modify(임시): `AgentHub/hook/agenthub-hook.js` (Stop 분기)

- [ ] **Step 1: 훅 Stop 분기를 임시로 하드코딩 주입으로 교체**

`AgentHub/hook/agenthub-hook.js`의 기존 Stop 분기를 임시로 아래처럼 바꾼다(스파이크 전용, 나중에 되돌림):

```js
if (p.hook_event_name === 'Stop') {
  process.stdout.write(JSON.stringify({ decision: 'block', reason: 'AGENTHUB_SPIKE: 사용자가 폰에서 답함 — "테스트 진행"이라고 말했다고 가정하고 이어서 진행하세요.' }));
  process.exit(0);
  return;
}
```

- [ ] **Step 2: 훅 설치 후 실측**

Agent Hub 앱을 빌드·실행(`install/Debug/AgentHub.exe`) → 콘솔에서 훅 설치 → 임의 폴더에서 `claude` 세션 실행 → 아무 프롬프트에 응답하게 해 턴을 끝낸다.

Expected: 턴이 끝나며 claude가 멈추지 않고(=block 적용), reason 텍스트를 다음 지시로 받아 "테스트 진행" 취지로 이어서 동작한다. 실제 화면 동작을 기록한다.

- [ ] **Step 3: 결과 기록 및 주입 형식 확정**

- 위대로 동작 → 이후 Task 7의 훅 출력 형식은 `{decision:'block', reason: r.reply}`로 확정.
- 동작하지 않으면(그냥 종료/에러) 폴백을 순서대로 시도해 동작하는 형식을 기록:
  1. `{"hookSpecificOutput":{"hookEventName":"Stop","additionalContext": r.reply}}` (+ 필요 시 `"decision":"block"` 병기)
  2. reason 문구를 지시형으로 감싼 형태(예: `사용자가 폰에서 답함: "<reply>". 이에 따라 계속하세요.`)
- 스펙 `docs/superpowers/specs/2026-07-13-phone-reply-design.md` §7-1 아래에 "확정 주입 형식: …" 한 줄을 추가한다.

- [ ] **Step 4: 스파이크 되돌리기**

`git checkout -- AgentHub/hook/agenthub-hook.js`로 임시 변경을 원복(다음 태스크들이 깨끗한 상태에서 시작). 커밋하지 않는다.

- [ ] **Step 5: 스펙 노트만 커밋**

```bash
git add docs/superpowers/specs/2026-07-13-phone-reply-design.md
git commit -m "docs: Stop 훅 주입 형식 실측 결과 기록(스파이크)"
```

---

### Task 2: ReplyRegistry (순수 로직, TDD)

**Files:**
- Create: `AgentHub/Server/Hook/ReplyRegistry.cs`
- Modify: `AgentHub.Tests/AgentHub.Tests.csproj` (소스 링크 추가)
- Test: `AgentHub.Tests/ReplyRegistryTests.cs`

**Interfaces:**
- Produces:
  - `Task<string> ReplyRegistry.AwaitReply(string id, string sessionId, string lastMessage, int timeoutMs)` — 답장 텍스트, 또는 타임아웃/닫기 시 `null`.
  - `void ReplyRegistry.Resolve(string id, string text)` — [전송]. 빈/공백 텍스트는 무시.
  - `void ReplyRegistry.Dismiss(string id)` — [완료(닫기)] → `null`로 해제.
  - `bool ReplyRegistry.TryGetPendingForSession(string sessionId, out string id, out string lastMessage)`.

- [ ] **Step 1: csproj에 소스 링크 추가**

`AgentHub.Tests/AgentHub.Tests.csproj`의 `<ItemGroup>`(소스 링크 목록, `RemoteAnswerConfig.cs` 줄 아래)에 추가:

```xml
    <Compile Include="..\AgentHub\Server\Hook\ReplyRegistry.cs" Link="Linked\ReplyRegistry.cs" />
```

- [ ] **Step 2: 실패하는 테스트 작성**

`AgentHub.Tests/ReplyRegistryTests.cs` 생성:

```csharp
using System.Threading.Tasks;
using AgentHub.Server.Hook;
using Xunit;

namespace AgentHub.Tests
{
    public class ReplyRegistryTests
    {
        [Fact]
        public async Task Resolve_returns_text()
        {
            var task = ReplyRegistry.AwaitReply("r1", "s1", "질문?", 5000);
            ReplyRegistry.Resolve("r1", "Subagent-Driven로 진행해");
            Assert.Equal("Subagent-Driven로 진행해", await task);
        }

        [Fact]
        public async Task Dismiss_returns_null()
        {
            var task = ReplyRegistry.AwaitReply("r2", "s1", "질문?", 5000);
            ReplyRegistry.Dismiss("r2");
            Assert.Null(await task);
        }

        [Fact]
        public async Task Timeout_returns_null()
        {
            Assert.Null(await ReplyRegistry.AwaitReply("r3", "s1", "질문?", 30));
        }

        [Fact]
        public async Task Blank_reply_is_ignored_then_times_out()
        {
            var task = ReplyRegistry.AwaitReply("r4", "s1", "질문?", 80);
            ReplyRegistry.Resolve("r4", "   ");
            Assert.Null(await task);
        }

        [Fact]
        public void Resolve_or_dismiss_unknown_id_is_noop()
        {
            ReplyRegistry.Resolve("nope", "hi");
            ReplyRegistry.Dismiss("nope");
        }

        [Fact]
        public async Task TryGetPendingForSession_finds_waiting_reply()
        {
            var task = ReplyRegistry.AwaitReply("r5", "sess-x", "마지막 메시지", 5000);
            Assert.True(ReplyRegistry.TryGetPendingForSession("sess-x", out var id, out var last));
            Assert.Equal("r5", id);
            Assert.Equal("마지막 메시지", last);
            ReplyRegistry.Dismiss("r5");
            await task;
        }
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test AgentHub.Tests\AgentHub.Tests.csproj`
Expected: 컴파일 실패(`ReplyRegistry` 없음).

- [ ] **Step 4: ReplyRegistry 구현**

`AgentHub/Server/Hook/ReplyRegistry.cs` 생성:

```csharp
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// 턴 종료(Stop) 후 폰의 자유 텍스트 답장을 관리. 훅(HTTP)이 답장을 대기하고,
    /// 폰의 reply가 Resolve로, [완료(닫기)]가 Dismiss로 대기를 해제한다.
    /// 타임아웃/무응답/닫기 시 null → 훅이 출력 없이 정상 종료(완료)로 폴백.
    /// 대기 중 sessionId·lastMessage를 보관해, 폰이 세션을 다시 열 때(watch) 답장 카드를 재전송한다.
    /// </summary>
    public static class ReplyRegistry
    {
        private class Pending
        {
            public TaskCompletionSource<string> Tcs;
            public string SessionId;
            public string LastMessage;
        }

        private static readonly ConcurrentDictionary<string, Pending> _pending
            = new ConcurrentDictionary<string, Pending>();

        /// <summary>id에 대한 답장을 대기. 초과/닫기/무응답 시 null.</summary>
        public static async Task<string> AwaitReply(string id, string sessionId, string lastMessage, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = new Pending { Tcs = tcs, SessionId = sessionId, LastMessage = lastMessage };
            try
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                return done == tcs.Task ? tcs.Task.Result : null;
            }
            finally { _pending.TryRemove(id, out _); }
        }

        /// <summary>폰 [전송] — 빈/공백 텍스트는 무시(폴백).</summary>
        public static void Resolve(string id, string text)
        {
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrWhiteSpace(text)
                && _pending.TryGetValue(id, out var p))
                p.Tcs.TrySetResult(text);
        }

        /// <summary>폰 [완료(닫기)] — null로 해제(정상 종료).</summary>
        public static void Dismiss(string id)
        {
            if (!string.IsNullOrEmpty(id) && _pending.TryGetValue(id, out var p))
                p.Tcs.TrySetResult(null);
        }

        /// <summary>해당 세션에 대기 중인 답장이 있으면 (id, lastMessage) 반환.</summary>
        public static bool TryGetPendingForSession(string sessionId, out string id, out string lastMessage)
        {
            id = null; lastMessage = null;
            if (string.IsNullOrEmpty(sessionId)) return false;
            foreach (var kv in _pending)
                if (kv.Value.SessionId == sessionId)
                { id = kv.Key; lastMessage = kv.Value.LastMessage; return true; }
            return false;
        }
    }
}
```

- [ ] **Step 5: AgentHub.csproj(메인 앱)에 컴파일 등록**

`AgentHub/AgentHub.csproj`는 클래식(비-SDK) 프로젝트라 새 .cs를 명시 등록해야 메인 앱이 컴파일한다. `<Compile Include="Server\Hook\AskRegistry.cs" />`(162행 부근) 바로 아래에 추가:

```xml
    <Compile Include="Server\Hook\ReplyRegistry.cs" />
```

- [ ] **Step 6: 테스트 통과 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj -v minimal`
Expected: 전체 PASS(ReplyRegistryTests 6개 포함).

- [ ] **Step 7: 커밋**

```bash
git add AgentHub/Server/Hook/ReplyRegistry.cs AgentHub/AgentHub.csproj AgentHub.Tests/ReplyRegistryTests.cs AgentHub.Tests/AgentHub.Tests.csproj
git commit -m "feat: 답장 대기 레지스트리(ReplyRegistry) 추가"
```

---

### Task 3: 마지막 어시스턴트 텍스트 추출 (TranscriptParser TDD + ClaudeSessionReader)

**Files:**
- Modify: `AgentHub/Server/Agents/TranscriptParser.cs`
- Modify: `AgentHub/Server/Agents/ClaudeSessionReader.cs`
- Test: `AgentHub.Tests/TranscriptParserLastAssistantTests.cs`

**Interfaces:**
- Produces:
  - `string TranscriptParser.LastAssistantText(IReadOnlyList<string> lines, int max = 300)` — 마지막 assistant 메시지의 text 블록(여러 개면 개행 결합), 없으면 `null`.
  - `string ClaudeSessionReader.LastAssistantTextOf(string sessionId)` — 파일 로드 후 위 호출. 실패 시 `null`.

- [ ] **Step 1: 실패하는 테스트 작성**

`AgentHub.Tests/TranscriptParserLastAssistantTests.cs` 생성:

```csharp
using System.Collections.Generic;
using AgentHub.Server.Agents;
using Xunit;

namespace AgentHub.Tests
{
    public class TranscriptParserLastAssistantTests
    {
        [Fact]
        public void Returns_last_assistant_text_block()
        {
            var lines = new List<string>
            {
                "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"진행해\"}}",
                "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"어느 쪽으로 진행할까요?\"}]}}"
            };
            Assert.Equal("어느 쪽으로 진행할까요?", TranscriptParser.LastAssistantText(lines));
        }

        [Fact]
        public void Joins_multiple_text_blocks_and_ignores_trailing_user()
        {
            var lines = new List<string>
            {
                "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"먼저\"},{\"type\":\"text\",\"text\":\"둘째\"}]}}",
                "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"x\",\"content\":\"ok\"}]}}"
            };
            Assert.Equal("먼저\n둘째", TranscriptParser.LastAssistantText(lines));
        }

        [Fact]
        public void Returns_null_when_last_assistant_has_no_text()
        {
            var lines = new List<string>
            {
                "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"name\":\"Bash\",\"input\":{\"command\":\"ls\"}}]}}"
            };
            Assert.Null(TranscriptParser.LastAssistantText(lines));
        }

        [Fact]
        public void Truncates_to_max()
        {
            var big = new string('가', 400);
            var lines = new List<string>
            {
                "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"" + big + "\"}]}}"
            };
            var r = TranscriptParser.LastAssistantText(lines, 300);
            Assert.True(r.Length <= 301); // 300 + 말줄임표 '…'
            Assert.EndsWith("…", r);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test AgentHub.Tests\AgentHub.Tests.csproj`
Expected: 컴파일 실패(`LastAssistantText` 없음).

- [ ] **Step 3: TranscriptParser.LastAssistantText 구현**

`AgentHub/Server/Agents/TranscriptParser.cs`의 `ExtractPendingAsk` 메서드 바로 위(같은 클래스 내부)에 추가:

```csharp
        /// <summary>마지막 어시스턴트 메시지의 text 블록(여러 개면 개행 결합). 없으면 null. 알림/카드 표시용 truncate.</summary>
        public static string LastAssistantText(IReadOnlyList<string> lines, int max = 300)
        {
            JObject lastAssistant = null;
            foreach (var line in lines)
            {
                var o = TryParse(line);
                if (o == null) continue;
                if (Str(o["type"]) == "assistant") lastAssistant = o;
            }
            if (lastAssistant == null) return null;

            var content = lastAssistant["message"]?["content"];
            string text = null;
            if (content is JArray arr)
            {
                var parts = new List<string>();
                foreach (var b in arr.OfType<JObject>())
                    if (Str(b["type"]) == "text")
                    {
                        var tx = Str(b["text"]);
                        if (!string.IsNullOrWhiteSpace(tx)) parts.Add(tx.Trim());
                    }
                if (parts.Count > 0) text = string.Join("\n", parts);
            }
            else if (content?.Type == JTokenType.String) text = content.Value<string>();

            return string.IsNullOrWhiteSpace(text) ? null : Truncate(text, max);
        }
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test AgentHub.Tests\AgentHub.Tests.csproj`
Expected: 전체 PASS(LastAssistant 4개 포함).

- [ ] **Step 5: ClaudeSessionReader.LastAssistantTextOf 구현**

`AgentHub/Server/Agents/ClaudeSessionReader.cs`의 `TitleOf` 메서드 바로 아래에 추가:

```csharp
        /// <summary>세션의 마지막 어시스턴트 텍스트(알림·답장 카드 표시용). 실패 시 null.</summary>
        public static string LastAssistantTextOf(string sessionId)
        {
            try
            {
                if (!_paths.TryGetValue(sessionId, out var path) || !File.Exists(path))
                {
                    path = FindSessionFile(sessionId);
                    if (path == null) return null;
                    _paths[sessionId] = path;
                }
                return TranscriptParser.LastAssistantText(ReadAllLinesShared(path));
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return null; }
        }
```

- [ ] **Step 6: 빌드 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공.

- [ ] **Step 7: 커밋**

```bash
git add AgentHub/Server/Agents/TranscriptParser.cs AgentHub/Server/Agents/ClaudeSessionReader.cs AgentHub.Tests/TranscriptParserLastAssistantTests.cs
git commit -m "feat: 세션 마지막 어시스턴트 텍스트 추출 추가"
```

---

### Task 4: 서버 브로드캐스트·수신 배선 (AgentMonitorService/Module)

**Files:**
- Modify: `AgentHub/Server/Agents/AgentMonitorService.cs`
- Modify: `AgentHub/Server/Socket/AgentMonitorModule.cs`

**Interfaces:**
- Consumes: `ReplyRegistry`(Task 2), `ClawdGuard.IsRunning()`.
- Produces:
  - `void AgentMonitorService.BroadcastReply(string id, string project, string message, string sessionId)` — WS `{type:"reply", id, project, message, sessionId}`.
  - `void AgentMonitorService.BroadcastReplyClose(string sessionId)` — WS `{type:"replyClose", sessionId}`.
  - `bool AgentMonitorService.IsSessionWatched(string sessionId)`.
  - `bool AgentMonitorModule.IsSessionWatched(string sessionId)`.

- [ ] **Step 1: AgentMonitorService에 브로드캐스트·게이트 추가**

`AgentHub/Server/Agents/AgentMonitorService.cs`의 `BroadcastElicit` 메서드 바로 아래에 추가:

```csharp
        /// <summary>턴 종료 후 답장 카드를 승인 기기에 push(폰이 그 세션을 볼 때만 표시).</summary>
        public static async void BroadcastReply(string id, string project, string message, string sessionId)
        {
            var msg = Json.Serialize(new { type = "reply", id, project, message, sessionId });
            await _sendGate.WaitAsync();
            try { if (_module != null) await _module.BroadcastMessageAsync(msg); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }

        /// <summary>답장 카드 닫기(답변/닫기/타임아웃 후 다른 기기 카드 정리).</summary>
        public static async void BroadcastReplyClose(string sessionId)
        {
            var msg = Json.Serialize(new { type = "replyClose", sessionId });
            await _sendGate.WaitAsync();
            try { if (_module != null) await _module.BroadcastMessageAsync(msg); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }

        /// <summary>승인된 폰이 그 세션을 watch(상세 보기) 중인지 — 턴 붙듦 게이트.</summary>
        public static bool IsSessionWatched(string sessionId) => _module != null && _module.IsSessionWatched(sessionId);
```

- [ ] **Step 2: AgentMonitorModule에 IsSessionWatched 추가**

`AgentHub/Server/Socket/AgentMonitorModule.cs`의 `IsConnected` 메서드 바로 아래에 추가:

```csharp
        /// <summary>승인된 소켓 중 그 세션을 watch 중인 것이 있는지.</summary>
        public bool IsSessionWatched(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return false;
            foreach (var kv in _watching)
            {
                if (kv.Value != sessionId) continue;
                if (_tokens.TryGetValue(kv.Key, out var h) && DeviceRegistry.StatusByHash(h) == DeviceStatus.Approved)
                    return true;
            }
            return false;
        }
```

- [ ] **Step 3: reply/replyDismiss 수신 처리 추가**

`AgentHub/Server/Socket/AgentMonitorModule.cs`의 `OnMessageReceivedAsync`에서 `elicitAnswer` 처리 블록(`else if (msg.Type == "elicitAnswer")` … 닫는 `}`) 바로 아래에 추가:

```csharp
                else if (msg.Type == "reply")
                {
                    // 폰 [전송] → 대기 중인 Stop 훅 해제. clawd 동시 실행 시 차단 안내.
                    if (AgentHub.Common.Util.ClawdGuard.IsRunning())
                        await SendSafe(context, Json.Serialize(new { type = "answerBlocked",
                            reason = "clawd", message = "answer.blockedClawd" }));
                    else if (!string.IsNullOrEmpty(msg.Id) && !string.IsNullOrWhiteSpace(msg.Text))
                        AgentHub.Server.Hook.ReplyRegistry.Resolve(msg.Id, msg.Text);
                }
                else if (msg.Type == "replyDismiss")
                {
                    // 폰 [완료(닫기)] → 대기 해제(정상 종료).
                    if (!string.IsNullOrEmpty(msg.Id))
                        AgentHub.Server.Hook.ReplyRegistry.Dismiss(msg.Id);
                }
```

- [ ] **Step 4: watch 시 미결 답장 카드 재전송**

같은 파일 `OnMessageReceivedAsync`의 watch 블록에서, elicit 재전송(`if (AgentHub.Server.Hook.AskRegistry.TryGetPendingForSession(...))` … 닫는 `}`) 바로 아래에 추가:

```csharp
                    if (AgentHub.Server.Hook.ReplyRegistry.TryGetPendingForSession(msg.SessionId, out var rid, out var rlast))
                        await SendSafe(context, Json.Serialize(new { type = "reply", id = rid,
                            message = rlast, sessionId = msg.SessionId, resent = true }));
```

- [ ] **Step 5: WatchMessage에 Text 필드 추가**

같은 파일 하단 `internal class WatchMessage`의 `Answers` 속성 아래에 추가:

```csharp
        public string Text { get; set; }          // reply: 폰이 보낸 자유 텍스트 답장
```

- [ ] **Step 6: 빌드 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공.

- [ ] **Step 7: 커밋**

```bash
git add AgentHub/Server/Agents/AgentMonitorService.cs AgentHub/Server/Socket/AgentMonitorModule.cs
git commit -m "feat: 답장 카드 브로드캐스트·수신·watch 게이트 배선"
```

---

### Task 5: /hook/stop 재작성 (게이트 + 대기 + 응답)

**Files:**
- Modify: `AgentHub/Server/Controller/ApiController.cs` (`HookStop`)

**Interfaces:**
- Consumes: `AgentMonitorService.IsSessionWatched/BroadcastReply/BroadcastReplyClose/BroadcastDone`(Task 4), `ClaudeSessionReader.LastAssistantTextOf`(Task 3), `ReplyRegistry.AwaitReply`(Task 2), `RemoteAnswerConfig`.
- Produces: `POST /api/hook/stop` → `{ reply }`(문자열 또는 null).

- [ ] **Step 1: HookStop 메서드 교체**

`AgentHub/Server/Controller/ApiController.cs`의 기존 `HookStop`(`[Route(HttpVerbs.Post, "/hook/stop")]` 메서드) 전체를 아래로 교체:

```csharp
        // Stop 훅(블로킹): 세션이 턴을 끝냄. 폰이 이 세션을 watch 중이면 서버가 답장을 기다렸다가
        // {reply}로 돌려준다 → 훅이 세션에 주입해 대화가 이어짐. watch 중이 아니면 오늘 그대로 '완료' 알림.
        [Route(HttpVerbs.Post, "/hook/stop")]
        public async Task HookStop()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            string reply = null;
            try
            {
                var o = JObject.Parse(raw);
                var project = LastSegment((string)o["cwd"] ?? "");
                var sessionId = (string)o["session_id"];
                if (AgentMonitorService.IsSessionWatched(sessionId))
                {
                    var id = Guid.NewGuid().ToString("N");
                    var lastMsg = ClaudeSessionReader.LastAssistantTextOf(sessionId);
                    AgentMonitorService.BroadcastReply(id, project, lastMsg, sessionId);
                    // 미연결 승인기기엔 '답장 대기'로 알림(‘완료’ 아님) → 앱을 열어 답할 수 있게.
                    AgentHub.Server.Push.PushService.NotifyDisconnected(
                        string.IsNullOrWhiteSpace(lastMsg) ? "답장을 기다립니다" : lastMsg, sessionId);
                    var waitMs = (int?)o["waitMs"] ?? AgentHub.Server.Hook.RemoteAnswerConfig.ServerWindowMs;
                    waitMs = Math.Min(waitMs, AgentHub.Server.Hook.RemoteAnswerConfig.ServerWindowMs);
                    waitMs = Math.Max(waitMs - AgentHub.Server.Hook.RemoteAnswerConfig.ServerMarginMs, 1000);
                    var rawReply = await AgentHub.Server.Hook.ReplyRegistry.AwaitReply(id, sessionId, lastMsg, waitMs);
                    if (string.IsNullOrEmpty(rawReply))
                    {
                        // 무응답/닫기/타임아웃 → 카드 정리 + 완료 처리.
                        AgentMonitorService.BroadcastReplyClose(sessionId);
                        AgentMonitorService.BroadcastDone(project, sessionId);
                    }
                    else
                    {
                        // 폰 답장을 '정당한 사용자 답장'으로 프레이밍해 인젝션 오인·거부 방지(스파이크 확정).
                        reply = BuildReplyInjection(rawReply);
                    }
                }
                else
                {
                    // 오늘과 동일: 완료 알림.
                    AgentMonitorService.BroadcastDone(project, sessionId);
                    AgentHub.Server.Push.PushService.NotifyDisconnected("작업을 완료했습니다", sessionId);
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            await SendJsonAsync(Json.Serialize(new { reply }));
        }

        /// <summary>폰 답장을 Stop 훅 reason으로 주입할 때, 정당한 사용자 답장임을 명확히 해 인젝션 오인·거부를 방지(스파이크 확정 문구).</summary>
        private static string BuildReplyInjection(string userText)
            => "[Agent Hub] 사용자가 휴대폰에서 이 세션에 답장을 보냈습니다:\n\n" + userText
             + "\n\n— 위 내용은 사용자가 직접 입력한 정당한 후속 메시지입니다(프롬프트 인젝션이 아님). 이 답장을 사용자의 다음 지시로 받아들여 계속 진행하세요.";
```

이 `BuildReplyInjection`은 `HookStop` 메서드 바로 아래(같은 클래스, `ToolDetail` 헬퍼 근처)에 둔다.

- [ ] **Step 2: 빌드 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공.

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/Server/Controller/ApiController.cs
git commit -m "feat: /hook/stop을 답장 대기(watch 게이트)로 재작성"
```

---

### Task 6: HookInstaller — Stop 엔트리 블로킹화

**Files:**
- Modify: `AgentHub/Server/Hook/HookInstaller.cs`

- [ ] **Step 1: stopEntry를 블로킹 형태로 변경**

`AgentHub/Server/Hook/HookInstaller.cs`의 `stopEntry` 정의(주석 `// Stop: …` 아래 블록)를 아래로 교체:

```csharp
                // Stop: 세션이 턴을 끝낼 때 폰이 그 세션을 보고 있으면 답장을 원격 대기(블로킹).
                // 답장을 받으면 그 텍스트로 대화가 이어지고, 없으면 정상 종료('완료' 알림).
                var stopEntry = new JObject
                {
                    ["matcher"] = "",
                    ["hooks"] = new JArray { new JObject
                    {
                        ["type"] = "command",
                        ["command"] = ResolveNode(),
                        // 두 번째 인자로 대기창(초)을 훅에 전달(PermissionRequest와 동일).
                        ["args"] = new JArray { ScriptPath, RemoteAnswerConfig.WindowSeconds.ToString() },
                        ["timeout"] = RemoteAnswerConfig.WindowSeconds
                    }}
                };
```

- [ ] **Step 2: 기존 설치본 강제 갱신(remove 후 add)**

같은 파일 `Install()`에서 `merged = HookConfigMerger.AddHook(merged, "Stop", stopEntry, Marker);` 한 줄을 아래 두 줄로 교체:

```csharp
                // 기존 설치본(옛 async/timeout)이 멱등 스킵으로 안 바뀌므로 제거 후 재추가해 강제 갱신.
                merged = HookConfigMerger.RemoveHook(merged, "Stop", Marker);
                merged = HookConfigMerger.AddHook(merged, "Stop", stopEntry, Marker);
```

- [ ] **Step 3: 빌드 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공.

- [ ] **Step 4: 커밋**

```bash
git add AgentHub/Server/Hook/HookInstaller.cs
git commit -m "feat: Stop 훅을 블로킹으로 설치(대기창 인자·강제 갱신)"
```

---

### Task 7: 훅 Stop 분기 블로킹화 (agenthub-hook.js)

**Files:**
- Modify: `AgentHub/hook/agenthub-hook.js` (`Stop` 분기)

**Interfaces:**
- Consumes: `/api/hook/stop` 응답 `{ reply }`(Task 5), Task 1에서 확정한 주입 형식.

- [ ] **Step 1: Stop 분기 교체**

`AgentHub/hook/agenthub-hook.js`의 기존 Stop 분기(`if (p.hook_event_name === 'Stop') { … }`) 전체를 아래로 교체(Task 1 스파이크가 `{decision:block, reason}` 확정 기준. 다른 형식이 확정됐으면 stdout.write 부분만 그 형식으로):

```js
  if (p.hook_event_name === 'Stop') {
    // 세션이 턴을 끝냄. 폰이 이 세션을 보고 있으면 서버가 답장을 기다렸다가 돌려준다(블로킹).
    // 답장을 받으면 세션에 주입해 대화를 잇고, 없으면(무응답/닫기/타임아웃/미watch) 정상 종료.
    const windowSec = Number(process.argv[2]) || 600;
    const budgetMs = Math.max((windowSec - 5) * 1000, 1000);
    post(port, '/api/hook/stop', {
      session_id: p.session_id, cwd: p.cwd, waitMs: budgetMs
    }, budgetMs + 2000, data => {
      try {
        const r = JSON.parse((data || '{}').replace(/^﻿/, '')); // 선행 BOM 제거
        if (r.reply) {
          // 폰 답장을 세션에 주입: 중단을 막고 그 텍스트로 이어가게 한다.
          process.stdout.write(JSON.stringify({ decision: 'block', reason: r.reply }));
        }
        // reply 없음 → 출력 없음 = 정상 종료.
      } catch (e) {}
      process.exit(0);
    });
    setTimeout(() => process.exit(0), budgetMs + 4000); // 안전망(Claude 훅 timeout 이내)
    return;
  }
```

- [ ] **Step 2: 문법 확인**

Run: `node --check AgentHub/hook/agenthub-hook.js`
Expected: 출력 없음(문법 OK).

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/hook/agenthub-hook.js
git commit -m "feat: Stop 훅을 답장 대기·주입 블로킹 방식으로 변경"
```

---

### Task 8: PWA 답장 카드 (index.html + app.js + i18n)

**Files:**
- Modify: `AgentHub/View/Htmls/index.html`
- Modify: `AgentHub/View/Htmls/js/app.js`
- Modify: `AgentHub/View/Htmls/js/i18n.js`

**Interfaces:**
- Consumes: WS `{type:"reply", id, project, message, sessionId, resent?}`, `{type:"replyClose", sessionId}`(Task 4). 전송: `{type:"reply", id, text}`, `{type:"replyDismiss", id}`.

- [ ] **Step 1: index.html에 답장 오버레이 추가**

`AgentHub/View/Htmls/index.html`의 `<div id="askExpired" …>` 블록 전체(닫는 `</div>` 포함) 바로 아래에 추가:

```html
    <!-- 답장 오버레이: 보고 있는 세션이 턴을 끝내면 자유 텍스트로 대화를 잇거나 완료로 닫음 -->
    <div id="reply" class="elicit-overlay" hidden>
      <div class="elicit-card">
        <div class="elicit-header" id="replyHeader" data-i18n="reply.title">Claude가 답장을 기다립니다</div>
        <div class="elicit-question" id="replyMessage"></div>
        <div class="elicit-hint" data-i18n="reply.hint">이어서 할 말을 입력해 보내거나, 완료로 닫으세요.</div>
        <textarea id="replyText" class="elicit-other" rows="3" data-i18n-ph="reply.ph"></textarea>
        <div class="elicit-actions">
          <button id="replyDismiss" class="elicit-btn ghost" data-i18n="reply.dismiss">완료(닫기)</button>
          <button id="replySend" class="elicit-btn primary" data-i18n="reply.send">전송</button>
        </div>
      </div>
    </div>
```

- [ ] **Step 2: i18n 키 추가(ko)**

`AgentHub/View/Htmls/js/i18n.js`의 ko 사전에서 `'elicit.submit': '답변 보내기',` 줄 아래에 추가:

```javascript
      'reply.title': 'Claude가 답장을 기다립니다',
      'reply.hint': '이어서 할 말을 입력해 보내거나, 완료로 닫으세요.',
      'reply.ph': '답장을 입력하세요',
      'reply.send': '전송',
      'reply.dismiss': '완료(닫기)',
```

- [ ] **Step 3: i18n 키 추가(en)**

같은 파일 en 사전에서 `'elicit.submit': 'Send answer',` 줄 아래에 추가:

```javascript
      'reply.title': 'Claude is waiting for your reply',
      'reply.hint': 'Type a message to continue, or close when done.',
      'reply.ph': 'Type your reply',
      'reply.send': 'Send',
      'reply.dismiss': 'Done (close)',
```

- [ ] **Step 4: app.js — WS 라우팅에 reply 추가**

`AgentHub/View/Htmls/js/app.js`의 `ws.onmessage` 라우팅에서 `else if (m.type === 'permission') { handlePermission(m); }` 줄 아래에 추가:

```javascript
      else if (m.type === 'reply') { handleReply(m); }
      else if (m.type === 'replyClose') { handleReplyClose(m); }
```

- [ ] **Step 5: app.js — 답장 핸들러·버튼 추가**

같은 파일에서 권한 요청 핸들러 블록(`document.getElementById('permDeny') … sendPermission('deny'));` 줄) 바로 아래에 추가:

```javascript

// ---- 답장(턴 종료 후 자유 텍스트로 세션 이어가기) ----
let replyState = null; // { id, sessionId }
function handleReply(m) {
  if (m.sessionId !== currentSessionId) return; // 지금 보고 있는 세션만(교차-세션 팝업 방지)
  replyState = { id: m.id, sessionId: m.sessionId };
  document.getElementById('replyMessage').textContent = m.message || '';
  const ta = document.getElementById('replyText'); if (ta) ta.value = '';
  // resent(재접속 재전송)면 시스템 알림 생략(중복 방지).
  if (!m.resent && ('Notification' in window) && Notification.permission === 'granted') {
    const opts = { body: titlePrefix(m.sessionId) + (m.message || ''), tag: 'reply-' + m.id, requireInteraction: true };
    if (navigator.serviceWorker && navigator.serviceWorker.ready)
      navigator.serviceWorker.ready.then(r => r.showNotification(t('reply.title'), opts)).catch(() => { try { new Notification(t('reply.title'), opts); } catch (e) {} });
    else try { new Notification(t('reply.title'), opts); } catch (e) {}
  }
  document.getElementById('reply').hidden = false;
  if (window.I18n) I18n.apply();
}
function closeReplyOverlay() {
  document.getElementById('reply').hidden = true;
  replyState = null;
}
function handleReplyClose(m) {
  if (replyState && m.sessionId === replyState.sessionId) closeReplyOverlay();
}
document.getElementById('replySend') && document.getElementById('replySend').addEventListener('click', () => {
  if (!replyState) return;
  const ta = document.getElementById('replyText');
  const text = ta ? ta.value.trim() : '';
  if (!text) return; // 빈 답장은 전송하지 않음
  send({ type: 'reply', id: replyState.id, text });
  closeReplyOverlay();
});
document.getElementById('replyDismiss') && document.getElementById('replyDismiss').addEventListener('click', () => {
  if (!replyState) return;
  send({ type: 'replyDismiss', id: replyState.id });
  closeReplyOverlay();
});
```

- [ ] **Step 6: app.js — clawd 차단 시 답장 오버레이 재열기**

같은 파일의 `handleAnswerBlocked` 함수 본문에서 `if (elicit) { document.getElementById('elicit').hidden = false; renderElicitStep(); }` 줄 아래에 추가:

```javascript
  else if (replyState) { document.getElementById('reply').hidden = false; }
```

- [ ] **Step 7: 빌드 확인(자산 포함)**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공(HTML/JS는 리소스로 포함).

- [ ] **Step 8: 커밋**

```bash
git add AgentHub/View/Htmls/index.html AgentHub/View/Htmls/js/app.js AgentHub/View/Htmls/js/i18n.js
git commit -m "feat: PWA 답장 카드(자유 텍스트 전송·완료 닫기) 추가"
```

---

### Task 9: 사용 가이드 동기화 (docs/index.html)

**Files:**
- Modify: `docs/index.html`

- [ ] **Step 1: 알림/질문 관련 섹션 위치 파악**

Run: `grep -n "질문\|AskUserQuestion\|완료\|알림" docs/index.html`
기존 모바일 모니터/알림 안내 문단(질문·권한 흐름 설명 근처)을 찾는다.

- [ ] **Step 2: 답장 기능 안내 문단 추가**

위에서 찾은 알림/질문 안내 문단과 같은 구획(같은 리스트/섹션)에 아래 취지의 문구를 기존 HTML 구조(태그·클래스)에 맞춰 추가. 예시 문안:

```
세션 상세 화면을 보는 중에 Claude가 턴(답변)을 끝내면, 화면에 답장 입력창이 뜹니다.
이어서 할 말을 입력해 [전송]하면 그 세션에서 대화가 이어지고, 더 할 말이 없으면 [완료(닫기)]로 마칠 수 있습니다.
(이 답장은 폰에서 해당 세션을 보고 있을 때 동작하며, 앱이 꺼져 있으면 지금처럼 '완료' 알림만 옵니다.)
객관식 질문(AskUserQuestion)은 기존처럼 보기에서 고르거나 '기타'에 직접 입력해 답할 수 있습니다.
```

한글/영문 병기 구조가 있으면 두 언어 모두 갱신한다.

- [ ] **Step 3: 커밋**

```bash
git add docs/index.html
git commit -m "docs: 폰 답장(턴 이어가기) 사용 가이드 추가"
```

---

### Task 10: 통합 검증 (빌드·테스트·엔드투엔드)

**Files:** 없음(검증 전용)

- [ ] **Step 1: 단위 테스트 전체 통과**

Run: `dotnet test AgentHub.Tests\AgentHub.Tests.csproj`
Expected: 전체 PASS(기존 + ReplyRegistry 6 + LastAssistant 4).

- [ ] **Step 2: 솔루션 빌드**

Run: `msbuild AgentHub.sln /t:Restore` → `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공, `install/Debug/AgentHub.exe` 생성.

- [ ] **Step 3: 엔드투엔드(수동) — 답장 주입**

`install/Debug/AgentHub.exe` 실행 → 훅 재설치(설정에서 uninstall→install로 새 Stop 엔트리 반영) → `claude` 세션 실행 → 폰(또는 PC 콘솔 세션)에서 그 세션 상세를 연다(watch) → Claude가 턴을 질문으로 끝냄 → 답장 카드가 뜨는지, 텍스트 [전송] 시 claude가 그 텍스트로 이어가는지 확인.
Expected: 답장 카드 표시 → 전송 → claude 계속 진행.

- [ ] **Step 4: 엔드투엔드(수동) — 완료(닫기) 및 회귀**

- 답장 카드에서 [완료(닫기)] → claude가 정상 종료되는지.
- 세션 상세를 보고 있지 않은 상태(목록/앱 닫힘)에서 턴 종료 → 예전처럼 '완료' 알림만 오고 claude가 붙들리지 않는지.
Expected: 닫기 시 정상 종료, 미watch 시 회귀 없음.

- [ ] **Step 5: 최종 상태 확인**

Run: `git status`
Expected: 워킹 트리 클린(모든 변경 커밋됨), 스파이크 임시 변경 없음.
