using System;
using System.Collections.Concurrent;
using System.Threading;
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

        // contextId -> 전송 직렬화 락. SslStream은 동시 write를 허용하지 않으므로(BeginWrite 예외),
        // 브로드캐스트·activity push·연결/승인 응답이 겹쳐도 소켓별로 write를 직렬화한다.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _sendLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>();

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

            await SendSafe(context, AuthMessage(status));

            if (status == DeviceStatus.Approved)
                await ActivateAsync(context, token);
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            _tokens.TryRemove(context.Id, out _);
            _watching.TryRemove(context.Id, out _);
            _sendLocks.TryRemove(context.Id, out _);
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
                    await SendSafe(context, AgentMonitorService.ActivityMessage(msg.SessionId));
                    // 이 세션에 아직 미답 elicit이 있으면 답변 화면을 다시 띄울 수 있도록 재전송
                    // (폰이 답변 화면을 닫았거나 새로고침해 잃어버린 경우 복구).
                    if (AgentHub.Server.Hook.AskRegistry.TryGetPendingForSession(msg.SessionId, out var eid, out var qsJson))
                        await SendSafe(context, Json.Serialize(new { type = "elicit", id = eid,
                            questions = Newtonsoft.Json.Linq.JToken.Parse(qsJson), sessionId = msg.SessionId, resent = true }));
                }
                else if (msg.Type == "unwatch")
                {
                    _watching.TryRemove(context.Id, out _);
                }
                else if (msg.Type == "permissionDecision")
                {
                    // 폰에서 온 권한 결정 → 대기 중인 PreToolUse 훅 해제.
                    if (!string.IsNullOrEmpty(msg.Id))
                        AgentHub.Server.Hook.PermissionRegistry.Resolve(msg.Id, msg.Decision);
                }
                else if (msg.Type == "elicitAnswer")
                {
                    // clawd-on-desk 동시 실행 시: 두 앱이 같은 훅을 잡아 답변이 전달되지 않으므로
                    // 여기서 차단하고 폰에 안내(사용자가 clawd 종료 후 재시도하도록).
                    if (AgentHub.Common.Util.ClawdGuard.IsRunning())
                        await SendSafe(context, Json.Serialize(new { type = "answerBlocked",
                            reason = "clawd", message = "answer.blockedClawd" }));
                    // 폰에서 고른 AskUserQuestion 답변 → 대기 중인 PermissionRequest 훅 해제.
                    else if (!string.IsNullOrEmpty(msg.Id) && msg.Answers != null)
                        AgentHub.Server.Hook.AskRegistry.Resolve(msg.Id, msg.Answers.ToString());
                }
                else if (msg.Type == "inject" && !string.IsNullOrEmpty(msg.SessionId) && !string.IsNullOrEmpty(msg.Text))
                {
                    // 원본 kill 없이 세션 콘솔에 직접 주입(Claude 전용).
                    bool ok = false; string reason;
                    if (AgentMonitorService.EngineOf(msg.SessionId) != "claude")
                        reason = "engine"; // Codex 등: 콘솔 없음 → 미지원
                    else if (!AgentHub.Server.Hook.SessionPidRegistry.TryGet(msg.SessionId, out var pid))
                        reason = "nopid";  // PID 미보고(세션 종료/훅 미실행)
                    else
                    {
                        // 텍스트→지연→Enter(별도) 시퀀스에 sleep이 있어 소켓 핸들러를 막지 않도록 Task.Run으로 처리.
                        var r = await System.Threading.Tasks.Task.Run(() =>
                            AgentHub.Server.Terminal.ConsoleInputInjector.Inject(pid, msg.Text, appendEnter: true));
                        ok = r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.Ok;
                        reason = ok ? null
                            : (r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.NoConsole ? "noconsole" : "failed");
                    }
                    await SendSafe(context, Json.Serialize(new
                    {
                        type = "injectResult", sessionId = msg.SessionId, ok, reason
                    }));
                }
                else if (msg.Type == "pickerAnswer" && !string.IsNullOrEmpty(msg.SessionId))
                {
                    bool ok = false; string reason;
                    if (AgentMonitorService.EngineOf(msg.SessionId) != "claude")
                        reason = "engine";
                    else if (!AgentHub.Server.Hook.SessionPidRegistry.TryGet(msg.SessionId, out var pid))
                        reason = "nopid";
                    else
                    {
                        var r = await System.Threading.Tasks.Task.Run(() =>
                            AgentHub.Server.Terminal.ConsoleInputInjector.InjectPickerAnswer(
                                pid, msg.Indices ?? new int[0], msg.Text, msg.OptionCount));
                        ok = r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.Ok;
                        reason = ok ? null
                            : (r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.NoConsole ? "noconsole" : "failed");
                    }
                    await SendSafe(context, Json.Serialize(new
                    {
                        type = "pickerAnswerResult", sessionId = msg.SessionId, ok, reason
                    }));
                }
                else if (msg.Type == "permissionInject" && !string.IsNullOrEmpty(msg.SessionId))
                {
                    // "ask"로 폴백된 권한 프롬프트(터미널 번호 메뉴)에 허용/거부를 콘솔 주입.
                    bool ok = false; string reason;
                    if (AgentMonitorService.EngineOf(msg.SessionId) != "claude")
                        reason = "engine";
                    else if (!AgentHub.Server.Hook.SessionPidRegistry.TryGet(msg.SessionId, out var pid))
                        reason = "nopid";
                    else
                    {
                        var r = await System.Threading.Tasks.Task.Run(() =>
                            AgentHub.Server.Terminal.ConsoleInputInjector.InjectPermissionAnswer(pid, msg.Choice));
                        ok = r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.Ok;
                        reason = ok ? null
                            : (r == AgentHub.Server.Terminal.ConsoleInputInjector.Result.NoConsole ? "noconsole" : "failed");
                        if (ok) AgentHub.Server.Hook.PendingPermissionRegistry.Clear(msg.SessionId); // 주입 성공 → 대기 해제
                    }
                    await SendSafe(context, Json.Serialize(new
                    {
                        type = "permissionInjectResult", sessionId = msg.SessionId, ok, reason
                    }));
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        /// <summary>응답 가능한(승인된) 소켓이 하나라도 연결돼 있는지.</summary>
        public bool HasApprovedClient()
        {
            foreach (var ctx in ActiveContexts)
                if (_tokens.TryGetValue(ctx.Id, out var h) && DeviceRegistry.StatusByHash(h) == DeviceStatus.Approved)
                    return true;
            return false;
        }

        /// <summary>해당 토큰해시의 기기가 현재 WS로 연결돼 있는지(푸시 대상에서 제외 판정용).</summary>
        public bool IsConnected(string tokenHash)
        {
            if (string.IsNullOrEmpty(tokenHash)) return false;
            foreach (var ctx in ActiveContexts)
                if (_tokens.TryGetValue(ctx.Id, out var h) && h == tokenHash) return true;
            return false;
        }

        /// <summary>변경 발생 시 각 구독 소켓에 해당 세션 활동을 push.</summary>
        public Task PushActivityToWatchers()
        {
            // 소켓별 직렬화는 SendSafe 세마포어가 담당. 소켓 간에는 병렬로 보내 느린 한 소켓이
            // 나머지 전송을 막지 않게 한다(head-of-line blocking 방지).
            var tasks = new System.Collections.Generic.List<Task>();
            foreach (var ctx in ActiveContexts)
            {
                if (!_watching.TryGetValue(ctx.Id, out var sid) || string.IsNullOrEmpty(sid)) continue;
                if (!_tokens.TryGetValue(ctx.Id, out var h) || DeviceRegistry.StatusByHash(h) != DeviceStatus.Approved) continue;
                tasks.Add(SendSafe(ctx, AgentMonitorService.ActivityMessage(sid)));
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>서비스에서 호출 — 승인된 소켓에만 broadcast(소켓별 write는 직렬화, 소켓 간에는 병렬).</summary>
        public Task BroadcastMessageAsync(string message)
        {
            var tasks = new System.Collections.Generic.List<Task>();
            foreach (var ctx in ActiveContexts)
            {
                if (!_tokens.TryGetValue(ctx.Id, out var h) || DeviceRegistry.StatusByHash(h) != DeviceStatus.Approved) continue;
                tasks.Add(SendSafe(ctx, message));
            }
            return Task.WhenAll(tasks);
        }

        private async void OnDeviceStatusChanged(string hash, string status)
        {
            foreach (var ctx in ActiveContexts)
            {
                if (!_tokens.TryGetValue(ctx.Id, out var h) || h != hash) continue;
                try
                {
                    await SendSafe(ctx, AuthMessage(status));
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
            await SendSafe(context, AgentMonitorService.CurrentSessionsMessage());
        }

        // SslStream 동시 write 금지 대응: contextId별 세마포어로 write를 직렬화한다.
        private SemaphoreSlim SendLock(string id) => _sendLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

        private async Task SendSafe(IWebSocketContext ctx, string message)
        {
            var g = SendLock(ctx.Id);
            await g.WaitAsync();
            try { await SendAsync(ctx, message); }
            catch { /* per-socket 실패 무시(끊김 등) */ }
            finally { try { g.Release(); } catch { } }
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
        public string Id { get; set; }        // permissionDecision / elicitAnswer 대상 id
        public string Decision { get; set; }  // "allow" | "deny"
        public Newtonsoft.Json.Linq.JObject Answers { get; set; }  // elicitAnswer: { [질문텍스트]: 라벨/배열 }
        public string Text { get; set; }      // inject: 세션 콘솔에 주입할 자유 텍스트
        public int[] Indices { get; set; }     // pickerAnswer: 선택한 옵션 0-based 인덱스들
        public int OptionCount { get; set; }   // pickerAnswer: 나열된 실제 옵션 수(Other 번호 계산용)
        public string Choice { get; set; }     // permissionInject: "allow" | "allowAlways" | "deny"
    }
}
