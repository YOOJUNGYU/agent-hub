using AgentHub.Server.Terminal;
using Xunit;

namespace AgentHub.Tests
{
    public class EngineSpecTests
    {
        [Theory]
        [InlineData(0, "\r")]
        [InlineData(1, "[B\r")]
        [InlineData(3, "[B[B[B\r")]
        public void AnswerKeystrokes(int i, string expected)
            => Assert.Equal(expected, EngineSpec.AnswerKeystrokes(i));
    }
}
