using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Util;
using AgentHub.Server.Devices;
using AgentHub.Server.Terminal;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 웹 터미널 WebSocket(/ws/term?token=). 게이트(토글+승인) 통과 시 ConPtySession 생성.
    /// 구조는 EmbedIO WebSocketTerminalModule 샘플을 따르되 Process 대신 ConPTY, raw 바이트 스트리밍.
    /// </summary>
    public class TerminalModule : WebSocketModule
    {
        private static readonly ConcurrentDictionary<string, TerminalModule> Instances = new ConcurrentDictionary<string, TerminalModule>();
        private readonly ConcurrentDictionary<string, ConPtySession> _sessions = new ConcurrentDictionary<string, ConPtySession>();

        public TerminalModule(string urlPath) : base(urlPath, true)
        {
            Instances[urlPath] = this;
        }

        /// <summary>토글 OFF 등에서 호출 — 모든 활성 세션 종료.</summary>
        public static void DisableAllInstances()
        {
            foreach (var m in Instances.Values) m.DisableAll();
        }

        public void DisableAll()
        {
            foreach (var kv in _sessions)
            {
                try { kv.Value.Dispose(); } catch { }
                try { var ctx = FindContext(kv.Key); if (ctx != null) _ = CloseAsync(ctx); } catch { }
            }
            _sessions.Clear();
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            try
            {
                var token = GetToken(context);
                var status = DeviceRegistry.StatusOf(token); // string 반환, 예: "approved" (DeviceStatus.Approved const)
                var enabled = Properties.Settings.Default.TerminalEnabled;
                if (!TerminalGate.IsAllowed(enabled, status))
                {
                    await SendAsync(context, Json.Serialize(new { type = "denied", reason = enabled ? "unauthorized" : "disabled" }));
                    await CloseAsync(context);
                    return;
                }

                var shell = string.IsNullOrWhiteSpace(Properties.Settings.Default.TerminalShell) ? "cmd.exe" : Properties.Settings.Default.TerminalShell;
                var cwd = Properties.Settings.Default.TerminalWorkingDir;
                if (string.IsNullOrWhiteSpace(cwd)) cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                var id = context.Id;
                var session = new ConPtySession(shell, cwd, 80, 24, (buf, n) => OnPtyOutput(id, buf, n));
                session.Exited += async () =>
                {
                    var ctx = FindContext(id);
                    if (ctx != null) { try { await SendAsync(ctx, Json.Serialize(new { type = "exit" })); await CloseAsync(ctx); } catch { } }
                };
                _sessions[id] = session;
                await SendAsync(context, Json.Serialize(new { type = "ready" }));
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                try { await CloseAsync(context); } catch { }
            }
        }

        private async void OnPtyOutput(string contextId, byte[] buf, int n)
        {
            var ctx = FindContext(contextId);
            if (ctx == null) return;
            // 승인 취소 시 즉시 중단
            if (!Properties.Settings.Default.TerminalEnabled)
            {
                if (_sessions.TryRemove(contextId, out var s)) { try { s.Dispose(); } catch { } }
                try { await CloseAsync(ctx); } catch { }
                return;
            }
            var slice = new byte[n];
            Buffer.BlockCopy(buf, 0, slice, 0, n);
            try { await SendAsync(ctx, slice); } catch { } // binary 프레임
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            try
            {
                if (!_sessions.TryGetValue(context.Id, out var session)) return Task.CompletedTask;
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = Json.Deserialize<TermIn>(text);
                if (msg == null) return Task.CompletedTask;
                if (msg.T == "i" && msg.D != null) session.Write(Encoding.UTF8.GetBytes(msg.D));
                else if (msg.T == "r") session.Resize((short)msg.Cols, (short)msg.Rows);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return Task.CompletedTask;
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            if (_sessions.TryRemove(context.Id, out var session))
            {
                try { session.Dispose(); } catch { }
            }
            return Task.CompletedTask;
        }

        private IWebSocketContext FindContext(string id)
        {
            foreach (var c in ActiveContexts) if (c.Id == id) return c;
            return null;
        }

        private static string GetToken(IWebSocketContext ctx)
        {
            var q = ctx.RequestUri?.Query;
            if (string.IsNullOrEmpty(q)) return null;
            foreach (var pair in q.TrimStart('?').Split('&'))
            {
                var i = pair.IndexOf('=');
                if (i > 0 && pair.Substring(0, i) == "token") return Uri.UnescapeDataString(pair.Substring(i + 1));
            }
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisableAll();
            base.Dispose(disposing);
        }

        private class TermIn { public string T { get; set; } public string D { get; set; } public int Cols { get; set; } public int Rows { get; set; } }
    }
}
