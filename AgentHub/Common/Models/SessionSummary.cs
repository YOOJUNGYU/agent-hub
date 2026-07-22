namespace AgentHub.Common.Models
{
    /// <summary>모바일 모니터의 세션 카드 1건 요약. (Claude Code 트랜스크립트에서 파생)</summary>
    public class SessionSummary
    {
        public string Id { get; set; }
        public string Engine { get; set; }        // claude | codex — 세션 소스 엔진(배지·이어받기 라우팅)
        public string Title { get; set; }
        public string Project { get; set; }        // cwd의 마지막 세그먼트
        public string Cwd { get; set; }
        public string GitBranch { get; set; }
        public string Status { get; set; }         // active | idle | ended
        public string CurrentTask { get; set; }
        public string ToolName { get; set; }       // 최신 tool_use 이름 (없으면 null)
        public string LastActivityAt { get; set; } // ISO 8601 (UTC)
        public string FirstActivityAt { get; set; }// ISO 8601 (UTC) — 세션 누적 경과 시간 계산용
        public string TurnStartAt { get; set; }    // ISO 8601 (UTC) — 마지막 사용자 프롬프트(현재 턴 경과 시간 계산용)
        public bool Working { get; set; }           // 현재 작업(도구 실행/응답 생성) 중인지 — 모바일 애니메이션용
        public long TotalTokens { get; set; }       // 세션 누적 토큰(input+cache_creation+output 합; 재사용 cache_read 제외)
        public int MessageCount { get; set; }
        public PendingAsk PendingAsk { get; set; }
        public bool Injectable { get; set; }   // claude + 살아있는 PID → 모바일 직접 주입 가능(세션연결 판별)
    }
}
