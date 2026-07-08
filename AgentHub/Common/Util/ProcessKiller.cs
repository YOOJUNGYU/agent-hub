using System;
using System.Diagnostics;
using System.Management;

namespace AgentHub.Common.Util
{
    /// <summary>세션 원본 프로세스(및 그 부모 쉘 창)를 종료. 모바일이 세션을 가져올 때 충돌 방지용.</summary>
    public static class ProcessKiller
    {
        // 부모가 이 중 하나면 창까지 닫는다(WindowsTerminal 등 공유 호스트는 제외 — 다른 탭 보호).
        private static readonly string[] Shells = { "powershell", "pwsh", "cmd" };

        /// <summary>
        /// claudePid(훅이 보고한 claude/node PID)를 종료하고, 그 부모가 알려진 쉘(powershell/pwsh/cmd)이면
        /// 부모(쉘 창)도 종료한다. 부모가 쉘이 아니면(IDE 실행 등) claude만 종료한다(안전 폴백).
        /// </summary>
        public static void KillSessionOwner(int claudePid)
        {
            if (claudePid <= 0) return;
            // PID 재사용 방지: 대상이 실제 claude(node) 프로세스일 때만 진행.
            try
            {
                var name = Process.GetProcessById(claudePid).ProcessName?.ToLowerInvariant();
                if (name != "node" && name != "claude") return; // 이미 종료 후 다른 프로세스로 재사용됨 → 건드리지 않음
            }
            catch { return; } // 이미 종료됨

            int parentPid = 0;
            string parentName = null;
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + claudePid))
                foreach (ManagementObject mo in s.Get())
                {
                    parentPid = Convert.ToInt32(mo["ParentProcessId"]);
                    break;
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }

            if (parentPid > 0)
            {
                try { parentName = Process.GetProcessById(parentPid).ProcessName?.ToLowerInvariant(); }
                catch { parentName = null; }
            }

            TryKill(claudePid); // claude 종료 → 충돌(중복 writer) 제거
            if (parentName != null && Array.IndexOf(Shells, parentName) >= 0)
                TryKill(parentPid); // 쉘 창 닫기
        }

        private static void TryKill(int pid)
        {
            try { Process.GetProcessById(pid).Kill(); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }
    }
}
