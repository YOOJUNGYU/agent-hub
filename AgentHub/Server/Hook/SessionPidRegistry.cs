using System.Collections.Concurrent;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// sessionId → 그 세션을 실행 중인 claude 프로세스 PID. 훅(claude 프로세스 안에서 실행)이
    /// process.ppid를 보고해 채운다. 모바일이 세션을 처음 가져올 때 원본 프로세스를 종료하는 데 쓴다.
    /// </summary>
    public static class SessionPidRegistry
    {
        private static readonly ConcurrentDictionary<string, int> _map = new ConcurrentDictionary<string, int>();

        public static void Record(string sessionId, int pid)
        {
            if (!string.IsNullOrEmpty(sessionId) && pid > 0) _map[sessionId] = pid;
        }

        public static bool TryGet(string sessionId, out int pid)
        {
            pid = 0;
            return !string.IsNullOrEmpty(sessionId) && _map.TryGetValue(sessionId, out pid);
        }

        public static void Remove(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId)) _map.TryRemove(sessionId, out _);
        }
    }
}
