namespace AgentHub.Common.Models
{
    /// <summary>모니터링 대상 Claude 에이전트의 상태. (현재 mock, 실제 연동 시 AgentMonitorService에서 채움)</summary>
    public class AgentStatus
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }        // working | idle | error
        public string CurrentTask { get; set; }
        public int Progress { get; set; }          // 0-100
        public string UpdatedAt { get; set; }      // ISO 8601 (UTC)
    }
}
