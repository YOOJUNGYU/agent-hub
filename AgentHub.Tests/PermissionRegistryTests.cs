using System.Linq;
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
            var task = PermissionRegistry.AwaitDecision("id-allow", "s1", "proj", "Bash", "ls", 5000);
            PermissionRegistry.Resolve("id-allow", "allow");
            Assert.Equal("allow", await task);
        }

        [Fact]
        public async Task Timeout_returns_ask()
        {
            Assert.Equal("ask", await PermissionRegistry.AwaitDecision("id-none", "s1", "proj", "Bash", "ls", 30));
        }

        [Fact]
        public async Task Resolve_normalizes_unknown_to_ask()
        {
            var task = PermissionRegistry.AwaitDecision("id-weird", "s1", "proj", "Bash", "ls", 5000);
            PermissionRegistry.Resolve("id-weird", "banana");
            Assert.Equal("ask", await task);
        }

        [Fact]
        public void Resolve_unknown_id_is_noop()
        {
            PermissionRegistry.Resolve("nonexistent", "allow"); // 예외 없이 무시
        }

        [Fact]
        public async Task PendingSnapshot_exposes_waiting_request_then_clears()
        {
            var task = PermissionRegistry.AwaitDecision("id-snap", "sess-snap", "proj", "WebFetch", "https://x", 5000);

            var pending = PermissionRegistry.PendingSnapshot().Single(p => p.Id == "id-snap");
            Assert.Equal("sess-snap", pending.SessionId);
            Assert.Equal("WebFetch", pending.Tool);
            Assert.Equal("https://x", pending.Detail);

            PermissionRegistry.Resolve("id-snap", "deny");
            Assert.Equal("deny", await task);
            Assert.DoesNotContain(PermissionRegistry.PendingSnapshot(), p => p.Id == "id-snap");
        }
    }
}
