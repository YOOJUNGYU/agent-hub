using System;
using System.IO;
using System.Security.Cryptography;
using AgentHub.Common.Util;
using Newtonsoft.Json;

namespace AgentHub.Server.Push
{
    /// <summary>
    /// VAPID(RFC 8292) 키 관리·서명. payload 없는 Web Push 인증 전용.
    /// net48 내장 ECDsa(P-256)만 사용 — 외부 NuGet/암호화 라이브러리 불필요.
    /// 최초 1회 키쌍 생성 후 %LocalAppData%\AgentHub\push-vapid.json 에 영속.
    /// </summary>
    public static class Vapid
    {
        private static readonly object _lock = new object();
        private static ECParameters _params;
        private static string _publicB64;
        private static bool _loaded;

        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentHub", "push-vapid.json");

        private class Stored { public string D; public string X; public string Y; }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                try
                {
                    if (File.Exists(_filePath))
                    {
                        var s = JsonConvert.DeserializeObject<Stored>(File.ReadAllText(_filePath));
                        if (s?.D != null && s.X != null && s.Y != null)
                        {
                            _params = new ECParameters
                            {
                                Curve = ECCurve.NamedCurves.nistP256,
                                D = FromB64Url(s.D),
                                Q = new ECPoint { X = FromB64Url(s.X), Y = FromB64Url(s.Y) }
                            };
                            _params.Validate();
                            Finish();
                            return;
                        }
                    }
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }

                try // 신규 생성
                {
                    using (var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                    {
                        _params = ec.ExportParameters(true);
                        var dir = Path.GetDirectoryName(_filePath);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(_filePath, JsonConvert.SerializeObject(new Stored
                        {
                            D = ToB64Url(_params.D), X = ToB64Url(_params.Q.X), Y = ToB64Url(_params.Q.Y)
                        }));
                        Finish();
                    }
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
        }

        private static void Finish()
        {
            // 공개키(applicationServerKey): 0x04 || X(32) || Y(32) 비압축 점.
            var pub = new byte[65];
            pub[0] = 0x04;
            Buffer.BlockCopy(_params.Q.X, 0, pub, 1, 32);
            Buffer.BlockCopy(_params.Q.Y, 0, pub, 33, 32);
            _publicB64 = ToB64Url(pub);
            _loaded = true;
        }

        /// <summary>클라이언트 subscribe에 쓸 VAPID 공개키(base64url). 실패 시 null.</summary>
        public static string PublicKeyBase64Url { get { EnsureLoaded(); return _publicB64; } }

        /// <summary>VAPID 개인키 D(base64url, 32B). 암호화 payload 전송(WebPush)의 VAPID 서명용. 실패 시 null.</summary>
        public static string PrivateKeyBase64Url { get { EnsureLoaded(); return _publicB64 == null ? null : ToB64Url(_params.D); } }

        private static string ToB64Url(byte[] b)
            => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromB64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }
    }
}
