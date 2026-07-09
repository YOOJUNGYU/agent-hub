using System;
using System.Diagnostics;
using System.Management;

namespace AgentHub.Common.Util
{
    /// <summary>세션 원본 프로세스(및 그 부모 쉘 창)를 종료. 모바일이 세션을 가져올 때 충돌 방지용.</summary>
    public static class ProcessKiller
    {
        // 이 이름의 부모는 "세션이 실행되던 쉘"로 보고 창까지 닫는다.
        // 그 외(WindowsTerminal·conhost·OpenConsole·Code·explorer·AgentHub 등 공유 호스트)에서 walk를 멈춰
        // 호스트 앱과 다른 탭을 보호한다.
        private static readonly string[] Shells = { "powershell", "pwsh", "cmd" };
        private const int MaxWalk = 8; // 부모 체인 순회 상한(루프/PID 재사용 방어)

        /// <summary>
        /// claudePid(훅이 보고한 claude/node PID)를 종료하고, 부모 체인을 따라 올라가며 알려진 쉘
        /// (powershell/pwsh/cmd)을 만나는 동안 계속 종료한다 — npm 셸 심(claude.cmd를 실행한 보이지 않는 cmd)과
        /// 그 위의 실제 쉘 창까지 닫기 위함. 쉘이 아닌 호스트(WindowsTerminal 등)를 만나면 그 지점에서 멈춘다.
        /// 부모가 처음부터 쉘이 아니면(IDE 실행 등) claude만 종료한다(안전 폴백). 기존 동작의 상위 호환.
        /// </summary>
        public static void KillSessionOwner(int claudePid)
        {
            if (claudePid <= 0) return;
            // PID 재사용 방지: 대상이 실제 claude(node) 프로세스일 때만 진행.
            var name = SafeName(claudePid);
            if (name != "node" && name != "claude") return; // 이미 종료 후 다른 프로세스로 재사용됨 → 건드리지 않음

            // 종료 전에 부모 체인을 먼저 수집한다(프로세스를 죽이면 WMI로 부모를 더 못 찾으므로).
            int parent = SafeParent(claudePid);
            TryKill(claudePid); // claude 종료 → 충돌(중복 writer) 제거

            for (int i = 0; i < MaxWalk && parent > 4; i++)
            {
                var pname = SafeName(parent);
                if (pname == null || Array.IndexOf(Shells, pname) < 0) break; // 쉘이 아님(호스트) → 보호하고 멈춤
                int grandparent = SafeParent(parent); // 죽이기 전에 다음 대상을 확보
                TryKill(parent); // 세션이 실행되던 쉘(심 cmd·보이는 쉘) 종료
                parent = grandparent;
            }
        }

        private static string SafeName(int pid)
        {
            try { return Process.GetProcessById(pid).ProcessName?.ToLowerInvariant(); }
            catch { return null; } // 이미 종료됨/접근 불가
        }

        private static int SafeParent(int pid)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + pid))
                foreach (ManagementObject mo in s.Get())
                    return Convert.ToInt32(mo["ParentProcessId"]);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return 0;
        }

        private static void TryKill(int pid)
        {
            try { Process.GetProcessById(pid).Kill(); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }
    }
}
