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
        public string Target { get; set; }   // 조회 도구의 전체 대상(파일 경로·URL·검색어·셸 명령) — 오피스 자료실 카드용
        public string ToolId { get; set; }   // tool_use의 id / tool_result가 답하는 tool_use id (오피스: 서브에이전트·셸 짝짓기)
        public bool? Background { get; set; } // run_in_background로 띄운 도구(백그라운드 셸·에이전트)
        public string TaskId { get; set; }   // 백그라운드 작업 id(결과 텍스트의 "ID: …"/"agentId: …", 완료 알림의 task-id)
    }
}
