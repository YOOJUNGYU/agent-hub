using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// PreToolUse 권한 요청의 미결(pending) 결정을 관리. 훅(HTTP)이 결정을 대기하고,
    /// 폰에서 온 응답이 Resolve로 대기를 해제한다. 타임아웃 시 "ask"(정상 흐름 폴백).
    /// </summary>
    public static class PermissionRegistry
    {
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending
            = new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        /// <summary>id에 대한 결정을 대기. 초과 시 "ask" 반환.</summary>
        public static async Task<string> AwaitDecision(string id, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
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
            if (!string.IsNullOrEmpty(id) && _pending.TryGetValue(id, out var tcs))
                tcs.TrySetResult(decision == "allow" || decision == "deny" ? decision : "ask");
        }
    }
}
