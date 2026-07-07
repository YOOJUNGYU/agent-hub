using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using static AgentHub.Server.Terminal.ConPtyInterop;

namespace AgentHub.Server.Terminal
{
    /// <summary>ConPTY 세션: 설정된 셸을 의사콘솔로 실행하고 stdin/stdout을 노출. 순수 interop(로깅 없음, 예외/이벤트로 표면화).</summary>
    public sealed class ConPtySession : IDisposable
    {
        // 호출 프로세스에 남아있는 표준 핸들(리다이렉트 등)이 있으면 자식이 의사콘솔 대신 그 핸들을
        // 물려받는 Windows ConPTY 동작이 있어(참고: microsoft/terminal#11276), CreateProcess 호출
        // 직전에만 일시적으로 비우고 직후 복원한다. 프로세스 전역 상태이므로 락으로 직렬화한다.
        private static readonly object CreateProcessLock = new object();

        private IntPtr _hPC, _attrList;
        private PROCESS_INFORMATION _pi;
        private FileStream _in, _out;
        private Thread _readThread, _waitThread;
        private volatile bool _disposed;

        public event Action Exited;

        public ConPtySession(string shell, string cwd, short cols, short rows, Action<byte[], int> onOutput)
        {
            if (onOutput == null) throw new ArgumentNullException(nameof(onOutput));
            if (cols <= 0) cols = 80;
            if (rows <= 0) rows = 24;

            if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0)) throw new InvalidOperationException("CreatePipe(in) failed");
            if (!CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0)) throw new InvalidOperationException("CreatePipe(out) failed");

            var hr = CreatePseudoConsole(new COORD { X = cols, Y = rows }, inRead, outWrite, 0, out _hPC);
            if (hr != 0) throw new InvalidOperationException("CreatePseudoConsole failed: 0x" + hr.ToString("X"));

            // 의사콘솔이 소유하게 된 끝단은 우리 쪽에서 닫는다.
            CloseHandle(inRead);
            CloseHandle(outWrite);

            var si = new STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            _attrList = Marshal.AllocHGlobal(size);
            si.lpAttributeList = _attrList;
            if (!InitializeProcThreadAttributeList(_attrList, 1, 0, ref size))
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed");
            if (!UpdateProcThreadAttribute(_attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("UpdateProcThreadAttribute failed");

            var workingDir = string.IsNullOrWhiteSpace(cwd) ? null : cwd;
            bool created;
            lock (CreateProcessLock)
            {
                var savedIn = GetStdHandle(STD_INPUT_HANDLE);
                var savedOut = GetStdHandle(STD_OUTPUT_HANDLE);
                var savedErr = GetStdHandle(STD_ERROR_HANDLE);
                SetStdHandle(STD_INPUT_HANDLE, IntPtr.Zero);
                SetStdHandle(STD_OUTPUT_HANDLE, IntPtr.Zero);
                SetStdHandle(STD_ERROR_HANDLE, IntPtr.Zero);
                try
                {
                    created = CreateProcess(null, shell, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, workingDir, ref si, out _pi);
                }
                finally
                {
                    SetStdHandle(STD_INPUT_HANDLE, savedIn);
                    SetStdHandle(STD_OUTPUT_HANDLE, savedOut);
                    SetStdHandle(STD_ERROR_HANDLE, savedErr);
                }
            }
            if (!created)
                throw new InvalidOperationException("CreateProcess failed: " + Marshal.GetLastWin32Error());

            _in = new FileStream(new SafeFileHandle(inWrite, true), FileAccess.Write);
            _out = new FileStream(new SafeFileHandle(outRead, true), FileAccess.Read);

            _readThread = new Thread(() => ReadLoop(onOutput)) { IsBackground = true, Name = "ConPty-read" };
            _readThread.Start();
            _waitThread = new Thread(WaitLoop) { IsBackground = true, Name = "ConPty-wait" };
            _waitThread.Start();
        }

        private void ReadLoop(Action<byte[], int> onOutput)
        {
            var buf = new byte[4096];
            try
            {
                int n;
                while (!_disposed && (n = _out.Read(buf, 0, buf.Length)) > 0)
                    onOutput(buf, n);
            }
            catch { /* 파이프 종료 */ }
        }

        private void WaitLoop()
        {
            try { WaitForSingleObject(_pi.hProcess, 0xFFFFFFFF); } catch { }
            if (!_disposed) { try { Exited?.Invoke(); } catch { } }
        }

        public void Write(byte[] data)
        {
            if (_disposed || data == null || data.Length == 0) return;
            try { _in.Write(data, 0, data.Length); _in.Flush(); } catch { }
        }

        public void Resize(short cols, short rows)
        {
            if (_disposed || _hPC == IntPtr.Zero || cols <= 0 || rows <= 0) return;
            try { ResizePseudoConsole(_hPC, new COORD { X = cols, Y = rows }); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // ClosePseudoConsole가 출력 파이프를 닫아 ReadLoop를 깨운다.
            try { if (_hPC != IntPtr.Zero) { ClosePseudoConsole(_hPC); _hPC = IntPtr.Zero; } } catch { }
            try { _in?.Dispose(); } catch { }
            try { _out?.Dispose(); } catch { }
            try
            {
                if (_pi.hProcess != IntPtr.Zero) { TerminateProcess(_pi.hProcess, 0); CloseHandle(_pi.hProcess); }
                if (_pi.hThread != IntPtr.Zero) CloseHandle(_pi.hThread);
            }
            catch { }
            try { if (_attrList != IntPtr.Zero) { DeleteProcThreadAttributeList(_attrList); Marshal.FreeHGlobal(_attrList); _attrList = IntPtr.Zero; } } catch { }
        }
    }
}
