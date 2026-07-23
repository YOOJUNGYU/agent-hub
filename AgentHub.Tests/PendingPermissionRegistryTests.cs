using System;
using System.Threading;
using AgentHub.Server.Hook;
using Xunit;

namespace AgentHub.Tests
{
    public class PendingPermissionRegistryTests
    {
        [Fact]
        public void Set_then_TryGet_returns_tool_and_detail()
        {
            PendingPermissionRegistry.Set("s-get", "Bash", "rm -rf x");
            Assert.True(PendingPermissionRegistry.TryGet("s-get", out var tool, out var detail));
            Assert.Equal("Bash", tool);
            Assert.Equal("rm -rf x", detail);
        }

        [Fact]
        public void TryGet_missing_session_is_false()
        {
            Assert.False(PendingPermissionRegistry.TryGet("s-missing", out _, out _));
        }

        [Fact]
        public void Set_overwrites_previous_for_same_session()
        {
            PendingPermissionRegistry.Set("s-over", "Bash", "first");
            PendingPermissionRegistry.Set("s-over", "Edit", "second");
            Assert.True(PendingPermissionRegistry.TryGet("s-over", out var tool, out var detail));
            Assert.Equal("Edit", tool);
            Assert.Equal("second", detail);
        }

        [Fact]
        public void Clear_removes_entry()
        {
            PendingPermissionRegistry.Set("s-clear", "Bash", "x");
            PendingPermissionRegistry.Clear("s-clear");
            Assert.False(PendingPermissionRegistry.TryGet("s-clear", out _, out _));
        }

        [Fact]
        public void PruneExpired_keeps_fresh_removes_stale()
        {
            PendingPermissionRegistry.Set("s-prune", "Bash", "x");
            PendingPermissionRegistry.PruneExpired(TimeSpan.FromMinutes(15)); // 신선 → 유지
            Assert.True(PendingPermissionRegistry.TryGet("s-prune", out _, out _));
            Thread.Sleep(10);
            PendingPermissionRegistry.PruneExpired(TimeSpan.Zero);            // 만료 → 제거
            Assert.False(PendingPermissionRegistry.TryGet("s-prune", out _, out _));
        }
    }
}
