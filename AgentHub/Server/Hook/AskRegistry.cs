using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// AskUserQuestion(elicitation)의 미결 답변을 관리. 훅(HTTP)이 폰의 답변을 대기하고,
    /// 폰에서 온 elicitAnswer가 Resolve로 대기를 해제한다. 값은 answers(JSON 문자열).
    /// 타임아웃/무응답 시 null → 훅이 출력 없이 정상 흐름(PC 프롬프트)으로 폴백.
    /// </summary>
    public static class AskRegistry
    {
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending
            = new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        /// <summary>id에 대한 answers(JSON)를 대기. 초과 시 null 반환.</summary>
        public static async Task<string> AwaitAnswer(string id, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            try
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                return done == tcs.Task ? tcs.Task.Result : null;
            }
            finally { _pending.TryRemove(id, out _); }
        }

        /// <summary>폰에서 온 답변(answers JSON)으로 대기 해제. 빈 값이면 무시(폴백).</summary>
        public static void Resolve(string id, string answersJson)
        {
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(answersJson)
                && _pending.TryGetValue(id, out var tcs))
                tcs.TrySetResult(answersJson);
        }
    }
}
