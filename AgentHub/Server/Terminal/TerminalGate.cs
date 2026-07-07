namespace AgentHub.Server.Terminal
{
    /// <summary>웹 터미널 접근 허용 판정(순수). enabled(호스트 토글) && 기기 승인.</summary>
    public static class TerminalGate
    {
        public static bool IsAllowed(bool enabled, string deviceStatus)
            => enabled && string.Equals(deviceStatus, "approved", System.StringComparison.OrdinalIgnoreCase);
    }
}
