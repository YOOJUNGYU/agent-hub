using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// 턴 종료(Stop) 후 폰의 자유 텍스트 답장을 관리. 훅(HTTP)이 답장을 대기하고,
    /// 폰의 reply가 Resolve로, [완료(닫기)]가 Dismiss로 대기를 해제한다.
    /// 타임아웃/무응답/닫기 시 null → 훅이 출력 없이 정상 종료(완료)로 폴백.
    /// 대기 중 sessionId·lastMessage를 보관해, 폰이 세션을 다시 열 때(watch) 답장 카드를 재전송한다.
    /// </summary>
    public static class ReplyRegistry
    {
        private class Pending
        {
            public TaskCompletionSource<string> Tcs;
            public string SessionId;
            public string LastMessage;
        }

        private static readonly ConcurrentDictionary<string, Pending> _pending
            = new ConcurrentDictionary<string, Pending>();

        /// <summary>id에 대한 답장을 대기. 초과/닫기/무응답 시 null.</summary>
        public static async Task<string> AwaitReply(string id, string sessionId, string lastMessage, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = new Pending { Tcs = tcs, SessionId = sessionId, LastMessage = lastMessage };
            try
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                return done == tcs.Task ? tcs.Task.Result : null;
            }
            finally { _pending.TryRemove(id, out _); }
        }

        /// <summary>폰 [전송] — 빈/공백 텍스트는 무시(폴백).</summary>
        public static void Resolve(string id, string text)
        {
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrWhiteSpace(text)
                && _pending.TryGetValue(id, out var p))
                p.Tcs.TrySetResult(text);
        }

        /// <summary>폰 [완료(닫기)] — null로 해제(정상 종료).</summary>
        public static void Dismiss(string id)
        {
            if (!string.IsNullOrEmpty(id) && _pending.TryGetValue(id, out var p))
                p.Tcs.TrySetResult(null);
        }

        /// <summary>해당 세션에 대기 중인 답장이 있으면 (id, lastMessage) 반환.</summary>
        public static bool TryGetPendingForSession(string sessionId, out string id, out string lastMessage)
        {
            id = null; lastMessage = null;
            if (string.IsNullOrEmpty(sessionId)) return false;
            foreach (var kv in _pending)
                if (kv.Value.SessionId == sessionId)
                { id = kv.Key; lastMessage = kv.Value.LastMessage; return true; }
            return false;
        }
    }
}
