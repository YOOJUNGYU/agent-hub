using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Devices;
using AgentHub.Server.Terminal;
using EmbedIO.WebSockets;
using Newtonsoft.Json.Linq;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 창 없는 터미널(PtyHost) 스트림: /ws/term?id=&lt;터미널 id&gt;&amp;token=…
    /// 접속 시 지난 출력(backlog)을 보내고, 이후 출력은 바이너리 프레임으로 실시간 전달한다.
    /// 클라이언트 → {type:"input", data} / {type:"resize", cols, rows}. 서버 → 바이너리(출력), 텍스트 {type:"exit"|"gone"}.
    /// PC(loopback) 또는 승인된 기기만 — 세션에 프롬프트를 보내는 것과 같은 권한.
    /// </summary>
    public class TerminalModule : WebSocketModule
    {
        private sealed class Link
        {
            public PtySession Pty;
            public Action<byte[]> OnData;
            public Action OnClosed;
            public readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        }

        private readonly ConcurrentDictionary<string, Link> _links = new ConcurrentDictionary<string, Link>();

        public TerminalModule(string urlPath) : base(urlPath, true) { }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            var approved = NetUtil.IsLoopback(context.RemoteEndPoint?.Address)
                           || DeviceRegistry.StatusOf(Query(context, "token")) == DeviceStatus.Approved;
            if (!approved) { await CloseAsync(context); return; }
            if (!PtyHost.TryGet(Query(context, "id"), out var pty))
            {
                await SendAsync(context, Json.Serialize(new { type = "gone" }));
                await CloseAsync(context);
                return;
            }
            var link = new Link { Pty = pty };
            link.OnData = bytes => _ = Send(context, link, bytes);
            link.OnClosed = () => _ = SendText(context, link, Json.Serialize(new { type = "exit" }));
            _links[context.Id] = link;
            var backlog = pty.Backlog();
            if (backlog.Length > 0) await Send(context, link, backlog);
            if (pty.Exited) await SendText(context, link, Json.Serialize(new { type = "exit" }));
            pty.Data += link.OnData;
            pty.Closed += link.OnClosed;
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            if (!_links.TryGetValue(context.Id, out var link)) return Task.CompletedTask;
            try
            {
                var o = JObject.Parse(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
                var type = (string)o["type"];
                if (type == "input") link.Pty.Write((string)o["data"]);
                else if (type == "resize") link.Pty.Resize((int?)o["cols"] ?? 0, (int?)o["rows"] ?? 0);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return Task.CompletedTask;
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            if (_links.TryRemove(context.Id, out var link))
            {
                link.Pty.Data -= link.OnData;
                link.Pty.Closed -= link.OnClosed;
            }
            return Task.CompletedTask;
        }

        private static string Query(IWebSocketContext ctx, string key)
        {
            foreach (var pair in (ctx.RequestUri?.Query ?? "").TrimStart('?').Split('&'))
            {
                var i = pair.IndexOf('=');
                if (i > 0 && pair.Substring(0, i) == key) return Uri.UnescapeDataString(pair.Substring(i + 1));
            }
            return null;
        }

        private async Task Send(IWebSocketContext ctx, Link link, byte[] bytes)
        {
            await link.Gate.WaitAsync();
            try { await SendAsync(ctx, bytes); }
            catch { /* 끊김 */ }
            finally { link.Gate.Release(); }
        }

        private async Task SendText(IWebSocketContext ctx, Link link, string text)
        {
            await link.Gate.WaitAsync();
            try { await SendAsync(ctx, text); }
            catch { /* 끊김 */ }
            finally { link.Gate.Release(); }
        }
    }
}
