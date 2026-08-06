using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using AgentHub.Common.Util;

namespace AgentHub.Server.Push
{
    /// <summary>Web Push 구독(브라우저 pushManager.subscribe 결과). endpoint + p256dh/auth로 암호화 payload 전송.</summary>
    public class PushSubscription
    {
        public string Endpoint { get; set; }
        public string P256dh { get; set; } // 클라 공개키 — payload 암호화(RFC 8291)용
        public string Auth { get; set; }   // 인증 시크릿 — payload 암호화용
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
            // 같은 브라우저가 토큰을 새로 받으면(재페어링·저장소 삭제) endpoint는 그대로인데 해시만 새로 생긴다.
            // 옛 항목을 남겨두면 '연결됨' 판정을 피해 같은 폰에 푸시가 한 번 더 가서 인앱 알림과 중복된다.
            foreach (var kv in _map)
                if (kv.Key != tokenHash && kv.Value?.Endpoint == sub.Endpoint) _map.TryRemove(kv.Key, out _);
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
