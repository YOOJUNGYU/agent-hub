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

    public class SessionReopenerTests
    {
        [Theory]
        [InlineData("019f5f7b-1a2b-3c4d-5e6f-708192a3b4c5", true)]
        [InlineData("abcdef0123456789", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("a && calc.exe", false)]   // 명령 주입 시도 차단
        [InlineData("../../etc", false)]
        public void IsValidSessionId_rejects_unsafe(string id, bool expected)
        {
            Assert.Equal(expected, AgentHub.Server.Terminal.SessionReopener.IsValidSessionId(id));
        }
    }
}
