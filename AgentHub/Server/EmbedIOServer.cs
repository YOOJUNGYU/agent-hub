using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using EmbedIO;
using EmbedIO.Files;
using EmbedIO.Security;
using EmbedIO.WebApi;
using Swan.Logging;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Controller;
using AgentHub.Server.Socket;
using static AgentHub.Common.Constants;

namespace AgentHub.Server
{
    public static class EmbedIOServer
    {
        private static WebServer _server;
        private static CancellationTokenSource _cts;

        public static bool IsRunning => _server != null && _server.State == WebServerState.Listening;
        public static int CurrentPort { get; private set; }

        /// <summary>표시/접속용 호스트 — 사설망(LAN) IPv4. 없으면 127.0.0.1.</summary>
        public static string CurrentHost { get; private set; } = "127.0.0.1";

        public static string CurrentUrl => $"https://{CurrentHost}:{CurrentPort}";

        /// <summary>서버 재시작(포트 변경 등) 완료 후 발생. 구독자는 새 <see cref="CurrentUrl"/>로 재이동할 수 있다.</summary>
        public static event Action Restarted;

        /// <summary>연결된 모든 네트워크 인터페이스의 사설 IPv4 목록(10.x / 172.16-31.x / 192.168.x).</summary>
        private static List<string> GetPrivateIPv4List()
        {
            var result = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var b = ua.Address.GetAddressBytes();
                        var isPrivate = b[0] == 10
                            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                            || (b[0] == 192 && b[1] == 168);
                        if (isPrivate) result.Add(ua.Address.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
            }
            return result;
        }

        private static bool CertCoversHost(X509Certificate2 cert, string host)
        {
            try
            {
                foreach (var ext in cert.Extensions)
                    if (ext.Oid?.Value == "2.5.29.17") // Subject Alternative Name
                        return ext.Format(false).IndexOf(host, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { /* ignore */ }
            return false;
        }

        private static X509Certificate2 GetSelfSignedCertificate(List<string> privateIps)
        {
            try
            {
                var pfxFilePathName = Path.Combine(SelfSigned.CertFilePath, SelfSigned.PfxFileName);
                var certPw = GetCertPassword();

                // 캐시된 인증서가 현재 호스트(LAN IP)를 SAN에 포함하면 재사용, 아니면 재발급.
                if (File.Exists(pfxFilePathName))
                {
                    try
                    {
                        var cached = new X509Certificate2(pfxFilePathName, certPw);
                        if (CertCoversHost(cached, CurrentHost)) return cached;
                    }
                    catch { /* 손상/불일치 → 재발급 */ }
                }

                using var rsa = RSA.Create(2048);
                var request = new CertificateRequest("CN=AgentHub, O=AgentHub, C=KR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddIpAddress(IPAddress.Loopback);   // 127.0.0.1
                sanBuilder.AddDnsName("localhost");
                foreach (var ip in privateIps)
                    if (IPAddress.TryParse(ip, out var addr)) sanBuilder.AddIpAddress(addr);
                request.CertificateExtensions.Add(sanBuilder.Build());

                var certificate = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(10));
                var certRawData = certificate.Export(X509ContentType.Pfx, certPw);

                if (!Directory.Exists(SelfSigned.CertFilePath)) Directory.CreateDirectory(SelfSigned.CertFilePath);
                // 기존 pfx가 Hidden 속성이면 덮어쓰기(WriteAllBytes)가 거부되므로 먼저 속성 해제.
                if (File.Exists(pfxFilePathName)) File.SetAttributes(pfxFilePathName, FileAttributes.Normal);
                File.WriteAllBytes(pfxFilePathName, certRawData);
                File.SetAttributes(pfxFilePathName, File.GetAttributes(pfxFilePathName) | FileAttributes.Hidden);

                var x509Certificate2 = new X509Certificate2(certRawData, certPw);
                File.WriteAllBytes(Path.Combine(SelfSigned.CertFilePath, SelfSigned.CrtFileName), x509Certificate2.Export(X509ContentType.Cert));

                using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
                {
                    store.Open(OpenFlags.ReadWrite);
                    // 기존 AgentHub 인증서를 제거해 누적을 막고 최신 것 1개만 유지.
                    var existing = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, x509Certificate2.Subject, false);
                    if (existing.Count > 0) store.RemoveRange(existing);
                    store.Add(x509Certificate2);
                    store.Close();
                }

                return x509Certificate2;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                throw;
            }
        }

        /// <summary>인증서 pfx 보호용 비밀번호. 최초 1회 생성해 사용자 설정에 저장·재사용(하드코딩 없음).</summary>
        private static string GetCertPassword()
        {
            var pw = Properties.Settings.Default.ServerCertPassword;
            if (string.IsNullOrEmpty(pw))
            {
                pw = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                Properties.Settings.Default.ServerCertPassword = pw;
                Properties.Settings.Default.Save();
            }
            return pw;
        }

        private static int ResolvePort()
        {
            var configured = Properties.Settings.Default.ServerPort;
            var active = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Select(p => p.Port).ToHashSet();

            if (configured >= 1024 && configured <= 65535 && !active.Contains(configured))
                return configured;

            for (var port = 8000; port <= 9000; port++)
                if (!active.Contains(port)) return port;

            throw new Exception("사용 가능한 포트를 찾을 수 없습니다.");
        }

        public static void StartServer()
        {
            try
            {
                CurrentPort = ResolvePort();

                var privateIps = GetPrivateIPv4List();
                CurrentHost = privateIps.Count > 0 ? privateIps[0] : "127.0.0.1";

                var certificate = GetSelfSignedCertificate(privateIps);
                var htmlPath = Path.Combine(Application.StartupPath, "View", "Htmls");

                // 전체 인터페이스에 바인딩(localhost + LAN). 표시 URL은 CurrentUrl(사설 IP).
                var bindPrefix = $"https://+:{CurrentPort}/";
                var options = new WebServerOptions()
                    .WithUrlPrefix(bindPrefix)
                    .WithCertificate(certificate)
                    .WithMode(HttpListenerMode.EmbedIO);

                var agentModule = new AgentMonitorModule("/ws/agents");

                _cts = new CancellationTokenSource();
                _server = new WebServer(options)
                    .WithIPBanning(o => o
                        .WithMaxRequestsPerSecond(100)
                        .WithRegexRules(100, 60, "HTTP exception 404"))
                    .WithLocalSessionManager()
                    .WithCors()
                    .WithWebApi("/api", m => m.WithController<ApiController>())
                    .WithModule(agentModule)
                    .WithModule(new HostMonitorModule("/ws/host"))
                    // 정적 SPA (반드시 마지막 — "/"는 catch-all). "/" -> index.html, "/host" -> host.html
                    .WithStaticFolder("/", htmlPath, false, m =>
                    {
                        m.WithContentCaching(false);
                        m.DefaultExtension = ".html";
                    });

                _server.StateChanged += (s, e) => $"WebServer New State - {e.NewState}".Info("WebServer");
                _server.RunAsync(_cts.Token).ConfigureAwait(false);

                AgentMonitorService.Start(agentModule);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                throw;
            }
        }

        public static void StopServer()
        {
            try
            {
                AgentMonitorService.Stop();
                _cts?.Cancel();
                _server?.Dispose();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
            }
            finally
            {
                _server = null;
                _cts = null;
            }
        }

        public static void RestartServer()
        {
            StopServer();
            Thread.Sleep(300);
            StartServer();
            Restarted?.Invoke();
        }
    }
}
