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
                Url = EmbedIOServer.CurrentUrl,
                CertHttpPort = EmbedIOServer.CurrentCertHttpPort,
                // 앱의 표시 언어를 PC UI 문화권에서 도출(지원 언어는 ko/en). PWA가 이 값을 따라간다.
                Lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "ko" : "en"
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

        // Stop/Notification 훅은 매 턴 발화한다. 마지막 멘트가 그대로면(새 text 없음·연속 턴 등)
        // 같은 본문이 반복 전송돼 "동일 알림 반복" 문제가 생긴다. 세션·종류별 직전 전송 본문을 기억해
        // 연속으로 동일하면 skip한다(내용이 바뀌면 다시 알림).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _lastNotified
            = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        private static bool IsDuplicateNotify(string kind, string sessionId, string body)
        {
            var key = kind + "|" + (sessionId ?? "");
            if (_lastNotified.TryGetValue(key, out var prev) && prev == body) return true;
            _lastNotified[key] = body;
            return false;
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
                var message = (string)o["message"] ?? "";
                // 사용자가 직접 응답해야 하는 알림만 '대기중'으로 취급한다(allowlist).
                // 서브에이전트 대기·진행상황·완료(agent_completed)·인증(auth_success) 등 사용자 개입이 필요 없는
                // 알림은 제외 — 알릴 필요가 없다.
                // permission_prompt·elicitation_dialog는 제외한다: AskUserQuestion(질문)은 /hook/elicit이 '질문 상세'와,
                // 도구 권한은 /hook/permission이 '도구 상세'와 함께 이미 알린다. 이를 또 통과시키면 질문/권한마다
                // "Claude needs your permission" 중복 알림이 생긴다. 여기서는 그 흐름 밖의 '입력 대기(idle)'류만 통과.
                var actionable =
                    ntype == "idle_prompt" || ntype == "agent_needs_input"
                    // notification_type 미제공(스톡 Claude Code)일 땐 '입력 대기' 메시지만 통과(서브에이전트/진행/권한 메시지 제외).
                    || (ntype.Length == 0 && message.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0);
                if (actionable)
                {
                    var project = LastSegment((string)o["cwd"] ?? "");
                    var sessionId = (string)o["session_id"];
                    var last = ClaudeSessionReader.LastAssistantTextOf(sessionId);
                    var msg = !string.IsNullOrWhiteSpace(last) ? last
                        : (string.IsNullOrEmpty(message) ? "입력이 필요합니다" : message);
                    if (!IsDuplicateNotify("ask", sessionId, msg))
                    {
                        AgentMonitorService.BroadcastAsk(project, msg, sessionId);
                        AgentHub.Server.Push.PushService.NotifyDisconnected(msg, sessionId);
                    }
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            await SendJsonAsync(Json.Serialize(new { ok = true }));
        }

        // Stop 훅(fire-and-forget): 세션이 턴을 끝냄 → 마지막 멘트를 알림 본문으로. 사용자가 보고 직접 판단.
        [Route(HttpVerbs.Post, "/hook/stop")]
        public async Task HookStop()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            try
            {
                var o = JObject.Parse(raw);
                var project = LastSegment((string)o["cwd"] ?? "");
                var sessionId = (string)o["session_id"];
                var last = ClaudeSessionReader.LastAssistantTextOf(sessionId);
                var body = string.IsNullOrWhiteSpace(last) ? "작업을 완료했습니다" : last;
                if (!IsDuplicateNotify("done", sessionId, body))
                {
                    AgentMonitorService.BroadcastDone(project, sessionId, body);
                    AgentHub.Server.Push.PushService.NotifyDisconnected(body, sessionId);
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            await SendJsonAsync(Json.Serialize(new { ok = true }));
        }

        // PreToolUse 훅(블로킹): 위험 도구 권한을 폰에서 원격 승인. {decision:"allow"|"deny"|"ask"} 반환.
        [Route(HttpVerbs.Post, "/hook/permission")]
        public async Task HookPermission()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            var decision = "ask";
            try
            {
                var o = JObject.Parse(raw);
                var mode = ((string)o["permission_mode"] ?? "").ToLowerInvariant();
                // default 모드 + 응답할 폰이 연결돼 있을 때만 원격 승인. 아니면 정상 흐름(PC 프롬프트)으로 폴백.
                if ((mode == "" || mode == "default") && AgentMonitorService.HasApprovedClient())
                {
                    var id = Guid.NewGuid().ToString("N");
                    var tool = (string)o["tool_name"] ?? "";
                    var detail = ToolDetail(tool, o["tool_input"] as JObject);
                    var project = LastSegment((string)o["cwd"] ?? "");
                    AgentMonitorService.BroadcastPermission(id, project, tool, detail, (string)o["session_id"]);
                    decision = await AgentHub.Server.Hook.PermissionRegistry.AwaitDecision(id, 110000);
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            await SendJsonAsync(Json.Serialize(new { decision }));
        }

        // PermissionRequest 훅(블로킹): AskUserQuestion(질문+답변 목록)을 폰에서 원격 답변.
        // 폰이 고른 답을 updatedInput.answers로 되돌려주면 Claude가 그 답으로 진행한다.
        // 응답할 폰이 없거나 무응답/타임아웃이면 updatedInput 없이 반환 → 훅이 정상 흐름으로 폴백.
        [Route(HttpVerbs.Post, "/hook/elicit")]
        public async Task HookElicit()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            object updatedInput = null;
            try
            {
                var o = JObject.Parse(raw);
                var toolInput = o["tool_input"] as JObject;
                var questions = toolInput?["questions"] as JArray;
                if (questions != null && questions.Count > 0)
                {
                    var project = LastSegment((string)o["cwd"] ?? "");
                    var sessionId = (string)o["session_id"];
                    // 앱이 꺼져 있어도(미연결 승인 기기) 알림. 연결된 기기엔 아래 broadcast가 담당(중복 없음).
                    // 질문 상세를 푸시 본문에 실어 어느 질문인지 바로 보이게 한다(여러 개면 첫 질문 + 개수).
                    var q0 = (questions[0] as JObject)?["question"]?.ToString();
                    var qmsg = string.IsNullOrWhiteSpace(q0) ? "질문에 답해 주세요" : q0.Trim();
                    if (questions.Count > 1) qmsg = "(질문 " + questions.Count + "개) " + qmsg;
                    AgentHub.Server.Push.PushService.NotifyDisconnected(qmsg, sessionId);
                    // 클라이언트가 아직 안 붙었어도(앱을 방금 켠 경우) 항상 등록·대기한다.
                    // 지금 연결된 승인 클라이언트엔 즉시 broadcast하고, 대기 중 새로 연결되는
                    // 클라이언트에는 watch 시 재전송(AskRegistry.TryGetPendingForSession)으로 전달된다.
                    var id = Guid.NewGuid().ToString("N");
                    AgentMonitorService.BroadcastElicit(id, project, questions, sessionId);
                    // 훅이 준 잔여시간(waitMs) 내에서 대기. 서버가 훅 HTTP 타임아웃보다 먼저 응답하도록 여유를 뺀다.
                    var waitMs = (int?)o["waitMs"] ?? AgentHub.Server.Hook.RemoteAnswerConfig.ServerWindowMs;
                    waitMs = Math.Min(waitMs, AgentHub.Server.Hook.RemoteAnswerConfig.ServerWindowMs);
                    waitMs = Math.Max(waitMs - AgentHub.Server.Hook.RemoteAnswerConfig.ServerMarginMs, 1000);
                    var answersJson = await AgentHub.Server.Hook.AskRegistry.AwaitAnswer(id, sessionId, questions.ToString(), waitMs);
                    if (!string.IsNullOrEmpty(answersJson))
                    {
                        var updated = (JObject)toolInput.DeepClone();
                        updated["answers"] = JToken.Parse(answersJson);
                        updatedInput = updated;
                    }
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            await SendJsonAsync(Json.Serialize(new { updatedInput }));
        }

        // 세션↔PID 보고(원본 종료용). 훅이 process.ppid를 보낸다.
        [Route(HttpVerbs.Post, "/hook/session-pid")]
        public async Task HookSessionPid()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            try
            {
                var o = JObject.Parse(raw);
                AgentHub.Server.Hook.SessionPidRegistry.Record((string)o["session_id"], (int?)o["pid"] ?? 0);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            await SendJsonAsync(Json.Serialize(new { ok = true }));
        }

        // ---- Web Push(앱 종료/백그라운드 상태 알림) ----

        [Route(HttpVerbs.Get, "/push/vapid-key")]
        public Task PushVapidKey()
            => SendJsonAsync(Json.Serialize(new { key = AgentHub.Server.Push.Vapid.PublicKeyBase64Url }));

        [Route(HttpVerbs.Post, "/push/subscribe")]
        public async Task PushSubscribe()
        {
            var token = DeviceToken();
            if (DeviceRegistry.StatusOf(token) != DeviceStatus.Approved)
            {
                HttpContext.Response.StatusCode = 401;
                await SendJsonAsync(Json.Serialize(new { ok = false }));
                return;
            }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            var ok = false;
            try
            {
                var o = JObject.Parse(raw);
                var endpoint = (string)o["endpoint"];
                var keys = o["keys"] as JObject;
                if (!string.IsNullOrEmpty(endpoint))
                {
                    AgentHub.Server.Push.PushSubscriptionRegistry.Save(DeviceRegistry.HashToken(token),
                        new AgentHub.Server.Push.PushSubscription
                        {
                            Endpoint = endpoint,
                            P256dh = (string)(keys?["p256dh"]),
                            Auth = (string)(keys?["auth"])
                        });
                    ok = true; // 실제 저장 성공 시에만 true(클라가 무음 실패를 성공으로 오인하지 않도록)
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            if (!ok) HttpContext.Response.StatusCode = 400;
            await SendJsonAsync(Json.Serialize(new { ok }));
        }

        [Route(HttpVerbs.Post, "/push/unsubscribe")]
        public Task PushUnsubscribe()
        {
            AgentHub.Server.Push.PushSubscriptionRegistry.Remove(DeviceRegistry.HashToken(DeviceToken()));
            return SendJsonAsync(Json.Serialize(new { ok = true }));
        }

        /// <summary>권한 카드에 보여줄 도구 요약(Bash→명령, 파일 도구→경로).</summary>
        private static string ToolDetail(string tool, JObject input)
        {
            if (input == null) return tool;
            switch (tool)
            {
                case "Bash": return (string)input["command"] ?? tool;
                case "Write":
                case "Edit":
                case "MultiEdit":
                case "NotebookEdit": return (string)input["file_path"] ?? tool;
                default: return tool;
            }
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

        // BOM 없는 UTF-8. Encoding.UTF8은 preamble(BOM)을 가지며 SendStringAsync가 이를 응답 앞에 붙인다.
        // 그 BOM이 훅(Node)의 JSON.parse를 깨뜨려 모바일 답변/권한 결정이 Claude로 전달되지 않았다.
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private Task SendJsonAsync(string json)
            => HttpContext.SendStringAsync(json, "application/json", Utf8NoBom);

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
