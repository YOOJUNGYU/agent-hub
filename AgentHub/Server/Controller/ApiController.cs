using System;
using System.Text;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;

namespace AgentHub.Server.Controller
{
    internal class ApiController : WebApiController
    {
        [Route(HttpVerbs.Get, "/server/status")]
        public Task ServerStatus()
        {
            var info = new ServerStatusInfo
            {
                Active = EmbedIOServer.IsRunning,
                Host = EmbedIOServer.CurrentHost,
                Port = EmbedIOServer.CurrentPort,
                Url = EmbedIOServer.CurrentUrl
            };
            return SendJsonAsync(Json.Serialize(info));
        }

        // 실시간은 WebSocket(/ws/agents). 이 엔드포인트는 초기 로드/폴백용 스냅샷.
        [Route(HttpVerbs.Get, "/agents")]
        public Task Agents() => SendJsonAsync(AgentMonitorService.CurrentAgentsSnapshot());

        [Route(HttpVerbs.Get, "/settings")]
        public Task GetSettings()
            => SendJsonAsync(Json.Serialize(new { port = Properties.Settings.Default.ServerPort }));

        [Route(HttpVerbs.Post, "/settings")]
        public async Task SaveSettings()
        {
            try
            {
                var raw = await HttpContext.GetRequestBodyAsStringAsync();
                var body = Json.Deserialize<PortSetting>(raw);
                if (body == null || body.Port < 1024 || body.Port > 65535)
                {
                    await SendJsonAsync(Json.Serialize(new { ok = false, message = "포트는 1024~65535 범위여야 합니다." }));
                    return;
                }

                Properties.Settings.Default.ServerPort = body.Port;
                Properties.Settings.Default.Save();

                await SendJsonAsync(Json.Serialize(new { ok = true, port = body.Port, url = $"https://{EmbedIOServer.CurrentHost}:{body.Port}" }));

                // 응답 전송 후 재시작 — 현재 연결이 끊기므로 클라이언트가 새 URL로 재접속
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    EmbedIOServer.RestartServer();
                });
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                await SendJsonAsync(Json.Serialize(new { ok = false, message = ex.Message }));
            }
        }

        private Task SendJsonAsync(string json)
            => HttpContext.SendStringAsync(json, "application/json", Encoding.UTF8);

        public class PortSetting
        {
            public int Port { get; set; }
        }
    }
}
