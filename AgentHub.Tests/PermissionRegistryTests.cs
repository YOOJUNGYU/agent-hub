using System.Threading.Tasks;
using AgentHub.Server.Hook;
using Xunit;

namespace AgentHub.Tests
{
    public class PermissionRegistryTests
    {
        [Fact]
        public async Task Resolve_returns_decision()
        {
            var task = PermissionRegistry.AwaitDecision("id-allow", 5000);
            PermissionRegistry.Resolve("id-allow", "allow");
            Assert.Equal("allow", await task);
        }

        [Fact]
        public async Task Timeout_returns_ask()
        {
            Assert.Equal("ask", await PermissionRegistry.AwaitDecision("id-none", 30));
        }

        [Fact]
        public async Task Resolve_normalizes_unknown_to_ask()
        {
            var task = PermissionRegistry.AwaitDecision("id-weird", 5000);
            PermissionRegistry.Resolve("id-weird", "banana");
            Assert.Equal("ask", await task);
        }

        [Fact]
        public void Resolve_unknown_id_is_noop()
        {
            PermissionRegistry.Resolve("nonexistent", "allow"); // 예외 없이 무시
        }
    }
}
