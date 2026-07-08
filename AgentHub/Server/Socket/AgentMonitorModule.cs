using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Devices;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 모바일 모니터용 WebSocket (route: /ws/agents?token=...).
    /// 접속 시 토큰 상태를 판별해 auth 메시지를 보낸다. 승인된 경우에만 레지스트리 등록 +
    /// 에이전트 스냅샷 전송. 승인/해제는 DeviceRegistry.StatusChanged 구독으로 실시간 push.
    /// </summary>
    public class AgentMonitorModule : WebSocketModule
    {
        // contextId -> tokenHash
        private readonly ConcurrentDictionary<string, string> _tokens =
            new ConcurrentDictionary<string, string>();

        // contextId -> 구독 중인 sessionId
        private readonly ConcurrentDictionary<string, string> _watching =
            new ConcurrentDictionary<string, string>();

        public AgentMonitorModule(string urlPath) : base(urlPath, true)
        {
            DeviceRegistry.StatusChanged += OnDeviceStatusChanged;
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            var token = GetToken(context);
            var status = DeviceRegistry.StatusOf(token);
            if (!string.IsNullOrEmpty(token))
                _tokens[context.Id] = DeviceRegistry.HashToken(token);

            await SendAsync(context, AuthMessage(status));

            if (status == DeviceStatus.Approved)
                await ActivateAsync(context, token);
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            _tokens.TryRemove(context.Id, out _);
            _watching.TryRemove(context.Id, out _);
            MonitorClientRegistry.Remove(context.Id);
            return Task.CompletedTask;
        }

        protected override async Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            try
            {
                if (!_tokens.TryGetValue(context.Id, out var h)
                    || DeviceRegistry.StatusByHash(h) != DeviceStatus.Approved) return;

                var text = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = Json.Deserialize<WatchMessage>(text);
                if (msg == null) return;

                if (msg.Type == "watch" && !string.IsNullOrEmpty(msg.SessionId))
                {
                    _watching[context.Id] = msg.SessionId;
                    await SendAsync(context, AgentMonitorService.ActivityMessage(msg.SessionId));
                }
                else if (msg.Type == "unwatch")
                {
                    _watching.TryRemove(context.Id, out _);
                }
                // 세션 제어(프롬프트/슬래시/답변)는 /ws/session 대화형 터미널에서 수행한다.
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        /// <summary>변경 발생 시 각 구독 소켓에 해당 세션 활동을 push.</summary>
        public async Task PushActivityToWatchers()
        {
            foreach (var ctx in ActiveContexts)
            {
                if (!_watching.TryGetValue(ctx.Id, out var sid) || string.IsNullOrEmpty(sid)) continue;
                if (!_tokens.TryGetValue(ctx.Id, out var h) || DeviceRegistry.StatusByHash(h) != DeviceStatus.Approved) continue;
                try { await SendAsync(ctx, AgentMonitorService.ActivityMessage(sid)); }
                catch { /* per-socket 실패 무시 */ }
            }
        }

        /// <summary>서비스에서 호출 — 승인된 소켓에만 broadcast.</summary>
        public Task BroadcastMessageAsync(string message)
            => BroadcastAsync(message, ctx =>
                _tokens.TryGetValue(ctx.Id, out var h)
                && DeviceRegistry.StatusByHash(h) == DeviceStatus.Approved);

        private async void OnDeviceStatusChanged(string hash, string status)
        {
            foreach (var ctx in ActiveContexts)
            {
                if (!_tokens.TryGetValue(ctx.Id, out var h) || h != hash) continue;
                try
                {
                    await SendAsync(ctx, AuthMessage(status));
                    if (status == DeviceStatus.Approved)
                        await ActivateAsync(ctx, null);
                    else
                        MonitorClientRegistry.Remove(ctx.Id);
                }
                catch { /* per-socket 실패 무시 */ }
            }
        }

        private async Task ActivateAsync(IWebSocketContext context, string tokenForSeen)
        {
            if (tokenForSeen != null) DeviceRegistry.MarkSeen(tokenForSeen);
            var ip = context.RemoteEndPoint?.Address?.ToString() ?? "unknown";
            var ua = context.Headers?["User-Agent"] ?? "unknown";
            MonitorClientRegistry.Add(context.Id, ip, ua);
            await SendAsync(context, AgentMonitorService.CurrentSessionsMessage());
        }

        private static string GetToken(IWebSocketContext ctx)
        {
            var q = ctx.RequestUri?.Query;
            if (string.IsNullOrEmpty(q)) return null;
            foreach (var pair in q.TrimStart('?').Split('&'))
            {
                var i = pair.IndexOf('=');
                if (i > 0 && pair.Substring(0, i) == "token")
                    return Uri.UnescapeDataString(pair.Substring(i + 1));
            }
            return null;
        }

        private static string AuthMessage(string status)
            => Json.Serialize(new { type = "auth", status });

        protected override void Dispose(bool disposing)
        {
            if (disposing) DeviceRegistry.StatusChanged -= OnDeviceStatusChanged;
            base.Dispose(disposing);
        }
    }

    internal class WatchMessage
    {
        public string Type { get; set; }
        public string SessionId { get; set; }
    }
}
