using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AgentHub.Common.Util
{
    /// <summary>
    /// Windows 로그인 시 자동 실행 등록/해제(HKCU Run 키). 관리자 권한 불필요.
    /// </summary>
    public static class AutoStartService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AgentHub";

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
                key?.SetValue(ValueName, "\"" + path + "\"", RegistryValueKind.String);
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
