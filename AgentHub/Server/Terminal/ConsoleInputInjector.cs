using System;
using System.Runtime.InteropServices;
using AgentHub.Common.Util;

namespace AgentHub.Server.Terminal
{
    /// <summary>
    /// 실행 중인 콘솔 프로세스(claude 세션)의 입력 버퍼에 텍스트/키를 직접 주입한다.
    /// 원본 프로세스를 종료하지 않는다(=/ws/session의 kill→resume과 별개).
    /// AttachConsole+WriteConsoleInput 기반 → 고전 conhost에서만 동작하고,
    /// ConPTY(Windows Terminal 등)에서는 AttachConsole 실패로 NoConsole을 반환한다.
    /// </summary>
    public static class ConsoleInputInjector
    {
        public enum Result { Ok, NoConsole, Failed }

        public struct KeyStroke { public ushort Vk; public char Ch; }

        /// <summary>문자 → (가상키코드, 유니코드문자). '\r'=VK_RETURN,
        /// 매핑 불가 문자(한글 등)는 vk 0 + UnicodeChar 유지(유니코드로 그대로 주입).</summary>
        public static KeyStroke MapChar(char c)
        {
            if (c == '\r') return new KeyStroke { Vk = 0x0D, Ch = c };
            short sc = VkKeyScan(c);
            ushort vk = sc == -1 ? (ushort)0 : (ushort)(sc & 0xFF);
            return new KeyStroke { Vk = vk, Ch = c };
        }

        private static readonly object _gate = new object();

        public static Result Inject(int pid, string text, bool appendEnter)
        {
            if (pid <= 0) return Result.Failed;
            string payload = (text ?? "") + (appendEnter ? "\r" : "");
            if (payload.Length == 0) return Result.Ok;

            lock (_gate)
            {
                bool attached = false;
                try
                {
                    FreeConsole(); // 우리(WinExe, 콘솔 없음)를 방어적으로 분리
                    if (!AttachConsole((uint)pid))
                        return Result.NoConsole; // err 6 등 — ConPTY이거나 대상 프로세스 종료
                    attached = true;

                    IntPtr hIn = GetStdHandle(STD_INPUT_HANDLE);
                    if (hIn == IntPtr.Zero || hIn == new IntPtr(-1)) return Result.Failed;

                    var records = new INPUT_RECORD[payload.Length * 2];
                    int i = 0;
                    foreach (char c in payload)
                    {
                        var k = MapChar(c);
                        records[i++] = KeyRecord(k, true);
                        records[i++] = KeyRecord(k, false);
                    }
                    bool ok = WriteConsoleInput(hIn, records, (uint)records.Length, out _);
                    return ok ? Result.Ok : Result.Failed;
                }
                catch (Exception ex) { LogService.Instance.Error(ex); return Result.Failed; }
                finally { if (attached) FreeConsole(); }
            }
        }

        private static INPUT_RECORD KeyRecord(KeyStroke k, bool down) => new INPUT_RECORD
        {
            EventType = KEY_EVENT,
            KeyEvent = new KEY_EVENT_RECORD
            {
                bKeyDown = down ? 1 : 0,
                wRepeatCount = 1,
                wVirtualKeyCode = k.Vk,
                wVirtualScanCode = 0,
                UnicodeChar = k.Ch,
                dwControlKeyState = 0
            }
        };

        private const int STD_INPUT_HANDLE = -10;
        private const ushort KEY_EVENT = 0x0001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct INPUT_RECORD { public ushort EventType; public KEY_EVENT_RECORD KeyEvent; }

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        private struct KEY_EVENT_RECORD
        {
            [FieldOffset(0)] public int bKeyDown;
            [FieldOffset(4)] public ushort wRepeatCount;
            [FieldOffset(6)] public ushort wVirtualKeyCode;
            [FieldOffset(8)] public ushort wVirtualScanCode;
            [FieldOffset(10)] public char UnicodeChar;
            [FieldOffset(12)] public uint dwControlKeyState;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WriteConsoleInputW")]
        private static extern bool WriteConsoleInput(IntPtr hConsoleInput, INPUT_RECORD[] lpBuffer, uint nLength, out uint written);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "VkKeyScanW")] private static extern short VkKeyScan(char ch);
    }
}
