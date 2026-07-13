using System.Threading.Tasks;
using AgentHub.Server.Hook;
using Xunit;

namespace AgentHub.Tests
{
    public class ReplyRegistryTests
    {
        [Fact]
        public async Task Resolve_returns_text()
        {
            var task = ReplyRegistry.AwaitReply("r1", "s1", "질문?", 5000);
            ReplyRegistry.Resolve("r1", "Subagent-Driven로 진행해");
            Assert.Equal("Subagent-Driven로 진행해", await task);
        }

        [Fact]
        public async Task Dismiss_returns_null()
        {
            var task = ReplyRegistry.AwaitReply("r2", "s1", "질문?", 5000);
            ReplyRegistry.Dismiss("r2");
            Assert.Null(await task);
        }

        [Fact]
        public async Task Timeout_returns_null()
        {
            Assert.Null(await ReplyRegistry.AwaitReply("r3", "s1", "질문?", 30));
        }

        [Fact]
        public async Task Blank_reply_is_ignored_then_times_out()
        {
            var task = ReplyRegistry.AwaitReply("r4", "s1", "질문?", 80);
            ReplyRegistry.Resolve("r4", "   ");
            Assert.Null(await task);
        }

        [Fact]
        public void Resolve_or_dismiss_unknown_id_is_noop()
        {
            ReplyRegistry.Resolve("nope", "hi");
            ReplyRegistry.Dismiss("nope");
        }

        [Fact]
        public async Task TryGetPendingForSession_finds_waiting_reply()
        {
            var task = ReplyRegistry.AwaitReply("r5", "sess-x", "마지막 메시지", 5000);
            Assert.True(ReplyRegistry.TryGetPendingForSession("sess-x", out var id, out var last));
            Assert.Equal("r5", id);
            Assert.Equal("마지막 메시지", last);
            ReplyRegistry.Dismiss("r5");
            await task;
        }
    }
}
