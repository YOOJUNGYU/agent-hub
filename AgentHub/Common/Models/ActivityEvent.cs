namespace AgentHub.Common.Models
{
    /// <summary>세션 상세 활동 피드의 이벤트 1건.</summary>
    public class ActivityEvent
    {
        public string Kind { get; set; }     // message | thinking | tool_use | tool_result | user_prompt | mode_change
        public string Ts { get; set; }       // ISO 8601
        public string ToolName { get; set; }
        public string Summary { get; set; }  // 한 줄 요약
        public string Text { get; set; }     // 본문
    }
}
