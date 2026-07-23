using System;
using System.Collections.Concurrent;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// PreToolUse 권한 요청이 "ask"로 폴백돼 Claude가 터미널에 번호 메뉴를 띄우고 대기 중인 상태를 보관.
    /// (라이브 배너로 allow/deny가 확정되면 터미널 프롬프트가 뜨지 않으므로 기록하지 않는다.)
    /// 세션당 하나. 폰이 세션 상세에서 콘솔 주입으로 허용/거부할 수 있도록 SessionSummary에 노출된다.
    /// 메모리 보관(디스크 영속 없음) — AskRegistry/PermissionRegistry와 동일. 재시작 시 소실(트랜스크립트로 복구 불가).
    /// </summary>
    public static class PendingPermissionRegistry
    {
        private class Entry { public string Tool; public string Detail; public DateTime CreatedAt; }

        private static readonly ConcurrentDictionary<string, Entry> _pending
            = new ConcurrentDictionary<string, Entry>();

        /// <summary>"ask" 전환 시 대기 권한을 기록(기존 있으면 덮어씀 = 새 PreToolUse supersede).</summary>
        public static void Set(string sessionId, string tool, string detail)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            _pending[sessionId] = new Entry { Tool = tool, Detail = detail, CreatedAt = DateTime.UtcNow };
        }

        /// <summary>세션의 대기 권한(스냅샷/주입용). 없으면 false.</summary>
        public static bool TryGet(string sessionId, out string tool, out string detail)
        {
            tool = null; detail = null;
            if (string.IsNullOrEmpty(sessionId)) return false;
            if (!_pending.TryGetValue(sessionId, out var e)) return false;
            tool = e.Tool; detail = e.Detail; return true;
        }

        /// <summary>대기 권한 제거(주입 성공 / 세션 종료 등).</summary>
        public static void Clear(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId)) _pending.TryRemove(sessionId, out _);
        }

        /// <summary>생성 후 ttl 초과한 기록을 정리(안전망). CurrentSessions 진입 시 호출.</summary>
        public static void PruneExpired(TimeSpan ttl)
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _pending)
                if (now - kv.Value.CreatedAt > ttl)
                    _pending.TryRemove(kv.Key, out _);
        }
    }
}
