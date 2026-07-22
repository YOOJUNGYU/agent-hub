using System;
using System.Diagnostics;
using System.IO;
using AgentHub.Common.Util;

namespace AgentHub.Server.Terminal
{
    /// <summary>
    /// 직접입력이 안 되는(ConPTY/종료된) claude 세션을, PC에서 고전 conhost 콘솔에
    /// claude --resume 로 다시 실행해 콘솔 주입이 가능한 상태로 되돌린다.
    /// 라이브 attach/스트리밍 없음. sessionId 외 임의 입력을 실행 커맨드에 넣지 않는다.
    /// </summary>
    public static partial class SessionReopener
    {
        public enum Result { Ok, NoCwd, Failed }

        public static Result Reopen(string sessionId, string cwd)
        {
            if (!IsValidSessionId(sessionId)) return Result.Failed;
            if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd)) return Result.NoCwd;
            try
            {
                // conhost.exe 를 앞세워 '고전 콘솔 호스트'를 강제(Windows Terminal 기본이어도 ConPTY 승격 방지).
                var psi = new ProcessStartInfo
                {
                    FileName = "conhost.exe",
                    Arguments = "pwsh -NoExit -Command \"claude --resume " + sessionId + "\"",
                    WorkingDirectory = cwd,
                    UseShellExecute = true   // 새 콘솔 창 표시
                };
                Process.Start(psi);
                return Result.Ok;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return Result.Failed; }
        }
    }
}
