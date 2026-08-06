using System;
using System.Collections.Generic;
using AgentHub.Server.Agents;
using Xunit;

namespace AgentHub.Tests
{
    public class CodexTranscriptParserTests
    {
        private static readonly DateTime Now = DateTime.Parse("2026-07-14T07:16:00Z").ToUniversalTime();

        // session_meta(cwd) + user_message + assistant message + function_call/output + token_count 를 담은 최소 Codex 트랜스크립트
        private static List<string> Sample() => new List<string>
        {
            "{\"timestamp\":\"2026-07-14T07:15:40Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"019f5f7b\",\"cwd\":\"C:\\\\GIT\\\\PRIVATE\\\\agent-hub\"}}",
            "{\"timestamp\":\"2026-07-14T07:15:41Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}",
            "{\"timestamp\":\"2026-07-14T07:15:44Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\",\"message\":\"TGumjinDBTool 위치 알려줘\"}}",
            "{\"timestamp\":\"2026-07-14T07:15:50Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"shell_command\",\"call_id\":\"call_1\",\"arguments\":\"{\\\"command\\\":\\\"Get-ChildItem C:\\\\\\\\GIT\\\"}\"}}",
            "{\"timestamp\":\"2026-07-14T07:15:51Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"call_id\":\"call_1\",\"output\":\"Exit code: 0\\nOK\"}}",
            "{\"timestamp\":\"2026-07-14T07:15:55Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"파일을 찾았습니다.\"}]}}",
            "{\"timestamp\":\"2026-07-14T07:15:56Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"total_tokens\":34150}}}}",
            "{\"timestamp\":\"2026-07-14T07:15:57Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}"
        };

        [Fact]
        public void Summarize_extracts_engine_cwd_project()
        {
            var s = CodexTranscriptParser.Summarize("019f5f7b", Sample(), Now);
            Assert.Equal("codex", s.Engine);
            Assert.Equal("C:\\GIT\\PRIVATE\\agent-hub", s.Cwd);
            Assert.Equal("agent-hub", s.Project);
        }

        [Fact]
        public void Summarize_title_falls_back_to_first_user_message()
        {
            var s = CodexTranscriptParser.Summarize("019f5f7b", Sample(), Now);
            Assert.Equal("TGumjinDBTool 위치 알려줘", s.Title);
        }

        [Fact]
        public void Summarize_reads_cumulative_tokens_and_tool()
        {
            var s = CodexTranscriptParser.Summarize("019f5f7b", Sample(), Now);
            Assert.Equal(34150, s.TotalTokens);
            Assert.Equal("shell_command", s.ToolName);
            Assert.Contains("Get-ChildItem", s.CurrentTask);
        }

        [Fact]
        public void Summarize_survives_null_token_info()
        {
            // Codex는 사용량 한도 등에서 {"type":"token_count","info":null}을 남긴다.
            // JSON null은 JValue.Null이라 ?. 가 통과 → 자식 접근에서 예외가 나면 세션이 목록에서 통째로 빠진다.
            var lines = Sample();
            lines.Add("{\"timestamp\":\"2026-07-14T07:15:58Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}");

            var s = CodexTranscriptParser.Summarize("019f5f7b", lines, Now);

            Assert.Equal(34150, s.TotalTokens); // 직전 유효 값 유지(널 줄은 무시)
        }

        [Fact]
        public void Summarize_completed_tool_is_not_working()
        {
            // 마지막 function_call에 output이 있고 task_complete로 끝났으므로 진행 중이 아니다.
            var s = CodexTranscriptParser.Summarize("019f5f7b", Sample(), Now);
            Assert.False(s.Working);
        }

        [Fact]
        public void Summarize_unfinished_tool_is_working_when_recent()
        {
            var now = DateTime.Parse("2026-07-14T07:16:05Z").ToUniversalTime();
            var lines = new List<string>
            {
                "{\"timestamp\":\"2026-07-14T07:16:00Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"x\",\"cwd\":\"C:\\\\tmp\"}}",
                "{\"timestamp\":\"2026-07-14T07:16:01Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}",
                // output 없는 function_call → 실행 중
                "{\"timestamp\":\"2026-07-14T07:16:02Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"shell_command\",\"call_id\":\"c9\",\"arguments\":\"{\\\"command\\\":\\\"sleep 1\\\"}\"}}"
            };
            var s = CodexTranscriptParser.Summarize("x", lines, now);
            Assert.True(s.Working);
            Assert.Equal("active", s.Status);
        }

        [Fact]
        public void LastAssistantText_returns_output_text()
        {
            Assert.Equal("파일을 찾았습니다.", CodexTranscriptParser.LastAssistantText(Sample()));
        }

        [Fact]
        public void ParseEvents_maps_prompt_tool_and_message()
        {
            var ev = CodexTranscriptParser.ParseEvents(Sample(), 200);
            Assert.Contains(ev, e => e.Kind == "user_prompt" && e.Text.Contains("TGumjinDBTool"));
            Assert.Contains(ev, e => e.Kind == "tool_use" && e.ToolName == "shell_command");
            Assert.Contains(ev, e => e.Kind == "tool_result");
            Assert.Contains(ev, e => e.Kind == "message" && e.Text.Contains("찾았습니다"));
        }

        [Fact]
        public void ExtractPendingAsk_returns_unanswered_request_user_input()
        {
            var lines = new List<string>
            {
                "{\"timestamp\":\"2026-07-14T07:16:00Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"x\",\"cwd\":\"C:\\\\tmp\"}}",
                "{\"timestamp\":\"2026-07-14T07:16:02Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"request_user_input\",\"call_id\":\"ask_1\",\"arguments\":\"{\\\"questions\\\":[{\\\"header\\\":\\\"방향\\\",\\\"question\\\":\\\"어디로?\\\",\\\"multiSelect\\\":false,\\\"options\\\":[{\\\"label\\\":\\\"A\\\"},{\\\"label\\\":\\\"B\\\"}]}]}\"}}"
            };

            var pending = CodexTranscriptParser.ExtractPendingAsk(lines);

            Assert.NotNull(pending);
            Assert.Equal("방향", pending.Header);
            Assert.Equal("어디로?", pending.Question);
            Assert.Equal(new[] { "A", "B" }, pending.Options);
            Assert.Equal(1, pending.QuestionCount);
        }

        [Fact]
        public void ExtractPendingAsk_returns_null_when_answered()
        {
            var lines = new List<string>
            {
                "{\"timestamp\":\"2026-07-14T07:16:00Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"x\",\"cwd\":\"C:\\\\tmp\"}}",
                "{\"timestamp\":\"2026-07-14T07:16:02Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"request_user_input\",\"call_id\":\"ask_1\",\"arguments\":\"{\\\"questions\\\":[{\\\"question\\\":\\\"q\\\",\\\"options\\\":[{\\\"label\\\":\\\"A\\\"}]}]}\"}}",
                "{\"timestamp\":\"2026-07-14T07:16:03Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"call_id\":\"ask_1\",\"output\":\"answered\"}}"
            };

            Assert.Null(CodexTranscriptParser.ExtractPendingAsk(lines));
        }
    }
}
