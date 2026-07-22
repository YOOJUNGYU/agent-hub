using System;
using System.Collections.Generic;
using System.Linq;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Socket;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// 세션 모니터링 데이터 소스(seam). Claude(ClaudeSessionReader) + Codex(CodexSessionReader)
    /// 트랜스크립트를 읽어 하나의 목록으로 병합해 /ws/agents 로 push한다. 변경은 FileSystemWatcher 콜백으로 즉시 반영.
    /// sessionId 기준 조회는 엔진(소유 리더)으로 라우팅한다.
    /// </summary>
    public static partial class AgentMonitorService
    {
        private static AgentMonitorModule _module;
        private const int MaxSessions = 30;

        public static List<SessionSummary> CurrentSessions()
        {
            var merged = new List<SessionSummary>();
            merged.AddRange(ClaudeSessionReader.ListSessions());
            if (CodexSessionReader.Available) merged.AddRange(CodexSessionReader.ListSessions());
            var list = merged
                .OrderByDescending(s => s.LastActivityAt ?? "", StringComparer.Ordinal)
                .Take(MaxSessions)
                .ToList();
            foreach (var s in list)
                s.Injectable = IsInjectable(s.Engine, Hook.SessionPidRegistry.TryGet(s.Id, out _));
            return list;
        }

        /// <summary>sessionId가 어느 엔진 소유인지(이어받기·라우팅용). Codex 파일이 있으면 codex, 아니면 claude.</summary>
        public static string EngineOf(string sessionId)
            => CodexSessionReader.Available && CodexSessionReader.Has(sessionId) ? "codex" : "claude";

        public static List<ActivityEvent> Activity(string sessionId, int max = 200)
            => EngineOf(sessionId) == "codex"
                ? CodexSessionReader.GetActivity(sessionId, max)
                : ClaudeSessionReader.GetActivity(sessionId, max);

        /// <summary>세션의 cwd(터미널 resume용). 엔진 라우팅.</summary>
        public static string CwdOf(string sessionId)
            => EngineOf(sessionId) == "codex"
                ? CodexSessionReader.CwdOf(sessionId)
                : ClaudeSessionReader.CwdOf(sessionId);

        /// <summary>세션의 마지막 어시스턴트 텍스트(알림 본문). 엔진 라우팅.</summary>
        public static string LastAssistantTextOf(string sessionId)
            => EngineOf(sessionId) == "codex"
                ? CodexSessionReader.LastAssistantTextOf(sessionId)
                : ClaudeSessionReader.LastAssistantTextOf(sessionId);

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
            CodexSessionReader.Start(OnChanged); // Codex 미설치 시 내부에서 조용히 비활성
        }

        public static void Stop()
        {
            ClaudeSessionReader.Stop();
            CodexSessionReader.Stop();
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

        /// <summary>세션이 턴을 끝냄 → 마지막 멘트를 알림 본문으로 push. 사용자가 보고 직접 판단.</summary>
        public static async void BroadcastDone(string project, string sessionId, string message)
        {
            var msg = Json.Serialize(new
            {
                type = "done",
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
