using System;
using System.Runtime.InteropServices;

namespace AgentHub.Common.Util
{
    /// <summary>
    /// PC 사용자가 자리에 있는지 판별(마지막 키/마우스 입력 이후 경과). 권한 요청을 폰 응답까지
    /// 붙들지(자리 비움) 즉시 PC 프롬프트로 넘길지(자리에 있음) 결정하는 데 쓴다.
    /// GetLastInputInfo는 이 프로세스가 속한 대화형 세션의 입력만 반영한다(트레이 앱 = 사용자 세션).
    /// </summary>
    public static class DesktopIdle
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        /// <summary>마지막 입력 이후 경과 초. 조회 실패 시 0(=자리에 있음으로 보수적 판단).</summary>
        public static int IdleSeconds()
        {
            try
            {
                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref lii)) return 0;
                // dwTime·GetTickCount는 32비트로 약 49.7일마다 순환한다. unchecked 뺄셈으로 순환 구간에서도 올바른 차이를 얻는다.
                var elapsedMs = unchecked((uint)Environment.TickCount - lii.dwTime);
                return (int)(elapsedMs / 1000);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return 0; }
        }
    }
}
