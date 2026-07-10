using System;

namespace AgentHub.Common
{
    public static class Constants
    {
        public static class ProgramInfo
        {
            public static readonly string Name = "AgentHub";
            public static readonly string KoreanName = "에이전트 허브";
        }

        public static class Messages
        {
            public static readonly string AlreadyExecuted = "프로그램이 이미 실행 중입니다.";
            public static readonly string ConfirmClose = "프로그램을 종료하시겠습니까?";
            public static readonly string ConfirmRemoveLog = "로그를 모두 지우시겠습니까?";
            public static readonly string UnhandledException = "Unhandled exception";
            public static readonly string ExpireToken = "The token is expired.";
            public static readonly string TrayProgram = "트레이로 실행됩니다.";
            public static readonly string ClawdRunningTitle = "Clawd on Desk 동시 실행 감지";
            public static readonly string ClawdRunningPrompt =
                "Clawd on Desk가 실행 중입니다.\n\n두 앱이 같은 질문(AskUserQuestion) 훅을 함께 가로채면,\n모바일에서 보낸 답변이 PC로 전달되지 않습니다.\n\nClawd on Desk를 종료하고 계속하시겠습니까?\n\n[예] 종료 후 실행   ·   [아니오] 유지(모바일 답변 불가)";
        }

        public static class SelfSigned
        {
            // 인증서는 설치 폴더가 아니라 LocalAppData에 보관한다(재설치해도 유지 → 폰의 인증서 신뢰가 살아있어 자동 재연결).
            public static string CertFilePath => $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\{ProgramInfo.Name}\Certificate";
            public static string PfxFileName => "AgentHub.pfx";
            public static string CrtFileName => "AgentHub.crt";
        }
    }
}
