using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Util;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 호스트 콘솔(/host)용 WebSocket (route: /ws/host).
    /// 접속한 모바일 클라이언트 목록을 실시간으로 전달한다.
    /// </summary>
    public class HostMonitorModule : WebSocketModule
    {
        public HostMonitorModule(string urlPath) : base(urlPath, true)
        {
            MonitorClientRegistry.Changed += OnRegistryChanged;
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
            => await SendAsync(context, ClientsMessage());

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
            => Task.CompletedTask;

        private async void OnRegistryChanged()
        {
            try { await BroadcastAsync(ClientsMessage()); }
            catch { /* broadcast 실패 무시 */ }
        }

        private static string ClientsMessage() => Json.Serialize(new
        {
            type = "clients",
            count = MonitorClientRegistry.Count,
            clients = MonitorClientRegistry.Snapshot()
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing) MonitorClientRegistry.Changed -= OnRegistryChanged;
            base.Dispose(disposing);
        }
    }
}
