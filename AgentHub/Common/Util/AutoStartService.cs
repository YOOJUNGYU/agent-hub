using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AgentHub.Common.Util
{
    /// <summary>
    /// Windows 로그인 시 자동 실행 등록/해제(HKCU Run 키)와 agenthub:// 주소 등록. 관리자 권한 불필요.
    /// </summary>
    public static class AutoStartService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AgentHub";
        /// <summary>로그인 자동 시작 표시 — 이 인자로 뜨면 화면을 열지 않고 트레이에만 상주한다.</summary>
        public const string Argument = "--autostart";

        /// <summary>
        /// agenthub:// — 서버가 꺼진 채 PWA를 열면 화면이 이 주소를 불러 Windows가 AgentHub를 켠다
        /// (브라우저가 처음 한 번 '열까요?'를 묻는다). 주소 내용은 쓰지 않는다 — 서버를 켜기만 한다.
        /// </summary>
        public const string UrlScheme = "agenthub";
        private const string SchemeKey = @"Software\Classes\" + UrlScheme;

        // 현재 실행 파일 경로. Velopack은 ...\AgentHub\current\ 경로가 업데이트 후에도 유지된다.
        private static string ExePath => Process.GetCurrentProcess().MainModule?.FileName;

        /// <summary>설정값에 맞춰 자동 실행을 등록/해제한다. 성공 여부 반환.</summary>
        public static bool Apply(bool enabled)
            => enabled ? Enable() : Disable();

        /// <summary>설치본(Velopack)에서만 시작 시 설정값을 반영한다(개발/미설치 환경은 skip).</summary>
        public static void SyncOnStartup(bool enabled)
        {
            if (!IsPackaged()) return;
            Apply(enabled);
            RegisterUrlScheme();
        }

        /// <summary>이 실행 파일로 agenthub:// 를 연다(매 시작마다 경로 갱신).</summary>
        private static void RegisterUrlScheme()
        {
            try
            {
                var path = ExePath;
                if (string.IsNullOrEmpty(path)) return;
                using var key = Registry.CurrentUser.CreateSubKey(SchemeKey);
                key.SetValue("", "URL:Agent Hub");
                key.SetValue("URL Protocol", "");
                using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + path + "\",0");
                using (var cmd = key.CreateSubKey(@"shell\open\command")) cmd.SetValue("", "\"" + path + "\" \"%1\"");
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        /// <summary>제거 시: 자동 실행과 agenthub:// 등록을 지운다(Velopack 제거 훅).</summary>
        public static void RemoveAll()
        {
            Disable();
            try { Registry.CurrentUser.DeleteSubKeyTree(SchemeKey, throwOnMissingSubKey: false); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private static bool Enable()
        {
            try
            {
                var path = ExePath;
                if (string.IsNullOrEmpty(path)) return false;
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKey);
                // 경로에 공백이 있어도 안전하도록 따옴표로 감싼다.
                key?.SetValue(ValueName, "\"" + path + "\" " + Argument, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        private static bool Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key?.GetValue(ValueName) != null) key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        // Velopack 설치 레이아웃(...\AgentHub\current\AgentHub.exe, 상위에 Update.exe) 여부.
        private static bool IsPackaged()
        {
            try
            {
                var exe = ExePath;
                if (string.IsNullOrEmpty(exe)) return false;
                var dir = Path.GetDirectoryName(exe);
                var parent = string.IsNullOrEmpty(dir) ? null : Directory.GetParent(dir)?.FullName;
                return parent != null && File.Exists(Path.Combine(parent, "Update.exe"));
            }
            catch { return false; }
        }
    }
}
