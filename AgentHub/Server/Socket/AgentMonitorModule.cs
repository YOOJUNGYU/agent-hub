using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Server.Agents;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 모바일 Claude 에이전트 모니터용 WebSocket (route: /ws/agents).
    /// 접속 시 레지스트리 등록 + 초기 스냅샷 전송, 종료 시 해제.
    /// 실시간 갱신은 <see cref="AgentMonitorService"/>가 <see cref="BroadcastMessageAsync"/>로 push.
    /// </summary>
    public class AgentMonitorModule : WebSocketModule
    {
        public AgentMonitorModule(string urlPath) : base(urlPath, true)
        {
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            var ip = context.RemoteEndPoint?.Address?.ToString() ?? "unknown";
            var ua = context.Headers?["User-Agent"] ?? "unknown";
            MonitorClientRegistry.Add(context.Id, ip, ua);
            await SendAsync(context, AgentMonitorService.CurrentAgentsMessage());
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            MonitorClientRegistry.Remove(context.Id);
            return Task.CompletedTask;
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
            => Task.CompletedTask; // 조회 전용 — 클라이언트 메시지 없음

        /// <summary>서비스에서 호출하는 public broadcast 래퍼.</summary>
        public Task BroadcastMessageAsync(string message) => BroadcastAsync(message);
    }
}
