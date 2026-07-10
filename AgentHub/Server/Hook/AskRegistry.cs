using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// AskUserQuestion(elicitation)의 미결 답변을 관리. 훅(HTTP)이 폰의 답변을 대기하고,
    /// 폰에서 온 elicitAnswer가 Resolve로 대기를 해제한다. 값은 answers(JSON 문자열).
    /// 타임아웃/무응답 시 null → 훅이 출력 없이 정상 흐름(PC 프롬프트)으로 폴백.
    /// 대기 중인 elicit은 sessionId·questions까지 보관해, 폰이 세션을 열 때(watch) 답변 화면을 다시 띄울 수 있게 한다.
    /// </summary>
    public static class AskRegistry
    {
        private class Pending
        {
            public TaskCompletionSource<string> Tcs;
            public string SessionId;
            public string QuestionsJson; // 재전송용 questions(JSON 문자열)
        }

        private static readonly ConcurrentDictionary<string, Pending> _pending
            = new ConcurrentDictionary<string, Pending>();

        /// <summary>id에 대한 answers(JSON)를 대기. 초과 시 null 반환. sessionId·questions는 재오픈 재전송용으로 보관.</summary>
        public static async Task<string> AwaitAnswer(string id, string sessionId, string questionsJson, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = new Pending { Tcs = tcs, SessionId = sessionId, QuestionsJson = questionsJson };
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
                && _pending.TryGetValue(id, out var p))
                p.Tcs.TrySetResult(answersJson);
        }

        /// <summary>해당 세션에 아직 답변되지 않은 elicit이 있으면 (id, questionsJson)를 반환.</summary>
        public static bool TryGetPendingForSession(string sessionId, out string id, out string questionsJson)
        {
            id = null; questionsJson = null;
            if (string.IsNullOrEmpty(sessionId)) return false;
            foreach (var kv in _pending)
                if (kv.Value.SessionId == sessionId)
                { id = kv.Key; questionsJson = kv.Value.QuestionsJson; return true; }
            return false;
        }
    }
}
