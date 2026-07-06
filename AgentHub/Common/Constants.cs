using System;
using System.Windows.Forms;

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
        }

        public static class SelfSigned
        {
            public static string CertFilePath => $@"{Application.StartupPath}\Certificate";
            public static string PfxFileName => "AgentHub.pfx";
            public static string CrtFileName => "AgentHub.crt";
        }
    }
}
