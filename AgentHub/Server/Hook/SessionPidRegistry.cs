using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using AgentHub.Common.Util;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// sessionId → 그 세션을 실행 중인 claude 프로세스 PID. 훅(claude 프로세스 안에서 실행)이
    /// process.ppid를 보고해 채운다. 모바일이 세션을 처음 가져올 때 원본 프로세스를 종료하는 데 쓴다.
    /// 디스크에 영속화한다 — AgentHub 재시작 후에도(살아있는 원본 세션의) PID를 복구해 종료할 수 있도록.
    /// 죽었거나 재사용된 PID는 ProcessKiller가 이름 가드로 안전하게 무시한다.
    /// </summary>
    public static class SessionPidRegistry
    {
        private static readonly ConcurrentDictionary<string, int> _map = new ConcurrentDictionary<string, int>();
        private static readonly object _fileLock = new object();

        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentHub", "session-pids.json");

        static SessionPidRegistry() => Load();

        private static void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var map = Json.Deserialize<Dictionary<string, int>>(File.ReadAllText(_filePath));
                if (map == null) return;
                foreach (var kv in map)
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value > 0) _map[kv.Key] = kv.Value;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private static void Save()
        {
            try
            {
                lock (_fileLock)
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_filePath, Json.Serialize(new Dictionary<string, int>(_map)));
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        public static void Record(string sessionId, int pid)
        {
            if (string.IsNullOrEmpty(sessionId) || pid <= 0) return;
            if (_map.TryGetValue(sessionId, out var cur) && cur == pid) return; // 변경 없음 → 저장 생략
            _map[sessionId] = pid;
            Save();
        }

        public static bool TryGet(string sessionId, out int pid)
        {
            pid = 0;
            return !string.IsNullOrEmpty(sessionId) && _map.TryGetValue(sessionId, out pid);
        }

        public static void Remove(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId) && _map.TryRemove(sessionId, out _)) Save();
        }
    }
}
