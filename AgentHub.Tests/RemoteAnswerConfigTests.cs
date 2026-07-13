using AgentHub.Server.Hook;
using Xunit;

namespace AgentHub.Tests
{
    public class RemoteAnswerConfigTests
    {
        [Fact]
        public void Default_window_is_documented_safe_600()
        {
            Assert.Equal(600, RemoteAnswerConfig.WindowSeconds);
        }

        [Fact]
        public void Cascade_is_strictly_nested_within_the_window()
        {
            // 서버 대기 < 훅 JS 예산 < Claude 훅 timeout(=WindowSeconds). 모두 600초 이내.
            Assert.True(RemoteAnswerConfig.ServerWindowMs < RemoteAnswerConfig.HookBudgetMs);
            Assert.True(RemoteAnswerConfig.HookBudgetMs < RemoteAnswerConfig.WindowSeconds * 1000);
            Assert.True(RemoteAnswerConfig.ServerMarginMs > 0);
        }

        [Fact]
        public void Window_is_wider_than_the_old_120s()
        {
            Assert.True(RemoteAnswerConfig.WindowSeconds > 120);
        }
    }
}
