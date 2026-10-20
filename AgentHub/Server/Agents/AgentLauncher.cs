using System;
using System.Collections.Generic;
using System.IO;
using AgentHub.Common.Util;
using AgentHub.Server.Terminal;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// 오피스의 "새 에이전트": Claude Code / Codex CLI를 새 콘솔 창으로 띄운다(Windows 또는 WSL 배포판).
    /// 콘솔 창이라 기존 흐름(훅 PID 보고 → 프롬프트 주입·질문 답변·권한 승인)이 그대로 붙는다.
    /// 첫 프롬프트는 명령줄에 끼워 넣지 않고 환경변수(AGENTHUB_PROMPT)로 넘긴다 —
    /// PowerShell·bash가 변수를 인자 하나로만 다루므로 프롬프트 내용이 명령으로 해석되지 않는다.
    /// </summary>
    public static class AgentLauncher
    {
        private const string PromptVar = "AGENTHUB_PROMPT";
        private const string CwdVar = "AGENTHUB_CWD";

        /// <returns>ok, 실패 사유, 띄운 터미널 id(오피스가 바로 그 터미널을 연다)</returns>
        public static (bool ok, string message, string pty) Launch(string engine, string env, string distro, string cwd, string prompt)
        {
            var cli = engine == "codex" ? "codex" : "claude";
            prompt = (prompt ?? "").Replace("\r", "").Trim();
            cwd = (cwd ?? "").Trim();
            try
            {
                string exe, args, workDir = null;
                var vars = new Dictionary<string, string> { [PromptVar] = prompt };
                if (env == "wsl")
                {
                    var wsl = Wsl.ResolveWslExe(); // 32비트 프로세스면 System32가 SysWOW64로 리다이렉트된다 → Sysnative 우선
                    if (wsl == null) return (false, "WSL이 설치되어 있지 않습니다.", null);
                    // wsl.exe는 -d·--cd 값의 따옴표를 벗기지 않는다 → 배포판은 설치 목록에 있는 이름만 그대로 넘기고,
                    // 작업 폴더·프롬프트는 환경변수로 넘겨 bash 안에서 "$VAR"로만 쓴다(공백·특수문자 안전).
                    if (!string.IsNullOrEmpty(distro) && !Wsl.InstalledDistros().Contains(distro))
                        return (false, $"WSL 배포판을 찾을 수 없습니다: {distro}", null);
                    // C:\a\b 로 받으면 /mnt/c/a/b 로. 비우면 WSL 홈.
                    var dir = string.IsNullOrEmpty(cwd) ? "" : (Wsl.ToMountPath(cwd) ?? cwd);
                    // 로그인·대화형 셸(-lic)이라 nvm 등으로 설치한 CLI와 별칭도 잡힌다.
                    var run = $"cd \"${{{CwdVar}:-$HOME}}\" && " + (string.IsNullOrEmpty(prompt) ? cli : $"{cli} \"${PromptVar}\"");
                    exe = wsl;
                    args = (string.IsNullOrEmpty(distro) ? "" : $"-d {distro} ") + $"-- bash -lic '{run}'";
                    vars[CwdVar] = dir;
                    vars["WSLENV"] = $"{PromptVar}/u:{CwdVar}/u";
                }
                else
                {
                    if (string.IsNullOrEmpty(cwd)) cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!Directory.Exists(cwd)) return (false, $"폴더가 없습니다: {cwd}", null);
                    exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                    var run = string.IsNullOrEmpty(prompt) ? $"& {cli}" : $"& {cli} $env:{PromptVar}";
                    args = $"-NoLogo -NoExit -Command \"{run}\"";
                    workDir = cwd;
                }
                // 창 없는 터미널(ConPTY)로 Agent Hub의 하위 프로세스로 띄운다 — 화면은 오피스 터미널 탭.
                var pty = PtyHost.Start($"\"{exe}\" {args}", workDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), vars);
                pty.Engine = cli;
                pty.Cwd = string.IsNullOrEmpty(cwd) ? "~" : cwd; // 사용자가 고른 폴더(WSL 경로 그대로)
                pty.Env = env == "wsl" ? "wsl" : "windows";
                pty.Title = $"{cli} · {(env == "wsl" ? "WSL " + distro : "Windows")} · {(string.IsNullOrEmpty(cwd) ? "~" : cwd)}";
                ApiLogger.Info($"새 에이전트 실행(터미널 {pty.Id}): {pty.Title}");
                return (true, null, pty.Id);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                return (false, ex.Message, null);
            }
        }
    }
}
