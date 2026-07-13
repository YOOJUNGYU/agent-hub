namespace AgentHub.Server.Hook
{
    /// <summary>
    /// 원격 AskUserQuestion 답변 대기창의 단일 진실 원천(계단식 타임아웃).
    /// 앱이 꺼져 있어도 이 창(초) 안에 앱을 켜면 훅이 서버를 기다렸다가 답을 받는다.
    /// 문서상 command 훅 기본 timeout이 600초라 WindowSeconds를 600으로 두어, 카스케이드
    /// 전체를 600초 이내에 중첩시킨다(>600초 존중 불확실성 회피).
    /// 순서(안쪽이 먼저 만료): ServerWindow < HookBudget < WindowSeconds(=Claude 훅 timeout).
    /// </summary>
    public static class RemoteAnswerConfig
    {
        /// <summary>settings.json에 기록할 Claude 훅 timeout(초). 가장 바깥(가장 큼). 문서상 안전값 600.</summary>
        public const int WindowSeconds = 600;

        /// <summary>훅(JS)의 총 대기 예산(ms) — 폴링+답변 대기 합. Claude timeout보다 5초 짧게.</summary>
        public const int HookBudgetMs = (WindowSeconds - 5) * 1000;

        /// <summary>서버 AwaitAnswer 최대 대기(ms). 훅 예산보다 짧게(2초 더).</summary>
        public const int ServerWindowMs = (WindowSeconds - 7) * 1000;

        /// <summary>서버가 훅 HTTP 타임아웃보다 먼저 응답하도록 빼는 여유(ms).</summary>
        public const int ServerMarginMs = 2000;
    }
}
