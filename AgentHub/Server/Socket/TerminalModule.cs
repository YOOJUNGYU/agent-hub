using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Devices;
using AgentHub.Server.Terminal;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 웹 터미널 WebSocket(/ws/term?token=). 게이트(토글+승인) 통과 시 ConPtySession 생성.
    /// 세션은 기기 토큰 기준으로 유지된다: 소켓이 끊겨도(화면 나가기) PTY를 살려 두고,
    /// 재접속 시 출력 버퍼를 재생해 이어서 본다. 토글 OFF·승인 취소·셸 종료·서버 정지 시에만 정리.
    /// 구조는 EmbedIO WebSocketTerminalModule 샘플 기반이나 Process 대신 ConPTY, raw 바이트 스트리밍.
    /// </summary>
    public class TerminalModule : WebSocketModule
    {
        private const int BufferCap = 128 * 1024; // 재접속 재생용 출력 버퍼 상한(바이트)

        private class TermSession
        {
            public ConPtySession Pty;
            public string TokenHash;
            public volatile string AttachedContextId; // 현재 붙어있는 소켓(없으면 null)
            private readonly object _gate = new object();
            private readonly LinkedList<byte[]> _buf = new LinkedList<byte[]>();
            private int _bufLen;

            public void Append(byte[] slice)
            {
                lock (_gate)
                {
                    _buf.AddLast(slice); _bufLen += slice.Length;
                    while (_bufLen > BufferCap && _buf.First != null)
                    { _bufLen -= _buf.First.Value.Length; _buf.RemoveFirst(); }
                }
            }
            public byte[][] Snapshot()
            {
                lock (_gate) { var a = new byte[_buf.Count][]; _buf.CopyTo(a, 0); return a; }
            }
        }

        private static readonly ConcurrentDictionary<string, TerminalModule> Instances = new ConcurrentDictionary<string, TerminalModule>();
        // tokenHash -> 영속 세션
        private readonly ConcurrentDictionary<string, TermSession> _byToken = new ConcurrentDictionary<string, TermSession>();
        // contextId -> tokenHash (입력 라우팅·분리·승인취소용)
        private readonly ConcurrentDictionary<string, string> _ctxToken = new ConcurrentDictionary<string, string>();
        // contextId -> 전송 직렬화 락
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _sendLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public TerminalModule(string urlPath) : base(urlPath, true)
        {
            Instances[urlPath] = this;
            DeviceRegistry.StatusChanged += OnDeviceStatusChanged;
        }

        /// <summary>토글 OFF 등에서 호출 — 모든 유지 세션 종료(초기화).</summary>
        public static void DisableAllInstances()
        {
            foreach (var m in Instances.Values) m.DisableAll();
        }

        public void DisableAll()
        {
            foreach (var kv in _byToken)
            {
                try { kv.Value.Pty.Dispose(); } catch { }
                var ctx = FindContext(kv.Value.AttachedContextId);
                if (ctx != null) { try { _ = CloseAsync(ctx); } catch { } }
            }
            _byToken.Clear();
            _ctxToken.Clear();
            _sendLocks.Clear();
            ManagedSessionRegistry.DisposeAll();
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            try
            {
                var token = GetToken(context);
                var status = DeviceRegistry.StatusOf(token);
                var enabled = Properties.Settings.Default.TerminalEnabled;
                if (!TerminalGate.IsAllowed(enabled, status))
                {
                    await SendTextSafe(context, Json.Serialize(new { type = "denied", reason = enabled ? "unauthorized" : "disabled" }));
                    await CloseAsync(context);
                    return;
                }

                var tokenHash = string.IsNullOrEmpty(token) ? ("ctx:" + context.Id) : DeviceRegistry.HashToken(token);
                _ctxToken[context.Id] = tokenHash;

                if (_byToken.TryGetValue(tokenHash, out var existing))
                {
                    // 재접속: 기존 세션에 재부착 + 그동안의 출력 버퍼 재생(화면 복원).
                    existing.AttachedContextId = context.Id;
                    await SendTextSafe(context, Json.Serialize(new { type = "ready", resumed = true }));
                    foreach (var chunk in existing.Snapshot())
                        await SendBytesSafe(context, chunk);
                    return;
                }

                var shell = string.IsNullOrWhiteSpace(Properties.Settings.Default.TerminalShell) ? "cmd.exe" : Properties.Settings.Default.TerminalShell;
                var cwd = Properties.Settings.Default.TerminalWorkingDir;
                if (string.IsNullOrWhiteSpace(cwd)) cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                var session = new TermSession { TokenHash = tokenHash, AttachedContextId = context.Id };
                session.Pty = new ConPtySession(shell, cwd, 80, 24, (buf, n) => OnPtyOutput(tokenHash, buf, n));
                session.Pty.Exited += async () => await OnSessionExited(tokenHash);
                _byToken[tokenHash] = session;
                await SendTextSafe(context, Json.Serialize(new { type = "ready" }));
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                try { await CloseAsync(context); } catch { }
            }
        }

        private async void OnPtyOutput(string tokenHash, byte[] buf, int n)
        {
            if (!_byToken.TryGetValue(tokenHash, out var s)) return;
            if (!Properties.Settings.Default.TerminalEnabled) { KillSession(tokenHash); return; }
            var slice = new byte[n];
            Buffer.BlockCopy(buf, 0, slice, 0, n);
            s.Append(slice); // 버퍼링(재접속 재생용)
            var ctx = FindContext(s.AttachedContextId);
            if (ctx != null) { try { await SendBytesSafe(ctx, slice); } catch { } } // 붙어있으면 라이브 전송
        }

        private async Task OnSessionExited(string tokenHash)
        {
            if (_byToken.TryGetValue(tokenHash, out var s))
            {
                var ctx = FindContext(s.AttachedContextId);
                if (ctx != null) { try { await SendTextSafe(ctx, Json.Serialize(new { type = "exit" })); await CloseAsync(ctx); } catch { } }
            }
            KillSession(tokenHash);
        }

        private void KillSession(string tokenHash)
        {
            if (_byToken.TryRemove(tokenHash, out var s)) { try { s.Pty.Dispose(); } catch { } }
        }

        /// <summary>기기 승인 취소/삭제 시 해당 토큰의 유지 세션을 즉시 종료.</summary>
        private void OnDeviceStatusChanged(string hash, string status)
        {
            if (status == DeviceStatus.Approved) return;
            if (_byToken.TryGetValue(hash, out var s))
            {
                var ctx = FindContext(s.AttachedContextId);
                if (ctx != null) { try { _ = CloseAsync(ctx); } catch { } }
                KillSession(hash);
            }
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            try
            {
                if (!_ctxToken.TryGetValue(context.Id, out var tokenHash)) return Task.CompletedTask;
                if (!_byToken.TryGetValue(tokenHash, out var s)) return Task.CompletedTask;
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = Json.Deserialize<TermIn>(text);
                if (msg == null) return Task.CompletedTask;
                if (msg.T == "i" && msg.D != null) s.Pty.Write(Encoding.UTF8.GetBytes(msg.D));
                else if (msg.T == "r") s.Pty.Resize((short)msg.Cols, (short)msg.Rows);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return Task.CompletedTask;
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            // 세션은 유지(재접속 대비). 소켓만 분리한다.
            if (_ctxToken.TryRemove(context.Id, out var tokenHash)
                && _byToken.TryGetValue(tokenHash, out var s)
                && s.AttachedContextId == context.Id)
            {
                s.AttachedContextId = null; // 분리(PTY는 계속 실행·버퍼링)
            }
            RemoveSendLock(context.Id);
            return Task.CompletedTask;
        }

        private IWebSocketContext FindContext(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in ActiveContexts) if (c.Id == id) return c;
            return null;
        }

        private SemaphoreSlim SendLock(string id) => _sendLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

        private void RemoveSendLock(string id) => _sendLocks.TryRemove(id, out _);

        private async Task SendTextSafe(IWebSocketContext ctx, string s)
        {
            var g = SendLock(ctx.Id);
            await g.WaitAsync();
            try { await SendAsync(ctx, s); } catch { } finally { try { g.Release(); } catch { } }
        }

        private async Task SendBytesSafe(IWebSocketContext ctx, byte[] b)
        {
            var g = SendLock(ctx.Id);
            await g.WaitAsync();
            try { await SendAsync(ctx, b); } catch { } finally { try { g.Release(); } catch { } }
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
            if (disposing)
            {
                DeviceRegistry.StatusChanged -= OnDeviceStatusChanged;
                DisableAll();
            }
            base.Dispose(disposing);
        }

        private class TermIn { public string T { get; set; } public string D { get; set; } public int Cols { get; set; } public int Rows { get; set; } }
    }
}
