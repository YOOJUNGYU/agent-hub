using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AgentHub.Common.Models;
using AgentHub.Common.Util;

namespace AgentHub.Server.Devices
{
    /// <summary>
    /// 등록 기기 저장소(정적, thread-safe, 파일 영속).
    /// 토큰 원문은 저장하지 않고 SHA-256 해시만 보관한다. 조회 키는 TokenHash.
    /// </summary>
    public static class DeviceRegistry
    {
        private static readonly ConcurrentDictionary<string, Device> ByHash =
            new ConcurrentDictionary<string, Device>();
        private static readonly object SaveLock = new object();
        private static string _filePath;

        /// <summary>목록 변경(추가/상태변경/삭제) 시 — 호스트 콘솔 갱신용.</summary>
        public static event Action Changed;

        /// <summary>특정 기기 상태 변경(tokenHash, status) — 모바일 소켓 push용.</summary>
        public static event Action<string, string> StatusChanged;

        public static void Load()
        {
            _filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentHub", "devices.json");
            try
            {
                if (!File.Exists(_filePath)) return;
                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                var list = Json.Deserialize<List<Device>>(json) ?? new List<Device>();
                ByHash.Clear();
                foreach (var d in list)
                    if (!string.IsNullOrEmpty(d.TokenHash)) ByHash[d.TokenHash] = d;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        public static string HashToken(string token)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token ?? ""));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string StatusOf(string token)
            => string.IsNullOrEmpty(token) ? DeviceStatus.None : StatusByHash(HashToken(token));

        public static string StatusByHash(string hash)
            => ByHash.TryGetValue(hash ?? "", out var d) ? d.Status : DeviceStatus.None;

        public static Device FindByToken(string token)
            => string.IsNullOrEmpty(token) ? null
               : (ByHash.TryGetValue(HashToken(token), out var d) ? d : null);

        /// <summary>인증 요청 등록(또는 기존 갱신). 이미 승인된 기기는 승인 유지.</summary>
        public static string Request(string token, string name, string ip, string userAgent)
        {
            var hash = HashToken(token);
            var now = DateTime.UtcNow.ToString("o");
            var d = ByHash.AddOrUpdate(hash,
                _ => new Device
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TokenHash = hash,
                    Name = name,
                    Ip = ip,
                    UserAgent = userAgent,
                    Status = DeviceStatus.Pending,
                    RequestedAt = now,
                    LastSeenAt = now
                },
                (_, existing) =>
                {
                    existing.Name = name;
                    existing.Ip = ip;
                    existing.UserAgent = userAgent;
                    if (existing.Status != DeviceStatus.Approved)
                    {
                        existing.Status = DeviceStatus.Pending;
                        existing.RequestedAt = now;
                    }
                    existing.LastSeenAt = now;
                    return existing;
                });
            Save();
            Changed?.Invoke();
            StatusChanged?.Invoke(hash, d.Status);
            return hash;
        }

        public static bool Approve(string id) => SetStatusById(id, DeviceStatus.Approved);
        public static bool Revoke(string id) => SetStatusById(id, DeviceStatus.Revoked);

        public static bool Delete(string id)
        {
            var entry = ByHash.FirstOrDefault(kv => kv.Value.Id == id);
            if (entry.Value == null) return false;
            if (!ByHash.TryRemove(entry.Key, out _)) return false;
            Save();
            Changed?.Invoke();
            StatusChanged?.Invoke(entry.Key, DeviceStatus.Revoked); // 접속 차단
            return true;
        }

        public static void MarkSeen(string token)
        {
            var d = FindByToken(token);
            if (d == null) return;
            d.LastSeenAt = DateTime.UtcNow.ToString("o"); // 빈번 → 저장 생략(상태변화 아님)
        }

        public static List<DeviceView> Snapshot()
            => ByHash.Values.OrderBy(d => d.RequestedAt).Select(ToView).ToList();

        private static DeviceView ToView(Device d) => new DeviceView
        {
            Id = d.Id, Name = d.Name, Ip = d.Ip, UserAgent = d.UserAgent,
            Status = d.Status, RequestedAt = d.RequestedAt,
            ApprovedAt = d.ApprovedAt, LastSeenAt = d.LastSeenAt
        };

        private static bool SetStatusById(string id, string status)
        {
            var d = ByHash.Values.FirstOrDefault(x => x.Id == id);
            if (d == null) return false;
            d.Status = status;
            if (status == DeviceStatus.Approved) d.ApprovedAt = DateTime.UtcNow.ToString("o");
            Save();
            Changed?.Invoke();
            StatusChanged?.Invoke(d.TokenHash, status);
            return true;
        }

        private static void Save()
        {
            lock (SaveLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var json = Json.Serialize(ByHash.Values.ToList());
                    var tmp = _filePath + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
                    else File.Move(tmp, _filePath);
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
        }
    }
}
