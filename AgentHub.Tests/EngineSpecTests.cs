using AgentHub.Server.Terminal;
using Xunit;

namespace AgentHub.Tests
{
    public class EngineSpecTests
    {
        [Theory]
        [InlineData(0, "\r")]
        [InlineData(1, "\x1b[B\r")]
        [InlineData(3, "\x1b[B\x1b[B\x1b[B\r")]
        public void AnswerKeystrokes(int i, string expected)
            => Assert.Equal(expected, EngineSpec.AnswerKeystrokes(i));
    }
}
