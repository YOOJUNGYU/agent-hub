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
