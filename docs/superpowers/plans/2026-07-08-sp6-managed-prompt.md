# SP6 관리 세션 프롬프트/답변(Claude) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** 모바일에서 Agent Hub가 PTY로 실행한 Claude "관리 세션"에 프롬프트를 보내고 AskUserQuestion 옵션을 선택해 답변한다. 외부 라이브 세션은 보기 전용.

**Architecture:** SP2의 `ConPtySession`을 재사용해 `claude`를 관리 PTY로 실행 → `ManagedSessionRegistry`가 sessionId↔PTY 상관. 프롬프트=stdin write, AskUserQuestion 답변=키입력 시퀀스. 명령은 `/ws/agents`(승인기기) + 터미널 허용 토글로 게이트. 엔진은 `EngineSpec`으로 추상화(SP7 Codex 대비).

**Tech Stack:** C# 8/.NET FW 4.8, EmbedIO, ConPTY(기존 SP2), Newtonsoft, xUnit. 새 NuGet 없음.

## Global Constraints
- 네임스페이스 `AgentHub.*`. `EmbedIO/` 수정 금지. 한글 UTF-8 유지. 새 NuGet 금지.
- 빌드 PowerShell msbuild(`& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`), 0 errors. 테스트 `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`.
- 순수 파일(EngineSpec 키시퀀스 계산, TranscriptParser.ExtractPendingAsk)은 소스 링크 테스트. PTY/레지스트리 I/O는 build+E2E.
- 관리 명령(startSession/prompt/answer)은 **`Properties.Settings.Default.TerminalEnabled == true` AND 기기 Approved**일 때만. (SP2 터미널과 동일 표면.)
- `claude` 실행: PATH의 shim → `cmd.exe /c claude`를 cwd에서 실행(ConPtySession). 실측 확인.
- 실측: `TranscriptParser`(Summarize/ParseEvents/ComputeStatus/SummarizeToolUse public static), `SessionSummary`(Id..MessageCount), `AgentMonitorModule.OnMessageReceivedAsync`가 `WatchMessage{Type,SessionId}` 처리·`_watching`·`_tokens`·승인 게이트 보유, `ConPtySession(shell,cwd,cols,rows,onOutput)`·`Write(byte[])`.
- 브랜치: `feature/sp6-managed-prompt`.

---

### Task 1: PendingAsk 모델 + TranscriptParser.ExtractPendingAsk (순수) + 테스트

**Files:** Create `AgentHub/Common/Models/PendingAsk.cs`; Modify `AgentHub/Common/Models/SessionSummary.cs`, `AgentHub/Server/Agents/TranscriptParser.cs`; Test `AgentHub.Tests/PendingAskTests.cs`

**Interfaces:**
- `class PendingAsk { string Question; string Header; bool MultiSelect; List<string> Options; }`
- `SessionSummary`에 `bool Managed`, `PendingAsk PendingAsk` 추가.
- `static PendingAsk TranscriptParser.ExtractPendingAsk(IReadOnlyList<string> lines)` — 마지막 assistant tool_use name=="AskUserQuestion"이고 대응 tool_result 없으면 첫 question의 {question,header,multiSelect,options[label]} 반환, 아니면 null.

- [ ] **Step 1: 실패 테스트** `PendingAskTests.cs`:
```csharp
using System.Collections.Generic;
using AgentHub.Server.Agents;
using Xunit;
namespace AgentHub.Tests {
  public class PendingAskTests {
    [Fact] public void Extracts_unanswered_ask() {
      var lines = new List<string>{
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"tu1\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":[{\"question\":\"어디로?\",\"header\":\"방향\",\"multiSelect\":false,\"options\":[{\"label\":\"A\",\"description\":\"a\"},{\"label\":\"B\",\"description\":\"b\"}]}]}}]}}"
      };
      var p = TranscriptParser.ExtractPendingAsk(lines);
      Assert.NotNull(p); Assert.Equal("어디로?", p.Question);
      Assert.Equal(2, p.Options.Count); Assert.Equal("A", p.Options[0]);
    }
    [Fact] public void Null_when_answered() {
      var lines = new List<string>{
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"tu1\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":[{\"question\":\"q\",\"options\":[{\"label\":\"A\"}]}]}}]}}",
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"tu1\",\"content\":\"answered\"}]}}"
      };
      Assert.Null(TranscriptParser.ExtractPendingAsk(lines));
    }
    [Fact] public void Null_when_no_ask() {
      Assert.Null(TranscriptParser.ExtractPendingAsk(new List<string>{"{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"hi\"}]}}"}));
    }
  }
}
```

- [ ] **Step 2: 실패 확인** `dotnet test ... --filter PendingAskTests` → FAIL.

- [ ] **Step 3: 모델 + 파서 구현**

`AgentHub/Common/Models/PendingAsk.cs`:
```csharp
using System.Collections.Generic;
namespace AgentHub.Common.Models {
  /// <summary>아직 답변되지 않은 AskUserQuestion(첫 질문).</summary>
  public class PendingAsk {
    public string Question { get; set; }
    public string Header { get; set; }
    public bool MultiSelect { get; set; }
    public List<string> Options { get; set; }
  }
}
```
`SessionSummary.cs`에 추가:
```csharp
        public bool Managed { get; set; }
        public PendingAsk PendingAsk { get; set; }
```
`TranscriptParser.cs`에 추가(기존 `TryParse`/`Str` 헬퍼 재사용):
```csharp
        public static PendingAsk ExtractPendingAsk(IReadOnlyList<string> lines)
        {
            JObject lastAsk = null; string askId = null;
            foreach (var line in lines)
            {
                var o = TryParse(line); if (o == null) continue;
                var content = o["message"]?["content"] as JArray; if (content == null) continue;
                foreach (var b in content.OfType<JObject>())
                {
                    if (Str(b["type"]) == "tool_use" && Str(b["name"]) == "AskUserQuestion")
                    { lastAsk = b; askId = Str(b["id"]); }
                    else if (Str(b["type"]) == "tool_result" && askId != null && Str(b["tool_use_id"]) == askId)
                    { lastAsk = null; askId = null; } // 답변됨
                }
            }
            if (lastAsk == null) return null;
            var q = (lastAsk["input"]?["questions"] as JArray)?.OfType<JObject>().FirstOrDefault();
            if (q == null) return null;
            var opts = new List<string>();
            foreach (var op in (q["options"] as JArray ?? new JArray()).OfType<JObject>())
            { var l = Str(op["label"]); if (l != null) opts.Add(l); }
            return new PendingAsk { Question = Str(q["question"]), Header = Str(q["header"]),
                MultiSelect = q["multiSelect"]?.Type == JTokenType.Boolean && q["multiSelect"].Value<bool>(),
                Options = opts };
        }
```
(`using AgentHub.Common.Models;`, `System.Linq`, `System.Collections.Generic` 확인.)

- [ ] **Step 4: 소스 링크 + csproj + 통과**
`AgentHub.Tests.csproj`에 `<Compile Include="..\AgentHub\Common\Models\PendingAsk.cs" Link="Linked\PendingAsk.cs" />`. `AgentHub.csproj`에 `<Compile Include="Common\Models\PendingAsk.cs" />`. (TranscriptParser·SessionSummary는 이미 등록/링크됨 — SessionSummary가 테스트에 링크돼 있으면 PendingAsk 참조 위해 함께 링크 필요.) `dotnet test` 전체 PASS.

- [ ] **Step 5: 커밋** `feat(sp6): PendingAsk 모델 + TranscriptParser.ExtractPendingAsk + 테스트`

---

### Task 2: EngineSpec (Claude) + answerKeystrokes (순수) + 테스트

**Files:** Create `AgentHub/Server/Terminal/EngineSpec.cs`; Test `AgentHub.Tests/EngineSpecTests.cs`

**Interfaces:**
- `abstract class EngineSpec { string Key; string LaunchCommand(); string ProjectDir(string cwd); static string AnswerKeystrokes(int optionIndex); }`
- `class ClaudeEngine : EngineSpec` — Key="claude", LaunchCommand()=`"cmd.exe /c claude"`, ProjectDir(cwd)=`~/.claude/projects/<encoded>`.
- `AnswerKeystrokes(i)` = `string.Concat(Enumerable.Repeat("[B", i)) + "\r"` (Down×i + Enter). 순수·정적.

- [ ] **Step 1: 실패 테스트** `EngineSpecTests.cs`:
```csharp
using AgentHub.Server.Terminal;
using Xunit;
namespace AgentHub.Tests {
  public class EngineSpecTests {
    [Theory]
    [InlineData(0, "\r")]
    [InlineData(1, "[B\r")]
    [InlineData(3, "[B[B[B\r")]
    public void AnswerKeystrokes(int i, string expected)
      => Assert.Equal(expected, EngineSpec.AnswerKeystrokes(i));
  }
}
```

- [ ] **Step 2: 실패 확인** → FAIL.

- [ ] **Step 3: 구현** `EngineSpec.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
namespace AgentHub.Server.Terminal
{
    public abstract class EngineSpec
    {
        public abstract string Key { get; }
        public abstract string LaunchCommand();
        public abstract string ProjectDir(string cwd);

        // AskUserQuestion 메뉴 선택(커서 최상단 가정): Down×i + Enter. best-effort.
        public static string AnswerKeystrokes(int optionIndex)
            => string.Concat(Enumerable.Repeat("[B", Math.Max(0, optionIndex))) + "\r";

        public static EngineSpec For(string key)
        {
            switch ((key ?? "claude").ToLowerInvariant())
            {
                case "claude": default: return new ClaudeEngine();
            }
        }
    }

    public sealed class ClaudeEngine : EngineSpec
    {
        public override string Key => "claude";
        public override string LaunchCommand() => "cmd.exe /c claude";
        public override string ProjectDir(string cwd)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var enc = (cwd ?? "").Replace(':', '-').Replace('\\', '-').Replace('/', '-');
            return Path.Combine(home, ".claude", "projects", enc);
        }
    }
}
```
> `ProjectDir` 인코딩은 Claude의 실제 인코딩과 일치해야 함(예: `C:\GIT\...` → `C--GIT-...`). Task 3 상관 로직에서 실측 확인 후 필요시 조정(정확 매칭 실패 시 project dir 전체 스캔 폴백).

- [ ] **Step 4: 소스 링크 + csproj + 통과** — `EngineSpec.cs` 테스트 링크 + `AgentHub.csproj` 등록. `dotnet test` PASS.

- [ ] **Step 5: 커밋** `feat(sp6): EngineSpec(Claude) + answerKeystrokes + 테스트`

---

### Task 3: `ManagedSessionRegistry` (I/O: 실행·상관·프롬프트·답변)

**Files:** Create `AgentHub/Server/Terminal/ManagedSessionRegistry.cs`

**Interfaces:**
- `static string ManagedSessionRegistry.Start(string engineKey, string cwd)` — ConPtySession 실행, 임시 handle 반환. 백그라운드로 sessionId 상관.
- `static bool IsManaged(string sessionId)`
- `static bool Prompt(string sessionId, string text)` / `static bool Answer(string sessionId, int optionIndex)`
- `static void DisposeAll()`

- [ ] **Step 1: 구현**
```csharp
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AgentHub.Common.Util;

namespace AgentHub.Server.Terminal
{
    /// <summary>Agent Hub가 실행·소유한 엔진 세션(PTY) 레지스트리. sessionId↔ConPtySession 상관.</summary>
    public static class ManagedSessionRegistry
    {
        private class Entry { public ConPtySession Pty; public string Cwd; public EngineSpec Engine; public volatile string SessionId; }
        private static readonly ConcurrentDictionary<string, Entry> ById = new ConcurrentDictionary<string, Entry>();
        private static readonly ConcurrentBag<Entry> Pending = new ConcurrentBag<Entry>();

        public static bool IsManaged(string sessionId)
            => !string.IsNullOrEmpty(sessionId) && ById.ContainsKey(sessionId);

        public static string Start(string engineKey, string cwd)
        {
            var engine = EngineSpec.For(engineKey);
            if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
                throw new InvalidOperationException("유효한 폴더가 아닙니다: " + cwd);
            var launchedAt = DateTime.UtcNow;
            var e = new Entry { Cwd = cwd, Engine = engine };
            e.Pty = new ConPtySession(engine.LaunchCommand(), cwd, 120, 40, (buf, n) => { /* 출력은 트랜스크립트 tail로 표시 */ });
            Pending.Add(e);
            // 상관: 새 트랜스크립트의 sessionId 확정
            var t = new Thread(() => Correlate(e, launchedAt)) { IsBackground = true };
            t.Start();
            return "starting"; // sessionId는 상관 완료 후 IsManaged로 노출
        }

        private static void Correlate(Entry e, DateTime after)
        {
            try
            {
                var dir = e.Engine.ProjectDir(e.Cwd);
                for (int i = 0; i < 60 && e.SessionId == null; i++) // 최대 ~30초
                {
                    Thread.Sleep(500);
                    if (!Directory.Exists(dir)) continue;
                    var f = new DirectoryInfo(dir).GetFiles("*.jsonl")
                        .Where(x => x.LastWriteTimeUtc >= after.AddSeconds(-2))
                        .OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                    if (f != null)
                    {
                        var id = Path.GetFileNameWithoutExtension(f.Name);
                        e.SessionId = id; ById[id] = e;
                    }
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        public static bool Prompt(string sessionId, string text)
        {
            if (!ById.TryGetValue(sessionId, out var e) || text == null) return false;
            try { e.Pty.Write(Encoding.UTF8.GetBytes(text + "\r")); return true; }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Answer(string sessionId, int optionIndex)
        {
            if (!ById.TryGetValue(sessionId, out var e)) return false;
            try { e.Pty.Write(Encoding.UTF8.GetBytes(EngineSpec.AnswerKeystrokes(optionIndex))); return true; }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static void DisposeAll()
        {
            foreach (var kv in ById) { try { kv.Value.Pty.Dispose(); } catch { } }
            ById.Clear();
        }
    }
}
```
> `ProjectDir` 인코딩을 실제 Claude 폴더명과 대조(예: 현재 세션 폴더 `C--GIT-PRIVATE-agent-hub`). 불일치 시 `~/.claude/projects` 전체에서 `after` 이후 최신 파일로 폴백 상관하도록 조정하고 report에 기록.

- [ ] **Step 2: csproj 등록 + 빌드** — `<Compile Include="Server\Terminal\ManagedSessionRegistry.cs" />`. PowerShell msbuild → 0 errors.

- [ ] **Step 3: 커밋** `feat(sp6): ManagedSessionRegistry — claude 관리 세션 실행/상관/프롬프트/답변`

---

### Task 4: WS 메시지(startSession/prompt/answer) + 관리 표식 + 게이트

**Files:** Modify `AgentHub/Server/Socket/AgentMonitorModule.cs`, `AgentHub/Server/Agents/ClaudeSessionReader.cs`, `AgentHub/Server/Socket/TerminalModule.cs`(DisposeAll 연동)

- [ ] **Step 1: `AgentMonitorModule` 메시지 확장**
`WatchMessage`에 필드 추가: `public string Cwd; public string Engine; public string Text; public int OptionIndex;`
`OnMessageReceivedAsync`의 승인 확인 뒤에 분기 추가(터미널 토글도 확인):
```csharp
                else if (msg.Type == "startSession")
                {
                    if (!Properties.Settings.Default.TerminalEnabled) return;
                    try { AgentHub.Server.Terminal.ManagedSessionRegistry.Start(msg.Engine ?? "claude", msg.Cwd); }
                    catch (Exception ex) { LogService.Instance.Error(ex); }
                }
                else if (msg.Type == "prompt")
                {
                    if (Properties.Settings.Default.TerminalEnabled && !string.IsNullOrEmpty(msg.SessionId))
                        AgentHub.Server.Terminal.ManagedSessionRegistry.Prompt(msg.SessionId, msg.Text);
                }
                else if (msg.Type == "answer")
                {
                    if (Properties.Settings.Default.TerminalEnabled && !string.IsNullOrEmpty(msg.SessionId))
                        AgentHub.Server.Terminal.ManagedSessionRegistry.Answer(msg.SessionId, msg.OptionIndex);
                }
```
(`using System;` 확인. prompt/answer는 승인 게이트가 이미 상단에 있음 — startSession도 동일 위치.)

- [ ] **Step 2: `ClaudeSessionReader` 관리 표식 + PendingAsk**
`Summarize` 호출 뒤 각 SessionSummary에 대해:
```csharp
                    s.Managed = AgentHub.Server.Terminal.ManagedSessionRegistry.IsManaged(s.Id);
                    s.PendingAsk = TranscriptParser.ExtractPendingAsk(lines);
```
(이미 `lines`를 읽어 Summarize에 넘기므로 같은 lines로 ExtractPendingAsk 호출.)

- [ ] **Step 3: 토글 OFF 시 관리 세션 정리**
`TerminalModule.DisableAll()`(또는 `DisableAllInstances`) 경로에 `AgentHub.Server.Terminal.ManagedSessionRegistry.DisposeAll();` 추가. `EmbedIOServer.StopServer`의 정리에도 추가.

- [ ] **Step 4: 빌드** → 0 errors.

- [ ] **Step 5: 커밋** `feat(sp6): /ws/agents startSession/prompt/answer + 관리 표식/PendingAsk + 토글 정리`

---

### Task 5: 프론트엔드 — 새 세션/프롬프트/옵션 답변

**Files:** Modify `AgentHub/View/Htmls/index.html`, `js/app.js`, `js/i18n.js`, `css/app.css`, `sw.js`

- [ ] **Step 1: index.html**
모니터에 "+ 새 세션" 버튼(터미널 허용 시 노출; SP2 termBtn 노출 로직 재사용 가능) + 폴더 입력 프롬프트. 상세(`#detail`)에 프롬프트 입력 영역:
```html
      <div id="promptBar" class="prompt-bar" hidden>
        <input id="promptInput" type="text" data-i18n-ph="prompt.ph" placeholder="프롬프트 입력…" />
        <button id="promptSend" data-i18n="prompt.send">보내기</button>
      </div>
      <div id="askBox" class="ask-box" hidden></div>
```

- [ ] **Step 2: app.js**
- `sessions` 렌더 시 각 세션 `managed` 여부 저장. 상세(`openDetail`) 진입 시 해당 세션이 managed면 `#promptBar` 노출, 아니면 숨김.
- `renderActivity`/`sessions`에서 현재 세션의 `pendingAsk`가 있으면 `#askBox`에 질문 + 옵션 버튼 렌더; 탭 시 `send({type:'answer', sessionId, optionIndex:i})`.
- `#promptSend` → `send({type:'prompt', sessionId:currentSessionId, text})` 후 입력 비움.
- "+ 새 세션" → 폴더 입력받아 `send({type:'startSession', engine:'claude', cwd})`.
- 관리/pendingAsk 정보는 `sessions` 메시지의 각 세션 객체에서 취득(서버가 포함). `sessionsById[id].managed / .pendingAsk`.

- [ ] **Step 3: i18n** `prompt.ph`("프롬프트 입력…"/"Type a prompt…"), `prompt.send`("보내기"/"Send"), `session.new`("+ 새 세션"/"+ New session"), `ask.pick`("선택하세요"/"Choose") ko/en.

- [ ] **Step 4: css** `.prompt-bar`(입력+버튼 flex), `.ask-box`(질문+옵션 버튼 세로), 옵션 버튼 스타일.

- [ ] **Step 5: sw 캐시 상향.**

- [ ] **Step 6: 검증** `node --check` app.js/i18n.js, PowerShell msbuild → 0 errors. 요소 ID/전송 메시지 스키마 일치 확인.

- [ ] **Step 7: 커밋** `feat(sp6): 모바일 새 세션 시작 + 프롬프트 전송 + AskUserQuestion 옵션 답변`

---

### Task 6: E2E + 빌드 게이트 + 마무리
- [ ] **Step 1: 전체 테스트** `dotnet test` PASS(PendingAsk/EngineSpec 포함).
- [ ] **Step 2: 빌드 게이트** PowerShell msbuild Restore+Build → 0 errors.
- [ ] **Step 3: 사용자 수동 E2E**: 터미널 허용 ON → 폰에서 "+ 새 세션"(폴더) → 관리 Claude 세션 생성·모니터에 등장 → 상세에서 프롬프트 전송 → Claude 응답 확인 → Claude가 AskUserQuestion 낼 때 옵션 버튼 → 탭 → 선택 반영(키입력 매핑 실제 검증; 어긋나면 폴백/조정 필요 기록). 외부 세션엔 프롬프트/옵션 UI 미노출. 토글 OFF 시 관리 세션 종료.
- [ ] **Step 4: 스펙 대비 점검.**
- [ ] **Step 5: 마무리** `superpowers:finishing-a-development-branch`.

---

## Self-Review
- **커버리지:** PendingAsk/파서=T1, EngineSpec/키시퀀스=T2, 레지스트리(실행/상관/프롬프트/답변)=T3, WS/게이트/표식/정리=T4, 프론트=T5, 검증=T6.
- **플레이스홀더:** 없음. 리스크 2곳 명시: (1) ProjectDir 인코딩 실측(T2/T3, 폴백=projects 전체 스캔), (2) AskUserQuestion 키입력 매핑 취약(T6 실측, 폴백=터미널 직접 선택).
- **타입 일관성:** `ManagedSessionRegistry.Start/IsManaged/Prompt/Answer/DisposeAll`, `EngineSpec.For/AnswerKeystrokes/LaunchCommand/ProjectDir`, `PendingAsk{Question,Header,MultiSelect,Options}`, WS `{startSession(engine,cwd)/prompt(sessionId,text)/answer(sessionId,optionIndex)}`, `SessionSummary.Managed/PendingAsk` 일치.
- **게이트:** 모든 관리 명령 TerminalEnabled+Approved. 토글 OFF/서버 정지 시 DisposeAll.
