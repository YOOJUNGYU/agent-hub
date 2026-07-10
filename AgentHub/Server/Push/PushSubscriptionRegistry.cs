using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using AgentHub.Common.Util;

namespace AgentHub.Server.Push
{
    /// <summary>Web Push 구독(브라우저 pushManager.subscribe 결과). payload-less라 endpoint만 있으면 전송 가능.</summary>
    public class PushSubscription
    {
        public string Endpoint { get; set; }
        public string P256dh { get; set; } // 미사용(payload 암호화 안 함) — 향후 호환 위해 보관
        public string Auth { get; set; }
    }

    /// <summary>기기 토큰해시 → Web Push 구독. 디스크 영속(재시작 후에도 유지).</summary>
    public static class PushSubscriptionRegistry
    {
        private static readonly ConcurrentDictionary<string, PushSubscription> _map
            = new ConcurrentDictionary<string, PushSubscription>();
        private static readonly object _fileLock = new object();
        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentHub", "push-subs.json");

        static PushSubscriptionRegistry() => Load();

        private static void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var map = Json.Deserialize<Dictionary<string, PushSubscription>>(File.ReadAllText(_filePath));
                if (map == null) return;
                foreach (var kv in map)
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value?.Endpoint))
                        _map[kv.Key] = kv.Value;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private static void Persist()
        {
            try
            {
                lock (_fileLock)
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_filePath, Json.Serialize(new Dictionary<string, PushSubscription>(_map)));
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        public static void Save(string tokenHash, PushSubscription sub)
        {
            if (string.IsNullOrEmpty(tokenHash) || string.IsNullOrEmpty(sub?.Endpoint)) return;
            _map[tokenHash] = sub;
            Persist();
        }

        public static void Remove(string tokenHash)
        {
            if (!string.IsNullOrEmpty(tokenHash) && _map.TryRemove(tokenHash, out _)) Persist();
        }

        public static IEnumerable<KeyValuePair<string, PushSubscription>> All() => _map;
    }
}
