using System;
using System.Net;
using System.Threading.Tasks;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Devices;

namespace AgentHub.Server.Push
{
    /// <summary>
    /// 승인됐지만 현재 WS로 연결돼 있지 않은(=앱이 꺼진/백그라운드) 기기에 payload 없는 Web Push를 보낸다(awareness).
    /// 연결된 기기는 인앱 알림이 담당하므로 제외 → 중복 없음. 전송은 fire-and-forget(훅 응답을 막지 않음).
    /// </summary>
    public static class PushService
    {
        public static void NotifyDisconnected(string project, string message, string sessionId)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    foreach (var kv in PushSubscriptionRegistry.All())
                    {
                        if (DeviceRegistry.StatusByHash(kv.Key) != DeviceStatus.Approved) continue;
                        if (AgentMonitorService.IsDeviceConnected(kv.Key)) continue; // 연결됨 → 인앱 알림 담당
                        SendOne(kv.Key, kv.Value);
                    }
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            });
        }

        private static void SendOne(string tokenHash, PushSubscription sub)
        {
            try
            {
                var auth = Vapid.AuthorizationHeader(sub.Endpoint);
                if (auth == null) return; // VAPID 키 준비 안 됨
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(sub.Endpoint);
                req.Method = "POST";
                req.Headers["Authorization"] = auth;
                req.Headers["TTL"] = "3600";
                req.ContentLength = 0;
                req.Timeout = 10000;
                using (req.GetRequestStream()) { }          // 빈 본문 확정(payload 없음)
                using (var resp = (HttpWebResponse)req.GetResponse()) { /* 2xx = 성공 */ }
            }
            catch (WebException wex)
            {
                var http = wex.Response as HttpWebResponse;
                // 404/410 = 구독 만료·해지, 403 = VAPID 키 불일치(사용 불가) → 정리(클라가 다음 접속 시 재구독). 그 외는 로깅만.
                if (http != null && (http.StatusCode == HttpStatusCode.NotFound
                    || http.StatusCode == HttpStatusCode.Gone || http.StatusCode == HttpStatusCode.Forbidden))
                    PushSubscriptionRegistry.Remove(tokenHash);
                else
                    LogService.Instance.Error(wex);
                try { http?.Dispose(); } catch { }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }
    }
}
