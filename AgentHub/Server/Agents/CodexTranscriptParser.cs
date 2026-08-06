using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using AgentHub.Common.Models;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// Codex 트랜스크립트(rollout JSONL) 라인을 SessionSummary / ActivityEvent로 변환하는 순수 파서.
    /// Claude용 <see cref="TranscriptParser"/>의 대응물 — 포맷만 다르고 결과 모델은 동일하다.
    /// 파일 I/O·로깅·UI 의존 없음(테스트 소스 링크 대상).
    ///
    /// Codex 라인 구조: { timestamp, type, payload }
    ///  - type=session_meta        → payload.{id, cwd}
    ///  - type=event_msg           → payload.type in { task_started, user_message, task_complete, turn_aborted, token_count, ... }
    ///  - type=response_item       → payload.type in { message(role user/assistant/developer), reasoning, function_call, function_call_output }
    /// 상태 판정(active/idle/ended) 시간창은 Claude와 동일하도록 <see cref="TranscriptParser.ComputeStatus"/>를 재사용.
    /// </summary>
    public static class CodexTranscriptParser
    {
        private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan EndedWindow = TimeSpan.FromMinutes(30);

        private static JObject TryParse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
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

        private static long LongVal(JToken t)
            => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<long>() : 0;

        /// <summary>response_item message(role=assistant)의 output_text 블록을 합쳐 반환. 없으면 null.</summary>
        public static string LastAssistantText(IReadOnlyList<string> lines, int max = 300)
        {
            JObject lastMsg = null;
            foreach (var line in lines)
            {
                var o = TryParse(line);
                var p = o?["payload"] as JObject;
                if (p == null) continue;
                if (Str(o["type"]) == "response_item" && Str(p["type"]) == "message" && Str(p["role"]) == "assistant")
                    lastMsg = p;
            }
            if (lastMsg == null) return null;
            var text = TextOf(lastMsg["content"], "output_text");
            return string.IsNullOrWhiteSpace(text) ? null : Truncate(text, max);
        }

        /// <summary>
        /// 아직 답이 오지 않은 마지막 질문(Codex의 <c>request_user_input</c> 도구 호출)을 뽑는다.
        /// Claude의 AskUserQuestion에 대응하며, 응답 창이 지난 뒤 세션 상세에서 picker 주입으로 답할 때 쓴다.
        /// </summary>
        public static PendingAsk ExtractPendingAsk(IReadOnlyList<string> lines)
        {
            JObject lastAskArgs = null;
            string askCallId = null;
            foreach (var line in lines)
            {
                var o = TryParse(line);
                var p = o?["payload"] as JObject;
                if (p == null || Str(o["type"]) != "response_item") continue;

                var ptype = Str(p["type"]);
                if (ptype == "function_call" && Str(p["name"]) == "request_user_input")
                {
                    lastAskArgs = ParseArgs(Str(p["arguments"]));
                    askCallId = Str(p["call_id"]);
                }
                else if (ptype == "function_call_output" && askCallId != null && Str(p["call_id"]) == askCallId)
                {
                    lastAskArgs = null;
                    askCallId = null;
                }
            }
            if (lastAskArgs == null) return null;

            var questions = lastAskArgs["questions"] as JArray;
            var q = questions?.OfType<JObject>().FirstOrDefault();
            if (q == null) return null;

            var opts = new List<string>();
            foreach (var op in (q["options"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var label = Str(op["label"]);
                if (label != null) opts.Add(label);
            }

            return new PendingAsk
            {
                Header = Str(q["header"]),
                Question = Str(q["question"]),
                MultiSelect = q["multiSelect"]?.Type == JTokenType.Boolean && q["multiSelect"].Value<bool>(),
                Options = opts,
                QuestionCount = questions?.Count ?? 1
            };
        }

        public static SessionSummary Summarize(string sessionId, IReadOnlyList<string> lines, DateTime nowUtc)
        {
            var s = new SessionSummary { Id = sessionId, Engine = "codex", Status = "ended" };
            string lastTs = null, firstTs = null;
            string lastUserPromptTs = null;   // 마지막 실제 사용자 프롬프트(event_msg user_message) ts — 현재 턴 시작
            string firstUserTextCandidate = null;
            long totalTokens = 0;
            int msgCount = 0;
            bool lastWasUser = false;         // 마지막 메시지가 사용자 프롬프트였는지(응답 임박 판정용)

            JObject lastFunctionCall = null;  // 마지막 function_call payload
            string lastFunctionCallId = null; // 그 call_id
            var completedCallIds = new HashSet<string>();
            string lastTaskStartTs = null, lastTaskEndTs = null; // task_started vs task_complete/turn_aborted

            foreach (var line in lines)
            {
                var o = TryParse(line);
                if (o == null) continue;
                var p = o["payload"] as JObject;
                if (p == null) continue;

                var ts = Str(o["timestamp"]);
                if (!string.IsNullOrWhiteSpace(ts)) { lastTs = ts; if (firstTs == null) firstTs = ts; }

                var top = Str(o["type"]);
                var ptype = Str(p["type"]);

                if (top == "session_meta")
                {
                    var cwd = Str(p["cwd"]);
                    if (!string.IsNullOrWhiteSpace(cwd)) { s.Cwd = cwd; s.Project = LastSegment(cwd); }
                    continue;
                }

                if (top == "event_msg")
                {
                    switch (ptype)
                    {
                        case "user_message":
                            var um = Str(p["message"]);
                            if (!string.IsNullOrWhiteSpace(um))
                            {
                                msgCount++; lastWasUser = true;
                                if (!string.IsNullOrWhiteSpace(ts)) lastUserPromptTs = ts;
                                if (firstUserTextCandidate == null) firstUserTextCandidate = Truncate(um.Trim(), 60);
                            }
                            break;
                        case "token_count":
                            // 실제 구조: payload.info.total_token_usage.total_tokens (구버전 대비 payload 직속도 폴백).
                            // info는 JSON null로 오는 줄이 있다(사용량 한도 등). JValue.Null은 C# null이 아니라
                            // ?. 가 통과해 자식 접근에서 터지므로 JObject로 캐스팅해 걸러낸다.
                            var usage = (p["info"] as JObject)?["total_token_usage"] as JObject
                                        ?? p["total_token_usage"] as JObject;
                            var total = usage?["total_tokens"];
                            if (total != null) totalTokens = LongVal(total);
                            break;
                        case "task_started":
                            if (!string.IsNullOrWhiteSpace(ts)) lastTaskStartTs = ts;
                            break;
                        case "task_complete":
                        case "turn_aborted":
                            if (!string.IsNullOrWhiteSpace(ts)) lastTaskEndTs = ts;
                            break;
                    }
                    continue;
                }

                if (top == "response_item")
                {
                    switch (ptype)
                    {
                        case "message":
                            var role = Str(p["role"]);
                            if (role == "assistant") { msgCount++; lastWasUser = false; }
                            break;
                        case "function_call":
                            lastFunctionCall = p;
                            lastFunctionCallId = Str(p["call_id"]);
                            lastWasUser = false;
                            break;
                        case "function_call_output":
                            var cid = Str(p["call_id"]);
                            if (cid != null) completedCallIds.Add(cid);
                            break;
                    }
                }
            }

            s.Title = firstUserTextCandidate; // 리더가 session_index의 thread_name으로 덮어쓴다(있으면 우선)
            s.MessageCount = msgCount;
            s.LastActivityAt = lastTs;
            s.FirstActivityAt = firstTs;
            s.TurnStartAt = lastUserPromptTs ?? firstTs;
            s.TotalTokens = totalTokens;

            // 현재 작업 + 도구명(마지막 function_call). 결과가 아직 없으면 실행 중(미완료).
            bool unfinishedTool = false;
            if (lastFunctionCall != null)
            {
                var name = Str(lastFunctionCall["name"]);
                s.ToolName = name;
                s.CurrentTask = SummarizeToolUse(name, Str(lastFunctionCall["arguments"]));
                unfinishedTool = lastFunctionCallId == null || !completedCallIds.Contains(lastFunctionCallId);
            }

            // 진행 중 턴: task_started가 마지막 task_complete/turn_aborted보다 새로움.
            bool activeTask = lastTaskStartTs != null
                && (lastTaskEndTs == null || string.CompareOrdinal(lastTaskStartTs, lastTaskEndTs) > 0);

            var age = EndedWindow + TimeSpan.FromSeconds(1);
            if (DateTime.TryParse(lastTs, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var last))
                age = nowUtc - last;

            bool busy = unfinishedTool || activeTask;
            s.Status = TranscriptParser.ComputeStatus(age, busy);
            s.Working = age <= EndedWindow && (busy || (lastWasUser && age <= ActiveWindow));

            if (string.IsNullOrEmpty(s.Project)) s.Project = "(unknown)";
            if (string.IsNullOrEmpty(s.Title)) s.Title = sessionId;
            return s;
        }

        public static List<ActivityEvent> ParseEvents(IReadOnlyList<string> lines, int max)
        {
            var all = new List<ActivityEvent>();
            foreach (var line in lines)
            {
                var o = TryParse(line);
                if (o == null) continue;
                var p = o["payload"] as JObject;
                if (p == null) continue;
                var ts = Str(o["timestamp"]);
                var top = Str(o["type"]);
                var ptype = Str(p["type"]);

                if (top == "event_msg" && ptype == "user_message")
                {
                    var m = Str(p["message"]);
                    if (!string.IsNullOrWhiteSpace(m))
                        all.Add(new ActivityEvent { Kind = "user_prompt", Ts = ts, Text = m, Summary = Truncate(m, 80) });
                    continue;
                }

                if (top != "response_item") continue;
                switch (ptype)
                {
                    case "message":
                        if (Str(p["role"]) == "assistant")
                        {
                            var tx = TextOf(p["content"], "output_text");
                            if (!string.IsNullOrWhiteSpace(tx))
                                all.Add(new ActivityEvent { Kind = "message", Ts = ts, Text = tx, Summary = Truncate(tx, 80) });
                        }
                        break;
                    case "reasoning":
                        all.Add(new ActivityEvent { Kind = "thinking", Ts = ts, Summary = "(사고)" });
                        break;
                    case "function_call":
                        var name = Str(p["name"]);
                        all.Add(new ActivityEvent { Kind = "tool_use", Ts = ts, ToolName = name, Summary = SummarizeToolUse(name, Str(p["arguments"])) });
                        break;
                    case "function_call_output":
                        var outp = OutputText(p["output"]);
                        all.Add(new ActivityEvent { Kind = "tool_result", Ts = ts, Summary = Truncate(FirstLine(outp), 80), Text = Truncate(outp, 2000) });
                        break;
                }
            }
            if (all.Count > max) all = all.GetRange(all.Count - max, max);
            return all;
        }

        /// <summary>Codex function_call을 사람이 읽는 한 줄로. arguments는 JSON 문자열.</summary>
        public static string SummarizeToolUse(string name, string argumentsJson)
        {
            string detail = null;
            JObject args = null;
            try { if (!string.IsNullOrWhiteSpace(argumentsJson)) args = JObject.Parse(argumentsJson); } catch { }
            if (args != null)
            {
                // Codex 셸 실행(shell_command/shell/local_shell)은 command로, 파일 편집(apply_patch)은 경로 힌트로.
                detail = FirstLine(Str(args["command"]))
                    ?? Str(args["cmd"])
                    ?? FirstLine(Str(args["input"]))
                    ?? Str(args["path"])
                    ?? Str(args["file_path"]);
            }
            detail = Truncate(detail, 80);
            return string.IsNullOrWhiteSpace(detail) ? (name ?? "tool") : $"{name}  {detail}";
        }

        private static JObject ParseArgs(string argumentsJson)
        {
            try { return string.IsNullOrWhiteSpace(argumentsJson) ? null : JObject.Parse(argumentsJson); }
            catch { return null; }
        }

        // content 배열에서 지정 타입(output_text/input_text)의 text를 개행 결합. 문자열 content도 지원.
        private static string TextOf(JToken content, string wantType)
        {
            if (content == null) return null;
            if (content.Type == JTokenType.String) return content.Value<string>();
            if (content is JArray arr)
            {
                var parts = new List<string>();
                foreach (var b in arr.OfType<JObject>())
                {
                    var bt = Str(b["type"]);
                    if (bt == wantType || bt == "text")
                    {
                        var tx = Str(b["text"]);
                        if (!string.IsNullOrWhiteSpace(tx)) parts.Add(tx.Trim());
                    }
                }
                if (parts.Count > 0) return string.Join("\n", parts);
            }
            return null;
        }

        // function_call_output.output는 문자열이거나 {output/content} 객체일 수 있다.
        private static string OutputText(JToken output)
        {
            if (output == null) return null;
            if (output.Type == JTokenType.String) return output.Value<string>();
            return Str(output["output"]) ?? Str(output["content"]) ?? output.ToString();
        }

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var trimmed = path.Replace('\\', '/').TrimEnd('/');
            var i = trimmed.LastIndexOf('/');
            return i >= 0 ? trimmed.Substring(i + 1) : trimmed;
        }

        private static string FirstLine(string s) => s?.Split('\n')[0]?.Trim();
        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
