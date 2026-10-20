using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server;
using AgentHub.Server.Agents;
using AgentHub.Server.Devices;
using AgentHub.Server.Socket;
using Swan.Logging;
using static AgentHub.Common.Constants;

namespace AgentHub.View
{
    /// <summary>
    /// 창 없는 상주 앱: EmbedIO 서버 + 트레이 아이콘. 화면은 브라우저/PWA(https://127.0.0.1:포트/host)가 담당한다.
    /// 트레이: 열기(브라우저/PWA) · 업데이트 · 완전 종료. 질문·권한·기기 인증 요청은 트레이 알림으로도 알린다.
    /// </summary>
    public class TrayApp : ApplicationContext, ILogger
    {
        private readonly NotifyIcon _notify;
        private readonly MenuItem _updateMenuItem;
        private readonly Control _ui = new Control(); // UI 스레드 마샬링용(BeginInvoke)
        private bool _updateReady;
        // 마지막 트레이 알림(질문·권한)의 세션 — 풍선 클릭 시 그 세션 대화창을 연다.
        private string _attentionSessionId;

        /// <summary>PC 콘솔 주소. open=세션ID면 오피스가 그 세션 대화창을 연다.</summary>
        public static string HostUrl(string openSessionId = null) =>
            $"{EmbedIOServer.LocalUrl}/host"
            + (string.IsNullOrEmpty(openSessionId) ? "" : "?open=" + Uri.EscapeDataString(openSessionId));

        public TrayApp(bool openOnStart)
        {
            _ui.CreateControl();
            ApiLogger.Initialize();
            Logger.RegisterLogger(this);

            var menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem("열기", (s, e) => OpenInBrowser(HostUrl())));
            _updateMenuItem = new MenuItem("업데이트 확인 중…", (s, e) => { if (_updateReady) UpdateService.ApplyAndRestart(); }) { Enabled = false };
            menu.MenuItems.Add(_updateMenuItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("완전 종료", (s, e) => Exit()));
            _notify = new NotifyIcon
            {
                Icon = Properties.Resources.trayicon_32x32,
                Visible = true,
                ContextMenu = menu,
                Text = ProgramInfo.KoreanName
            };
            _notify.DoubleClick += (s, e) => OpenInBrowser(HostUrl());
            _notify.BalloonTipClicked += (s, e) =>
            {
                OpenInBrowser(HostUrl(_attentionSessionId)); // 기기 인증 풍선이면 콘솔만 연다
                _attentionSessionId = null;
            };

            PromptClawdOnDeskIfRunning();
            EmbedIOServer.StartServer();
            // 설정에서 포트를 바꿔 서버가 재시작되면 새 주소로 화면을 다시 연다.
            EmbedIOServer.Restarted += () => OpenInBrowser(HostUrl());

            DeviceRegistry.StatusChanged += (hash, status) =>
            {
                if (status != DeviceStatus.Pending) return;
                OnUi(() =>
                {
                    _attentionSessionId = null;
                    _notify.ShowBalloonTip(5000, "Agent Hub", "새 기기 인증 요청이 도착했습니다. 클릭하면 콘솔을 엽니다.", ToolTipIcon.Info);
                });
            };
            // 질문·권한은 트레이 알림으로도(브라우저가 닫혀 있어도 놓치지 않게). 입력 대기(ask)는 턴마다 와서 제외.
            AgentMonitorService.Attention += (kind, project, sessionId) =>
            {
                if (kind == "ask") return;
                OnUi(() =>
                {
                    _attentionSessionId = sessionId;
                    var what = kind == "permission" ? "권한 승인 요청" : "질문";
                    _notify.ShowBalloonTip(8000, "Agent Hub", $"[{project}] {what}이(가) 도착했습니다. 클릭하면 대화창을 엽니다.", ToolTipIcon.Info);
                });
            };

            if (openOnStart) OpenInBrowser(HostUrl());
            CheckForUpdate();
        }

        /// <summary>기본 브라우저(설치한 PWA가 있으면 그 앱)로 연다.</summary>
        public static void OpenInBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private void OnUi(Action a)
        {
            try { _ui.BeginInvoke(a); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private void CheckForUpdate()
        {
            _ = UpdateService.CheckAndDownloadAsync().ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                var result = t.Result;
                OnUi(() =>
                {
                    if (result == UpdateService.CheckResult.UpdateReady)
                    {
                        _updateReady = true;
                        _updateMenuItem.Text = "지금 업데이트 후 재시작";
                        _updateMenuItem.Enabled = true;
                        _notify.ShowBalloonTip(5000, "Agent Hub", "새 버전이 준비되었습니다. 재시작 시 적용됩니다.", ToolTipIcon.Info);
                    }
                    else
                    {
                        _updateMenuItem.Text = result == UpdateService.CheckResult.UpToDate ? "최신 버전입니다" : "업데이트 확인 불가";
                        _updateMenuItem.Enabled = false;
                    }
                });
            });
        }

        // clawd-on-desk가 실행 중이면 안내하고, '예'면 종료 후 계속한다(이중 훅 응답 충돌 방지).
        private static void PromptClawdOnDeskIfRunning()
        {
            try
            {
                if (!ClawdGuard.IsRunning()) return;
                var r = MessageBox.Show(Messages.ClawdRunningPrompt, Messages.ClawdRunningTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
                if (r == DialogResult.Yes) ClawdGuard.KillAll();
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private void Exit()
        {
            try { EmbedIOServer.StopServer(); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            _notify.Visible = false;
            _notify.Dispose();
            ExitThread();
        }

        // ---- Swan 로그(요청/응답) → 파일 + 콘솔 로그 화면(/ws/host) ----
        public LogLevel LogLevel => LogLevel.None;

        public void Log(LogMessageReceivedEventArgs logEvent)
        {
            Task.Run(() =>
            {
                try
                {
                    if (logEvent.Message.Contains(Messages.UnhandledException)) return;
                    if (logEvent.Exception != null && !logEvent.Message.Contains(Messages.ExpireToken))
                        LogService.Instance.Error(logEvent.Exception);
                    var message = logEvent.Message;
                    if (string.IsNullOrEmpty(message)) return;
                    if ((logEvent.MessageType == LogLevel.Debug || logEvent.MessageType == LogLevel.Trace) && !IsHttpLine(message))
                        return;
                    var kind = logEvent.MessageType == LogLevel.Debug ? "Request"
                        : logEvent.MessageType == LogLevel.Info ? "Response" : logEvent.MessageType.ToString();
                    var line = $"{logEvent.Sequence}. [{logEvent.UtcDate.ToLocalTime():yyyy-MM-dd(ddd) HH:mm:ss.fff}] [{kind}] {message}";
                    ApiLogger.Info(line);
                    HostMonitorModule.PublishLog(line);
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            });
        }

        private static bool IsHttpLine(string m)
        {
            foreach (var verb in new[] { "ANY", "DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT" })
                if (m.IndexOf(verb, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>실행 중인 인스턴스의 콘솔 주소(두 번째 실행이 그 화면만 열고 끝낼 때). 훅용 endpoint.txt의 포트를 쓴다.</summary>
        public static string RunningHostUrl()
        {
            try
            {
                var f = Path.Combine(Application.StartupPath, "hook", "endpoint.txt");
                if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out var port))
                    return $"https://127.0.0.1:{port}/host";
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return $"https://127.0.0.1:{Properties.Settings.Default.ServerPort}/host";
        }
    }
}
