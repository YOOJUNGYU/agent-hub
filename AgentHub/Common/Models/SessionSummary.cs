namespace AgentHub.Common.Models
{
    /// <summary>모바일 모니터의 세션 카드 1건 요약. (Claude Code 트랜스크립트에서 파생)</summary>
    public class SessionSummary
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Project { get; set; }        // cwd의 마지막 세그먼트
        public string Cwd { get; set; }
        public string GitBranch { get; set; }
        public string Status { get; set; }         // active | idle | ended
        public string CurrentTask { get; set; }
        public string ToolName { get; set; }       // 최신 tool_use 이름 (없으면 null)
        public string LastActivityAt { get; set; } // ISO 8601 (UTC)
        public int MessageCount { get; set; }
    }
}
