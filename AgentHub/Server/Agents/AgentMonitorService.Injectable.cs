namespace AgentHub.Server.Agents
{
    public static partial class AgentMonitorService
    {
        /// <summary>모바일 직접 주입 가능 여부(세션연결 판별): claude 엔진 + 살아있는 PID.</summary>
        public static bool IsInjectable(string engine, bool hasPid) => engine == "claude" && hasPid;
    }
}
