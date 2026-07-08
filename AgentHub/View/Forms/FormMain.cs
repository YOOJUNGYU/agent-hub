using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgentHub.Common.Util;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Swan.Logging;
using AgentHub.Server;
using AgentHub.Common.Models;
using AgentHub.Server.Devices;
using static AgentHub.Common.Constants;

namespace AgentHub.View.Forms
{
    public partial class FormMain : Form, ILogger
    {
        #region titleBar doubleClick
        private DateTime _lastClick;
        private bool _inDoubleClick;
        private Rectangle _doubleClickArea;
        private TimeSpan _doubleClickMaxTime;
        private Action _doubleClickMaximize;
        private Action _doubleClickRestore;
        private Timer _clickTimer;
        #endregion

        #region window State
        private enum CustomWindowState
        {
            Restored,
            Maximized
        }
        private CustomWindowState _customWindowState;
        private Point _windowRestorePoint;
        private int _windowRestoreHeight;
        private int _windowRestoreWidth;
        #endregion

        #region resize
        private int _oldLocationX;
        private int _oldLocationY;
        private int _oldWidth;
        private int _oldHeight;
        private bool _resizeRight;
        private bool _resizeLeft;
        private bool _resizeTop;
        private bool _resizeBottom;
        #endregion

        private NotifyIcon _notify;
        private MenuItem _updateMenuItem;
        private bool _isExiting;
        private bool _updateReady;
        private Panel _loadingOverlay;
        private Label _loadingLabel;

        public FormMain()
            => InitializeComponent();

        private async void InitializeControl()
        {
            ApiLogger.Initialize();
            Logger.RegisterLogger(this);

            SetVersionInfo();
            InitTrayMenu();
            LoadSetting();
            ControlBox = false;
            ActiveControl = lblTitle;

            // 로딩 표시를 먼저 띄운다(검은 화면 방지).
            ShowLoading("서버 시작 중…");
            // 실행 시 창을 보여준다(트레이로 바로 숨지 않음 — 실행 확인 편의).
            SetShowWindow(true);

            // EmbedIO 서버를 먼저 시작한 뒤 호스트 콘솔(/host)을 로드한다.
            EmbedIOServer.StartServer();
            DeviceRegistry.StatusChanged += (hash, status) =>
            {
                if (status != DeviceStatus.Pending || IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() => _notify?.ShowBalloonTip(
                        5000, "Agent Hub", "새 기기 인증 요청이 도착했습니다.", ToolTipIcon.Info)));
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            };

            await webViewServer.EnsureCoreWebView2Async();
            var core = webViewServer.CoreWebView2;

            // 로컬 자체서명 인증서 허용 (HTTPS 유지)
            core.ServerCertificateErrorDetected += (s, e) =>
                e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;

            // 접속 URL 등 외부 링크는 시스템 기본 브라우저로 연다.
            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                try { Process.Start(e.Uri); }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            };

            // 페이지 로드가 끝나면 로딩 표시를 감추고 화면을 보여준다.
            core.NavigationCompleted += (s, e) => HideLoading();

            // 포트 변경 등으로 서버가 재시작되면 로딩 표시 후 새 주소로 다시 이동한다.
            EmbedIOServer.Restarted += () =>
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        ShowLoading("서버 재시작 중…");
                        webViewServer.CoreWebView2?.Navigate($"{EmbedIOServer.LocalUrl}/host");
                    }));
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            };

            ShowLoading("화면 불러오는 중…");
            core.Navigate($"{EmbedIOServer.LocalUrl}/host");

            // 백그라운드 자동 업데이트 확인(설치본에서만). 결과에 따라 트레이 메뉴 표시 갱신.
            _ = UpdateService.CheckAndDownloadAsync().ContinueWith(t =>
            {
                if (t.IsFaulted || IsDisposed) return;
                var result = t.Result;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (result == UpdateService.CheckResult.UpdateReady)
                        {
                            _updateReady = true;
                            if (_updateMenuItem != null) { _updateMenuItem.Text = "지금 업데이트 후 재시작"; _updateMenuItem.Enabled = true; }
                            _notify?.ShowBalloonTip(5000, "Agent Hub",
                                "새 버전이 준비되었습니다. 재시작 시 적용됩니다.", ToolTipIcon.Info);
                        }
                        else if (_updateMenuItem != null)
                        {
                            // 최신 버전(UpToDate) 또는 확인 불가(Unavailable) → 재시작 버튼 대신 상태만 표시.
                            _updateMenuItem.Text = result == UpdateService.CheckResult.UpToDate ? "최신 버전입니다" : "업데이트 확인 불가";
                            _updateMenuItem.Enabled = false;
                        }
                    }));
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            });
        }

        // WebView2는 airspace 특성상 WinForms 패널로 덮이지 않으므로,
        // 로딩 중엔 WebView를 숨기고 오버레이만 표시한다.
        private void ShowLoading(string text)
        {
            if (_loadingOverlay == null)
            {
                _loadingOverlay = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(18, 20, 31)
                };
                _loadingLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Color.FromArgb(230, 233, 242),
                    Font = new Font("맑은 고딕", 12F, FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _loadingOverlay.Controls.Add(_loadingLabel);
                pnlMainCenter.Controls.Add(_loadingOverlay);
            }
            _loadingLabel.Text = text;
            webViewServer.Visible = false;
            _loadingOverlay.Visible = true;
            _loadingOverlay.BringToFront();
        }

        private void HideLoading()
        {
            webViewServer.Visible = true;
            if (_loadingOverlay != null) _loadingOverlay.Visible = false;
            webViewServer.BringToFront();
        }

        private void SetVersionInfo()
            => lblVersionInfo.Text = $@"version: {EtcUtil.GetFileVersionInfo().FileVersion} ({EtcUtil.GetBuildDateTime():yyyy-MM-dd})";

        private void LoadSetting()
        {
            var savedLocation = new Point(Properties.Settings.Default.FormMainX, Properties.Settings.Default.FormMainY);
            Width = Properties.Settings.Default.FormMainWidth;
            Height = Properties.Settings.Default.FormMainHeight;
            Location = ViewUtil.IsLocationInWorkingArea(savedLocation, Width, Height) ? savedLocation : new Point(0, 0);
            var bound = Screen.FromHandle(Handle).WorkingArea;
            if (Width < bound.Width || Height < bound.Height)
            {
                btnWindowMaximize.BringToFront();
                _customWindowState = CustomWindowState.Restored;
            }
            else
            {
                btnWindowRestore.BringToFront();
                _customWindowState = CustomWindowState.Maximized;
            }
        }

        private void InitTrayMenu()
        {
            var menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem("열기", (s, e) => SetShowWindow(true)));
            // 초기: 확인 중. 확인 완료 후 결과에 따라 "지금 업데이트 후 재시작"/"최신 버전입니다"로 갱신.
            _updateMenuItem = new MenuItem("업데이트 확인 중…", (s, e) => { if (_updateReady) UpdateService.ApplyAndRestart(); }) { Enabled = false };
            menu.MenuItems.Add(_updateMenuItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("완전 종료", (s, e) => ExitApplication()));

            _notify = new NotifyIcon
            {
                Icon = Properties.Resources.trayicon_32x32,
                Visible = true,
                ContextMenu = menu,
                Text = ProgramInfo.KoreanName
            };
            _notify.DoubleClick += Notify_DoubleClick;
        }

        private void ExitApplication()
        {
            _isExiting = true;
            try { EmbedIOServer.StopServer(); }
            catch (Exception ex) { LogService.Instance.Error(ex); }

            if (_notify != null)
            {
                _notify.Visible = false;
                _notify.Dispose();
                _notify = null;
            }
            Application.Exit();
        }

        private void Notify_DoubleClick(object sender, EventArgs e)
            => SetShowWindow(true);
        
        private void SetShowWindow(bool isShow)
        {
            Opacity = isShow ? 100 : 0;
            ShowInTaskbar = isShow;
            var process = Process.GetCurrentProcess();
            Win32.ShowWindow(process.MainWindowHandle, isShow ? 1 : 0);
            SavePosition();
            if (isShow) WindowState = FormWindowState.Normal;
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            try
            {
                InitializeControl();
                InitializeMouseClick();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
            }
        }

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

                    var logMessage = logEvent.Message;
                    if (string.IsNullOrEmpty(logMessage)) return;

                    if (logEvent.MessageType == LogLevel.Debug || logEvent.MessageType == LogLevel.Trace)
                    {
                        if (!(logMessage.IndexOf("ANY", StringComparison.OrdinalIgnoreCase) >= 0
                              || logMessage.IndexOf("DELETE", StringComparison.OrdinalIgnoreCase) >= 0
                              || logMessage.IndexOf("GET", StringComparison.OrdinalIgnoreCase) >= 0
                              || logMessage.IndexOf("HEAD", StringComparison.OrdinalIgnoreCase) >= 0
                              || logMessage.IndexOf("OPTIONS", StringComparison.OrdinalIgnoreCase) >= 0
                              || logMessage.IndexOf("PATCH", StringComparison.OrdinalIgnoreCase) >= 0
                              || logMessage.IndexOf("POST", StringComparison.OrdinalIgnoreCase) >= 0
                              || logMessage.IndexOf("PUT", StringComparison.OrdinalIgnoreCase) >= 0))
                            return;
                    }

                    if (InvokeRequired)
                    {
                        Invoke((Action)(() => { WriteLog(logEvent); }));
                    }
                    else
                    {
                        WriteLog(logEvent);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error(ex);
                }
            });
        }

        private void WriteLog(LogMessageReceivedEventArgs logEvent)
        {
            try
            {
                webViewServer.CoreWebView2?.ExecuteScriptAsync($"addLog({JsonConvert.SerializeObject(logEvent)})");
                string msgType;
                switch (logEvent.MessageType)
                {
                    case LogLevel.Debug:
                        msgType = "Request";
                        break;
                    case LogLevel.Info:
                        msgType = "Response";
                        break;
                    default:
                        msgType = logEvent.MessageType.ToString();
                        break;
                }
                ApiLogger.Info($"{logEvent.Sequence}. [{logEvent.UtcDate.ToLocalTime():yyyy-MM-dd(ddd) HH:mm:ss.fff}] [{msgType}] {logEvent.Message}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
            }
        }

        #region titleBar
        private void pnlTitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (_inDoubleClick)
            {
                _inDoubleClick = false;
                var length = DateTime.UtcNow - _lastClick;
                if (!_doubleClickArea.Contains(e.Location) || length >= _doubleClickMaxTime) return;
                _clickTimer.Stop();
                switch (_customWindowState)
                {
                    case CustomWindowState.Restored:
                        _doubleClickMaximize();
                        break;
                    case CustomWindowState.Maximized:
                        _doubleClickRestore();
                        break;
                }
                return;
            }
            _clickTimer.Stop();
            _clickTimer.Start();
            _lastClick = DateTime.UtcNow;
            _inDoubleClick = true;
            _doubleClickArea = new Rectangle(e.Location, SystemInformation.DoubleClickSize);
            btnWindowMaximize.BringToFront();
            Win32.ReleaseCapture();
            Win32.SendMessage(Handle, 0x112, 0xf012, 0);
        }

        private void btnWindowMinimize_Click(object sender, EventArgs e)
            => WindowState = FormWindowState.Minimized;

        private void btnWindowRestore_Click(object sender, EventArgs e)
            => FormRestore();

        private void btnWindowMaximize_Click(object sender, EventArgs e)
            => FormMaximize();

        private void btnWindowClose_Click(object sender, EventArgs e)
            => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                SetShowWindow(false);
                _notify?.ShowBalloonTip(3000, ProgramInfo.Name, Messages.TrayProgram, ToolTipIcon.Info);
            }
            base.OnClosing(e);
        }

        private void ClickTimer_Tick(object sender, EventArgs e)
        {
            _inDoubleClick = false;
            _clickTimer.Stop();
        }

        private void InitializeMouseClick()
        {
            _customWindowState = CustomWindowState.Restored;
            _doubleClickMaxTime = TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime);

            _clickTimer = new Timer { Interval = SystemInformation.DoubleClickTime };
            _clickTimer.Tick += ClickTimer_Tick;

            _doubleClickMaximize = FormMaximize;
            _doubleClickRestore = FormRestore;
        }

        private void SavePosition()
        {
            Properties.Settings.Default.FormMainX = Location.X;
            Properties.Settings.Default.FormMainY = Location.Y;
            Properties.Settings.Default.FormMainHeight = Height;
            Properties.Settings.Default.FormMainWidth = Width;
            Properties.Settings.Default.Save();
        }

        private void FormMaximize()
        {
            _windowRestorePoint = Location;
            _windowRestoreHeight = Height;
            _windowRestoreWidth = Width;

            var bounds = Screen.FromHandle(Handle).WorkingArea;
            Location = bounds.Location;
            Width = bounds.Width;
            Height = bounds.Height;
            btnWindowRestore.BringToFront();
            _customWindowState = CustomWindowState.Maximized;
            SavePosition();
        }

        private void FormRestore()
        {
            Location = _windowRestorePoint;
            Height = _windowRestoreHeight;
            Width = _windowRestoreWidth;
            btnWindowMaximize.BringToFront();
            _customWindowState = CustomWindowState.Restored;
            SavePosition();
        }
#endregion

#region Resize
        private void pnlResizeBorder_MouseLeave(object sender, EventArgs e)
            => Cursor = Cursors.Default;


#region ResizeTop
        private void pnlResizeBorderTop_MouseHover(object sender, EventArgs e)
            => Cursor = Cursors.SizeNS;

        private void pnlResizeBorderTop_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizeTop = true;
            _oldLocationY = Location.Y;
            _oldHeight = Height;
        }

        private void pnlResizeBorderTop_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizeTop = false;
            Cursor = Cursors.Default;
        }

        private void pnlResizeBorderTop_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizeTop) return;
            Height = _oldHeight + _oldLocationY - Cursor.Position.Y;
            if (Height <= MinimumSize.Height) return;
            Location = new Point(Location.X, Cursor.Position.Y);
        }
#endregion

#region ResizeBottom
        private void pnlResizeBorderBottom_MouseHover(object sender, EventArgs e)
            => Cursor = Cursors.SizeNS;

        private void pnlResizeBorderBottom_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizeBottom = true;
        }

        private void pnlResizeBorderBottom_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizeBottom = false;
            Cursor = Cursors.Default;
        }

        private void pnlResizeBorderBottom_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizeBottom) return;
            Height = Cursor.Position.Y - Location.Y;
        }
#endregion

#region ResizeLeft
        private void pnlResizeBorderLeft_MouseHover(object sender, EventArgs e)
            => Cursor = Cursors.SizeWE;

        private void pnlResizeBorderLeft_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizeLeft = true;
            _oldLocationX = Location.X;
            _oldWidth = Width;
        }

        private void pnlResizeBorderLeft_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizeLeft = false;
            Cursor = Cursors.Default;
        }

        private void pnlResizeBorderLeft_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizeLeft) return;
            Width = _oldWidth + _oldLocationX - Cursor.Position.X;
            if (Width <= MinimumSize.Width) return;
            Location = new Point(Cursor.Position.X, Location.Y);
        }

#endregion

#region ResizeRight
        private void pnlResizeBorderRight_MouseHover(object sender, EventArgs e)
            => Cursor = Cursors.SizeWE;

        private void pnlResizeBorderRight_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizeRight = true;
        }

        private void pnlResizeBorderRight_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizeRight = false;
            Cursor = Cursors.Default;
        }

        private void pnlResizeBorderRight_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizeRight) return;
            Width = Cursor.Position.X - Location.X;
        }
        #endregion

        #endregion
    }
}
