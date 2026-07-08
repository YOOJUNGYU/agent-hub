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
