using AgentHub.Server.Agents;
using Xunit;

namespace AgentHub.Tests
{
    public class SessionInjectableTests
    {
        [Theory]
        [InlineData("claude", true, true)]   // claude + PID 있음 → 주입 가능
        [InlineData("claude", false, false)] // claude + PID 없음 → 불가(세션연결)
        [InlineData("codex", true, true)]    // codex CLI + PID 있음 → claude와 동일하게 주입 가능
        [InlineData("codex", false, false)]
        [InlineData("unknown", true, false)]
        [InlineData(null, true, false)]
        public void IsInjectable_rule(string engine, bool hasPid, bool expected)
        {
            Assert.Equal(expected, AgentMonitorService.IsInjectable(engine, hasPid));
        }

        [Theory]
        [InlineData("claude", true)]
        [InlineData("codex", true)]
        [InlineData("unknown", false)]
        [InlineData(null, false)]
        public void SupportsConsoleInjection_only_for_cli_engines(string engine, bool expected)
        {
            Assert.Equal(expected, AgentMonitorService.SupportsConsoleInjection(engine));
        }
    }
}
