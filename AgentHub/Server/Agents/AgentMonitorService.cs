using System;
using System.Collections.Generic;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Socket;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// 세션 모니터링 데이터 소스(seam). ClaudeSessionReader(트랜스크립트)를 읽어
    /// /ws/agents 로 push한다. 변경은 FileSystemWatcher 콜백으로 즉시 반영.
    /// </summary>
    public static class AgentMonitorService
    {
        private static AgentMonitorModule _module;

        public static List<SessionSummary> CurrentSessions() => ClaudeSessionReader.ListSessions();

        public static List<ActivityEvent> Activity(string sessionId, int max = 200)
            => ClaudeSessionReader.GetActivity(sessionId, max);

        public static string CurrentSessionsMessage() =>
            Json.Serialize(new { type = "sessions", sessions = CurrentSessions() });

        public static string CurrentSessionsSnapshot() =>
            Json.Serialize(new { sessions = CurrentSessions() });

        public static string ActivityMessage(string sessionId) =>
            Json.Serialize(new { type = "activity", sessionId, events = Activity(sessionId) });

        public static void Start(AgentMonitorModule module)
        {
            _module = module;
            ClaudeSessionReader.Start(OnChanged);
        }

        public static void Stop()
        {
            ClaudeSessionReader.Stop();
            _module = null;
        }

        private static readonly System.Threading.SemaphoreSlim _sendGate = new System.Threading.SemaphoreSlim(1, 1);

        public static async void BroadcastAsk(string project, string message, string sessionId)
        {
            var msg = Json.Serialize(new
            {
                type = "ask",
                project,
                message,
                sessionId,
                at = DateTime.UtcNow.ToString("o")
            });
            await _sendGate.WaitAsync();
            try { if (_module != null) await _module.BroadcastMessageAsync(msg); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }

        /// <summary>AskUserQuestion(질문+답변 목록)을 승인 기기에 push(폰이 답을 선택).</summary>
        public static async void BroadcastElicit(string id, string project, object questions, string sessionId)
        {
            var msg = Json.Serialize(new { type = "elicit", id, project, questions, sessionId });
            await _sendGate.WaitAsync();
            try { if (_module != null) await _module.BroadcastMessageAsync(msg); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }

        /// <summary>응답할 승인 기기(폰)가 하나라도 연결돼 있는지.</summary>
        public static bool HasApprovedClient() => _module != null && _module.HasApprovedClient();

        /// <summary>해당 토큰해시의 기기가 현재 WS로 연결돼 있는지(푸시 대상 제외 판정).</summary>
        public static bool IsDeviceConnected(string tokenHash) => _module != null && _module.IsConnected(tokenHash);

        /// <summary>PreToolUse 권한 요청을 승인 기기에 push(폰이 허용/거부 선택).</summary>
        public static async void BroadcastPermission(string id, string project, string tool, string detail, string sessionId)
        {
            var msg = Json.Serialize(new { type = "permission", id, project, tool, detail, sessionId });
            await _sendGate.WaitAsync();
            try { if (_module != null) await _module.BroadcastMessageAsync(msg); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }

        private static async void OnChanged()
        {
            await _sendGate.WaitAsync();
            try
            {
                if (_module != null)
                {
                    await _module.BroadcastMessageAsync(CurrentSessionsMessage());
                    await _module.PushActivityToWatchers();
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }
    }
}
