using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Devices;
using AgentHub.Server.Terminal;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 세션별 대화형 터미널 WebSocket(/ws/session?token=&amp;session=&lt;id&gt;).
    /// 한 세션(sessionId)당 Agent Hub가 소유하는 ConPTY 하나(`claude --resume &lt;id&gt;`)를 두고,
    /// PC(호스트 콘솔·loopback)와 승인된 폰 등 <b>여러 클라이언트가 동시에 붙어 입출력을 공유</b>한다
    /// (원격처럼 실시간 동기화). 출력은 붙어있는 모든 소켓에 브로드캐스트하고, 입력은 아무 소켓에서나 PTY로 전달.
    /// PTY는 sessionId 기준으로 유지(모든 소켓이 끊겨도 살려 두고 재접속 시 버퍼 재생). claude 종료·서버 정지 시 정리.
    /// </summary>
    public class SessionTerminalModule : WebSocketModule
    {
        private const int BufferCap = 128 * 1024; // 재접속 재생용 출력 버퍼 상한(바이트)
        private const string LoopbackToken = "loopback"; // PC 호스트 콘솔(토큰 없음) 표식

        private class Session
        {
            public ConPtySession Pty;
            public string SessionId;
            // 여러 클라이언트가 동시에 입력할 수 있으므로 PTY 쓰기를 직렬화(바이트 인터리브 방지).
            public readonly object WriteGate = new object();
            // 현재 붙어있는 소켓들(context.Id 집합). 출력 브로드캐스트 대상.
            public readonly ConcurrentDictionary<string, byte> Attached = new ConcurrentDictionary<string, byte>();
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

        private static readonly ConcurrentDictionary<string, SessionTerminalModule> Instances = new ConcurrentDictionary<string, SessionTerminalModule>();
        // sessionId -> 영속 PTY 세션
        private readonly ConcurrentDictionary<string, Session> _bySession = new ConcurrentDictionary<string, Session>();
        // contextId -> sessionId (입력 라우팅·분리용)
        private readonly ConcurrentDictionary<string, string> _ctxSession = new ConcurrentDictionary<string, string>();
        // contextId -> tokenHash (승인취소 감지용; loopback은 LoopbackToken)
        private readonly ConcurrentDictionary<string, string> _ctxToken = new ConcurrentDictionary<string, string>();
        // contextId -> 전송 직렬화 락
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _sendLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public SessionTerminalModule(string urlPath) : base(urlPath, true)
        {
            Instances[urlPath] = this;
            DeviceRegistry.StatusChanged += OnDeviceStatusChanged;
        }

        /// <summary>서버 정지 시 호출 — 모든 유지 세션 종료.</summary>
        public static void DisableAllInstances()
        {
            foreach (var m in Instances.Values) m.DisableAll();
        }

        public void DisableAll()
        {
            foreach (var kv in _bySession)
            {
                try { kv.Value.Pty.Dispose(); } catch { }
                foreach (var ctxId in kv.Value.Attached.Keys)
                {
                    var ctx = FindContext(ctxId);
                    if (ctx != null) { try { _ = CloseAsync(ctx); } catch { } }
                }
            }
            _bySession.Clear();
            _ctxSession.Clear();
            _ctxToken.Clear();
            _sendLocks.Clear();
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            try
            {
                var token = GetQuery(context, "token");
                var sessionId = GetQuery(context, "session");
                var loopback = NetUtil.IsLoopback(context.RemoteEndPoint?.Address);
                // PC 호스트 콘솔(loopback)은 토큰 없이 허용, 그 외는 승인 기기만.
                if (!loopback && DeviceRegistry.StatusOf(token) != DeviceStatus.Approved)
                {
                    await SendTextSafe(context, Json.Serialize(new { type = "denied", reason = "unauthorized" }));
                    await CloseAsync(context);
                    return;
                }
                if (string.IsNullOrEmpty(sessionId))
                {
                    await SendTextSafe(context, Json.Serialize(new { type = "denied", reason = "nosession" }));
                    await CloseAsync(context);
                    return;
                }

                _ctxSession[context.Id] = sessionId;
                _ctxToken[context.Id] = loopback ? LoopbackToken : DeviceRegistry.HashToken(token);

                if (_bySession.TryGetValue(sessionId, out var existing))
                {
                    // 기존 세션에 합류: 브로드캐스트 대상에 추가 + 그동안의 출력 버퍼 재생(화면 복원).
                    existing.Attached[context.Id] = 0;
                    await SendTextSafe(context, Json.Serialize(new { type = "ready", resumed = true }));
                    foreach (var chunk in existing.Snapshot())
                        await SendBytesSafe(context, chunk);
                    return;
                }

                var cwd = ClaudeSessionReader.CwdOf(sessionId);
                if (string.IsNullOrEmpty(cwd) || !System.IO.Directory.Exists(cwd))
                {
                    await SendTextSafe(context, Json.Serialize(new { type = "denied", reason = "nocwd" }));
                    await CloseAsync(context);
                    return;
                }

                var command = EngineSpec.For("claude").ResumeCommand(sessionId);
                var session = new Session { SessionId = sessionId };
                session.Attached[context.Id] = 0;
                session.Pty = new ConPtySession(command, cwd, 100, 30, (buf, n) => OnPtyOutput(sessionId, buf, n));
                session.Pty.Exited += async () => await OnSessionExited(sessionId);
                _bySession[sessionId] = session;
                await SendTextSafe(context, Json.Serialize(new { type = "ready" }));
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                try { await CloseAsync(context); } catch { }
            }
        }

        private async void OnPtyOutput(string sessionId, byte[] buf, int n)
        {
            if (!_bySession.TryGetValue(sessionId, out var s)) return;
            var slice = new byte[n];
            Buffer.BlockCopy(buf, 0, slice, 0, n);
            s.Append(slice); // 버퍼링(재접속 재생용)
            // 붙어있는 모든 소켓에 브로드캐스트(공유 화면).
            foreach (var ctxId in s.Attached.Keys)
            {
                var ctx = FindContext(ctxId);
                if (ctx != null) { try { await SendBytesSafe(ctx, slice); } catch { } }
            }
        }

        private async Task OnSessionExited(string sessionId)
        {
            if (_bySession.TryGetValue(sessionId, out var s))
            {
                foreach (var ctxId in s.Attached.Keys)
                {
                    var ctx = FindContext(ctxId);
                    if (ctx != null) { try { await SendTextSafe(ctx, Json.Serialize(new { type = "exit" })); await CloseAsync(ctx); } catch { } }
                }
            }
            KillSession(sessionId);
        }

        private void KillSession(string sessionId)
        {
            if (_bySession.TryRemove(sessionId, out var s)) { try { s.Pty.Dispose(); } catch { } }
        }

        /// <summary>기기 승인 취소/삭제 시 그 기기에 붙어있던 소켓만 닫는다(세션 PTY·다른 소켓은 유지).</summary>
        private void OnDeviceStatusChanged(string hash, string status)
        {
            if (status == DeviceStatus.Approved) return;
            foreach (var kv in _ctxToken)
            {
                if (kv.Value != hash) continue;
                var ctx = FindContext(kv.Key);
                if (ctx != null) { try { _ = CloseAsync(ctx); } catch { } }
            }
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            try
            {
                if (!_ctxSession.TryGetValue(context.Id, out var sessionId)) return Task.CompletedTask;
                if (!_bySession.TryGetValue(sessionId, out var s)) return Task.CompletedTask;
                // 입력 시점에도 권한 재확인(취소된 기기의 잔여 소켓 차단; loopback은 항상 허용).
                if (_ctxToken.TryGetValue(context.Id, out var h) && h != LoopbackToken
                    && DeviceRegistry.StatusByHash(h) != DeviceStatus.Approved) return Task.CompletedTask;
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = Json.Deserialize<TermIn>(text);
                if (msg == null) return Task.CompletedTask;
                if (msg.T == "i" && msg.D != null) { lock (s.WriteGate) { s.Pty.Write(Encoding.UTF8.GetBytes(msg.D)); } }
                else if (msg.T == "r") s.Pty.Resize((short)msg.Cols, (short)msg.Rows);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return Task.CompletedTask;
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            // 이 소켓만 분리(PTY와 다른 소켓은 유지 — 재접속·타 기기 대비).
            if (_ctxSession.TryRemove(context.Id, out var sessionId)
                && _bySession.TryGetValue(sessionId, out var s))
            {
                s.Attached.TryRemove(context.Id, out _);
            }
            _ctxToken.TryRemove(context.Id, out _);
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

        private static string GetQuery(IWebSocketContext ctx, string key)
        {
            var q = ctx.RequestUri?.Query;
            if (string.IsNullOrEmpty(q)) return null;
            foreach (var pair in q.TrimStart('?').Split('&'))
            {
                var i = pair.IndexOf('=');
                if (i > 0 && pair.Substring(0, i) == key) return Uri.UnescapeDataString(pair.Substring(i + 1));
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
