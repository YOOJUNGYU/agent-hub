using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AgentHub.Server.Socket
{
    /// <summary>모바일 모니터(/ws/agents)에 접속한 클라이언트 정보.</summary>
    public class MonitorClient
    {
        public string Id { get; set; }
        public string Ip { get; set; }
        public string UserAgent { get; set; }
        public string ConnectedAt { get; set; }   // ISO 8601 (UTC)
    }

    /// <summary>
    /// 접속한 모니터 클라이언트 레지스트리(정적, thread-safe).
    /// AgentMonitorModule이 갱신하고 HostMonitorModule이 <see cref="Changed"/>를 구독해 호스트 콘솔에 broadcast.
    /// </summary>
    public static class MonitorClientRegistry
    {
        private static readonly ConcurrentDictionary<string, MonitorClient> Clients =
            new ConcurrentDictionary<string, MonitorClient>();

        /// <summary>클라이언트 추가/제거 시 발생.</summary>
        public static event Action Changed;

        public static void Add(string id, string ip, string userAgent)
        {
            Clients[id] = new MonitorClient
            {
                Id = id,
                Ip = ip,
                UserAgent = userAgent,
                ConnectedAt = DateTime.UtcNow.ToString("o")
            };
            Changed?.Invoke();
        }

        public static void Remove(string id)
        {
            if (Clients.TryRemove(id, out _)) Changed?.Invoke();
        }

        public static List<MonitorClient> Snapshot() =>
            Clients.Values.OrderBy(c => c.ConnectedAt).ToList();

        public static int Count => Clients.Count;
    }
}
