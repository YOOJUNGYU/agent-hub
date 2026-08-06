using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AgentHub.Common.Util
{
    /// <summary>
    /// WSL 배포판 안의 Claude/Codex 홈(~/.claude, ~/.codex)을 Windows에서 찾는다. WSL에서 실행된 CLI 세션은
    /// 트랜스크립트를 배포판 안에 쓰므로 \\wsl.localhost\&lt;배포판&gt;\... 경로로 읽어야 목록에 나온다.
    /// '실행 중'인 배포판만 다룬다 — 멈춘 배포판의 경로에 접근하면 WSL이 자동으로 켜지기 때문(주기 스캔이 VM을 깨우면 안 됨).
    /// 결과는 30초 캐시(배포판 목록 조회에 wsl.exe를 띄우므로).
    /// </summary>
    public static class Wsl
    {
        /// <summary>배포판 안의 Claude 홈 1건.</summary>
        public class ClaudeHome
        {
            public string Distro { get; set; }
            public string ClaudeDir { get; set; } // \\wsl.localhost\<배포판>\home\<사용자>\.claude
        }

        /// <summary>배포판 안의 Codex 홈 1건.</summary>
        public class CodexHome
        {
            public string Distro { get; set; }
            public string CodexDir { get; set; } // \\wsl.localhost\<배포판>\home\<사용자>\.codex
        }

        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
        // 스캔은 wsl.exe 기동 + 9p 경로 탐색이라 수백 ms 걸린다. 락을 공유하면 Claude 스캔이
        // Codex 스캔을 그대로 막으므로(둘 다 5초 폴링에서 호출) 캐시별로 분리한다.
        private static readonly object _claudeGate = new object();
        private static readonly object _codexGate = new object();
        private static IList<ClaudeHome> _claudeCache;
        private static DateTime _claudeCachedAt;
        private static IList<CodexHome> _codexCache;
        private static DateTime _codexCachedAt;

        /// <summary>실행 중인 배포판들의 Claude 홈. WSL이 없으면 빈 목록.</summary>
        public static IList<ClaudeHome> ClaudeHomes()
        {
            lock (_claudeGate)
            {
                if (_claudeCache != null && DateTime.UtcNow - _claudeCachedAt < CacheTtl) return _claudeCache;
                _claudeCache = ScanClaude();
                _claudeCachedAt = DateTime.UtcNow;
                return _claudeCache;
            }
        }

        /// <summary>실행 중인 배포판들의 Codex 홈. WSL/Codex가 없으면 빈 목록.</summary>
        public static IList<CodexHome> CodexHomes()
        {
            lock (_codexGate)
            {
                if (_codexCache != null && DateTime.UtcNow - _codexCachedAt < CacheTtl) return _codexCache;
                _codexCache = ScanCodex();
                _codexCachedAt = DateTime.UtcNow;
                return _codexCache;
            }
        }

        private static IList<ClaudeHome> ScanClaude()
        {
            var homes = new List<ClaudeHome>();
            foreach (var distro in RunningDistros())
            {
                var root = @"\\wsl.localhost\" + distro;
                foreach (var home in HomeDirs(root))
                {
                    try
                    {
                        var claude = Path.Combine(home, ".claude");
                        if (Directory.Exists(claude))
                            homes.Add(new ClaudeHome { Distro = distro, ClaudeDir = claude });
                    }
                    catch (Exception ex) { LogService.Instance.Error(ex); }
                }
            }
            return homes;
        }

        private static IList<CodexHome> ScanCodex()
        {
            var homes = new List<CodexHome>();
            foreach (var distro in RunningDistros())
            {
                var root = @"\\wsl.localhost\" + distro;
                foreach (var home in HomeDirs(root))
                {
                    try
                    {
                        var codex = Path.Combine(home, ".codex");
                        if (Directory.Exists(codex))
                            homes.Add(new CodexHome { Distro = distro, CodexDir = codex });
                    }
                    catch (Exception ex) { LogService.Instance.Error(ex); }
                }
            }
            return homes;
        }

        // /home/* 와 /root — 실제 사용자 홈만 골라내는 건 호출자(.claude/.codex 존재 확인)가 한다.
        private static IEnumerable<string> HomeDirs(string root)
        {
            var list = new List<string>();
            try { list.AddRange(Directory.EnumerateDirectories(Path.Combine(root, "home"))); }
            catch { /* 배포판이 막 종료됐거나 접근 불가 — 조용히 건너뜀 */ }
            try
            {
                var r = Path.Combine(root, "root");
                if (Directory.Exists(r)) list.Add(r);
            }
            catch { /* 위와 동일 */ }
            return list;
        }

        /// <summary>
        /// wsl.exe의 실제 경로. 이 앱은 32비트 프로세스로 돌 수 있고, 그때 System32는 WOW64가
        /// SysWOW64로 리다이렉트해 wsl.exe를 찾지 못한다 → Sysnative(32비트에서만 보이는 실제 System32)를 먼저 본다.
        /// WSL 없는 PC면 null.
        /// </summary>
        private static string ResolveWslExe()
        {
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var candidates = Environment.Is64BitProcess
                ? new[] { Path.Combine(windir, "System32", "wsl.exe") }
                : new[] { Path.Combine(windir, "Sysnative", "wsl.exe"), Path.Combine(windir, "System32", "wsl.exe") };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        /// <summary>실행 중인 배포판 이름. WSL 미설치/조회 실패 시 빈 목록(조용히 비활성).</summary>
        private static IList<string> RunningDistros()
        {
            var names = new List<string>();
            var wsl = ResolveWslExe();
            if (wsl == null) return names; // WSL 없는 PC — wsl.exe를 띄우지 않는다(예외·로그 폭주 방지)
            try
            {
                var psi = new ProcessStartInfo(wsl, "--list --running --quiet")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Unicode // wsl.exe는 UTF-16LE로 출력한다
                };
                using (var p = Process.Start(psi))
                {
                    var outp = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return names; }
                    foreach (var line in (outp ?? "").Split('\n'))
                    {
                        var n = line.Trim();
                        if (n.Length > 0) names.Add(n);
                    }
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return names;
        }

        /// <summary>Windows 경로 → WSL에서 실행 가능한 마운트 경로(C:\a\b → /mnt/c/a/b). 드라이브 경로가 아니면 null.</summary>
        public static string ToMountPath(string winPath)
        {
            if (string.IsNullOrEmpty(winPath) || winPath.Length < 3) return null;
            if (winPath[1] != ':' || (winPath[2] != '\\' && winPath[2] != '/')) return null;
            return "/mnt/" + char.ToLowerInvariant(winPath[0]) + winPath.Substring(2).Replace('\\', '/');
        }
    }
}
