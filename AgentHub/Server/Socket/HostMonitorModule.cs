using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
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
        // SslStream은 동시 write를 못 한다 → 소켓마다 한 번에 하나씩 보낸다. 없으면 로그가 몰릴 때
        // 'write 보류 중' 예외가 나고, 그 예외가 다시 로그로 퍼지며 끝없이 반복됐다.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _sendLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

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
            if (_instance != null) await _instance.Broadcast(Json.Serialize(new { type = "log", line }));
        }

        /// <summary>열려 있는 PC 콘솔에 '이 세션 대화창 열기'를 보낸다(트레이 알림 클릭 → 이미 열린 창).</summary>
        public static void RequestOpen(string sessionId)
        {
            if (_instance != null && !string.IsNullOrEmpty(sessionId))
                _ = _instance.Broadcast(Json.Serialize(new { type = "open", sessionId }));
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            // 호스트 콘솔은 PC(loopback) 전용 — LAN 클라이언트의 직접 접속 차단.
            if (!NetUtil.IsLoopback(context.RemoteEndPoint?.Address))
            {
                await CloseAsync(context);
                return;
            }

            await SendSafe(context, ClientsMessage());
            await SendSafe(context, DevicesMessage());
            string[] backlog;
            lock (_logGate) backlog = _logs.ToArray();
            await SendSafe(context, Json.Serialize(new { type = "logs", lines = backlog }));
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            _sendLocks.TryRemove(context.Id, out _);
            return Task.CompletedTask;
        }

        /// <summary>열려 있는 PC 콘솔 모두에 보낸다(소켓별 직렬화, 소켓 간에는 병렬).</summary>
        private Task Broadcast(string message)
        {
            var tasks = new List<Task>();
            foreach (var ctx in ActiveContexts)
                if (IsLoopback(ctx)) tasks.Add(SendSafe(ctx, message));
            return Task.WhenAll(tasks);
        }

        private async Task SendSafe(IWebSocketContext ctx, string message)
        {
            var gate = _sendLocks.GetOrAdd(ctx.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (ctx.WebSocket.State == System.Net.WebSockets.WebSocketState.Open) await SendAsync(ctx, message);
            }
            catch { /* 끊긴 소켓 — 무시 */ }
            finally { gate.Release(); }
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
            => Task.CompletedTask;

        private async void OnRegistryChanged() => await Broadcast(ClientsMessage());

        private async void OnDevicesChanged() => await Broadcast(DevicesMessage());

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
