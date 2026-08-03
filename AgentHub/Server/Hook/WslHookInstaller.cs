using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AgentHub.Common.Util;

namespace AgentHub.Server.Hook
{
    /// <summary>
    /// WSL 배포판의 ~/.claude/settings.json에 Agent Hub 훅을 멱등 설치/제거.
    /// WSL 안에서는 Windows의 127.0.0.1로 접속할 수 없으므로(NAT/방화벽), 훅을 Windows node.exe로
    /// 실행한다(인터롭) → HTTP가 Windows 네트워크에서 나가 loopback으로 서버에 닿는다.
    /// 그래서 command는 node.exe의 마운트 경로(/mnt/c/...), 스크립트 인자는 Windows 경로(C:\...)다.
    /// 배포판은 나중에 켜질 수 있으므로 주기적으로 동기화한다(실행 중인 배포판만 — 멈춘 VM을 깨우지 않기 위해).
    /// </summary>
    public static class WslHookInstaller
    {
        private const int SyncIntervalMs = 60000;
        private static Timer _sync;

        /// <summary>즉시 1회 설치 후 주기 동기화 시작(배포판이 나중에 켜지는 경우 대응).</summary>
        public static void StartSync()
        {
            StopSync();
            Install();
            _sync = new Timer(_ => Install(), null, SyncIntervalMs, SyncIntervalMs);
        }

        public static void StopSync()
        {
            _sync?.Dispose();
            _sync = null;
        }

        /// <summary>실행 중인 배포판 전체에 훅을 멱등 설치. 이미 최신이면 파일을 건드리지 않는다.</summary>
        public static void Install()
        {
            var node = Wsl.ToMountPath(HookInstaller.ResolveNode());
            if (node == null)
            {
                // ResolveNode가 절대 경로를 못 찾아 "node"(PATH 폴백)를 준 경우. 그대로 쓰면 WSL의 리눅스 node가
                // 실행돼 서버(127.0.0.1)에 닿지 못하므로 설치하지 않는다.
                LogService.Instance.Error("Windows node.exe 경로를 찾지 못해 WSL 훅 설치를 건너뜁니다.");
                return;
            }
            foreach (var home in Wsl.ClaudeHomes())
                Apply(home, json => HookInstaller.Merge(json, node, HookInstaller.ScriptPath));
        }

        /// <summary>실행 중인 배포판 전체에서 훅 제거.</summary>
        public static void Uninstall()
        {
            foreach (var home in Wsl.ClaudeHomes())
                Apply(home, HookInstaller.Strip);
        }

        private static void Apply(Wsl.ClaudeHome home, Func<string, string> transform)
        {
            var path = Path.Combine(home.ClaudeDir, "settings.json");
            try
            {
                var existing = File.Exists(path) ? File.ReadAllText(path) : "{}";
                var normalized = Normalize(existing);
                if (normalized == null)
                {
                    LogService.Instance.Error(path + " 파싱 실패 — WSL 훅 설치/제거 중단(파일 미변경)");
                    return;
                }
                var next = transform(existing);
                if (next == normalized) return; // 이미 최신 — 주기 동기화가 매번 파일을 쓰지 않게.
                // 9p(\\wsl.localhost) 경로는 File.Replace를 지원하지 않으므로 백업 복사 후 그대로 쓴다.
                try { if (File.Exists(path)) File.Copy(path, path + ".agenthub.bak", true); }
                catch (Exception ex) { LogService.Instance.Error(ex); }
                File.WriteAllText(path, next);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        // 병합 결과(Formatting.Indented)와 비교할 수 있게 기존 내용을 같은 형식으로 정규화. 파싱 실패 시 null.
        private static string Normalize(string json)
        {
            try { return JObject.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).ToString(Formatting.Indented); }
            catch { return null; }
        }
    }
}
