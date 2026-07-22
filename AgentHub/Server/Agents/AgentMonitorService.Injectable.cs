namespace AgentHub.Server.Agents
{
    public static partial class AgentMonitorService
    {
        /// <summary>모바일 직접 주입 가능 여부(세션연결 판별): claude 엔진 + 등록된 PID(레지스트리 존재; 죽은 PID면 전송 실패 후 세션연결로 복구).</summary>
        public static bool IsInjectable(string engine, bool hasPid) => engine == "claude" && hasPid;
    }
}
