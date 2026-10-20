using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using AgentHub.Common.Util;
using Microsoft.Win32.SafeHandles;

namespace AgentHub.Server.Terminal
{
    /// <summary>
    /// 창 없는 터미널(ConPTY, Windows 10 1809+): 오피스의 "새 에이전트"를 Agent Hub의 하위 프로세스로 띄운다.
    /// 화면에 콘솔 창이 뜨지 않고, 출력은 /ws/term 으로 오피스 터미널 탭에 흘러가며 입력도 거기서 받는다.
    /// Agent Hub가 끝나면 의사 콘솔이 닫히며 그 안의 프로세스도 함께 끝난다(Agent Hub 안에서만 사는 세션).
    /// 프롬프트·질문 답변·권한 입력은 ConsoleInputInjector가 이 터미널에 속한 PID면 콘솔 주입 대신 여기로 쓴다.
    /// </summary>
    public sealed class PtySession
    {
        private const int BacklogBytes = 512 * 1024;

        public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string Title { get; set; }
        public string Engine { get; set; }
        public string Env { get; set; }
        public string Cwd { get; set; }
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public int RootPid { get; internal set; }
        public bool Exited { get; private set; }

        internal IntPtr Hpc;
        internal FileStream Input;
        internal FileStream Output;
        internal IntPtr Process;
        private readonly object _gate = new object();
        private readonly List<byte> _backlog = new List<byte>();

        /// <summary>새 출력 바이트(UTF-8 VT 스트림).</summary>
        public event Action<byte[]> Data;
        public event Action Closed;

        public byte[] Backlog()
        {
            lock (_gate) return _backlog.ToArray();
        }

        public void Write(string text)
        {
            if (Exited || string.IsNullOrEmpty(text)) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            lock (Input) { Input.Write(bytes, 0, bytes.Length); Input.Flush(); }
        }

        public void Resize(int cols, int rows)
        {
            if (Exited || cols < 2 || rows < 2) return;
            PtyHost.ResizePseudoConsole(Hpc, new PtyHost.COORD { X = (short)cols, Y = (short)rows });
        }

        internal void Pump()
        {
            var buf = new byte[8192];
            try
            {
                int n;
                while ((n = Output.Read(buf, 0, buf.Length)) > 0)
                {
                    var chunk = new byte[n];
                    Buffer.BlockCopy(buf, 0, chunk, 0, n);
                    lock (_gate)
                    {
                        _backlog.AddRange(chunk);
                        if (_backlog.Count > BacklogBytes) _backlog.RemoveRange(0, _backlog.Count - BacklogBytes);
                    }
                    Data?.Invoke(chunk);
                }
            }
            catch (Exception) { /* 의사 콘솔이 닫혀 파이프가 끊김 */ }
        }

        internal void OnExit()
        {
            Exited = true;
            try { PtyHost.ClosePseudoConsole(Hpc); } catch { }
            try { Input?.Dispose(); } catch { }
            Closed?.Invoke();
        }
    }

    public static class PtyHost
    {
        private static readonly ConcurrentDictionary<string, PtySession> _sessions = new ConcurrentDictionary<string, PtySession>();

        public static IEnumerable<PtySession> Sessions => _sessions.Values;

        public static bool TryGet(string id, out PtySession s) => _sessions.TryGetValue(id ?? "", out s);

        /// <summary>commandLine을 창 없는 의사 콘솔에서 실행. extraEnv는 현재 환경에 덧붙인다.</summary>
        public static PtySession Start(string commandLine, string cwd, IDictionary<string, string> extraEnv, int cols = 120, int rows = 32)
        {
            if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0) || !CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0))
                throw new InvalidOperationException("pipe 생성 실패");
            var hr = CreatePseudoConsole(new COORD { X = (short)cols, Y = (short)rows }, inRead, outWrite, 0, out var hpc);
            if (hr != 0) throw new InvalidOperationException($"ConPTY를 만들 수 없습니다(Windows 10 1809 이상 필요): 0x{hr:X}");

            var si = new STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            si.lpAttributeList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref size)
                || !UpdateProcThreadAttribute(si.lpAttributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("ConPTY 속성 설정 실패");

            var env = EnvironmentBlock(extraEnv);
            try
            {
                if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                        EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, env, cwd, ref si, out var pi))
                    throw new InvalidOperationException($"프로세스를 시작할 수 없습니다: {Marshal.GetLastWin32Error()}");
                CloseHandle(pi.hThread);

                // 자식 쪽 파이프 끝은 의사 콘솔이 가졌으니 부모는 닫는다(안 닫으면 종료를 감지하지 못한다).
                inRead.Dispose();
                outWrite.Dispose();
                var s = new PtySession
                {
                    Hpc = hpc, RootPid = pi.dwProcessId, Process = pi.hProcess,
                    Input = new FileStream(inWrite, FileAccess.Write),
                    Output = new FileStream(outRead, FileAccess.Read),
                    Cwd = cwd
                };
                _sessions[s.Id] = s;
                new Thread(s.Pump) { IsBackground = true, Name = "pty-out-" + s.Id }.Start();
                new Thread(() =>
                {
                    WaitForSingleObject(pi.hProcess, INFINITE);
                    CloseHandle(pi.hProcess);
                    s.OnExit();
                }) { IsBackground = true, Name = "pty-wait-" + s.Id }.Start();
                return s;
            }
            finally
            {
                DeleteProcThreadAttributeList(si.lpAttributeList);
                Marshal.FreeHGlobal(si.lpAttributeList);
                Marshal.FreeHGlobal(env);
            }
        }

        public static void Kill(PtySession s)
        {
            if (s == null || s.Exited) return;
            try { TerminateProcess(s.Process, 1); } catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        /// <summary>훅이 보고한 세션 PID(CLI 또는 그 콘솔의 wsl.exe)가 어느 터미널 안의 프로세스인지.</summary>
        public static PtySession FindByPid(int pid)
        {
            var live = _sessions.Values.Where(x => !x.Exited).ToList();
            if (live.Count == 0 || pid <= 0) return null;
            var parents = ParentMap();
            for (int depth = 0, p = pid; depth < 6 && p > 0; depth++)
            {
                var hit = live.FirstOrDefault(x => x.RootPid == p);
                if (hit != null) return hit;
                if (!parents.TryGetValue(p, out p)) break;
            }
            return null;
        }

        /// <summary>그 PID가 터미널 소속이면 콘솔 주입 대신 터미널 입력으로 쓴다(성공 시 true).</summary>
        public static bool TryWrite(int pid, string payload)
        {
            var s = FindByPid(pid);
            if (s == null) return false;
            s.Write(payload);
            return true;
        }

        private static Dictionary<int, int> ParentMap()
        {
            var map = new Dictionary<int, int>();
            var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (Process32First(snap, ref e))
                    do { map[(int)e.th32ProcessID] = (int)e.th32ParentProcessID; } while (Process32Next(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return map;
        }

        private static IntPtr EnvironmentBlock(IDictionary<string, string> extra)
        {
            var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry kv in Environment.GetEnvironmentVariables()) vars[(string)kv.Key] = (string)kv.Value;
            if (extra != null) foreach (var kv in extra) vars[kv.Key] = kv.Value;
            var sb = new StringBuilder();
            foreach (var kv in vars) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
            sb.Append('\0');
            return Marshal.StringToHGlobalUni(sb.ToString());
        }

        // ---- Win32 ----
        private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint INFINITE = 0xFFFFFFFF;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct COORD { public short X; public short Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
        [DllImport("kernel32.dll")] internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
        [DllImport("kernel32.dll")] internal static extern void ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
        [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    }
}
