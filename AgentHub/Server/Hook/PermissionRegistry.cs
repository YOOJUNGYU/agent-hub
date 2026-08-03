using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// PermissionRequest 권한 요청의 미결(pending) 결정을 관리. 훅(HTTP)이 결정을 대기하고,
    /// 폰에서 온 응답이 Resolve로 대기를 해제한다. 타임아웃 시 "ask"(정상 흐름 폴백).
    /// 대기 중인 요청은 세션·도구 정보까지 보관해, 폰이 (푸시를 보고) 뒤늦게 접속할 때
    /// 권한 배너를 다시 띄울 수 있게 한다(PendingSnapshot).
    /// </summary>
    public static class PermissionRegistry
    {
        /// <summary>재전송용 미결 권한 요청 1건.</summary>
        public class Pending
        {
            public string Id;
            public string SessionId;
            public string Project;
            public string Tool;
            public string Detail;
            internal TaskCompletionSource<string> Tcs;
        }

        private static readonly ConcurrentDictionary<string, Pending> _pending
            = new ConcurrentDictionary<string, Pending>();

        /// <summary>id에 대한 결정을 대기. 초과 시 "ask" 반환. 세션·도구 정보는 뒤늦은 접속 재전송용으로 보관.</summary>
        public static async Task<string> AwaitDecision(string id, string sessionId, string project,
            string tool, string detail, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = new Pending
            {
                Id = id, SessionId = sessionId, Project = project, Tool = tool, Detail = detail, Tcs = tcs
            };
            try
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                return done == tcs.Task ? tcs.Task.Result : "ask";
            }
            finally { _pending.TryRemove(id, out _); }
        }

        /// <summary>폰에서 온 결정으로 대기 해제. allow/deny 외에는 ask로 정규화.</summary>
        public static void Resolve(string id, string decision)
        {
            if (!string.IsNullOrEmpty(id) && _pending.TryGetValue(id, out var p))
                p.Tcs.TrySetResult(decision == "allow" || decision == "deny" ? decision : "ask");
        }

        /// <summary>아직 응답되지 않은 권한 요청 목록(새로 접속/재구독한 폰에 재전송용).</summary>
        public static IList<Pending> PendingSnapshot()
        {
            var list = new List<Pending>();
            foreach (var kv in _pending) list.Add(kv.Value);
            return list;
        }
    }
}
