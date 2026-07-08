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
    /// 승인 기기면 항상 허용(범용 웹 터미널 토글과 무관). 접속 시 Agent Hub가 소유하는
    /// ConPTY로 `claude --resume &lt;sessionId&gt;`를 그 세션의 cwd에서 실행해 대화를 이어받고,
    /// 모바일 xterm에 raw 바이트로 스트리밍한다 → 슬래시 명령·메뉴 등 PC CLI와 동일하게 동작.
    /// PTY는 sessionId 기준으로 유지(소켓이 끊겨도 살려 두고 재접속 시 버퍼 재생). claude 종료·서버 정지 시 정리.
    /// 구조는 TerminalModule과 동형이나 키가 tokenHash가 아닌 sessionId다.
    /// </summary>
    public class SessionTerminalModule : WebSocketModule
    {
        private const int BufferCap = 128 * 1024; // 재접속 재생용 출력 버퍼 상한(바이트)

        private class Session
        {
            public ConPtySession Pty;
            public string SessionId;
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

        private static readonly ConcurrentDictionary<string, SessionTerminalModule> Instances = new ConcurrentDictionary<string, SessionTerminalModule>();
        // sessionId -> 영속 PTY 세션
        private readonly ConcurrentDictionary<string, Session> _bySession = new ConcurrentDictionary<string, Session>();
        // contextId -> sessionId (입력 라우팅·분리용)
        private readonly ConcurrentDictionary<string, string> _ctxSession = new ConcurrentDictionary<string, string>();
        // contextId -> tokenHash (승인취소 감지용)
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
                var ctx = FindContext(kv.Value.AttachedContextId);
                if (ctx != null) { try { _ = CloseAsync(ctx); } catch { } }
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
                var status = DeviceRegistry.StatusOf(token);
                if (status != DeviceStatus.Approved)
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
                _ctxToken[context.Id] = DeviceRegistry.HashToken(token);

                if (_bySession.TryGetValue(sessionId, out var existing))
                {
                    // 재접속: 기존 PTY에 재부착 + 그동안의 출력 버퍼 재생(화면 복원).
                    existing.AttachedContextId = context.Id;
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
                var session = new Session { SessionId = sessionId, AttachedContextId = context.Id };
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
            var ctx = FindContext(s.AttachedContextId);
            if (ctx != null) { try { await SendBytesSafe(ctx, slice); } catch { } } // 붙어있으면 라이브 전송
        }

        private async Task OnSessionExited(string sessionId)
        {
            if (_bySession.TryGetValue(sessionId, out var s))
            {
                var ctx = FindContext(s.AttachedContextId);
                if (ctx != null) { try { await SendTextSafe(ctx, Json.Serialize(new { type = "exit" })); await CloseAsync(ctx); } catch { } }
            }
            KillSession(sessionId);
        }

        private void KillSession(string sessionId)
        {
            if (_bySession.TryRemove(sessionId, out var s)) { try { s.Pty.Dispose(); } catch { } }
        }

        /// <summary>기기 승인 취소/삭제 시 그 기기에 붙어있던 소켓만 닫는다(세션 PTY는 유지 — 다른 승인 기기가 쓸 수 있음).</summary>
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
                // 입력 시점에도 승인 상태 재확인(취소된 기기의 잔여 소켓 차단).
                if (!_ctxToken.TryGetValue(context.Id, out var h) || DeviceRegistry.StatusByHash(h) != DeviceStatus.Approved) return Task.CompletedTask;
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
            // PTY는 유지(재접속 대비). 소켓만 분리한다.
            if (_ctxSession.TryRemove(context.Id, out var sessionId)
                && _bySession.TryGetValue(sessionId, out var s)
                && s.AttachedContextId == context.Id)
            {
                s.AttachedContextId = null; // 분리(PTY는 계속 실행·버퍼링)
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
