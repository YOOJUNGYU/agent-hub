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
                // 읽기 → 병합 → 쓰기 사이에 외부 프로세스(예: clawd-on-desk)가 settings.json을
                // 동시에 수정하면 나중에 쓰는 쪽이 이겨 그 변경이 유실될 수 있다(lost update).
                // 수동 설치 동작이라 best-effort로 감수한다(락 없음).
                var existing = ReadSettings();
                if (!IsWritable(existing)) return false;
                // Notification: 알림용(fire-and-forget, async).
                var notifyEntry = new JObject
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
                // PreToolUse: 위험 도구(파일/셸)의 권한을 폰에서 원격 승인. 블로킹(동기) — 결정을 반환해야 함.
                var permEntry = new JObject
                {
                    ["matcher"] = "Bash|Write|Edit|MultiEdit|NotebookEdit",
                    ["hooks"] = new JArray { new JObject
                    {
                        ["type"] = "command",
                        ["command"] = ResolveNode(),
                        ["args"] = new JArray { ScriptPath },
                        ["timeout"] = 120
                    }}
                };
                var merged = HookConfigMerger.AddHook(existing, "Notification", notifyEntry, Marker);
                merged = HookConfigMerger.AddHook(merged, "PreToolUse", permEntry, Marker);
                WriteSettingsWithBackup(merged);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Uninstall()
        {
            try
            {
                // Install()과 동일한 lost-update 가능성에 대한 주의 사항 참고.
                var existing = ReadSettings();
                if (!IsWritable(existing)) return false;
                var removed = HookConfigMerger.RemoveHook(existing, "Notification", Marker);
                removed = HookConfigMerger.RemoveHook(removed, "PreToolUse", Marker);
                WriteSettingsWithBackup(removed);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        private static string ReadSettings()
            => File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";

        /// <summary>내용이 비어있지 않은데 JSON으로 파싱되지 않으면 쓰기를 중단시킨다(데이터 유실 방지).</summary>
        private static bool IsWritable(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return true;
            try
            {
                JObject.Parse(content);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("settings.json 파싱 실패 — 훅 설치/제거 중단(파일 미변경)", ex);
                return false;
            }
        }

        private static void WriteSettingsWithBackup(string content)
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmp = SettingsPath + ".agenthub.tmp";
            File.WriteAllText(tmp, content);
            try
            {
                if (File.Exists(SettingsPath))
                    // File.Replace: tmp를 SettingsPath로 원자적으로 교체하고, 기존 파일은 .bak으로 이동.
                    // 삭제 후 이동 방식과 달리 중간에 크래시가 나도 settings.json이 사라지지 않는다.
                    File.Replace(tmp, SettingsPath, SettingsPath + ".agenthub.bak");
                else
                    File.Move(tmp, SettingsPath);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
                throw;
            }
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
