using System;
using System.Text;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Devices;

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

        // 실시간은 WebSocket(/ws/agents). 이 엔드포인트는 승인된 기기용 스냅샷 폴백.
        [Route(HttpVerbs.Get, "/agents")]
        public Task Agents()
        {
            var status = DeviceRegistry.StatusOf(DeviceToken());
            if (status != DeviceStatus.Approved)
            {
                HttpContext.Response.StatusCode = 401;
                return SendJsonAsync(Json.Serialize(new { ok = false, status }));
            }
            return SendJsonAsync(AgentMonitorService.CurrentSessionsSnapshot());
        }

        // ---- 기기 인증 (모바일) ----

        [Route(HttpVerbs.Get, "/devices/status")]
        public Task DeviceStatusEndpoint()
            => SendJsonAsync(Json.Serialize(new { status = DeviceRegistry.StatusOf(DeviceToken()) }));

        [Route(HttpVerbs.Post, "/devices/request")]
        public async Task DeviceRequest()
        {
            var token = DeviceToken();
            if (string.IsNullOrEmpty(token))
            {
                HttpContext.Response.StatusCode = 400;
                await SendJsonAsync(Json.Serialize(new { ok = false, message = "토큰이 없습니다." }));
                return;
            }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            var body = Json.Deserialize<DeviceRequestBody>(raw) ?? new DeviceRequestBody();
            var ip = HttpContext.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
            var ua = HttpContext.Request.Headers["User-Agent"] ?? "unknown";
            DeviceRegistry.Request(token, (body.Name ?? "").Trim(), ip, ua);
            await SendJsonAsync(Json.Serialize(new { ok = true, status = DeviceRegistry.StatusOf(token) }));
        }

        // ---- 기기 관리 (PC/loopback 전용) ----

        [Route(HttpVerbs.Get, "/devices")]
        public Task Devices()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { devices = DeviceRegistry.Snapshot() }));
        }

        [Route(HttpVerbs.Post, "/devices/{id}/approve")]
        public Task ApproveDevice(string id)
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = DeviceRegistry.Approve(id) }));
        }

        [Route(HttpVerbs.Post, "/devices/{id}/revoke")]
        public Task RevokeDevice(string id)
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = DeviceRegistry.Revoke(id) }));
        }

        [Route(HttpVerbs.Delete, "/devices/{id}")]
        public Task DeleteDevice(string id)
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = DeviceRegistry.Delete(id) }));
        }

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

        private string DeviceToken() => HttpContext.Request.Headers["X-Device-Token"];

        private bool IsLoopback()
            => NetUtil.IsLoopback(HttpContext.Request.RemoteEndPoint?.Address);

        private Task Forbidden()
        {
            HttpContext.Response.StatusCode = 403;
            return SendJsonAsync(Json.Serialize(new { ok = false, message = "forbidden" }));
        }

        public class PortSetting
        {
            public int Port { get; set; }
        }
    }
}
