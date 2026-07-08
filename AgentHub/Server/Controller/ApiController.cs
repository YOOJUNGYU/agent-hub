using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Newtonsoft.Json.Linq;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Devices;
using AgentHub.Server.Socket;
using static AgentHub.Common.Constants;

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

        // 자체 서명 CA 인증서(.crt) 다운로드 — 모바일 신뢰 설치용.
        // 인증 게이트 없음: 인증서는 기기 등록 이전에 필요하고, 공개 키라 민감정보가 아니다.
        [Route(HttpVerbs.Get, "/cert")]
        public async Task DownloadCert()
        {
            var path = Path.Combine(SelfSigned.CertFilePath, SelfSigned.CrtFileName);
            if (!File.Exists(path))
            {
                HttpContext.Response.StatusCode = 404;
                await SendJsonAsync(Json.Serialize(new { ok = false, message = "인증서 파일을 찾을 수 없습니다." }));
                return;
            }

            var bytes = File.ReadAllBytes(path);
            HttpContext.Response.ContentType = "application/x-x509-ca-cert"; // Android/iOS 인증서 설치 유도
            HttpContext.Response.ContentLength64 = bytes.Length;
            HttpContext.Response.Headers.Add(HttpHeaderNames.ContentDisposition, "attachment; filename=\"AgentHub.crt\"");
            await HttpContext.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        // 실시간은 WebSocket(/ws/agents). 이 엔드포인트는 승인된 기기용 스냅샷 폴백.
        [Route(HttpVerbs.Get, "/sessions")]
        public Task Sessions()
        {
            // PC 호스트 콘솔(loopback)은 토큰 없이 허용, 그 외는 승인 기기만.
            var status = DeviceRegistry.StatusOf(DeviceToken());
            if (!IsLoopback() && status != DeviceStatus.Approved)
            {
                HttpContext.Response.StatusCode = 401;
                return SendJsonAsync(Json.Serialize(new { ok = false, status }));
            }
            return SendJsonAsync(AgentMonitorService.CurrentSessionsSnapshot());
        }

        [Route(HttpVerbs.Get, "/sessions/{id}")]
        public Task SessionActivity(string id)
        {
            var status = DeviceRegistry.StatusOf(DeviceToken());
            if (status != DeviceStatus.Approved)
            {
                HttpContext.Response.StatusCode = 401;
                return SendJsonAsync(Json.Serialize(new { ok = false, status }));
            }
            return SendJsonAsync(Json.Serialize(new { sessionId = id, events = AgentMonitorService.Activity(id) }));
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

        // ---- 터미널 설정 ----

        [Route(HttpVerbs.Get, "/terminal/status")]
        public Task TerminalStatus()
            => SendJsonAsync(Json.Serialize(new { enabled = Properties.Settings.Default.TerminalEnabled }));

        [Route(HttpVerbs.Get, "/terminal/config")]
        public Task GetTerminalConfig()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new
            {
                enabled = Properties.Settings.Default.TerminalEnabled,
                shell = Properties.Settings.Default.TerminalShell,
                workingDir = Properties.Settings.Default.TerminalWorkingDir
            }));
        }

        [Route(HttpVerbs.Post, "/terminal/config")]
        public async Task SaveTerminalConfig()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            var body = Json.Deserialize<TerminalConfigBody>(raw) ?? new TerminalConfigBody();
            Properties.Settings.Default.TerminalEnabled = body.Enabled;
            if (body.Shell != null) Properties.Settings.Default.TerminalShell = body.Shell.Trim();
            if (body.WorkingDir != null) Properties.Settings.Default.TerminalWorkingDir = body.WorkingDir.Trim();
            Properties.Settings.Default.Save();
            if (!body.Enabled) TerminalModule.DisableAllInstances();
            await SendJsonAsync(Json.Serialize(new { ok = true, enabled = body.Enabled }));
        }

        // ---- Notification 훅 설치/제거 (PC/loopback 전용) ----

        [Route(HttpVerbs.Get, "/hook/status")]
        public Task HookStatus()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { installed = AgentHub.Server.Hook.HookInstaller.IsInstalled() }));
        }

        [Route(HttpVerbs.Post, "/hook/install")]
        public Task HookInstall()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = AgentHub.Server.Hook.HookInstaller.Install() }));
        }

        [Route(HttpVerbs.Post, "/hook/uninstall")]
        public Task HookUninstall()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = AgentHub.Server.Hook.HookInstaller.Uninstall() }));
        }

        [Route(HttpVerbs.Post, "/hook/notification")]
        public async Task HookNotification()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            try
            {
                var o = JObject.Parse(raw);
                var ntype = ((string)o["notification_type"] ?? "").ToLowerInvariant();
                // 비-actionable 타입만 무시, 그 외(및 미지정)는 알림
                var skip = ntype == "auth_success" || ntype == "agent_completed"
                        || ntype == "elicitation_complete" || ntype == "elicitation_response";
                if (!skip)
                {
                    var cwd = (string)o["cwd"] ?? "";
                    var project = LastSegment(cwd);
                    var message = (string)o["message"] ?? "입력이 필요합니다";
                    AgentMonitorService.BroadcastAsk(project, message, (string)o["session_id"]);
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            await SendJsonAsync(Json.Serialize(new { ok = true }));
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

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var t = path.Replace('\\', '/').TrimEnd('/');
            var i = t.LastIndexOf('/');
            return i >= 0 ? t.Substring(i + 1) : t;
        }

        public class PortSetting
        {
            public int Port { get; set; }
        }

        internal class TerminalConfigBody
        {
            public bool Enabled { get; set; }
            public string Shell { get; set; }
            public string WorkingDir { get; set; }
        }
    }
}
