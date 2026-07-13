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
