namespace AgentHub.Server.Agents
{
    public static partial class AgentMonitorService
    {
        /// <summary>콘솔 입력 주입을 지원하는 CLI 엔진. 실제 콘솔 여부는 AttachConsole 결과로 최종 판정한다.</summary>
        public static bool SupportsConsoleInjection(string engine)
            => string.Equals(engine, "claude", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(engine, "codex", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>모바일 직접 주입 가능 여부: 지원 엔진 + 등록된 PID. 죽은 PID/비콘솔 PID면 전송 시 명확히 실패한다.</summary>
        public static bool IsInjectable(string engine, bool hasPid) => hasPid && SupportsConsoleInjection(engine);

        /// <summary>훅이 보고한 세션 PID의 프로세스가 아직 살아 있는지(세션 창을 닫았으면 false).</summary>
        public static bool IsProcessAlive(int pid)
        {
            try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) return !p.HasExited; }
            catch { return false; } // 없거나(종료) 접근 불가
        }
    }
}
