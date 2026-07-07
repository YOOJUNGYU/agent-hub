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
