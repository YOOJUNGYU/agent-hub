using System;
using System.IO;
using Newtonsoft.Json.Linq;
using AgentHub.Common.Util;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// ~/.codex/hooks.json에 Agent Hub 훅을 백업·멱등 설치/제거(I/O). Claude용 <see cref="HookInstaller"/>의 대응물.
    /// Codex 훅 I/O 계약은 Claude 호환(같은 hook_event_name·hookSpecificOutput)이라 동일한 agenthub-hook.js를 재사용한다.
    /// 차이는 hooks.json의 명령 표기뿐: Codex는 단일 문자열 `command`(PowerShell 호출형 `&amp; "node" "script"`)를 쓴다(args 배열 없음).
    /// Codex 미설치(~/.codex 없음)면 조용히 no-op.
    /// </summary>
    public static class CodexHookInstaller
    {
        private const string Marker = "agenthub-hook.js";

        private static string CodexHome =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        private static string HooksPath => Path.Combine(CodexHome, "hooks.json");

        private static string ScriptPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hook", "agenthub-hook.js");

        /// <summary>Codex가 설치돼 있는지(~/.codex 존재). 아니면 설치/제거를 건너뛴다.</summary>
        public static bool Available => Directory.Exists(CodexHome);

        public static bool IsInstalled()
        {
            if (!Available) return false;
            try { return HookConfigMerger.IsInstalled(ReadSettings(), "SessionStart", Marker); }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        // Codex hooks.json 명령: `& "<node>" "<script>" ["<windowSec>"]`. node/script 경로는 공백 대비 따옴표.
        private static string Command(int? windowSec)
        {
            var node = HookInstaller.ResolveNode();
            var cmd = "& \"" + node + "\" \"" + ScriptPath + "\"";
            if (windowSec.HasValue) cmd += " \"" + windowSec.Value + "\"";
            return cmd;
        }

        private static JObject Entry(string matcher, int timeout, int? windowSec)
            => new JObject
            {
                ["matcher"] = matcher,
                ["hooks"] = new JArray { new JObject
                {
                    ["type"] = "command",
                    ["command"] = Command(windowSec),
                    ["timeout"] = timeout
                }}
            };

        public static bool Install()
        {
            if (!Available) return false;
            try
            {
                var existing = ReadSettings();
                if (!IsWritable(existing)) return false;

                // Claude와 동일한 이벤트 집합(단, Codex엔 Notification 이벤트가 없음).
                // SessionStart: PID 보고(원본 종료용). PreToolUse: 원격 권한(블로킹).
                // PermissionRequest: 원격 답변(블로킹). Stop: 완료 알림.
                var startEntry = Entry("", 10, null);
                var permEntry = Entry("shell_command|shell|local_shell|apply_patch|write_file|edit", 120, null);
                var permReqEntry = Entry("", RemoteAnswerConfig.WindowSeconds, RemoteAnswerConfig.WindowSeconds);
                var stopEntry = Entry("", 10, null);

                var merged = HookConfigMerger.AddHook(existing, "SessionStart", startEntry, Marker);
                merged = HookConfigMerger.AddHook(merged, "PreToolUse", permEntry, Marker);
                // 기존 설치본(옛 timeout/args)이 멱등 스킵으로 안 바뀌므로, 우리 항목만 제거 후 재추가해 강제 갱신.
                merged = HookConfigMerger.RemoveHook(merged, "PermissionRequest", Marker);
                merged = HookConfigMerger.AddHook(merged, "PermissionRequest", permReqEntry, Marker);
                merged = HookConfigMerger.RemoveHook(merged, "Stop", Marker);
                merged = HookConfigMerger.AddHook(merged, "Stop", stopEntry, Marker);
                WriteSettingsWithBackup(merged);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Uninstall()
        {
            if (!Available) return false;
            try
            {
                var existing = ReadSettings();
                if (!IsWritable(existing)) return false;
                var removed = HookConfigMerger.RemoveHook(existing, "SessionStart", Marker);
                removed = HookConfigMerger.RemoveHook(removed, "PreToolUse", Marker);
                removed = HookConfigMerger.RemoveHook(removed, "PermissionRequest", Marker);
                removed = HookConfigMerger.RemoveHook(removed, "Stop", Marker);
                WriteSettingsWithBackup(removed);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        private static string ReadSettings()
            => File.Exists(HooksPath) ? File.ReadAllText(HooksPath) : "{}";

        /// <summary>내용이 비어있지 않은데 JSON으로 파싱되지 않으면 쓰기를 중단시킨다(데이터 유실 방지).</summary>
        private static bool IsWritable(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return true;
            try { JObject.Parse(content); return true; }
            catch (Exception ex)
            {
                LogService.Instance.Error("codex/hooks.json 파싱 실패 — 훅 설치/제거 중단(파일 미변경)", ex);
                return false;
            }
        }

        private static void WriteSettingsWithBackup(string content)
        {
            var dir = Path.GetDirectoryName(HooksPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmp = HooksPath + ".agenthub.tmp";
            File.WriteAllText(tmp, content);
            try
            {
                if (File.Exists(HooksPath))
                    File.Replace(tmp, HooksPath, HooksPath + ".agenthub.bak");
                else
                    File.Move(tmp, HooksPath);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
                throw;
            }
        }
    }
}
