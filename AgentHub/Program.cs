using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AgentHub.Common;
using AgentHub.Common.Util;
using AgentHub.View.Forms;

namespace AgentHub
{
    internal static class Program
    {
        // ReSharper disable UnusedMember.Local
        private enum ProcessDpiAwareness
        {
            ProcessDpiUnaware = 0,         // DPI를 인식하지 않음 (고정된 해상도)
            ProcessSystemDpiAware = 1,     // 시스템 DPI를 따름
            ProcessPerMonitorDpiAware = 2  // 모니터별 DPI를 따름
        }

        [DllImport("Shcore.dll")]
        private static extern int SetProcessDpiAwareness(ProcessDpiAwareness value);

        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // Velopack 설치/업데이트/제거 훅 — 반드시 다른 어떤 코드보다 먼저.
            Velopack.VelopackApp.Build().Run();

            // 전역 예외 그물 — 처리되지 않은 예외로 프로세스가 조용히 죽는 것을 막고 항상 로그를 남긴다.
            // (백그라운드 스레드/async void에서 새어 나온 예외는 로그가 없으면 원인 규명이 불가능하다.)
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                LogService.Instance.Error("처리되지 않은 예외(프로세스 종료 위험)", e.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            { LogService.Instance.Error("관측되지 않은 Task 예외", e.Exception); e.SetObserved(); };
            Application.ThreadException += (s, e) =>
                LogService.Instance.Error("UI 스레드 예외(무시하고 계속)", e.Exception);

            SetProcessDpiAwareness(ProcessDpiAwareness.ProcessSystemDpiAware);

            //이미 프로그램이 실행 중 일때...
            var strCurrentProcess = Process.GetCurrentProcess().ProcessName.ToUpper();
            var processes = Process.GetProcessesByName(strCurrentProcess);
            if (processes.Length > 1)
            {
                MessageBox.Show(Constants.Messages.AlreadyExecuted);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); // UI 스레드 예외 → ThreadException 핸들러로
            AppSettingsProvider.ApplyProvider(Properties.Settings.Default);
            // Windows 로그인 시 자동 실행을 설정값에 맞춰 동기화(설치본에서만, 기본 켜짐).
            AutoStartService.SyncOnStartup(Properties.Settings.Default.AutoStart);
            Application.Run(new FormMain());
        }
    }
}
