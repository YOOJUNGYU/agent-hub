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
            AppSettingsProvider.ApplyProvider(Properties.Settings.Default);
            Application.Run(new FormMain());
        }
    }
}
