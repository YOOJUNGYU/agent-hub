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
        internal const string Marker = "agenthub-hook.js";

        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        internal static string ScriptPath =>
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
                var merged = Merge(existing, ResolveNode(), ScriptPath);
                WriteSettingsWithBackup(merged);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        /// <summary>
        /// 훅 5종(Notification·PermissionRequest·SessionStart·SessionEnd·Stop)을 settings.json에 멱등 병합해 반환.
        /// node 명령과 스크립트 경로만 다르면 그대로 재사용되므로 WSL 배포판 설치(WslHookInstaller)도 이 함수를 쓴다.
        /// </summary>
        internal static string Merge(string existing, string nodeCommand, string scriptPath)
        {
            // Notification: 알림용(fire-and-forget, async).
            var notifyEntry = new JObject
            {
                ["matcher"] = "",
                ["hooks"] = new JArray { new JObject
                {
                    ["type"] = "command",
                    ["command"] = nodeCommand,
                    ["args"] = new JArray { scriptPath },
                    ["async"] = true,
                    ["timeout"] = 5
                }}
            };
            // PermissionRequest: 승인이 필요한 모든 도구 호출 + AskUserQuestion(질문)을 폰에서 원격 응답. 블로킹(동기).
            // 도구 종류를 가리지 않아야(WebFetch·MCP 등) 사각지대가 없으므로 matcher는 전체("").
            // PreToolUse는 쓰지 않는다: 권한이 필요 없는 호출에도 발화해 유령 카드를 만들고 매 호출을 지연시킨다.
            var permReqEntry = new JObject
            {
                ["matcher"] = "",
                ["hooks"] = new JArray { new JObject
                {
                    ["type"] = "command",
                    ["command"] = nodeCommand,
                    // 두 번째 인자로 대기창(초)을 훅에 전달 → 훅이 서버를 기다리는 폴링 deadline로 사용.
                    ["args"] = new JArray { scriptPath, RemoteAnswerConfig.WindowSeconds.ToString() },
                    ["timeout"] = RemoteAnswerConfig.WindowSeconds
                }}
            };
            // SessionStart: 세션 시작 시 PID 보고(콘솔 주입 대상 지도). fire-and-forget.
            var startEntry = new JObject
            {
                ["matcher"] = "",
                ["hooks"] = new JArray { new JObject
                {
                    ["type"] = "command",
                    ["command"] = nodeCommand,
                    ["args"] = new JArray { scriptPath },
                    ["async"] = true,
                    ["timeout"] = 5
                }}
            };
            // SessionEnd: 세션 종료 시 PID 지도에서 제거. fire-and-forget.
            // WSL 세션은 보고되는 PID가 claude 자신이 아니라 터미널의 wsl.exe라서 claude가 끝나도 살아있다
            // → 지우지 않으면 끝난 세션에 보낸 답변이 그 터미널의 셸 프롬프트로 들어간다.
            var endEntry = new JObject
            {
                ["matcher"] = "",
                ["hooks"] = new JArray { new JObject
                {
                    ["type"] = "command",
                    ["command"] = nodeCommand,
                    ["args"] = new JArray { scriptPath },
                    ["async"] = true,
                    ["timeout"] = 5
                }}
            };
            // Stop: 세션이 턴을 끝냄 → '완료/마지막 멘트' 알림. fire-and-forget.
            var stopEntry = new JObject
            {
                ["matcher"] = "",
                ["hooks"] = new JArray { new JObject
                {
                    ["type"] = "command",
                    ["command"] = nodeCommand,
                    ["args"] = new JArray { scriptPath },
                    ["async"] = true,
                    ["timeout"] = 5
                }}
            };
            var merged = HookConfigMerger.AddHook(existing, "Notification", notifyEntry, Marker);
            // 옛 버전이 설치한 PreToolUse 항목 정리(권한은 PermissionRequest로 이관).
            merged = HookConfigMerger.RemoveHook(merged, "PreToolUse", Marker);
            // 기존 설치본(옛 timeout/args)이 멱등 스킵으로 안 바뀌므로, 우리 항목만 제거 후 재추가해 강제 갱신.
            merged = HookConfigMerger.RemoveHook(merged, "PermissionRequest", Marker);
            merged = HookConfigMerger.AddHook(merged, "PermissionRequest", permReqEntry, Marker);
            merged = HookConfigMerger.AddHook(merged, "SessionStart", startEntry, Marker);
            merged = HookConfigMerger.AddHook(merged, "SessionEnd", endEntry, Marker);
            // 기존 설치본(옛 async/timeout)이 멱등 스킵으로 안 바뀌므로 제거 후 재추가해 강제 갱신.
            merged = HookConfigMerger.RemoveHook(merged, "Stop", Marker);
            merged = HookConfigMerger.AddHook(merged, "Stop", stopEntry, Marker);
            return merged;
        }

        /// <summary>settings.json에서 우리 훅 항목만 모두 제거해 반환(WSL 제거에도 재사용).</summary>
        internal static string Strip(string existing)
        {
            var removed = HookConfigMerger.RemoveHook(existing, "Notification", Marker);
            removed = HookConfigMerger.RemoveHook(removed, "PreToolUse", Marker);
            removed = HookConfigMerger.RemoveHook(removed, "PermissionRequest", Marker);
            removed = HookConfigMerger.RemoveHook(removed, "SessionStart", Marker);
            removed = HookConfigMerger.RemoveHook(removed, "SessionEnd", Marker);
            removed = HookConfigMerger.RemoveHook(removed, "Stop", Marker);
            return removed;
        }

        public static bool Uninstall()
        {
            try
            {
                // Install()과 동일한 lost-update 가능성에 대한 주의 사항 참고.
                var existing = ReadSettings();
                if (!IsWritable(existing)) return false;
                WriteSettingsWithBackup(Strip(existing));
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

        internal static string ResolveNode()
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
