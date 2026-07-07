# SP1 실제 에이전트 활동 피드 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 모바일 모니터의 mock 데이터를 Claude Code 트랜스크립트(JSONL) 기반 실제 세션 목록 + 실시간 활동 피드로 교체한다.

**Architecture:** 순수 파서(`TranscriptParser`)가 JSONL 라인 → `SessionSummary`/`ActivityEvent`로 변환하고, I/O 레이어(`ClaudeSessionReader`)가 `~/.claude/projects`를 스캔·증분 tail·`FileSystemWatcher`로 감시해 기존 `AgentMonitorService` seam으로 push한다. 전송은 기존 `/ws/agents` WebSocket을 확장(`sessions`/`watch`/`activity`)하고, 프론트엔드는 세션 리스트 + 상세 피드로 재구성한다.

**Tech Stack:** C# 8 / .NET Framework 4.8, Newtonsoft.Json 13(기존), EmbedIO(기존, 미변경), xUnit(신규 테스트 프로젝트), 프론트엔드 vanilla JS.

## Global Constraints

- 루트 네임스페이스 `AgentHub.*` (신규 코드 포함).
- 서드파티 `EmbedIO/` 및 `EmbedIO` 네임스페이스는 **수정 금지**.
- 한글(UTF-8) 문자열 인코딩 훼손 금지 — Edit 도구/바이트 단위로만 편집.
- 새 NuGet 의존성 금지(파서는 기존 Newtonsoft.Json 사용). 테스트 프로젝트만 xUnit 추가.
- `TranscriptParser`는 **순수**해야 한다: `LogService`/`EmbedIO`/`System.Windows.Forms`/파일 I/O 의존 금지. Newtonsoft.Json + DTO에만 의존(테스트 소스 링크 가능하도록).
- 모든 런타임 예외는 기존 `AgentHub.Common.Util.LogService.Instance.Error(ex)` 경유.
- 빌드 게이트: `msbuild AgentHub.sln /t:Restore` → `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"` 통과. 산출물 `install/Debug/AgentHub.exe`.
- 기본값(스펙): 활동 윈도우 24h, 세션 상한 30, active=60초, ended=30분 초과, 상세 최근 이벤트 200.
- 작업 브랜치: `feature/sp1-agent-activity-feed` (이미 생성됨, 스펙 커밋 존재).

---

### Task 1: DTO + 순수 파서(요약) + 테스트 프로젝트

세션 JSONL 라인 → `SessionSummary`(제목/프로젝트/브랜치/현재작업). 순수 파서와 이를 검증할 xUnit 프로젝트를 함께 만든다.

**Files:**
- Create: `AgentHub/Common/Models/SessionSummary.cs`
- Create: `AgentHub/Common/Models/ActivityEvent.cs`
- Create: `AgentHub/Server/Agents/TranscriptParser.cs`
- Create: `AgentHub.Tests/AgentHub.Tests.csproj`
- Test: `AgentHub.Tests/TranscriptParserSummaryTests.cs`

**Interfaces:**
- Produces:
  - `class AgentHub.Common.Models.SessionSummary { string Id; string Title; string Project; string Cwd; string GitBranch; string Status; string CurrentTask; string ToolName; string LastActivityAt; int MessageCount; }`
  - `class AgentHub.Common.Models.ActivityEvent { string Kind; string Ts; string ToolName; string Summary; string Text; }`
  - `static SessionSummary TranscriptParser.Summarize(string sessionId, IReadOnlyList<string> lines, DateTime nowUtc)`
  - `static string TranscriptParser.SummarizeToolUse(string name, Newtonsoft.Json.Linq.JObject input)`
  - `static string TranscriptParser.ComputeStatus(TimeSpan age, bool lastIsUnfinishedTool)` (Task 2에서 사용/검증)

- [ ] **Step 1: DTO 2개 작성**

`AgentHub/Common/Models/SessionSummary.cs`:
```csharp
namespace AgentHub.Common.Models
{
    /// <summary>모바일 모니터의 세션 카드 1건 요약. (Claude Code 트랜스크립트에서 파생)</summary>
    public class SessionSummary
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Project { get; set; }        // cwd의 마지막 세그먼트
        public string Cwd { get; set; }
        public string GitBranch { get; set; }
        public string Status { get; set; }         // active | idle | ended
        public string CurrentTask { get; set; }
        public string ToolName { get; set; }       // 최신 tool_use 이름 (없으면 null)
        public string LastActivityAt { get; set; } // ISO 8601 (UTC)
        public int MessageCount { get; set; }
    }
}
```

`AgentHub/Common/Models/ActivityEvent.cs`:
```csharp
namespace AgentHub.Common.Models
{
    /// <summary>세션 상세 활동 피드의 이벤트 1건.</summary>
    public class ActivityEvent
    {
        public string Kind { get; set; }     // message | thinking | tool_use | tool_result | user_prompt | mode_change
        public string Ts { get; set; }       // ISO 8601
        public string ToolName { get; set; }
        public string Summary { get; set; }  // 한 줄 요약
        public string Text { get; set; }     // 본문
    }
}
```

- [ ] **Step 2: 테스트 프로젝트 생성 + 솔루션에 추가**

`AgentHub.Tests/AgentHub.Tests.csproj` (SDK 스타일, net48. WinForms/WebView2 전체 빌드를 피하려 **순수 파서 슬라이스만 소스 링크**):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>8</LangVersion>
    <IsPackable>false</IsPackable>
    <AssemblyName>AgentHub.Tests</AssemblyName>
    <RootNamespace>AgentHub.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.6.3" />
    <PackageReference Include="xunit" Version="2.4.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.4.5" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="..\AgentHub\Server\Agents\TranscriptParser.cs" Link="Linked\TranscriptParser.cs" />
    <Compile Include="..\AgentHub\Common\Models\SessionSummary.cs" Link="Linked\SessionSummary.cs" />
    <Compile Include="..\AgentHub\Common\Models\ActivityEvent.cs" Link="Linked\ActivityEvent.cs" />
  </ItemGroup>
</Project>
```
솔루션에 추가: `dotnet sln AgentHub.sln add AgentHub.Tests/AgentHub.Tests.csproj`
> 주의: 이 테스트 프로젝트는 SDK 스타일이라 `dotnet test`로만 실행한다. 기존 `msbuild AgentHub.sln` 빌드 게이트에는 SDK 프로젝트가 섞여도 `AgentHub`/`EmbedIO` 빌드에는 영향이 없어야 하며, 만약 `msbuild`가 SDK 프로젝트를 문제 삼으면 솔루션 구성에서 테스트 프로젝트를 Debug/Any CPU 빌드에서 제외한다.

- [ ] **Step 3: 실패하는 요약 테스트 작성**

`AgentHub.Tests/TranscriptParserSummaryTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using AgentHub.Common.Models;
using AgentHub.Server.Agents;
using Xunit;

namespace AgentHub.Tests
{
    public class TranscriptParserSummaryTests
    {
        private static readonly DateTime Now = DateTime.Parse("2026-07-07T10:00:30Z").ToUniversalTime();

        // aiTitle, cwd, gitBranch, 마지막 tool_use(Edit)를 담은 최소 트랜스크립트
        private static List<string> Sample() => new List<string>
        {
            "{\"type\":\"ai-title\",\"aiTitle\":\"에이전트 활동 피드 구현\",\"timestamp\":\"2026-07-07T10:00:00Z\"}",
            "{\"type\":\"user\",\"cwd\":\"C:/GIT/PRIVATE/agent-hub\",\"gitBranch\":\"feature/sp1-agent-activity-feed\",\"timestamp\":\"2026-07-07T10:00:05Z\",\"message\":{\"role\":\"user\",\"content\":\"진행해\"}}",
            "{\"type\":\"assistant\",\"timestamp\":\"2026-07-07T10:00:20Z\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"파일을 수정합니다.\"},{\"type\":\"tool_use\",\"name\":\"Edit\",\"input\":{\"file_path\":\"C:/GIT/PRIVATE/agent-hub/AgentHub/View/Forms/FormMain.cs\"}}]}}"
        };

        [Fact]
        public void Summarize_extracts_title_project_branch()
        {
            var s = TranscriptParser.Summarize("sess-1", Sample(), Now);
            Assert.Equal("sess-1", s.Id);
            Assert.Equal("에이전트 활동 피드 구현", s.Title);
            Assert.Equal("agent-hub", s.Project);
            Assert.Equal("feature/sp1-agent-activity-feed", s.GitBranch);
        }

        [Fact]
        public void Summarize_uses_last_tool_use_as_current_task()
        {
            var s = TranscriptParser.Summarize("sess-1", Sample(), Now);
            Assert.Equal("Edit", s.ToolName);
            Assert.Contains("FormMain.cs", s.CurrentTask);
        }

        [Fact]
        public void Summarize_counts_messages_and_sets_last_activity()
        {
            var s = TranscriptParser.Summarize("sess-1", Sample(), Now);
            Assert.Equal(2, s.MessageCount); // user + assistant
            Assert.Equal("2026-07-07T10:00:20Z", s.LastActivityAt);
        }
    }
}
```

- [ ] **Step 4: 테스트가 실패하는지 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`
Expected: 컴파일 실패 — `TranscriptParser` 형식이 없음.

- [ ] **Step 5: `TranscriptParser`(요약) 구현**

`AgentHub/Server/Agents/TranscriptParser.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using AgentHub.Common.Models;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// Claude Code 트랜스크립트(JSONL) 라인을 SessionSummary / ActivityEvent로 변환하는 순수 파서.
    /// 파일 I/O·로깅·UI 의존 없음(테스트 소스 링크 대상).
    /// </summary>
    public static class TranscriptParser
    {
        private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan EndedWindow = TimeSpan.FromMinutes(30);

        private static JObject TryParse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try { return JObject.Parse(line); } catch { return null; }
        }

        private static string Str(JToken t) => t?.Type == JTokenType.String ? t.Value<string>() : null;

        public static SessionSummary Summarize(string sessionId, IReadOnlyList<string> lines, DateTime nowUtc)
        {
            var s = new SessionSummary { Id = sessionId, Status = "ended" };
            string lastTs = null;
            int msgCount = 0;
            JObject lastAssistant = null;

            foreach (var line in lines)
            {
                var o = TryParse(line);
                if (o == null) continue;

                var title = Str(o["aiTitle"]);
                if (!string.IsNullOrWhiteSpace(title)) s.Title = title;
                if (s.Title == null) { var slug = Str(o["slug"]); if (!string.IsNullOrWhiteSpace(slug)) s.Title = slug; }

                var cwd = Str(o["cwd"]);
                if (!string.IsNullOrWhiteSpace(cwd)) { s.Cwd = cwd; s.Project = LastSegment(cwd); }
                var branch = Str(o["gitBranch"]);
                if (!string.IsNullOrWhiteSpace(branch)) s.GitBranch = branch;

                var ts = Str(o["timestamp"]);
                if (!string.IsNullOrWhiteSpace(ts)) lastTs = ts;

                var type = Str(o["type"]);
                if (type == "assistant" || type == "user") msgCount++;
                if (type == "assistant") lastAssistant = o;

                // 제목 폴백: 첫 사용자 텍스트
                if (s.Title == null && type == "user")
                {
                    var utext = FirstUserText(o);
                    if (!string.IsNullOrWhiteSpace(utext)) s.Title = Truncate(utext, 60);
                }
            }

            s.MessageCount = msgCount;
            s.LastActivityAt = lastTs;

            // 현재 작업 + 도구명
            var (task, tool, unfinishedTool) = CurrentTask(lastAssistant, lines);
            s.CurrentTask = task;
            s.ToolName = tool;

            // 상태
            var age = EndedWindow + TimeSpan.FromSeconds(1); // 타임스탬프 없으면 ended
            if (DateTime.TryParse(lastTs, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var last))
                age = nowUtc - last;
            s.Status = ComputeStatus(age, unfinishedTool);

            if (string.IsNullOrEmpty(s.Project)) s.Project = "(unknown)";
            if (string.IsNullOrEmpty(s.Title)) s.Title = sessionId;
            return s;
        }

        public static string ComputeStatus(TimeSpan age, bool lastIsUnfinishedTool)
        {
            if (age <= EndedWindow)
                return (age <= ActiveWindow || lastIsUnfinishedTool) ? "active" : "idle";
            return "ended";
        }

        public static string SummarizeToolUse(string name, JObject input)
        {
            if (input == null) return name;
            string detail = null;
            switch (name)
            {
                case "Bash": detail = FirstLine(Str(input["command"])); break;
                case "Read":
                case "Edit":
                case "Write":
                case "NotebookEdit": detail = BaseName(Str(input["file_path"])); break;
                case "Grep": detail = Str(input["pattern"]); break;
                case "Glob": detail = Str(input["pattern"]); break;
                case "Task": detail = Str(input["description"]); break;
                case "WebFetch": detail = Str(input["url"]); break;
                default: detail = null; break;
            }
            detail = Truncate(detail, 80);
            return string.IsNullOrWhiteSpace(detail) ? name : $"{name}  {detail}";
        }

        // (task, toolName, lastIsUnfinishedTool)
        private static (string, string, bool) CurrentTask(JObject lastAssistant, IReadOnlyList<string> lines)
        {
            if (lastAssistant == null) return (null, null, false);
            var content = lastAssistant["message"]?["content"] as JArray;
            if (content == null) return (null, null, false);

            JObject lastToolUse = null;
            string lastText = null;
            foreach (var b in content.OfType<JObject>())
            {
                var bt = Str(b["type"]);
                if (bt == "tool_use") lastToolUse = b;
                else if (bt == "text") lastText = Str(b["text"]);
            }

            if (lastToolUse != null)
            {
                var name = Str(lastToolUse["name"]);
                var input = lastToolUse["input"] as JObject;
                var id = Str(lastToolUse["id"]);
                bool unfinished = id == null || !HasToolResult(lines, id);
                return (SummarizeToolUse(name, input), name, unfinished);
            }
            return (Truncate(lastText, 120), null, false);
        }

        private static bool HasToolResult(IReadOnlyList<string> lines, string toolUseId)
        {
            foreach (var line in lines)
            {
                if (line.IndexOf(toolUseId, StringComparison.Ordinal) < 0) continue;
                var o = TryParse(line);
                var content = o?["message"]?["content"] as JArray;
                if (content == null) continue;
                foreach (var b in content.OfType<JObject>())
                    if (Str(b["type"]) == "tool_result" && Str(b["tool_use_id"]) == toolUseId) return true;
            }
            return false;
        }

        private static string FirstUserText(JObject userEvent)
        {
            var content = userEvent["message"]?["content"];
            if (content == null) return null;
            if (content.Type == JTokenType.String) return content.Value<string>();
            if (content is JArray arr)
                foreach (var b in arr.OfType<JObject>())
                    if (Str(b["type"]) == "text") return Str(b["text"]);
            return null;
        }

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var trimmed = path.Replace('\\', '/').TrimEnd('/');
            var i = trimmed.LastIndexOf('/');
            return i >= 0 ? trimmed.Substring(i + 1) : trimmed;
        }

        private static string BaseName(string path) => LastSegment(path);
        private static string FirstLine(string s) => s?.Split('\n')[0]?.Trim();
        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
```

- [ ] **Step 6: 테스트 통과 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`
Expected: 3개 PASS.

- [ ] **Step 7: 커밋**

```bash
git add AgentHub/Common/Models/SessionSummary.cs AgentHub/Common/Models/ActivityEvent.cs AgentHub/Server/Agents/TranscriptParser.cs AgentHub.Tests/ AgentHub.sln
git commit -m "feat(sp1): SessionSummary/ActivityEvent DTO + TranscriptParser 요약 + xUnit 테스트"
```

---

### Task 2: 파서 상태 판정(active/idle/ended)

`ComputeStatus`의 경계 동작을 명세로 고정한다.

**Files:**
- Modify: `AgentHub/Server/Agents/TranscriptParser.cs` (이미 `ComputeStatus` 구현됨 — 테스트로 계약 고정)
- Test: `AgentHub.Tests/TranscriptParserStatusTests.cs`

**Interfaces:**
- Consumes: `TranscriptParser.ComputeStatus(TimeSpan, bool)`, `TranscriptParser.Summarize(...)`

- [ ] **Step 1: 실패하는 상태 테스트 작성**

`AgentHub.Tests/TranscriptParserStatusTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using AgentHub.Server.Agents;
using Xunit;

namespace AgentHub.Tests
{
    public class TranscriptParserStatusTests
    {
        [Theory]
        [InlineData(10, false, "active")]    // 60초 이내
        [InlineData(300, false, "idle")]     // 5분, 도구 안 돎 → 대기
        [InlineData(300, true, "active")]    // 5분이지만 미완료 도구 실행 중
        [InlineData(7200, false, "ended")]   // 2시간
        [InlineData(7200, true, "ended")]    // ended 윈도우 밖이면 도구 여부 무시
        public void ComputeStatus_boundaries(int ageSeconds, bool unfinishedTool, string expected)
        {
            Assert.Equal(expected, TranscriptParser.ComputeStatus(TimeSpan.FromSeconds(ageSeconds), unfinishedTool));
        }

        [Fact]
        public void Summarize_marks_active_when_last_block_is_unfinished_tool()
        {
            var now = DateTime.Parse("2026-07-07T10:10:00Z").ToUniversalTime();
            var lines = new List<string>
            {
                // 5분 전 tool_use, 대응 tool_result 없음 → 실행 중
                "{\"type\":\"assistant\",\"timestamp\":\"2026-07-07T10:05:00Z\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"tu_1\",\"name\":\"Bash\",\"input\":{\"command\":\"msbuild\"}}]}}"
            };
            var s = TranscriptParser.Summarize("x", lines, now);
            Assert.Equal("active", s.Status);
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj --filter TranscriptParserStatusTests`
Expected: (구현이 이미 있으면 통과할 수 있음) — 만약 통과하면 계약 확인 완료. 실패 시 `ComputeStatus`/`Summarize` 로직을 테스트에 맞춰 수정.

- [ ] **Step 3: 필요 시 로직 조정 후 통과**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`
Expected: 전체 PASS.

- [ ] **Step 4: 커밋**

```bash
git add AgentHub.Tests/TranscriptParserStatusTests.cs AgentHub/Server/Agents/TranscriptParser.cs
git commit -m "test(sp1): 세션 상태 판정 경계 케이스 고정"
```

---

### Task 3: 파서 활동 피드(ParseEvents)

세션 라인 → 정규화된 `ActivityEvent` 리스트(최근 N).

**Files:**
- Modify: `AgentHub/Server/Agents/TranscriptParser.cs`
- Test: `AgentHub.Tests/TranscriptParserEventsTests.cs`

**Interfaces:**
- Produces: `static List<ActivityEvent> TranscriptParser.ParseEvents(IReadOnlyList<string> lines, int max)`

- [ ] **Step 1: 실패하는 이벤트 테스트 작성**

`AgentHub.Tests/TranscriptParserEventsTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using AgentHub.Server.Agents;
using Xunit;

namespace AgentHub.Tests
{
    public class TranscriptParserEventsTests
    {
        [Fact]
        public void ParseEvents_normalizes_blocks_in_order()
        {
            var lines = new List<string>
            {
                "{\"type\":\"user\",\"timestamp\":\"t1\",\"message\":{\"role\":\"user\",\"content\":\"안녕\"}}",
                "{\"type\":\"assistant\",\"timestamp\":\"t2\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"음\"},{\"type\":\"text\",\"text\":\"실행합니다\"},{\"type\":\"tool_use\",\"id\":\"tu1\",\"name\":\"Bash\",\"input\":{\"command\":\"ls -la\\npwd\"}}]}}",
                "{\"type\":\"user\",\"timestamp\":\"t3\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"tu1\",\"content\":\"file1\\nfile2\"}]}}"
            };
            var ev = TranscriptParser.ParseEvents(lines, 200);
            var kinds = ev.Select(e => e.Kind).ToArray();
            Assert.Equal(new[] { "user_prompt", "thinking", "message", "tool_use", "tool_result" }, kinds);

            var toolUse = ev.First(e => e.Kind == "tool_use");
            Assert.Equal("Bash", toolUse.ToolName);
            Assert.Contains("ls -la", toolUse.Summary); // 첫 줄만
            Assert.DoesNotContain("pwd", toolUse.Summary);
        }

        [Fact]
        public void ParseEvents_respects_max_keeping_latest()
        {
            var lines = Enumerable.Range(0, 10)
                .Select(i => "{\"type\":\"user\",\"timestamp\":\"t" + i + "\",\"message\":{\"role\":\"user\",\"content\":\"m" + i + "\"}}")
                .ToList();
            var ev = TranscriptParser.ParseEvents(lines, 3);
            Assert.Equal(3, ev.Count);
            Assert.Equal("t9", ev.Last().Ts); // 최신 유지
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj --filter TranscriptParserEventsTests`
Expected: FAIL — `ParseEvents` 미정의.

- [ ] **Step 3: `ParseEvents` 구현 (TranscriptParser.cs에 추가)**

`TranscriptParser` 클래스 안에 추가:
```csharp
        public static List<ActivityEvent> ParseEvents(IReadOnlyList<string> lines, int max)
        {
            var all = new List<ActivityEvent>();
            foreach (var line in lines)
            {
                var o = TryParse(line);
                if (o == null) continue;
                var type = Str(o["type"]);
                var ts = Str(o["timestamp"]);

                if (type == "mode" || type == "permission-mode")
                {
                    var mode = Str(o["mode"]) ?? Str(o["permissionMode"]);
                    if (!string.IsNullOrWhiteSpace(mode))
                        all.Add(new ActivityEvent { Kind = "mode_change", Ts = ts, Summary = mode });
                    continue;
                }

                var content = o["message"]?["content"];
                if (content == null) continue;

                if (content.Type == JTokenType.String)
                {
                    if (type == "user")
                        all.Add(new ActivityEvent { Kind = "user_prompt", Ts = ts, Text = content.Value<string>(), Summary = Truncate(content.Value<string>(), 80) });
                    continue;
                }

                if (content is JArray arr)
                {
                    foreach (var b in arr.OfType<JObject>())
                    {
                        var bt = Str(b["type"]);
                        switch (bt)
                        {
                            case "text":
                                all.Add(new ActivityEvent { Kind = "message", Ts = ts, Text = Str(b["text"]), Summary = Truncate(Str(b["text"]), 80) });
                                break;
                            case "thinking":
                                all.Add(new ActivityEvent { Kind = "thinking", Ts = ts, Text = Str(b["thinking"]), Summary = "(사고)" });
                                break;
                            case "tool_use":
                                var name = Str(b["name"]);
                                all.Add(new ActivityEvent { Kind = "tool_use", Ts = ts, ToolName = name, Summary = SummarizeToolUse(name, b["input"] as JObject) });
                                break;
                            case "tool_result":
                                var txt = b["content"]?.ToString();
                                all.Add(new ActivityEvent { Kind = "tool_result", Ts = ts, Summary = Truncate(FirstLine(txt), 80), Text = Truncate(txt, 2000) });
                                break;
                        }
                    }
                }
            }
            if (all.Count > max) all = all.GetRange(all.Count - max, max);
            return all;
        }
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`
Expected: 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add AgentHub/Server/Agents/TranscriptParser.cs AgentHub.Tests/TranscriptParserEventsTests.cs
git commit -m "feat(sp1): TranscriptParser.ParseEvents 활동 피드 정규화"
```

---

### Task 4: `ClaudeSessionReader` (I/O·스캔·증분 tail·감시)

`~/.claude/projects`를 스캔해 요약 목록·세션 상세를 제공하고, `FileSystemWatcher`로 변경을 감지한다. 순수 파서를 호출만 한다.

**Files:**
- Create: `AgentHub/Server/Agents/ClaudeSessionReader.cs`

**Interfaces:**
- Produces:
  - `static List<SessionSummary> ClaudeSessionReader.ListSessions()`
  - `static List<ActivityEvent> ClaudeSessionReader.GetActivity(string sessionId, int max = 200)`
  - `static void ClaudeSessionReader.Start(Action onChanged)` / `static void ClaudeSessionReader.Stop()`
- Consumes: `TranscriptParser.Summarize/ParseEvents`

- [ ] **Step 1: 구현 작성**

`AgentHub/Server/Agents/ClaudeSessionReader.cs`:
```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AgentHub.Common.Models;
using AgentHub.Common.Util;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// ~/.claude/projects 의 세션 트랜스크립트(JSONL)를 읽어 요약/상세를 제공하고,
    /// FileSystemWatcher로 변경을 감지해 콜백을 알린다. 파싱 로직은 TranscriptParser에 위임.
    /// </summary>
    public static class ClaudeSessionReader
    {
        private static readonly TimeSpan Window = TimeSpan.FromHours(24);
        private const int MaxSessions = 30;

        private static FileSystemWatcher _watcher;
        private static Action _onChanged;
        private static Timer _debounce;

        // sessionId -> 파일 경로 (최근 스캔 캐시)
        private static readonly ConcurrentDictionary<string, string> _paths =
            new ConcurrentDictionary<string, string>();

        private static string ProjectsRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        public static List<SessionSummary> ListSessions()
        {
            var root = ProjectsRoot;
            var result = new List<SessionSummary>();
            if (!Directory.Exists(root)) return result;

            var now = DateTime.UtcNow;
            var cutoff = now - Window;

            var files = new List<FileInfo>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                    foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl"))
                    {
                        var fi = new FileInfo(f);
                        if (fi.LastWriteTimeUtc >= cutoff) files.Add(fi);
                    }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }

            foreach (var fi in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxSessions))
            {
                try
                {
                    var id = Path.GetFileNameWithoutExtension(fi.Name);
                    _paths[id] = fi.FullName;
                    var lines = ReadAllLinesShared(fi.FullName);
                    result.Add(TranscriptParser.Summarize(id, lines, now));
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
            return result;
        }

        public static List<ActivityEvent> GetActivity(string sessionId, int max = 200)
        {
            if (!_paths.TryGetValue(sessionId, out var path) || !File.Exists(path))
            {
                path = FindSessionFile(sessionId);
                if (path == null) return new List<ActivityEvent>();
                _paths[sessionId] = path;
            }
            try
            {
                var lines = ReadAllLinesShared(path);
                return TranscriptParser.ParseEvents(lines, max);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return new List<ActivityEvent>(); }
        }

        private static string FindSessionFile(string sessionId)
        {
            var root = ProjectsRoot;
            if (!Directory.Exists(root)) return null;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var candidate = Path.Combine(dir, sessionId + ".jsonl");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return null;
        }

        // 잠긴(쓰기 중) 파일도 읽도록 FileShare.ReadWrite.
        private static List<string> ReadAllLinesShared(string path)
        {
            var lines = new List<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null) lines.Add(line);
            }
            return lines;
        }

        public static void Start(Action onChanged)
        {
            _onChanged = onChanged;
            var root = ProjectsRoot;
            try
            {
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                _watcher = new FileSystemWatcher(root, "*.jsonl")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnFsEvent;
                _watcher.Created += OnFsEvent;
                _watcher.Renamed += OnFsEvent;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private static void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            // 300ms 디바운스 — 연속 쓰기 폭주 완화
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                try { _onChanged?.Invoke(); }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }, null, 300, Timeout.Infinite);
        }

        public static void Stop()
        {
            try
            {
                if (_watcher != null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); _watcher = null; }
                _debounce?.Dispose(); _debounce = null;
                _onChanged = null;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }
    }
}
```

- [ ] **Step 2: 빌드 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 빌드 성공(경고 무방), 오류 0.

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/Server/Agents/ClaudeSessionReader.cs
git commit -m "feat(sp1): ClaudeSessionReader — projects 스캔 + FileSystemWatcher 감시"
```

---

### Task 5: `AgentMonitorService` 재배선 + `AgentMonitorModule` watch/unwatch/activity

mock을 제거하고 실제 reader로 위임. WebSocket 메시지 스키마를 `sessions`/`activity`로 확장하고 구독을 지원한다.

**Files:**
- Modify: `AgentHub/Server/Agents/AgentMonitorService.cs` (전면 교체)
- Modify: `AgentHub/Server/Socket/AgentMonitorModule.cs`
- Delete(정리): `AgentHub/Common/Models/AgentStatus.cs` (본 변경으로 참조가 사라지는 orphan)

**Interfaces:**
- Produces:
  - `AgentMonitorService.CurrentSessionsMessage()` → `{type:"sessions", sessions:[...]}`
  - `AgentMonitorService.CurrentSessionsSnapshot()` → `{sessions:[...]}`
  - `AgentMonitorService.ActivityMessage(sessionId)` → `{type:"activity", sessionId, events:[...]}`
  - `AgentMonitorService.Start(AgentMonitorModule)` / `Stop()`
- Consumes: `ClaudeSessionReader.*`, `AgentMonitorModule.BroadcastMessageAsync`

- [ ] **Step 1: `AgentMonitorService.cs` 교체**

```csharp
using System;
using System.Collections.Generic;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Socket;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// 세션 모니터링 데이터 소스(seam). ClaudeSessionReader(트랜스크립트)를 읽어
    /// /ws/agents 로 push한다. 변경은 FileSystemWatcher 콜백으로 즉시 반영.
    /// </summary>
    public static class AgentMonitorService
    {
        private static AgentMonitorModule _module;

        public static List<SessionSummary> CurrentSessions() => ClaudeSessionReader.ListSessions();

        public static List<ActivityEvent> Activity(string sessionId, int max = 200)
            => ClaudeSessionReader.GetActivity(sessionId, max);

        public static string CurrentSessionsMessage() =>
            Json.Serialize(new { type = "sessions", sessions = CurrentSessions() });

        public static string CurrentSessionsSnapshot() =>
            Json.Serialize(new { sessions = CurrentSessions() });

        public static string ActivityMessage(string sessionId) =>
            Json.Serialize(new { type = "activity", sessionId, events = Activity(sessionId) });

        public static void Start(AgentMonitorModule module)
        {
            _module = module;
            ClaudeSessionReader.Start(OnChanged);
        }

        public static void Stop()
        {
            ClaudeSessionReader.Stop();
            _module = null;
        }

        private static void OnChanged()
        {
            try
            {
                _module?.BroadcastMessageAsync(CurrentSessionsMessage());
                _module?.PushActivityToWatchers();
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }
    }
}
```

- [ ] **Step 2: `AgentMonitorModule.cs` 수정 — 구독 상태 + watch/unwatch + activity push**

`_tokens` 필드 아래에 구독 맵을 추가:
```csharp
        // contextId -> 구독 중인 sessionId
        private readonly ConcurrentDictionary<string, string> _watching =
            new ConcurrentDictionary<string, string>();
```

`OnMessageReceivedAsync`를 아래로 교체:
```csharp
        protected override async Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            try
            {
                if (!_tokens.TryGetValue(context.Id, out var h)
                    || DeviceRegistry.StatusByHash(h) != DeviceStatus.Approved) return;

                var text = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = Json.Deserialize<WatchMessage>(text);
                if (msg == null) return;

                if (msg.Type == "watch" && !string.IsNullOrEmpty(msg.SessionId))
                {
                    _watching[context.Id] = msg.SessionId;
                    await SendAsync(context, AgentMonitorService.ActivityMessage(msg.SessionId));
                }
                else if (msg.Type == "unwatch")
                {
                    _watching.TryRemove(context.Id, out _);
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        /// <summary>변경 발생 시 각 구독 소켓에 해당 세션 활동을 push.</summary>
        public async void PushActivityToWatchers()
        {
            foreach (var ctx in ActiveContexts)
            {
                if (!_watching.TryGetValue(ctx.Id, out var sid) || string.IsNullOrEmpty(sid)) continue;
                if (!_tokens.TryGetValue(ctx.Id, out var h) || DeviceRegistry.StatusByHash(h) != DeviceStatus.Approved) continue;
                try { await SendAsync(ctx, AgentMonitorService.ActivityMessage(sid)); }
                catch { /* per-socket 실패 무시 */ }
            }
        }
```

`OnClientDisconnectedAsync`에 구독 정리 추가:
```csharp
            _watching.TryRemove(context.Id, out _);
```

`ActivateAsync`의 마지막 줄(스냅샷 전송)을 세션 메시지로 교체:
```csharp
            await SendAsync(context, AgentMonitorService.CurrentSessionsMessage());
```

파일 상단에 `using AgentHub.Common.Util;`가 없으면 추가(Json 사용). 그리고 파일 하단(네임스페이스 안, 클래스 밖 또는 별도 파일)에 DTO 추가:
```csharp
    internal class WatchMessage
    {
        public string Type { get; set; }
        public string SessionId { get; set; }
    }
```

- [ ] **Step 3: mock 시절 메서드 참조 정리**

`AgentMonitorModule`/`ApiController`에서 옛 이름(`CurrentAgentsMessage`, `CurrentAgentsSnapshot`)을 쓰던 곳을 새 이름으로 교체(컴파일러가 미해결 참조로 알려줌). `ApiController`는 Task 6에서 처리.

- [ ] **Step 4: `AgentStatus.cs` 삭제(orphan)**

```bash
git rm AgentHub/Common/Models/AgentStatus.cs
```
`AgentHub.csproj`의 `<Compile Include="Common\Models\AgentStatus.cs" />` 항목도 제거(레거시 csproj는 파일을 명시 참조). Grep으로 잔여 참조 확인:
Run: `grep -rn "AgentStatus\|CurrentAgentsMessage\|CurrentAgentsSnapshot" AgentHub --include=*.cs`
Expected: (없음)

- [ ] **Step 5: 빌드 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 성공, 오류 0.

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "feat(sp1): AgentMonitorService reader 위임 + WS watch/activity 구독"
```

---

### Task 6: REST 폴백 엔드포인트 `/sessions`

승인 기기용 스냅샷 폴백(기존 `/agents` 패턴 대체).

**Files:**
- Modify: `AgentHub/Server/Controller/ApiController.cs`

**Interfaces:**
- Consumes: `AgentMonitorService.CurrentSessionsSnapshot()`, `AgentMonitorService.ActivityMessage/Activity`, `DeviceRegistry.StatusOf`, `DeviceToken()`

- [ ] **Step 1: 기존 `/agents` 라우트를 `/sessions`로 교체 + 상세 추가**

`ApiController.cs`의 `Agents()` 메서드를 아래로 교체:
```csharp
        // 실시간은 WebSocket(/ws/agents). 이 엔드포인트는 승인된 기기용 스냅샷 폴백.
        [Route(HttpVerbs.Get, "/sessions")]
        public Task Sessions()
        {
            var status = DeviceRegistry.StatusOf(DeviceToken());
            if (status != DeviceStatus.Approved)
            {
                HttpContext.Response.StatusCode = 401;
                return SendJsonAsync(Json.Serialize(new { ok = false, status }));
            }
            return SendJsonAsync(AgentMonitorService.CurrentSessionsSnapshot());
        }

        [Route(HttpVerbs.Get, "/sessions/{id}")]
        public Task SessionActivity(string id)
        {
            var status = DeviceRegistry.StatusOf(DeviceToken());
            if (status != DeviceStatus.Approved)
            {
                HttpContext.Response.StatusCode = 401;
                return SendJsonAsync(Json.Serialize(new { ok = false, status }));
            }
            return SendJsonAsync(Json.Serialize(new { sessionId = id, events = AgentMonitorService.Activity(id) }));
        }
```

- [ ] **Step 2: 빌드 확인**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 성공, 오류 0.

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/Server/Controller/ApiController.cs
git commit -m "feat(sp1): /api/sessions, /api/sessions/{id} REST 폴백"
```

---

### Task 7: 프론트엔드 — 세션 리스트 + 상세 피드 + i18n + CSS

모바일 모니터를 세션 리스트/상세로 재구성.

**Files:**
- Modify: `AgentHub/View/Htmls/index.html`
- Modify: `AgentHub/View/Htmls/js/app.js`
- Modify: `AgentHub/View/Htmls/js/i18n.js`
- Modify: `AgentHub/View/Htmls/css/app.css`

**Interfaces:**
- Consumes(WS 메시지): `{type:"sessions", sessions:[SessionSummary]}`, `{type:"activity", sessionId, events:[ActivityEvent]}`, `{type:"auth", status}`
- Produces(WS 전송): `{type:"watch", sessionId}`, `{type:"unwatch"}`

- [ ] **Step 1: `index.html` monitor 섹션 교체**

기존 `<section id="monitor" ...>` 블록을 아래로 교체(리스트 + 상세 두 화면):
```html
    <!-- 모니터: 세션 리스트 -->
    <section id="monitor" class="screen" hidden>
      <div class="summary" id="summary"></div>
      <div class="session-list" id="sessionList">
        <div class="loading"><span class="spinner"></span><span data-i18n="monitor.loading">세션 정보를 불러오는 중…</span></div>
      </div>
    </section>

    <!-- 모니터: 세션 상세(활동 피드) -->
    <section id="detail" class="screen" hidden>
      <div class="detail-head">
        <button id="backBtn" class="back-btn" data-i18n="detail.back">← 목록</button>
        <div class="detail-title" id="detailTitle"></div>
      </div>
      <div class="activity-feed" id="activityFeed"></div>
    </section>
```

- [ ] **Step 2: `js/app.js` 렌더링/라우팅 교체**

기존 파일에서 `m.type === 'agents'` 처리와 `render(...)`를 세션용으로 교체한다. 핵심 변경:

(a) 메시지 스위치 (기존 `onmessage` 내부):
```javascript
      if (m.type === 'auth') { handleAuth(m.status); }
      else if (m.type === 'sessions') { showScreen('monitor'); renderSessions(m.sessions); }
      else if (m.type === 'activity') { renderActivity(m.sessionId, m.events); }
```

(b) 렌더링 함수 추가(파일 하단):
```javascript
    let currentSessionId = null;

    function renderSessions(sessions) {
      const list = document.getElementById('sessionList');
      const sum = document.getElementById('summary');
      if (!sessions || sessions.length === 0) {
        list.innerHTML = '<div class="empty" data-i18n="monitor.empty">최근 활동한 세션이 없습니다.</div>';
        sum.textContent = '';
        if (window.I18N) I18N.apply();
        return;
      }
      const active = sessions.filter(s => s.status === 'active').length;
      sum.textContent = (window.I18N ? I18N.t('summary.count') : '세션') + ': ' + sessions.length + ' · active ' + active;
      list.innerHTML = sessions.map(cardHtml).join('');
      list.querySelectorAll('.session-card').forEach(el =>
        el.addEventListener('click', () => openDetail(el.getAttribute('data-id'))));
    }

    function cardHtml(s) {
      const badge = '<span class="badge-status ' + s.status + '">' + s.status + '</span>';
      return '<div class="session-card" data-id="' + esc(s.id) + '">'
        + '<div class="card-top">' + badge + '<span class="card-title">' + esc(s.title) + '</span></div>'
        + '<div class="card-meta">' + esc(s.project || '') + (s.gitBranch ? ' · ' + esc(s.gitBranch) : '') + '</div>'
        + '<div class="card-task">' + esc(s.currentTask || '') + '</div>'
        + '<div class="card-time">' + rel(s.lastActivityAt) + '</div>'
        + '</div>';
    }

    function openDetail(id) {
      currentSessionId = id;
      document.getElementById('activityFeed').innerHTML =
        '<div class="loading"><span class="spinner"></span></div>';
      showScreen('detail');
      send({ type: 'watch', sessionId: id });
    }

    function renderActivity(sessionId, events) {
      if (sessionId !== currentSessionId) return;
      const feed = document.getElementById('activityFeed');
      if (!events || events.length === 0) { feed.innerHTML = '<div class="empty">—</div>'; return; }
      feed.innerHTML = events.map(evHtml).join('');
      feed.scrollTop = feed.scrollHeight;
    }

    function evHtml(e) {
      const icon = ({message:'💬', thinking:'💭', tool_use:'🔧', tool_result:'↩︎', user_prompt:'🧑', mode_change:'⚙︎'})[e.kind] || '•';
      const body = e.text && e.kind !== 'thinking'
        ? '<div class="ev-text">' + esc(e.text) + '</div>' : '';
      return '<div class="ev ev-' + e.kind + '">'
        + '<div class="ev-head"><span class="ev-icon">' + icon + '</span>'
        + '<span class="ev-summary">' + esc(e.summary || e.toolName || e.kind) + '</span></div>'
        + body + '</div>';
    }

    function send(obj) { try { ws && ws.readyState === 1 && ws.send(JSON.stringify(obj)); } catch (_) {} }
    function esc(s) { return (s == null ? '' : String(s)).replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }
    function rel(iso) {
      if (!iso) return '';
      const d = new Date(iso); const s = Math.floor((Date.now() - d.getTime())/1000);
      if (s < 60) return s + 's'; if (s < 3600) return Math.floor(s/60) + 'm';
      if (s < 86400) return Math.floor(s/3600) + 'h'; return Math.floor(s/86400) + 'd';
    }
```
> `ws` 변수와 `send`는 기존 소켓 인스턴스를 참조하도록 연결한다(기존 코드의 소켓 변수명에 맞춰 `send`가 그 소켓을 쓰게 함). 기존 `render`/`agents` 전용 코드는 제거(내 변경으로 orphan).

(c) 뒤로가기 + 화면 전환: `showScreen`이 `detail`도 처리하도록 하고, backBtn 바인딩 추가(초기화 부분):
```javascript
    document.getElementById('backBtn').addEventListener('click', () => {
      send({ type: 'unwatch' }); currentSessionId = null; showScreen('monitor');
    });
```
`showScreen`이 `['authRequest','authPending','monitor','detail']`를 모두 토글하도록 목록에 `detail` 추가.

- [ ] **Step 3: `js/i18n.js` 키 추가**

ko/en 사전에 각각 추가(기존 구조에 맞춰):
```javascript
// ko
'monitor.loading': '세션 정보를 불러오는 중…',
'monitor.empty': '최근 활동한 세션이 없습니다.',
'detail.back': '← 목록',
'summary.count': '세션',
// en
'monitor.loading': 'Loading sessions…',
'monitor.empty': 'No recently active sessions.',
'detail.back': '← List',
'summary.count': 'Sessions',
```

- [ ] **Step 4: `css/app.css` 스타일 추가**

파일 끝에 추가:
```css
.session-list { display: flex; flex-direction: column; gap: 10px; }
.session-card { background: #1b2030; border: 1px solid #2a3145; border-radius: 12px; padding: 12px 14px; cursor: pointer; }
.session-card:active { background: #222941; }
.card-top { display: flex; align-items: center; gap: 8px; }
.card-title { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.card-meta { color: #8b93a7; font-size: 12px; margin-top: 4px; }
.card-task { color: #c9d1e4; font-size: 13px; margin-top: 6px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.card-time { color: #6b7280; font-size: 11px; margin-top: 6px; text-align: right; }
.badge-status { font-size: 11px; padding: 2px 8px; border-radius: 999px; text-transform: uppercase; }
.badge-status.active { background: #14532d; color: #4ade80; }
.badge-status.idle { background: #3f3f16; color: #fde047; }
.badge-status.ended { background: #3a2030; color: #9ca3af; }
.detail-head { display: flex; align-items: center; gap: 10px; margin-bottom: 10px; }
.back-btn { background: none; border: none; color: #7aa2ff; font-size: 15px; cursor: pointer; }
.detail-title { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.activity-feed { display: flex; flex-direction: column; gap: 8px; overflow-y: auto; }
.ev { background: #171b28; border-left: 3px solid #2a3145; border-radius: 8px; padding: 8px 10px; }
.ev-tool_use { border-left-color: #7aa2ff; }
.ev-tool_result { border-left-color: #4ade80; }
.ev-user_prompt { border-left-color: #fbbf24; }
.ev-head { display: flex; gap: 8px; align-items: center; font-size: 13px; }
.ev-text { color: #aab3c5; font-size: 12px; margin-top: 4px; white-space: pre-wrap; word-break: break-word; max-height: 8em; overflow: hidden; }
.empty { color: #6b7280; text-align: center; padding: 24px; }
```

- [ ] **Step 5: 빌드 + 실행 확인(수동)**

Run: `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
그리고 `install/Debug/AgentHub.exe` 실행 → 브라우저에서 `https://127.0.0.1:<포트>/` 접속(승인된 상태) → 세션 리스트가 실제 세션으로 보이고, 카드 탭 시 활동 피드가 뜨는지 확인.
Expected: 목록/상세 정상 렌더, 실시간 갱신.

- [ ] **Step 6: 커밋**

```bash
git add AgentHub/View/Htmls/
git commit -m "feat(sp1): 모바일 세션 리스트 + 활동 피드 UI + i18n/css"
```

---

### Task 8: 엔드투엔드 검증 + 빌드 게이트 + 마무리

**Files:** (검증 전용, 코드 변경 없음 원칙)

- [ ] **Step 1: 전체 테스트**

Run: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`
Expected: 전체 PASS.

- [ ] **Step 2: 빌드 게이트**

Run: `msbuild AgentHub.sln /t:Restore` → `msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
Expected: 성공, 산출물 `install/Debug/AgentHub.exe` 생성.

- [ ] **Step 3: 실제 플로우 구동 (verify 스킬)**

앱 실행 상태에서 별도 Claude Code 세션을 하나 돌리며(예: 아무 프로젝트에서 명령 실행), 모바일/브라우저에서:
- 새 세션이 목록에 active로 등장하는지
- 도구 실행 시 currentTask/피드가 갱신되는지
- 세션이 유휴가 되면 idle 배지로 바뀌는지
확인. 이상 있으면 systematic-debugging으로 원인 규명.

- [ ] **Step 4: 스펙 대비 최종 점검**

`docs/superpowers/specs/2026-07-07-sp1-agent-activity-feed-design.md`의 각 요구사항이 구현됐는지 체크. 누락 시 태스크 추가.

- [ ] **Step 5: 브랜치 마무리**

`superpowers:finishing-a-development-branch`로 병합/PR 방식 결정.

---

## Self-Review (계획 검증)

- **스펙 커버리지:** 데이터 소스(트랜스크립트)=Task1·4, 요약/상태/피드=Task1·2·3, 감시/증분=Task4, 전송(sessions/watch/activity)=Task5, REST 폴백=Task6, 프론트엔드=Task7, 크로스프로젝트·기본값=Task1·4 상수, 검증=Task8. 커버됨.
  - 비고: 스펙의 "증분 tail(offset 캐시)"은 계획에서 **전체 라인 재읽기 + 파일별 mtime 기준 스캔**으로 단순화했다(정확성 우선, 대용량은 24h·30개 상한과 디바운스로 완화). 성능 문제가 실측되면 후속 태스크로 offset tail 도입. — 스펙 3.1의 최적화는 "권장"이며 정확성 요건은 충족.
- **플레이스홀더 스캔:** TODO/TBD 없음.
- **타입 일관성:** `SessionSummary`/`ActivityEvent` 속성명, `CurrentSessionsMessage`/`ActivityMessage`/`ListSessions`/`GetActivity`/`Start`/`Stop` 시그니처가 Task 간 일치.

> 계획상 스펙의 offset 증분 tail을 단순화한 점은 사용자 검토 포인트로 남긴다.
