using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using AgentHub.Common.Util;

namespace AgentHub.Server.Hook
{
    /// <summary>~/.claude/settings.json에 Agent Hub Notification 훅을 백업·멱등 설치/제거(I/O).</summary>
    public static class HookInstaller
    {
        private const string Marker = "agenthub-hook.js";

        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        private static string ScriptPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hook", "agenthub-hook.js");

        public static bool IsInstalled()
        {
            try { return HookConfigMerger.IsInstalled(ReadSettings(), Marker); }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Install()
        {
            try
            {
                var entry = new JObject
                {
                    ["matcher"] = "",
                    ["hooks"] = new JArray { new JObject
                    {
                        ["type"] = "command",
                        ["command"] = ResolveNode(),
                        ["args"] = new JArray { ScriptPath },
                        ["async"] = true,
                        ["timeout"] = 5
                    }}
                };
                var merged = HookConfigMerger.AddNotificationHook(ReadSettings(), entry, Marker);
                WriteSettingsWithBackup(merged);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Uninstall()
        {
            try
            {
                var removed = HookConfigMerger.RemoveNotificationHook(ReadSettings(), Marker);
                WriteSettingsWithBackup(removed);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        private static string ReadSettings()
            => File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";

        private static void WriteSettingsWithBackup(string content)
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(SettingsPath))
                File.Copy(SettingsPath, SettingsPath + ".agenthub.bak", true);
            var tmp = SettingsPath + ".agenthub.tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            File.Move(tmp, SettingsPath);
        }

        private static string ResolveNode()
        {
            var pf = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(pf))
            {
                var p = Path.Combine(pf, "nodejs", "node.exe");
                if (File.Exists(p)) return p;
            }
            try
            {
                var psi = new ProcessStartInfo("where", "node")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var proc = Process.Start(psi))
                {
                    var outp = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                    var first = (outp ?? "").Split('\n')[0].Trim();
                    if (!string.IsNullOrEmpty(first) && File.Exists(first)) return first;
                }
            }
            catch { /* fall through */ }
            return "node"; // PATH 폴백
        }
    }
}
