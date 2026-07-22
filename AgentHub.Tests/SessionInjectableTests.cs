using AgentHub.Server.Agents;
using Xunit;

namespace AgentHub.Tests
{
    public class SessionInjectableTests
    {
        [Theory]
        [InlineData("claude", true, true)]   // claude + PID 있음 → 주입 가능
        [InlineData("claude", false, false)] // claude + PID 없음 → 불가(세션연결)
        [InlineData("codex", true, false)]   // codex → 콘솔 없음, 불가
        [InlineData("codex", false, false)]
        [InlineData(null, true, false)]
        public void IsInjectable_rule(string engine, bool hasPid, bool expected)
        {
            Assert.Equal(expected, AgentMonitorService.IsInjectable(engine, hasPid));
        }
    }
}
