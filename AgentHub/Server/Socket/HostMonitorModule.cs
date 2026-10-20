using System.Collections.Generic;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Util;
using AgentHub.Server.Devices;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 호스트 콘솔(/host)용 WebSocket (route: /ws/host).
    /// 접속한 라이브 클라이언트 목록(clients)과 등록 기기 목록(devices)을 실시간 전달한다.
    /// </summary>
    public class HostMonitorModule : WebSocketModule
    {
        // 요청/응답 로그(트레이 앱이 수집) — 콘솔 로그 화면용. 최근 것만 보관해 접속 시 한 번에 보낸다.
        private const int LogBacklog = 500;
        private static readonly Queue<string> _logs = new Queue<string>();
        private static readonly object _logGate = new object();
        private static HostMonitorModule _instance;

        public HostMonitorModule(string urlPath) : base(urlPath, true)
        {
            _instance = this;
            MonitorClientRegistry.Changed += OnRegistryChanged;
            DeviceRegistry.Changed += OnDevicesChanged;
        }

        /// <summary>로그 한 줄을 보관하고 열려 있는 콘솔에 실시간 전달.</summary>
        public static async void PublishLog(string line)
        {
            lock (_logGate)
            {
                _logs.Enqueue(line);
                while (_logs.Count > LogBacklog) _logs.Dequeue();
            }
            try { if (_instance != null) await _instance.BroadcastAsync(Json.Serialize(new { type = "log", line }), IsLoopback); }
            catch { /* broadcast 실패 무시 */ }
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            // 호스트 콘솔은 PC(loopback) 전용 — LAN 클라이언트의 직접 접속 차단.
            if (!NetUtil.IsLoopback(context.RemoteEndPoint?.Address))
            {
                await CloseAsync(context);
                return;
            }

            await SendAsync(context, ClientsMessage());
            await SendAsync(context, DevicesMessage());
            string[] backlog;
            lock (_logGate) backlog = _logs.ToArray();
            await SendAsync(context, Json.Serialize(new { type = "logs", lines = backlog }));
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
            => Task.CompletedTask;

        private async void OnRegistryChanged()
        {
            try { await BroadcastAsync(ClientsMessage(), IsLoopback); }
            catch { /* broadcast 실패 무시 */ }
        }

        private async void OnDevicesChanged()
        {
            try { await BroadcastAsync(DevicesMessage(), IsLoopback); }
            catch { /* broadcast 실패 무시 */ }
        }

        private static bool IsLoopback(IWebSocketContext ctx)
            => NetUtil.IsLoopback(ctx.RemoteEndPoint?.Address);

        private static string ClientsMessage() => Json.Serialize(new
        {
            type = "clients",
            count = MonitorClientRegistry.Count,
            clients = MonitorClientRegistry.Snapshot()
        });

        private static string DevicesMessage() => Json.Serialize(new
        {
            type = "devices",
            devices = DeviceRegistry.Snapshot()
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_instance == this) _instance = null;
                MonitorClientRegistry.Changed -= OnRegistryChanged;
                DeviceRegistry.Changed -= OnDevicesChanged;
            }
            base.Dispose(disposing);
        }
    }
}
