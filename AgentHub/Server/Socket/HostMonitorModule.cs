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
        public HostMonitorModule(string urlPath) : base(urlPath, true)
        {
            MonitorClientRegistry.Changed += OnRegistryChanged;
            DeviceRegistry.Changed += OnDevicesChanged;
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            await SendAsync(context, ClientsMessage());
            await SendAsync(context, DevicesMessage());
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
            => Task.CompletedTask;

        private async void OnRegistryChanged()
        {
            try { await BroadcastAsync(ClientsMessage()); }
            catch { /* broadcast 실패 무시 */ }
        }

        private async void OnDevicesChanged()
        {
            try { await BroadcastAsync(DevicesMessage()); }
            catch { /* broadcast 실패 무시 */ }
        }

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
                MonitorClientRegistry.Changed -= OnRegistryChanged;
                DeviceRegistry.Changed -= OnDevicesChanged;
            }
            base.Dispose(disposing);
        }
    }
}
