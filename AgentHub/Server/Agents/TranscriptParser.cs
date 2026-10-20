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

        private static long TokenVal(JObject u, string key)
        {
            var t = u[key];
            return t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<long>() : 0;
        }

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

        public static PendingAsk ExtractPendingAsk(IReadOnlyList<string> lines) => Fold(lines).PendingAsk();

        public static SessionSummary Summarize(string sessionId, IReadOnlyList<string> lines, DateTime nowUtc)
            => Fold(lines).Build(sessionId, nowUtc);

        private static SummaryFold Fold(IReadOnlyList<string> lines)
        {
            var f = new SummaryFold();
            foreach (var line in lines) f.Feed(line);
            return f;
        }

        /// <summary>
        /// 트랜스크립트를 한 줄씩 접어 요약을 만든다. 파일이 자라면 새 줄만 <see cref="Feed"/>하면 되므로
        /// 수십 MB 트랜스크립트를 변경마다 통째로 다시 읽지 않는다(ClaudeSessionReader의 증분 캐시).
        /// </summary>
        public sealed class SummaryFold
        {
            private string _cwd, _project, _branch;
            private string _lastTs, _firstTs;
            private string _lastUserPromptTs; // 마지막 사용자 프롬프트 ts — 현재 턴 시작(도구결과 user 메시지는 제외)
            private string _lastMsgType;      // 마지막 메시지(assistant/user) 종류 — 작업중 판정용
            private long _totalTokens;        // 세션 누적 토큰(input+cache_creation+output)
            private int _msgCount;
            private JObject _lastAssistant;
            private string _lastToolId;       // 마지막 assistant의 마지막 tool_use id — 결과가 오면 _lastToolDone
            private bool _lastToolDone;
            private JObject _ask;             // 아직 답하지 않은 AskUserQuestion
            private string _askId;
            // 제목 후보들: 우선순위는 Build에서 해결
            private string _aiTitle, _slug, _firstUserText;

            public void Feed(string line)
            {
                var o = TryParse(line);
                if (o == null) return;

                var title = Str(o["aiTitle"]);
                if (!string.IsNullOrWhiteSpace(title)) _aiTitle = title;

                var slug = Str(o["slug"]);
                if (!string.IsNullOrWhiteSpace(slug)) _slug = slug;

                var cwd = Str(o["cwd"]);
                if (!string.IsNullOrWhiteSpace(cwd)) { _cwd = cwd; _project = LastSegment(cwd); }
                var branch = Str(o["gitBranch"]);
                if (!string.IsNullOrWhiteSpace(branch)) _branch = branch;

                var ts = Str(o["timestamp"]);
                if (!string.IsNullOrWhiteSpace(ts)) { _lastTs = ts; if (_firstTs == null) _firstTs = ts; }

                var type = Str(o["type"]);
                if (type == "assistant" || type == "user") { _msgCount++; _lastMsgType = type; }
                if (type == "user")
                {
                    var utext = FirstUserText(o);
                    if (utext != null && !string.IsNullOrWhiteSpace(ts)) _lastUserPromptTs = ts;
                    if (_firstUserText == null && !string.IsNullOrWhiteSpace(utext)) _firstUserText = Truncate(utext, 60);
                }

                var content = o["message"]?["content"] as JArray;
                if (type == "assistant")
                {
                    _lastAssistant = o;
                    var u = o["message"]?["usage"] as JObject;
                    if (u != null)
                        _totalTokens += TokenVal(u, "input_tokens") + TokenVal(u, "cache_creation_input_tokens") + TokenVal(u, "output_tokens");
                    _lastToolId = null;
                    _lastToolDone = false;
                }
                if (content == null) return;
                foreach (var b in content.OfType<JObject>())
                {
                    var bt = Str(b["type"]);
                    if (bt == "tool_use")
                    {
                        if (type == "assistant") _lastToolId = Str(b["id"]);
                        if (Str(b["name"]) == "AskUserQuestion") { _ask = b; _askId = Str(b["id"]); }
                    }
                    else if (bt == "tool_result")
                    {
                        var id = Str(b["tool_use_id"]);
                        if (_askId != null && id == _askId) { _ask = null; _askId = null; } // 답변됨
                        if (_lastToolId != null && id == _lastToolId) _lastToolDone = true;
                    }
                }
            }

            public SessionSummary Build(string sessionId, DateTime nowUtc)
            {
                var s = new SessionSummary
                {
                    Id = sessionId,
                    Cwd = _cwd,
                    Project = string.IsNullOrEmpty(_project) ? "(unknown)" : _project,
                    GitBranch = _branch,
                    // 제목 우선순위: aiTitle > slug > firstUserText
                    Title = _aiTitle ?? _slug ?? _firstUserText ?? sessionId,
                    MessageCount = _msgCount,
                    LastActivityAt = _lastTs,
                    FirstActivityAt = _firstTs,
                    TurnStartAt = _lastUserPromptTs ?? _firstTs,
                    TotalTokens = _totalTokens,
                };

                // 현재 작업 + 도구명
                var unfinished = false;
                var content = _lastAssistant?["message"]?["content"] as JArray;
                if (content != null)
                {
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
                        s.ToolName = Str(lastToolUse["name"]);
                        s.CurrentTask = SummarizeToolUse(s.ToolName, lastToolUse["input"] as JObject);
                        unfinished = _lastToolId == null || !_lastToolDone;
                    }
                    else s.CurrentTask = Truncate(lastText, 120);
                }

                // 상태
                var age = EndedWindow + TimeSpan.FromSeconds(1); // 타임스탬프 없으면 ended
                if (DateTime.TryParse(_lastTs, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var last))
                    age = nowUtc - last;
                s.Status = ComputeStatus(age, unfinished);
                // 작업 중(모바일 애니메이션): 도구 미완료(실행 중) 또는 방금 사용자 프롬프트(응답 생성 임박). 종료 세션은 제외.
                s.Working = age <= EndedWindow && (unfinished || (_lastMsgType == "user" && age <= ActiveWindow));
                return s;
            }

            public PendingAsk PendingAsk()
            {
                if (_ask == null) return null;
                var questions = _ask["input"]?["questions"] as JArray;
                var q = questions?.OfType<JObject>().FirstOrDefault();
                if (q == null) return null;
                var opts = new List<string>();
                foreach (var op in (q["options"] as JArray ?? new JArray()).OfType<JObject>())
                { var l = Str(op["label"]); if (l != null) opts.Add(l); }
                return new PendingAsk { Question = Str(q["question"]), Header = Str(q["header"]),
                    MultiSelect = q["multiSelect"]?.Type == JTokenType.Boolean && q["multiSelect"].Value<bool>(),
                    Options = opts,
                    QuestionCount = questions?.Count ?? 1 };
            }
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
                    {
                        var u = content.Value<string>();
                        var done = TaskNotification(u);
                        all.Add(done ?? new ActivityEvent { Kind = "user_prompt", Ts = ts, Text = u, Summary = Truncate(u, 80) });
                        if (done != null) done.Ts = ts;
                    }
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
                                var bodyText = Str(b["text"]);
                                var note = type == "user" ? TaskNotification(bodyText) : null;
                                if (note != null) { note.Ts = ts; all.Add(note); break; }
                                all.Add(new ActivityEvent { Kind = "message", Ts = ts, Text = bodyText, Summary = Truncate(bodyText, 80) });
                                break;
                            case "thinking":
                                all.Add(new ActivityEvent { Kind = "thinking", Ts = ts, Text = Str(b["thinking"]), Summary = "(사고)" });
                                break;
                            case "tool_use":
                                var name = Str(b["name"]);
                                var inp = b["input"] as JObject;
                                all.Add(new ActivityEvent
                                {
                                    Kind = "tool_use", Ts = ts, ToolName = name, ToolId = Str(b["id"]),
                                    Summary = SummarizeToolUse(name, inp), Target = ToolTarget(name, inp),
                                    Background = inp?["run_in_background"]?.Type == JTokenType.Boolean && inp.Value<bool>("run_in_background") ? true : (bool?)null
                                });
                                break;
                            case "tool_result":
                                var txt = b["content"]?.ToString();
                                all.Add(new ActivityEvent
                                {
                                    Kind = "tool_result", Ts = ts, Summary = Truncate(FirstLine(txt), 80), Text = Truncate(txt, 2000),
                                    ToolId = Str(b["tool_use_id"]), TaskId = BackgroundTaskId(txt)
                                });
                                break;
                        }
                    }
                }
            }
            if (all.Count > max) all = all.GetRange(all.Count - max, max);
            return all;
        }

        private static readonly System.Text.RegularExpressions.Regex TaskIdRx =
            new System.Text.RegularExpressions.Regex(@"(?:with ID|agentId):\s*([A-Za-z0-9_-]+)");
        private static readonly System.Text.RegularExpressions.Regex NotifyRx =
            new System.Text.RegularExpressions.Regex(@"<task-id>([^<]+)</task-id>[\s\S]*?<status>([^<]+)</status>");

        /// <summary>백그라운드 도구 결과("…running in background with ID: b123" / "agentId: a1b2")에서 작업 id.</summary>
        public static string BackgroundTaskId(string resultText)
        {
            if (string.IsNullOrEmpty(resultText)) return null;
            var m = TaskIdRx.Match(resultText);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>하네스가 남기는 백그라운드 작업 완료 알림(&lt;task-notification&gt;) → task_done 이벤트. 아니면 null.</summary>
        public static ActivityEvent TaskNotification(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("<task-notification>", StringComparison.Ordinal) < 0) return null;
            var m = NotifyRx.Match(text);
            return m.Success ? new ActivityEvent { Kind = "task_done", TaskId = m.Groups[1].Value.Trim(), Summary = m.Groups[2].Value.Trim() } : null;
        }

        /// <summary>조회 도구가 실제로 보는 대상 전체(요약과 달리 자르지 않은 경로/URL/검색어). 그 외 도구는 null.</summary>
        public static string ToolTarget(string name, JObject input)
        {
            if (input == null) return null;
            switch (name)
            {
                case "Read": return Str(input["file_path"]);
                case "WebFetch": return Str(input["url"]);
                case "WebSearch": return Str(input["query"]);
                case "Glob": return Str(input["pattern"]);
                case "Grep":
                    var path = Str(input["path"]);
                    return string.IsNullOrEmpty(path) ? Str(input["pattern"]) : Str(input["pattern"]) + "  in " + path;
                default: return null;
            }
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
