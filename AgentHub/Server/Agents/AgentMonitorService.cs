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
            Hook.PendingPermissionRegistry.PruneExpired(TimeSpan.FromMinutes(15)); // 대기 권한 안전망 정리
            foreach (var s in list)
            {
                // 등록된 PID가 실제로 살아 있어야 입력 가능 — 꺼진 세션은 오피스에서 사라진다.
                var hasPid = Hook.SessionPidRegistry.TryGet(s.Id, out var pid) && IsProcessAlive(pid);
                s.Injectable = IsInjectable(s.Engine, hasPid);
                s.Pty = hasPid ? Terminal.PtyHost.FindByPid(pid)?.Id : null;
                // 리더가 요약 인스턴스를 캐시해 재사용할 수 있으므로 해제 시 반드시 null로 덮어쓴다(대기 카드 잔상 방지).
                s.PendingPermission = Hook.PendingPermissionRegistry.TryGet(s.Id, out var pTool, out var pDetail)
                    ? new PendingPermission { Tool = pTool, Detail = pDetail }
                    : null;
            }
            return list;
        }

        /// <summary>sessionId가 어느 엔진 소유인지(이어받기·라우팅용). Codex 파일이 있으면 codex, 아니면 claude.</summary>
        public static string EngineOf(string sessionId)
            => CodexSessionReader.Available && CodexSessionReader.Has(sessionId) ? "codex" : "claude";

        public static List<ActivityEvent> Activity(string sessionId, int max = 200, int tailBytes = 0)
            => EngineOf(sessionId) == "codex"
                ? CodexSessionReader.GetActivity(sessionId, max, tailBytes)
                : ClaudeSessionReader.GetActivity(sessionId, max, tailBytes);

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

        private const int OfficeEventsPerSession = 15;
        private const int OfficeTailBytes = 256 * 1024; // 최근 이벤트 15건엔 충분, 수 MB 트랜스크립트도 즉시

        /// <summary>
        /// 오피스 뷰용: 세션 목록 + 작업 중(working) 세션들의 최근 활동 몇 건. 캐릭터의 방 이동·참조 카드·생각
        /// 말풍선을 그리는 데 쓴다. 쉬는 세션은 활동을 다시 읽지 않는다(트랜스크립트 전체 읽기 비용).
        /// </summary>
        public static string OfficeMessage(List<SessionSummary> sessions = null)
        {
            sessions = sessions ?? CurrentSessions();
            var activity = new Dictionary<string, List<ActivityEvent>>();
            var subagents = new Dictionary<string, List<ClaudeSessionReader.SubagentInfo>>();
            foreach (var s in sessions)
            {
                if (s.Status == "ended") continue;
                // 서브에이전트(별도 캐릭터로 메인 옆에) — Claude만 별도 트랜스크립트를 남긴다. 백그라운드로 도는
                // 서브에이전트는 메인이 쉬는 동안에도 일하므로 작업 중 여부와 상관없이 본다(폴더 목록 조회라 가볍다).
                var subs = EngineOf(s.Id) == "claude"
                    ? ClaudeSessionReader.GetSubagents(s.Id, OfficeEventsPerSession, 64 * 1024)
                    : new List<ClaudeSessionReader.SubagentInfo>();
                if (subs.Count > 0) subagents[s.Id] = subs;
                if (s.Working || s.Status == "active" || subs.Count > 0)
                    activity[s.Id] = Activity(s.Id, OfficeEventsPerSession, OfficeTailBytes);
            }
            return Json.Serialize(new { type = "office", sessions, activity, subagents });
        }

        /// <summary>사용자 응답이 필요한 일(질문·권한·입력 대기)이 생김 — PC 트레이 알림용. (kind, project, sessionId)</summary>
        public static event Action<string, string, string> Attention;

        private static void RaiseAttention(string kind, string project, string sessionId)
        {
            try { Attention?.Invoke(kind, project, sessionId); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        public static void Start(AgentMonitorModule module)
        {
            _module = module;
            ClaudeSessionReader.Start(OnChanged);
            CodexSessionReader.Start(OnChanged); // Codex 미설치 시 내부에서 조용히 비활성
            OnChanged(); // 첫 스캔(큰 트랜스크립트 9p 읽기)을 미리 — 첫 접속이 빈 오피스로 오래 머물지 않게
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
            RaiseAttention("ask", project, sessionId);
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
            RaiseAttention("elicit", project, sessionId);
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
            RaiseAttention("permission", project, sessionId);
            var msg = Json.Serialize(new { type = "permission", id, project, tool, detail, sessionId });
            await _sendGate.WaitAsync();
            try { if (_module != null) await _module.BroadcastMessageAsync(msg); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }

        /// <summary>세션 스냅샷을 즉시 재브로드캐스트(대기 권한 등록/해제를 폰에 바로 반영).</summary>
        public static void NotifyChanged() => OnChanged();

        private static async void OnChanged()
        {
            await _sendGate.WaitAsync();
            try
            {
                if (_module != null)
                {
                    var sessions = CurrentSessions(); // 한 번만 계산(트랜스크립트 스캔) — 목록·오피스가 공유
                    await _module.BroadcastMessageAsync(Json.Serialize(new { type = "sessions", sessions }));
                    await _module.PushActivityToWatchers();
                    await _module.PushOfficeToSubscribers(sessions);
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }
    }
}
