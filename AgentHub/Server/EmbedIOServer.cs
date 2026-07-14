using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EmbedIO;
using EmbedIO.Files;
using EmbedIO.Security;
using EmbedIO.WebApi;
using Swan.Logging;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Controller;
using AgentHub.Server.Devices;
using AgentHub.Server.Socket;
using static AgentHub.Common.Constants;

namespace AgentHub.Server
{
    public static class EmbedIOServer
    {
        private static WebServer _server;
        private static WebServer _certServer; // 인증서(.crt) 평문 HTTP 부트스트랩 전용(삭제/만료 후 재설치)
        private static CancellationTokenSource _cts;

        public static bool IsRunning => _server != null && _server.State == WebServerState.Listening;
        public static int CurrentPort { get; private set; }

        /// <summary>인증서 평문 HTTP 부트스트랩 포트(자체서명이 깨져 HTTPS로 못 받을 때 .crt 재설치용). 0=비활성.</summary>
        public static int CurrentCertHttpPort { get; private set; }

        /// <summary>표시/접속용 호스트 — 사설망(LAN) IPv4. 없으면 127.0.0.1.</summary>
        public static string CurrentHost { get; private set; } = "127.0.0.1";

        public static string CurrentUrl => $"https://{CurrentHost}:{CurrentPort}";

        /// <summary>PC(호스트 콘솔) 전용 loopback URL. WebView2가 이 주소로 /host를 로드한다.</summary>
        public static string LocalUrl => $"https://127.0.0.1:{CurrentPort}";

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

        /// <summary>
        /// 인증서 pfx 보호용 비밀번호. LocalAppData\AgentHub\Certificate\cert.pw 에 영속한다.
        /// .NET 사용자 설정(user.config)은 설치 경로·버전별로 폴더가 갈려 재설치·업데이트 시 값이 사라지고,
        /// 그러면 남아있는 pfx를 복호화하지 못해 인증서가 재발급된다(폰의 신뢰가 깨짐). 파일로 영속하면
        /// 경로/버전이 바뀌어도 같은 pfx를 계속 복호화·재사용해 인증서와 기기 승인이 그대로 유지된다.
        /// 최초 실행 시 구버전의 사용자 설정 값이 있으면 마이그레이션해 기존 인증서를 보존한다.
        /// </summary>
        private static string GetCertPassword()
        {
            var pwFile = Path.Combine(SelfSigned.CertFilePath, "cert.pw");
            try
            {
                if (File.Exists(pwFile))
                {
                    var saved = File.ReadAllText(pwFile).Trim();
                    if (!string.IsNullOrEmpty(saved)) return saved;
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }

            // 영속 파일 없음 → 구버전 사용자 설정 값 마이그레이션, 없으면 신규 생성.
            var pw = Properties.Settings.Default.ServerCertPassword;
            if (string.IsNullOrEmpty(pw))
            {
                pw = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                try { Properties.Settings.Default.ServerCertPassword = pw; Properties.Settings.Default.Save(); }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
            try
            {
                if (!Directory.Exists(SelfSigned.CertFilePath)) Directory.CreateDirectory(SelfSigned.CertFilePath);
                if (File.Exists(pwFile)) File.SetAttributes(pwFile, FileAttributes.Normal); // Hidden이면 덮어쓰기 거부되므로 해제
                File.WriteAllText(pwFile, pw);
                File.SetAttributes(pwFile, File.GetAttributes(pwFile) | FileAttributes.Hidden);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return pw;
        }

        private static int ResolvePort()
        {
            var configured = Properties.Settings.Default.ServerPort;
            var active = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Select(p => p.Port).ToHashSet();

            if (configured >= 1024 && configured <= 65535 && !active.Contains(configured))
                return configured;

            // 흔히 쓰이는 대역을 피해 조용한 47600번대에서 폴백 포트 선택.
            for (var port = 47600; port <= 47700; port++)
                if (!active.Contains(port)) return port;

            throw new Exception("사용 가능한 포트를 찾을 수 없습니다.");
        }

        public static void StartServer()
        {
            try
            {
                DeviceRegistry.Load();

                // 질문 알림 훅은 항상 설치(옵션 아님). 매 시작마다 멱등 설치 — HookConfigMerger가 중복을 제거한다.
                try { Hook.HookInstaller.Install(); }
                catch (Exception ex) { LogService.Instance.Error(ex); }

                CurrentPort = ResolvePort();

                try
                {
                    var hookDir = Path.Combine(Application.StartupPath, "hook");
                    if (!Directory.Exists(hookDir)) Directory.CreateDirectory(hookDir);
                    File.WriteAllText(Path.Combine(hookDir, "endpoint.txt"), CurrentPort.ToString());
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }

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
                    .WithModule(new TerminalModule("/ws/term"))
                    .WithModule(new SessionTerminalModule("/ws/session"))
                    // /host, /host.html 는 PC(loopback)에서만 접근 허용. 그 외는 정적 폴더로 통과.
                    .WithAction("/host", HttpVerbs.Any, GuardHostAsync)
                    .WithAction("/host.html", HttpVerbs.Any, GuardHostAsync)
                    // 서비스워커: 캐시 키({{VER}})를 자산 해시로 치환해 서빙 → 빌드마다 자동 무효화.
                    .WithAction("/sw.js", HttpVerbs.Any, ctx => ServeServiceWorkerAsync(ctx, htmlPath))
                    // 정적 SPA (반드시 마지막 — "/"는 catch-all). "/" -> index.html, "/host" -> host.html
                    .WithStaticFolder("/", htmlPath, false, m =>
                    {
                        m.WithContentCaching(false);
                        m.DefaultExtension = ".html";
                    });

                _server.StateChanged += (s, e) => $"WebServer New State - {e.NewState}".Info("WebServer");
                _server.RunAsync(_cts.Token).ConfigureAwait(false);

                // 인증서(.crt)는 메인 HTTPS 포트의 /api/cert로 받는다(클라이언트가 같은 포트에서 다운로드).
                // 별도 HTTP 부트스트랩 포트는 추가 방화벽/포트 승인을 유발하므로 비활성화한다.
                // (인증서가 깨진 경우에도 자체서명 특성상 브라우저 보안경고를 계속 진행하면 HTTPS로 재설치 가능.)
                // StartCertHttpServer();

                AgentMonitorService.Start(agentModule);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                throw;
            }
        }

        /// <summary>
        /// /sw.js 를 동적으로 서빙한다. 캐시 키 자리표시자({{VER}})를 View/Htmls 자산의
        /// 해시로 치환하므로, 빌드로 파일이 바뀌면 서비스워커 내용이 달라져 브라우저가
        /// 재설치하고 옛 캐시를 폐기한다(수동 버전 증가 불필요). 항상 no-store 로 내려
        /// 브라우저가 매번 최신 sw.js 를 비교하도록 한다.
        /// </summary>
        private static async Task ServeServiceWorkerAsync(EmbedIO.IHttpContext ctx, string htmlPath)
        {
            string content;
            try { content = File.ReadAllText(Path.Combine(htmlPath, "sw.js"), Encoding.UTF8); }
            catch { throw RequestHandler.PassThrough(); } // 없으면 정적 폴더로 위임

            content = content.Replace("{{VER}}", ComputeAssetVersion(htmlPath));

            ctx.Response.Headers.Set(HttpHeaderNames.CacheControl, "no-store, no-cache, must-revalidate");
            ctx.Response.Headers.Set(HttpHeaderNames.Pragma, "no-cache");
            await ctx.SendStringAsync(content, "text/javascript", Encoding.UTF8);
        }

        /// <summary>View/Htmls 하위 모든 파일의 (상대경로·수정시각·크기) 해시. 빌드로 자산이 갱신되면 값이 바뀐다.</summary>
        private static string ComputeAssetVersion(string htmlPath)
        {
            try
            {
                var files = Directory.GetFiles(htmlPath, "*", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                var sb = new StringBuilder();
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    sb.Append(fi.Name).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append('|').Append(fi.Length).Append(';');
                }
                using var md5 = MD5.Create();
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash, 0, 5).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                return "static";
            }
        }

        /// <summary>loopback이면 정적 폴더로 통과, 아니면 403.</summary>
        private static Task GuardHostAsync(EmbedIO.IHttpContext ctx)
        {
            if (NetUtil.IsLoopback(ctx.Request.RemoteEndPoint?.Address))
                throw RequestHandler.PassThrough();
            ctx.Response.StatusCode = 403;
            return ctx.SendStringAsync("Forbidden", "text/plain", Encoding.UTF8);
        }

        /// <summary>
        /// 인증서(.crt)를 평문 HTTP로도 서빙하는 최소 서버를 시작한다.
        /// 자체서명 인증서를 삭제/만료해 HTTPS 신뢰가 깨지면 앱/브라우저가 HTTPS로 .crt를 다시 받을 수 없다
        /// (닭-달걀). HTTP 부트스트랩으로 그 경로를 연다. 인증서는 공개 CA 공개키라 기밀정보는 아니나,
        /// HTTP는 무결성 보장이 없어 LAN 상 능동적 MITM에 이론적으로 취약 — 신뢰된 LAN 전용 편의 기능이다.
        /// 포트는 HTTPS 포트+1부터 빈 포트를 사용하며 /server/status로 노출한다. 실패해도 본 서버엔 영향 없음.
        /// </summary>
        private static void StartCertHttpServer()
        {
            try
            {
                CurrentCertHttpPort = ResolveCertHttpPort(CurrentPort);
                if (CurrentCertHttpPort <= 0) return;
                var prefix = $"http://+:{CurrentCertHttpPort}/";
                _certServer = new WebServer(o => o.WithUrlPrefix(prefix).WithMode(HttpListenerMode.EmbedIO))
                    .WithAction("/", HttpVerbs.Any, ServeCertAsync); // catch-all: 모든 경로에서 .crt 응답(/cert 포함)
                // bind는 "빈 포트 확인 → 실제 bind" 사이 레이스로 비동기 실패할 수 있다. 그 경우
                // 죽은 포트를 status로 광고하지 않도록 포트를 0으로 되돌린다(취소는 faulted 아님 → 해당 없음).
                _certServer.RunAsync(_cts.Token).ContinueWith(t =>
                {
                    LogService.Instance.Error(t.Exception);
                    CurrentCertHttpPort = 0;
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                CurrentCertHttpPort = 0;
                _certServer = null;
            }
        }

        private static int ResolveCertHttpPort(int httpsPort)
        {
            try
            {
                var active = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners().Select(p => p.Port).ToHashSet();
                for (var p = httpsPort + 1; p <= httpsPort + 10 && p <= 65535; p++)
                    if (!active.Contains(p)) return p;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return 0;
        }

        /// <summary>.crt 파일을 첨부(다운로드)로 응답. 평문 HTTP(인증 게이트 없음 — 공개키).</summary>
        private static async Task ServeCertAsync(EmbedIO.IHttpContext ctx)
        {
            var path = Path.Combine(SelfSigned.CertFilePath, SelfSigned.CrtFileName);
            if (!File.Exists(path))
            {
                ctx.Response.StatusCode = 404;
                await ctx.SendStringAsync("cert not found", "text/plain", Encoding.UTF8);
                return;
            }
            var bytes = File.ReadAllBytes(path);
            ctx.Response.ContentType = "application/x-x509-ca-cert";
            ctx.Response.Headers.Add(HttpHeaderNames.ContentDisposition, "attachment; filename=\"AgentHub.crt\"");
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        public static void StopServer()
        {
            try
            {
                AgentMonitorService.Stop();
                Socket.TerminalModule.DisableAllInstances();
                Socket.SessionTerminalModule.DisableAllInstances();
                _cts?.Cancel();
                try { _server?.Dispose(); } catch (Exception ex) { LogService.Instance.Error(ex); }
                try { _certServer?.Dispose(); } catch (Exception ex) { LogService.Instance.Error(ex); } // 앞 dispose 예외에도 반드시 정리
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
            }
            finally
            {
                _server = null;
                _certServer = null;
                CurrentCertHttpPort = 0;
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
