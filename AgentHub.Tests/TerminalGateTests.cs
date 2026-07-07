using AgentHub.Server.Terminal;
using Xunit;

namespace AgentHub.Tests
{
    public class TerminalGateTests
    {
        [Theory]
        [InlineData(true, "approved", true)]
        [InlineData(false, "approved", false)]   // 토글 OFF
        [InlineData(true, "pending", false)]     // 미승인
        [InlineData(true, "revoked", false)]
        [InlineData(true, "none", false)]
        [InlineData(false, "pending", false)]
        public void IsAllowed(bool enabled, string status, bool expected)
            => Assert.Equal(expected, TerminalGate.IsAllowed(enabled, status));
    }
}
