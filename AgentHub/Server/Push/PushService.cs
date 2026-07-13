using System;
using System.Net;
using System.Threading.Tasks;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Devices;
using WebPush;

namespace AgentHub.Server.Push
{
    /// <summary>
    /// 승인됐지만 현재 WS로 연결돼 있지 않은(=앱이 꺼진/백그라운드) 기기에 Web Push를 보낸다(awareness).
    /// 연결된 기기는 인앱 알림이 담당하므로 제외 → 중복 없음. 전송은 fire-and-forget(훅 응답을 막지 않음).
    /// 질문 상세를 보여주기 위해 암호화(RFC 8291 aes128gcm) payload {title, body}를 실어 보낸다(WebPush 라이브러리).
    /// </summary>
    public static class PushService
    {
        public static void NotifyDisconnected(string message, string sessionId)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var pub = Vapid.PublicKeyBase64Url;
                    var priv = Vapid.PrivateKeyBase64Url;
                    if (pub == null || priv == null) return; // VAPID 키 준비 안 됨

                    // 앱 이름([agent-hub])은 굳이 붙이지 않고, 어느 세션인지 알 수 있게 세션 제목을 (괄호)로 앞에 붙인다.
                    var st = ClaudeSessionReader.TitleOf(sessionId);
                    var prefix = string.IsNullOrEmpty(st) ? "" : "(" + (st.Length > 40 ? st.Substring(0, 40) + "…" : st) + ") ";
                    var body = prefix + (message ?? "");
                    var payload = Json.Serialize(new { title = "Agent Hub", body });
                    var vapid = new VapidDetails("mailto:noreply@agenthub.local", pub, priv);
                    var client = new WebPushClient();
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    foreach (var kv in PushSubscriptionRegistry.All())
                    {
                        if (DeviceRegistry.StatusByHash(kv.Key) != DeviceStatus.Approved) continue;
                        if (AgentMonitorService.IsDeviceConnected(kv.Key)) continue; // 연결됨 → 인앱 알림 담당
                        SendOne(client, vapid, payload, kv.Key, kv.Value);
                    }
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            });
        }

        private static void SendOne(WebPushClient client, VapidDetails vapid, string payload, string tokenHash, PushSubscription sub)
        {
            // 암호화 payload는 구독의 p256dh/auth 키가 있어야 함. 없으면(옛 구독) 건너뜀 → 다음 접속 시 재구독으로 보강.
            if (string.IsNullOrEmpty(sub.P256dh) || string.IsNullOrEmpty(sub.Auth)) return;
            try
            {
                var wsub = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                client.SendNotification(wsub, payload, vapid);
            }
            catch (WebPushException wex)
            {
                // 404/410 = 구독 만료·해지, 403 = VAPID 키 불일치(사용 불가) → 정리(클라가 다음 접속 시 재구독). 그 외는 로깅만.
                if (wex.StatusCode == HttpStatusCode.NotFound
                    || wex.StatusCode == HttpStatusCode.Gone || wex.StatusCode == HttpStatusCode.Forbidden)
                    PushSubscriptionRegistry.Remove(tokenHash);
                else
                    LogService.Instance.Error(wex);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }
    }
}
