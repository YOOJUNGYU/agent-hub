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
            // DateParseHandling.None: timestamp 등 ISO 8601 문자열이 JTokenType.Date로 자동 변환되지 않도록 방지.
            try
            {
                using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(line)))
                {
                    reader.DateParseHandling = Newtonsoft.Json.DateParseHandling.None;
                    return JObject.Load(reader);
                }
            }
            catch { return null; }
        }

        private static string Str(JToken t) => t?.Type == JTokenType.String ? t.Value<string>() : null;

        public static SessionSummary Summarize(string sessionId, IReadOnlyList<string> lines, DateTime nowUtc)
        {
            var s = new SessionSummary { Id = sessionId, Status = "ended" };
            string lastTs = null;
            int msgCount = 0;
            JObject lastAssistant = null;

            // 제목 후보들: 루프 단일 패스로 수집, 우선순위는 루프 후 해결
            string aiTitleCandidate = null;
            string slugCandidate = null;
            string firstUserTextCandidate = null;

            foreach (var line in lines)
            {
                var o = TryParse(line);
                if (o == null) continue;

                var title = Str(o["aiTitle"]);
                if (!string.IsNullOrWhiteSpace(title)) aiTitleCandidate = title;

                var slug = Str(o["slug"]);
                if (!string.IsNullOrWhiteSpace(slug)) slugCandidate = slug;

                var cwd = Str(o["cwd"]);
                if (!string.IsNullOrWhiteSpace(cwd)) { s.Cwd = cwd; s.Project = LastSegment(cwd); }
                var branch = Str(o["gitBranch"]);
                if (!string.IsNullOrWhiteSpace(branch)) s.GitBranch = branch;

                var ts = Str(o["timestamp"]);
                if (!string.IsNullOrWhiteSpace(ts)) lastTs = ts;

                var type = Str(o["type"]);
                if (type == "assistant" || type == "user") msgCount++;
                if (type == "assistant") lastAssistant = o;

                // 첫 사용자 텍스트 (한 번만)
                if (firstUserTextCandidate == null && type == "user")
                {
                    var utext = FirstUserText(o);
                    if (!string.IsNullOrWhiteSpace(utext)) firstUserTextCandidate = Truncate(utext, 60);
                }
            }

            // 제목 우선순위 해결: aiTitle > slug > firstUserText
            s.Title = aiTitleCandidate ?? slugCandidate ?? firstUserTextCandidate;

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
