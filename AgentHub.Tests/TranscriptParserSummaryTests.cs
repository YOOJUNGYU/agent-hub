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

        [Fact]
        public void Summarize_respects_title_priority_order_independent()
        {
            // user event with text BEFORE event with slug, no aiTitle
            // slug should win over user text regardless of order in transcript
            var lines = new List<string>
            {
                "{\"type\":\"user\",\"timestamp\":\"2026-07-07T10:00:05Z\",\"message\":{\"role\":\"user\",\"content\":\"This is user text\"}}",
                "{\"type\":\"metadata\",\"slug\":\"preferred-slug-title\",\"timestamp\":\"2026-07-07T10:00:10Z\"}"
            };

            var s = TranscriptParser.Summarize("sess-1", lines, Now);
            Assert.Equal("preferred-slug-title", s.Title);
        }

        [Fact]
        public void Summarize_aiTitle_wins_over_slug()
        {
            // aiTitle should win over slug even if slug appears earlier
            var lines = new List<string>
            {
                "{\"type\":\"metadata\",\"slug\":\"slug-title\",\"timestamp\":\"2026-07-07T10:00:05Z\"}",
                "{\"type\":\"ai-title\",\"aiTitle\":\"ai-title-wins\",\"timestamp\":\"2026-07-07T10:00:10Z\"}"
            };

            var s = TranscriptParser.Summarize("sess-1", lines, Now);
            Assert.Equal("ai-title-wins", s.Title);
        }
    }
}
