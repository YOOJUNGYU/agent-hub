using System;
using System.Runtime.InteropServices;
using AgentHub.Common.Util;

namespace AgentHub.Server.Terminal
{
    /// <summary>
    /// 실행 중인 콘솔 프로세스(claude 세션)의 입력 버퍼에 텍스트/키를 직접 주입한다.
    /// 원본 프로세스를 종료하지 않고 실행 중인 세션에 그대로 답변/프롬프트를 전달한다.
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
            if (c == '\x1b') return new KeyStroke { Vk = 0x1B, Ch = c }; // Esc — 권한 거부 폴백용
            short sc = VkKeyScan(c);
            ushort vk = sc == -1 ? (ushort)0 : (ushort)(sc & 0xFF);
            return new KeyStroke { Vk = vk, Ch = c };
        }

        private static readonly object _gate = new object();

        public static Result Inject(int pid, string text, bool appendEnter)
        {
            if (pid <= 0) return Result.Failed;
            string body = text ?? "";
            lock (_gate)
            {
                if (body.Length > 0)
                {
                    var r = WriteOnce(pid, body);
                    if (r != Result.Ok) return r;
                }
                if (!appendEnter) return Result.Ok;
                // raw-mode TUI(claude)는 텍스트와 Enter가 한 배치로 들어오면 Enter를 '제출'로 인식하지 못할 때가 있다.
                // 텍스트가 반영될 시간을 준 뒤 Enter를 '별도' 이벤트로 보낸다(picker 주입과 동일 패턴).
                if (body.Length > 0) System.Threading.Thread.Sleep(PickerStepDelayMs);
                return WriteOnce(pid, "\r");
            }
        }

        private const int PickerStepDelayMs = 500;

        /// <summary>
        /// 만료된 AskUserQuestion 터미널 picker에 답을 주입한다(스파이크 검증 시퀀스).
        /// - text 있음(Other): (optionCount+1)="Type something" → 지연 → text → 지연 → Enter(별도).
        /// - indices 1개(단일선택): 그 번호만(즉시 제출, Enter 불필요).
        /// - indices 다수(다중선택, best-effort): 각 번호 토글 → 지연 → Enter.
        /// 시퀀스 전체를 _gate로 직렬화(중간 끼어들기 방지).
        /// </summary>
        public static Result InjectPickerAnswer(int pid, int[] indices, string text, int optionCount)
        {
            if (pid <= 0) return Result.Failed;
            lock (_gate)
            {
                try
                {
                    // 한계(스펙 §10): 옵션 번호가 두 자리(옵션 10개↑)면 단일선택 자동제출이 첫 자리에서 끊길 수 있음.
                    //        만료 다중선택+Other 동시 선택 시 text 우선(indices 무시) — 만료 다중선택은 best-effort 범위.
                    if (!string.IsNullOrEmpty(text))
                    {
                        var r1 = WriteOnce(pid, (optionCount + 1).ToString()); if (r1 != Result.Ok) return r1;
                        System.Threading.Thread.Sleep(PickerStepDelayMs);
                        var r2 = WriteOnce(pid, text); if (r2 != Result.Ok) return r2;
                        System.Threading.Thread.Sleep(PickerStepDelayMs);
                        return WriteOnce(pid, "\r");
                    }
                    if (indices == null || indices.Length == 0) return Result.Failed;
                    if (indices.Length == 1)
                        return WriteOnce(pid, (indices[0] + 1).ToString());
                    foreach (var idx in indices)
                    {
                        var r = WriteOnce(pid, (idx + 1).ToString()); if (r != Result.Ok) return r;
                        System.Threading.Thread.Sleep(150);
                    }
                    System.Threading.Thread.Sleep(PickerStepDelayMs);
                    return WriteOnce(pid, "\r");
                }
                catch (Exception ex) { LogService.Instance.Error(ex); return Result.Failed; }
            }
        }

        /// <summary>
        /// "ask"로 폴백된 권한 프롬프트(터미널 번호 메뉴)에 답을 주입한다.
        /// 매핑(검증 리스크 — 실제 conhost로 확인 후 조정, 한 곳에 격리):
        ///   allow→"1" / allowAlways→"2" / deny→"3". 단일 숫자키 = picker 단일선택과 동일하게 즉시 제출.
        /// 옵션이 2개뿐인 프롬프트에서 3이 어긋나면 거부는 Esc("\x1b")로 대체(MapChar가 VK_ESCAPE로 매핑).
        /// </summary>
        public static string PermissionPayload(string choice)
        {
            switch (choice)
            {
                case "allow": return "1";
                case "allowAlways": return "2";
                case "deny": return "3";
                default: return null;
            }
        }

        public static Result InjectPermissionAnswer(int pid, string choice)
        {
            if (pid <= 0) return Result.Failed;
            var payload = PermissionPayload(choice);
            if (payload == null) return Result.Failed;
            lock (_gate) { return WriteOnce(pid, payload); }
        }

        // attach→write→free 원자 1회(락 없음 — 호출자가 _gate 보유). payload에 필요한 문자를 그대로(예: Enter는 "\r").
        private static Result WriteOnce(int pid, string payload)
        {
            if (string.IsNullOrEmpty(payload)) return Result.Ok;
            bool attached = false;
            try
            {
                FreeConsole();
                if (!AttachConsole((uint)pid)) return Result.NoConsole;
                attached = true;
                IntPtr hIn = GetStdHandle(STD_INPUT_HANDLE);
                if (hIn == IntPtr.Zero || hIn == new IntPtr(-1)) return Result.Failed;
                // 주의: BMP 문자 가정(한글·ASCII). 서로게이트쌍(이모지 등 보충문자)은 코드유닛 단위로 분리됨.
                var records = new INPUT_RECORD[payload.Length * 2];
                int i = 0;
                foreach (char c in payload)
                {
                    var k = MapChar(c);
                    records[i++] = KeyRecord(k, true);
                    records[i++] = KeyRecord(k, false);
                }
                return WriteConsoleInput(hIn, records, (uint)records.Length, out _) ? Result.Ok : Result.Failed;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return Result.Failed; }
            finally { if (attached) FreeConsole(); }
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
