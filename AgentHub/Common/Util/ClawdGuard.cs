using System;
using System.Diagnostics;
using System.Linq;

namespace AgentHub.Common.Util
{
    /// <summary>
    /// clawd-on-desk(Electron 데스크톱 앱, 프로세스명 "Clawd on Desk") 동시 실행 감지/종료.
    /// 두 앱이 같은 AskUserQuestion(PermissionRequest) 훅을 가로채면 이중 응답이 되어 모바일 답변이 PC로
    /// 전달되지 않는다. agent-hub는 단독 실행을 전제로 하므로, 시작 시 감지해 종료를 유도하고
    /// (거부 시) 답변 시점에 차단 안내를 보낸다. "claude" CLI 프로세스와는 이름이 구분된다("clawd" ⊄ "claude").
    /// </summary>
    public static class ClawdGuard
    {
        private static bool NameMatches(string name)
            => !string.IsNullOrEmpty(name) && name.IndexOf("clawd", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool SafeMatches(Process p)
        {
            try { return NameMatches(p.ProcessName); }
            catch { return false; } // 접근 불가/종료된 프로세스는 무시
        }

        /// <summary>clawd-on-desk 프로세스가 하나라도 실행 중인지.</summary>
        public static bool IsRunning()
        {
            try
            {
                var procs = Process.GetProcesses();
                try { return procs.Any(SafeMatches); }
                finally { foreach (var p in procs) p.Dispose(); }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        /// <summary>실행 중인 모든 clawd-on-desk 프로세스를 강제 종료.</summary>
        public static void KillAll()
        {
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch (Exception ex) { LogService.Instance.Error(ex); return; }
            foreach (var p in all)
            {
                try { if (SafeMatches(p)) p.Kill(); }
                catch (Exception ex) { LogService.Instance.Error(ex); }
                finally { p.Dispose(); }
            }
        }
    }
}
