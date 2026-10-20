using System.Collections.Generic;
using System.Linq;
using AgentHub.Server.Agents;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentHub.Tests
{
    /// <summary>오피스의 서브 캐릭터(백그라운드 셸·에이전트)를 만드는 활동 이벤트: tool id, 백그라운드 여부, 작업 id, 완료 알림.</summary>
    public class BackgroundTaskEventsTests
    {
        private static string Rec(string type, object content) =>
            new JObject { ["type"] = type, ["timestamp"] = "2026-10-20T07:00:00Z", ["message"] = new JObject { ["content"] = JToken.FromObject(content) } }
                .ToString(Newtonsoft.Json.Formatting.None);

        [Fact]
        public void Background_bash_carries_its_id_flag_and_task_id()
        {
            var ev = TranscriptParser.ParseEvents(new List<string>
            {
                Rec("assistant", new[] { new { type = "tool_use", id = "toolu_1", name = "Bash", input = new { command = "sleep 60", run_in_background = true } } }),
                Rec("user", new[] { new { type = "tool_result", tool_use_id = "toolu_1", content = "Command running in background with ID: bkvmi4fy6. Output is being written to: /tmp/x" } }),
                Rec("user", "<task-notification>\n<task-id>bkvmi4fy6</task-id>\n<status>completed</status>\n</task-notification>"),
            }, 200);

            Assert.Equal(new[] { "tool_use", "tool_result", "task_done" }, ev.Select(e => e.Kind));
            Assert.Equal("toolu_1", ev[0].ToolId);
            Assert.True(ev[0].Background);
            Assert.Equal("toolu_1", ev[1].ToolId);
            Assert.Equal("bkvmi4fy6", ev[1].TaskId);
            Assert.Equal("bkvmi4fy6", ev[2].TaskId);
            Assert.Equal("completed", ev[2].Summary);
        }

        [Fact]
        public void Foreground_tools_have_no_background_flag_and_async_agents_report_agent_id()
        {
            var ev = TranscriptParser.ParseEvents(new List<string>
            {
                Rec("assistant", new[] { new { type = "tool_use", id = "toolu_2", name = "Read", input = new { file_path = "/a.ts" } } }),
                Rec("user", new[] { new { type = "tool_result", tool_use_id = "toolu_3", content = "Async agent launched successfully. agentId: a1b2a871a8353b296" } }),
            }, 200);

            Assert.Null(ev[0].Background);
            Assert.Equal("a1b2a871a8353b296", ev[1].TaskId);
        }
    }
}
