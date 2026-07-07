using System;
using System.Text;
using System.Threading;
using AgentHub.Server.Terminal;
using Xunit;

namespace AgentHub.Tests
{
    public class ConPtySessionTests
    {
        [Fact]
        public void Spawns_and_streams_output()
        {
            var sb = new StringBuilder();
            var done = new ManualResetEventSlim(false);
            // 실행 후 종료하는 명령 — 출력에 마커가 포함되는지 확인
            using (var s = new ConPtySession("cmd.exe /c echo hello_conpty_marker", null, 80, 24,
                (buf, n) => { lock (sb) sb.Append(Encoding.UTF8.GetString(buf, 0, n)); }))
            {
                s.Exited += () => done.Set();
                done.Wait(TimeSpan.FromSeconds(10));
                Thread.Sleep(200); // 잔여 출력 flush 여유
            }
            Assert.Contains("hello_conpty_marker", sb.ToString());
        }
    }
}
