# SP2 웹 터미널(ConPTY) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 모바일 PWA에서 로컬 PC 셸을 실제 터미널(ConPTY)로 제어하고 `claude` CLI를 실행한다. 접근은 호스트 토글(기본 OFF)+승인기기로 게이팅.

**Architecture:** ConPTY를 P/Invoke로 감싼 `ConPtySession`을 `TerminalModule`(WebSocket `/ws/term`)이 연결별로 생성한다(EmbedIO `WebSocketTerminalModule` 샘플 구조 채택, 프로세스만 ConPTY로 교체). 게이트(토글+승인) 통과 시에만 spawn. 프론트는 로컬 벤더링한 xterm.js로 렌더. PC 콘솔은 토글/셸/폴더 설정만 담당.

**Tech Stack:** C# 8 / .NET Framework 4.8, ConPTY(kernel32 P/Invoke), EmbedIO(WebSocketModule), xterm.js 5.5(벤더), Newtonsoft.Json(기존), xUnit(테스트).

## Global Constraints

- 루트 네임스페이스 `AgentHub.*`. 서드파티 `EmbedIO/`·`EmbedIO` 네임스페이스 **수정 금지**.
- 한글(UTF-8) 문자열 인코딩 훼손 금지 — Edit/바이트 단위만.
- 새 NuGet 의존성 금지(ConPTY=P/Invoke, xterm=벤더 정적파일).
- 빌드는 **PowerShell**로: `& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"` (git bash는 `/t:` `/p:` 훼손). 0 errors 요구(기존 AppSettingsProvider.cs CS0168 경고는 무방).
- 테스트: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`. 순수/interop 파일은 소스 링크로 테스트.
- 외부 다운로드는 `curl --ssl-no-revoke` (환경의 schannel 폐기검사 이슈).
- `ConPtySession`/interop/게이트 로직은 `LogService`/`EmbedIO`/`WinForms` 비의존(테스트 소스 링크 가능하게). 예외는 throw 또는 이벤트로 표면화, 로깅은 호출측(모듈)에서.
- 보안: `/ws/term`은 `TerminalEnabled && Approved`에서만. config 변경 REST는 loopback 전용. 토글 OFF 시 활성 세션 전부 종료.
- 작업 브랜치: `feature/sp2-web-terminal` (생성됨, 스펙 커밋 존재).
- 기본값: 셸 `cmd.exe`, 시작폴더 빈값→`%USERPROFILE%`, 터미널 기본 80x24.

---

### Task 1: 설정 3종 추가 (TerminalEnabled/Shell/WorkingDir)

**Files:**
- Modify: `AgentHub/Properties/Settings.settings`
- Modify: `AgentHub/Properties/Settings.Designer.cs`

**Interfaces:**
- Produces: `Properties.Settings.Default.TerminalEnabled` (bool), `.TerminalShell` (string), `.TerminalWorkingDir` (string)

- [ ] **Step 1: `.settings`에 3개 항목 추가**

`Settings.settings`의 `</Settings>` 앞에 추가:
```xml
    <Setting Name="TerminalEnabled" Type="System.Boolean" Scope="User">
      <Value Profile="(Default)">False</Value>
    </Setting>
    <Setting Name="TerminalShell" Type="System.String" Scope="User">
      <Value Profile="(Default)">cmd.exe</Value>
    </Setting>
    <Setting Name="TerminalWorkingDir" Type="System.String" Scope="User">
      <Value Profile="(Default)" />
    </Setting>
```

- [ ] **Step 2: `Settings.Designer.cs`에 프로퍼티 3개 추가**

기존 `ServerCertPassword` 프로퍼티 뒤(클래스 닫는 `}` 앞)에 추가(기존 ServerPort 프로퍼티의 attribute 패턴을 그대로 따름):
```csharp
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool TerminalEnabled {
            get { return ((bool)(this["TerminalEnabled"])); }
            set { this["TerminalEnabled"] = value; }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("cmd.exe")]
        public string TerminalShell {
            get { return ((string)(this["TerminalShell"])); }
            set { this["TerminalShell"] = value; }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string TerminalWorkingDir {
            get { return ((string)(this["TerminalWorkingDir"])); }
            set { this["TerminalWorkingDir"] = value; }
        }
```
> 기존 ServerPort/ServerCertPassword 프로퍼티에 붙은 attribute 형태와 정확히 일치시킬 것(파일을 열어 확인 후 동일 패턴 사용).

- [ ] **Step 3: 빌드**

PowerShell msbuild → 0 errors.

- [ ] **Step 4: 커밋**

```bash
git add AgentHub/Properties/Settings.settings AgentHub/Properties/Settings.Designer.cs
git commit -m "feat(sp2): 터미널 설정(TerminalEnabled/Shell/WorkingDir) 추가"
```

---

### Task 2: 게이트 판정 로직 (순수) + 테스트

**Files:**
- Create: `AgentHub/Server/Terminal/TerminalGate.cs`
- Test: `AgentHub.Tests/TerminalGateTests.cs`

**Interfaces:**
- Produces: `static bool AgentHub.Server.Terminal.TerminalGate.IsAllowed(bool enabled, string deviceStatus)` — `enabled && deviceStatus == "approved"`.

- [ ] **Step 1: 실패 테스트**

`AgentHub.Tests/TerminalGateTests.cs`:
```csharp
using AgentHub.Server.Terminal;
using Xunit;

namespace AgentHub.Tests
{
    public class TerminalGateTests
    {
        [Theory]
        [InlineData(true, "approved", true)]
        [InlineData(false, "approved", false)]   // 토글 OFF
        [InlineData(true, "pending", false)]     // 미승인
        [InlineData(true, "revoked", false)]
        [InlineData(true, "none", false)]
        [InlineData(false, "pending", false)]
        public void IsAllowed(bool enabled, string status, bool expected)
            => Assert.Equal(expected, TerminalGate.IsAllowed(enabled, status));
    }
}
```

- [ ] **Step 2: 실패 확인** — `dotnet test ... --filter TerminalGateTests` → FAIL(형식 없음).

- [ ] **Step 3: 구현**

`AgentHub/Server/Terminal/TerminalGate.cs`:
```csharp
namespace AgentHub.Server.Terminal
{
    /// <summary>웹 터미널 접근 허용 판정(순수). enabled(호스트 토글) && 기기 승인.</summary>
    public static class TerminalGate
    {
        public static bool IsAllowed(bool enabled, string deviceStatus)
            => enabled && string.Equals(deviceStatus, "approved", System.StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: 테스트 프로젝트에 소스 링크 + 통과**

`AgentHub.Tests/AgentHub.Tests.csproj`의 `<ItemGroup>`(소스 링크) 에 추가:
```xml
    <Compile Include="..\AgentHub\Server\Terminal\TerminalGate.cs" Link="Linked\TerminalGate.cs" />
```
`dotnet test AgentHub.Tests/AgentHub.Tests.csproj` → 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add AgentHub/Server/Terminal/TerminalGate.cs AgentHub.Tests/TerminalGateTests.cs AgentHub.Tests/AgentHub.Tests.csproj
git commit -m "feat(sp2): TerminalGate 접근 판정 + 테스트"
```

---

### Task 3: `ConPtySession` + ConPTY interop + 통합 테스트

**Files:**
- Create: `AgentHub/Server/Terminal/ConPtyInterop.cs`
- Create: `AgentHub/Server/Terminal/ConPtySession.cs`
- Test: `AgentHub.Tests/ConPtySessionTests.cs`

**Interfaces:**
- Produces:
  - `class ConPtySession : IDisposable`
    - `ConPtySession(string shell, string cwd, short cols, short rows, System.Action<byte[],int> onOutput)`
    - `void Write(byte[] data)`
    - `void Resize(short cols, short rows)`
    - `event System.Action Exited`
    - `void Dispose()`

- [ ] **Step 1: interop 작성**

`AgentHub/Server/Terminal/ConPtyInterop.cs`:
```csharp
using System;
using System.Runtime.InteropServices;

namespace AgentHub.Server.Terminal
{
    /// <summary>ConPTY(Win10 1809+) P/Invoke. 외부 의존성 없음.</summary>
    internal static class ConPtyInterop
    {
        internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        internal static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;

        [StructLayout(LayoutKind.Sequential)]
        internal struct COORD { public short X; public short Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, [In] ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    }
}
```

- [ ] **Step 2: `ConPtySession` 작성**

`AgentHub/Server/Terminal/ConPtySession.cs`:
```csharp
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
            if (!CreateProcess(null, shell, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, workingDir, ref si, out _pi))
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
```

- [ ] **Step 3: 통합 테스트 작성 (실제 ConPTY로 echo 왕복)**

`AgentHub.Tests/ConPtySessionTests.cs`:
```csharp
using System;
using System.Text;
using System.Threading;
using AgentHub.Server.Terminal;
using Xunit;

namespace AgentHub.Tests
{
    public class ConPtySessionTests
    {
        [Fact]
        public void Spawns_and_streams_output()
        {
            var sb = new StringBuilder();
            var done = new ManualResetEventSlim(false);
            // 실행 후 종료하는 명령 — 출력에 마커가 포함되는지 확인
            using (var s = new ConPtySession("cmd.exe /c echo hello_conpty_marker", null, 80, 24,
                (buf, n) => { lock (sb) sb.Append(Encoding.UTF8.GetString(buf, 0, n)); }))
            {
                s.Exited += () => done.Set();
                done.Wait(TimeSpan.FromSeconds(10));
                Thread.Sleep(200); // 잔여 출력 flush 여유
            }
            Assert.Contains("hello_conpty_marker", sb.ToString());
        }
    }
}
```
소스 링크 추가(`AgentHub.Tests.csproj`):
```xml
    <Compile Include="..\AgentHub\Server\Terminal\ConPtyInterop.cs" Link="Linked\ConPtyInterop.cs" />
    <Compile Include="..\AgentHub\Server\Terminal\ConPtySession.cs" Link="Linked\ConPtySession.cs" />
```

- [ ] **Step 4: csproj 등록 + 빌드 + 테스트**

`AgentHub/AgentHub.csproj`에 `<Compile Include="Server\Terminal\ConPtyInterop.cs" />`, `<Compile Include="Server\Terminal\ConPtySession.cs" />` 추가(레거시 csproj 명시 등록).
PowerShell msbuild → 0 errors. `dotnet test` → 전체 PASS(통합 테스트가 실제 ConPTY로 통과해야 함). 실패 시 Dispose/파이프 소유·닫는 순서를 MS ConPTY 샘플과 대조해 수정.

- [ ] **Step 5: 커밋**

```bash
git add AgentHub/Server/Terminal/ConPtyInterop.cs AgentHub/Server/Terminal/ConPtySession.cs AgentHub.Tests/ConPtySessionTests.cs AgentHub.Tests/AgentHub.Tests.csproj AgentHub/AgentHub.csproj
git commit -m "feat(sp2): ConPtySession(ConPTY P/Invoke) + 통합 테스트"
```

---

### Task 4: `TerminalModule` (WebSocket `/ws/term`)

EmbedIO 샘플 구조 + ConPTY 백엔드 + 게이트.

**Files:**
- Create: `AgentHub/Server/Socket/TerminalModule.cs`

**Interfaces:**
- Consumes: `ConPtySession`, `TerminalGate.IsAllowed`, `DeviceRegistry.StatusOf/HashToken/StatusByHash`, `Properties.Settings.Default.Terminal*`, `Json`
- Produces: `class TerminalModule : WebSocketModule` (`/ws/term`), `void DisableAll()`

- [ ] **Step 1: 구현**

`AgentHub/Server/Socket/TerminalModule.cs`:
```csharp
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Util;
using AgentHub.Server.Devices;
using AgentHub.Server.Terminal;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 웹 터미널 WebSocket(/ws/term?token=). 게이트(토글+승인) 통과 시 ConPtySession 생성.
    /// 구조는 EmbedIO WebSocketTerminalModule 샘플을 따르되 Process 대신 ConPTY, raw 바이트 스트리밍.
    /// </summary>
    public class TerminalModule : WebSocketModule
    {
        private static readonly ConcurrentDictionary<string, TerminalModule> Instances = new ConcurrentDictionary<string, TerminalModule>();
        private readonly ConcurrentDictionary<string, ConPtySession> _sessions = new ConcurrentDictionary<string, ConPtySession>();

        public TerminalModule(string urlPath) : base(urlPath, true)
        {
            Instances[urlPath] = this;
        }

        /// <summary>토글 OFF 등에서 호출 — 모든 활성 세션 종료.</summary>
        public static void DisableAllInstances()
        {
            foreach (var m in Instances.Values) m.DisableAll();
        }

        public void DisableAll()
        {
            foreach (var kv in _sessions)
            {
                try { kv.Value.Dispose(); } catch { }
                try { var ctx = FindContext(kv.Key); if (ctx != null) _ = CloseAsync(ctx); } catch { }
            }
            _sessions.Clear();
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            try
            {
                var token = GetToken(context);
                var status = DeviceRegistry.StatusOf(token); // string 반환, 예: "approved" (DeviceStatus.Approved const)
                var enabled = Properties.Settings.Default.TerminalEnabled;
                if (!TerminalGate.IsAllowed(enabled, status))
                {
                    await SendAsync(context, Json.Serialize(new { type = "denied", reason = enabled ? "unauthorized" : "disabled" }));
                    await CloseAsync(context);
                    return;
                }

                var shell = string.IsNullOrWhiteSpace(Properties.Settings.Default.TerminalShell) ? "cmd.exe" : Properties.Settings.Default.TerminalShell;
                var cwd = Properties.Settings.Default.TerminalWorkingDir;
                if (string.IsNullOrWhiteSpace(cwd)) cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                var id = context.Id;
                var session = new ConPtySession(shell, cwd, 80, 24, (buf, n) => OnPtyOutput(id, buf, n));
                session.Exited += async () =>
                {
                    var ctx = FindContext(id);
                    if (ctx != null) { try { await SendAsync(ctx, Json.Serialize(new { type = "exit" })); await CloseAsync(ctx); } catch { } }
                };
                _sessions[id] = session;
                await SendAsync(context, Json.Serialize(new { type = "ready" }));
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                try { await CloseAsync(context); } catch { }
            }
        }

        private async void OnPtyOutput(string contextId, byte[] buf, int n)
        {
            var ctx = FindContext(contextId);
            if (ctx == null) return;
            // 승인 취소 시 즉시 중단
            if (!Properties.Settings.Default.TerminalEnabled)
            {
                if (_sessions.TryRemove(contextId, out var s)) { try { s.Dispose(); } catch { } }
                try { await CloseAsync(ctx); } catch { }
                return;
            }
            var slice = new byte[n];
            Buffer.BlockCopy(buf, 0, slice, 0, n);
            try { await SendAsync(ctx, slice); } catch { } // binary 프레임
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            try
            {
                if (!_sessions.TryGetValue(context.Id, out var session)) return Task.CompletedTask;
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = Json.Deserialize<TermIn>(text);
                if (msg == null) return Task.CompletedTask;
                if (msg.T == "i" && msg.D != null) session.Write(Encoding.UTF8.GetBytes(msg.D));
                else if (msg.T == "r") session.Resize((short)msg.Cols, (short)msg.Rows);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return Task.CompletedTask;
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            if (_sessions.TryRemove(context.Id, out var session))
            {
                try { session.Dispose(); } catch { }
            }
            return Task.CompletedTask;
        }

        private IWebSocketContext FindContext(string id)
        {
            foreach (var c in ActiveContexts) if (c.Id == id) return c;
            return null;
        }

        private static string GetToken(IWebSocketContext ctx)
        {
            var q = ctx.RequestUri?.Query;
            if (string.IsNullOrEmpty(q)) return null;
            foreach (var pair in q.TrimStart('?').Split('&'))
            {
                var i = pair.IndexOf('=');
                if (i > 0 && pair.Substring(0, i) == "token") return Uri.UnescapeDataString(pair.Substring(i + 1));
            }
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisableAll();
            base.Dispose(disposing);
        }

        private class TermIn { public string T { get; set; } public string D { get; set; } public int Cols { get; set; } public int Rows { get; set; } }
    }
}
```
> 확인됨: `DeviceRegistry.StatusOf(token)`는 **문자열**을 반환하고(`DeviceStatus.None/Pending/Approved/Revoked`는 `Device.cs`의 `const string` = 소문자 "approved" 등), `TerminalGate.IsAllowed(bool, string)`가 문자열 "approved"를 받으므로 **변환 없이 그대로 전달**하면 된다. `Json.Deserialize`는 대소문자 무시 기본 바인딩이라 `TermIn`의 `T/D/Cols/Rows`가 `{t,d,cols,rows}` JSON과 정상 매핑된다.

- [ ] **Step 2: 빌드** — PowerShell msbuild → 0 errors.

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/Server/Socket/TerminalModule.cs
git commit -m "feat(sp2): TerminalModule(/ws/term) — ConPTY 세션 + 게이트"
```

---

### Task 5: 터미널 config/status REST 엔드포인트

**Files:**
- Modify: `AgentHub/Server/Controller/ApiController.cs`

**Interfaces:**
- Produces: `GET /api/terminal/config`(loopback), `POST /api/terminal/config`(loopback), `GET /api/terminal/status`(공개)
- Consumes: `TerminalModule.DisableAllInstances()`, `Properties.Settings`, 기존 `IsLoopback()`/`Forbidden()`

- [ ] **Step 1: 엔드포인트 추가**

`ApiController.cs`에 추가(기존 `IsLoopback()`/`Forbidden()` 패턴 사용, `using AgentHub.Server.Socket;` 필요 시 추가):
```csharp
        [Route(HttpVerbs.Get, "/terminal/status")]
        public Task TerminalStatus()
            => SendJsonAsync(Json.Serialize(new { enabled = Properties.Settings.Default.TerminalEnabled }));

        [Route(HttpVerbs.Get, "/terminal/config")]
        public Task GetTerminalConfig()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new
            {
                enabled = Properties.Settings.Default.TerminalEnabled,
                shell = Properties.Settings.Default.TerminalShell,
                workingDir = Properties.Settings.Default.TerminalWorkingDir
            }));
        }

        [Route(HttpVerbs.Post, "/terminal/config")]
        public async Task SaveTerminalConfig()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            var body = Json.Deserialize<TerminalConfigBody>(raw) ?? new TerminalConfigBody();
            Properties.Settings.Default.TerminalEnabled = body.Enabled;
            if (body.Shell != null) Properties.Settings.Default.TerminalShell = body.Shell.Trim();
            if (body.WorkingDir != null) Properties.Settings.Default.TerminalWorkingDir = body.WorkingDir.Trim();
            Properties.Settings.Default.Save();
            if (!body.Enabled) AgentHub.Server.Socket.TerminalModule.DisableAllInstances();
            await SendJsonAsync(Json.Serialize(new { ok = true, enabled = body.Enabled }));
        }
```
그리고 파일 하단 Models 영역(다른 body 클래스 옆)에:
```csharp
    internal class TerminalConfigBody { public bool Enabled { get; set; } public string Shell { get; set; } public string WorkingDir { get; set; } }
```
> `Forbidden()`가 `Task` 반환인지 확인해 `await Forbidden()` 형태를 맞출 것(기존 사용처와 동일 패턴).

- [ ] **Step 2: 빌드** → 0 errors.

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/Server/Controller/ApiController.cs
git commit -m "feat(sp2): /api/terminal config(loopback)/status 엔드포인트"
```

---

### Task 6: `EmbedIOServer`에 TerminalModule 등록 + 정리

**Files:**
- Modify: `AgentHub/Server/EmbedIOServer.cs`

- [ ] **Step 1: 모듈 등록**

`StartServer()`의 모듈 체인에서 `AgentMonitorModule`/`HostMonitorModule` 등록부 근처에 추가:
```csharp
                    .WithModule(new TerminalModule("/ws/term"))
```
(`using AgentHub.Server.Socket;`는 이미 있음 — 확인.)

- [ ] **Step 2: 정지 시 세션 정리**

`StopServer()`에서 서버 dispose 전에 활성 터미널 세션 정리:
```csharp
                Socket.TerminalModule.DisableAllInstances();
```
(`AgentMonitorService.Stop()` 호출 근처.)

- [ ] **Step 3: 빌드** → 0 errors.

- [ ] **Step 4: 커밋**

```bash
git add AgentHub/Server/EmbedIOServer.cs
git commit -m "feat(sp2): EmbedIOServer에 TerminalModule 등록 + 정지 시 세션 정리"
```

---

### Task 7: xterm.js 벤더링

**Files:**
- Create: `AgentHub/View/Htmls/js/xterm.js`, `AgentHub/View/Htmls/js/addon-fit.js`, `AgentHub/View/Htmls/css/xterm.css`
- Modify: `AgentHub/AgentHub.csproj` (Content 등록 + 출력 복사)

- [ ] **Step 1: dist 다운로드**

```bash
cd /c/GIT/PRIVATE/agent-hub
curl -sS --ssl-no-revoke -o AgentHub/View/Htmls/js/xterm.js  https://cdn.jsdelivr.net/npm/@xterm/xterm@5.5.0/lib/xterm.js
curl -sS --ssl-no-revoke -o AgentHub/View/Htmls/css/xterm.css https://cdn.jsdelivr.net/npm/@xterm/xterm@5.5.0/css/xterm.css
curl -sS --ssl-no-revoke -o AgentHub/View/Htmls/js/addon-fit.js https://cdn.jsdelivr.net/npm/@xterm/addon-fit@0.10.0/lib/addon-fit.js
# 크기 확인(각각 0바이트 아님)
wc -c AgentHub/View/Htmls/js/xterm.js AgentHub/View/Htmls/css/xterm.css AgentHub/View/Htmls/js/addon-fit.js
```
기대: xterm.js ≈ 280KB+, addon-fit ≈ 수 KB, xterm.css ≈ 수 KB. UMD 전역: `window.Terminal`, `window.FitAddon.FitAddon`.

- [ ] **Step 2: csproj Content 등록**

`AgentHub/AgentHub.csproj`에서 기존 `View\Htmls\js\*.js`가 어떻게 출력 복사되는지 확인(Content + CopyToOutputDirectory 패턴). 동일 패턴으로 3개 파일을 추가:
```xml
    <Content Include="View\Htmls\js\xterm.js"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>
    <Content Include="View\Htmls\js\addon-fit.js"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>
    <Content Include="View\Htmls\css\xterm.css"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>
```
> 기존 js 파일들의 등록 방식이 다르면(예: 글롭/링크) 그 방식을 그대로 따를 것.

- [ ] **Step 3: 빌드 + 출력 확인**

PowerShell msbuild → 0 errors. `install/Debug/View/Htmls/js/xterm.js` 등 3개가 복사됐는지 확인.

- [ ] **Step 4: 커밋**

```bash
git add AgentHub/View/Htmls/js/xterm.js AgentHub/View/Htmls/js/addon-fit.js AgentHub/View/Htmls/css/xterm.css AgentHub/AgentHub.csproj
git commit -m "chore(sp2): xterm.js 5.5 + fit addon 로컬 벤더링"
```

---

### Task 8: 모바일 프론트엔드 — 터미널 화면

**Files:**
- Modify: `AgentHub/View/Htmls/index.html`, `js/app.js`, `js/i18n.js`, `css/app.css`, `sw.js`
- Create: `AgentHub/View/Htmls/js/term.js`

**Interfaces:**
- WS(`/ws/term?token=`): 서버→클라 `{type:"ready"|"denied"|"exit", reason?}` + binary(PTY 출력); 클라→서버 `{t:"i",d}` / `{t:"r",cols,rows}`.

- [ ] **Step 1: index.html — xterm 자산 + 터미널 화면 + 버튼**

`<head>`에 xterm css:
```html
  <link rel="stylesheet" href="/css/xterm.css" />
```
모니터 섹션 상단(summary 옆)에 터미널 진입 버튼(기본 숨김):
```html
      <button id="termBtn" class="term-btn" hidden data-i18n="term.open">⌨ 터미널</button>
```
`#detail` 섹션 뒤에 터미널 화면 추가:
```html
    <section id="terminal" class="screen" hidden>
      <div class="detail-head">
        <button id="termBack" class="back-btn" data-i18n="detail.back">← 목록</button>
        <div class="detail-title" data-i18n="term.title">터미널</div>
      </div>
      <p class="term-warn" data-i18n="term.warn">이 터미널은 PC에서 실제 명령을 실행합니다.</p>
      <div id="termView"></div>
    </section>
```
`</body>` 전 스크립트에 xterm/addon/term 추가(app.js 앞):
```html
  <script src="/js/xterm.js"></script>
  <script src="/js/addon-fit.js"></script>
  <script src="/js/i18n.js"></script>
  <script src="/js/app.js"></script>
  <script src="/js/term.js"></script>
```

- [ ] **Step 2: term.js — xterm 연결 로직**

`AgentHub/View/Htmls/js/term.js`:
```javascript
// 웹 터미널 화면: xterm.js ⇄ /ws/term (ConPTY). app.js의 showScreen/getToken/$ 재사용.
(function () {
  let term, fit, tws, opened = false;

  async function terminalEnabled() {
    try { const s = await (await fetch('/api/terminal/status')).json(); return !!s.enabled; }
    catch (_) { return false; }
  }

  // 모니터 진입 시 버튼 노출 여부 갱신 (app.js에서 호출)
  window.refreshTermButton = async function () {
    const btn = document.getElementById('termBtn');
    if (!btn) return;
    btn.hidden = !(await terminalEnabled());
  };

  function ensureTerm() {
    if (term) return;
    term = new Terminal({ cursorBlink: true, fontSize: 13, theme: { background: '#0b0f1a' } });
    fit = new FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open(document.getElementById('termView'));
    term.onData(d => send({ t: 'i', d }));
    window.addEventListener('resize', doFit);
  }

  function doFit() {
    if (!term || !fit) return;
    try { fit.fit(); send({ t: 'r', cols: term.cols, rows: term.rows }); } catch (_) {}
  }

  function send(o) { try { tws && tws.readyState === 1 && tws.send(JSON.stringify(o)); } catch (_) {} }

  window.openTerminal = function () {
    ensureTerm();
    term.reset();
    showScreen('terminal');
    history.pushState({ screen: 'terminal' }, '');
    setTimeout(doFit, 60);
    const url = (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host
      + '/ws/term?token=' + encodeURIComponent(getToken());
    tws = new WebSocket(url);
    tws.binaryType = 'arraybuffer';
    opened = true;
    tws.onmessage = ev => {
      if (typeof ev.data === 'string') {
        let m; try { m = JSON.parse(ev.data); } catch (_) { return; }
        if (m.type === 'ready') doFit();
        else if (m.type === 'denied') { term.write('\r\n[' + (m.reason === 'disabled' ? '터미널이 비활성화됨' : '권한 없음') + ']\r\n'); }
        else if (m.type === 'exit') { term.write('\r\n[세션 종료]\r\n'); }
      } else {
        term.write(new Uint8Array(ev.data));
      }
    };
    tws.onclose = () => { /* 유지: 사용자가 뒤로가기로 정리 */ };
  };

  function closeTerminal() {
    try { tws && tws.close(); } catch (_) {}
    tws = null; opened = false;
  }

  document.getElementById('termBtn') && document.getElementById('termBtn').addEventListener('click', () => window.openTerminal());
  document.getElementById('termBack') && document.getElementById('termBack').addEventListener('click', () => { if (opened) history.back(); });

  // 뒤로가기(popstate)로 터미널을 벗어나면 정리 후 목록으로
  window.addEventListener('popstate', () => {
    if (opened) { closeTerminal(); showScreen('monitor'); }
  });
})();
```
> `showScreen`, `getToken`, `$`는 app.js의 전역 함수(스크립트 로드 순서상 app.js 뒤에 term.js). app.js의 `showScreen` 토글 목록에 `'terminal'` 추가 필요(Step 3).

- [ ] **Step 3: app.js — showScreen에 terminal 추가 + 버튼 갱신 + popstate 정합**

`showScreen`의 배열에 `'terminal'` 추가:
```javascript
  ['authRequest', 'authPending', 'monitor', 'detail', 'terminal'].forEach(id => {
```
`sessions` 수신으로 monitor를 보일 때 터미널 버튼 갱신:
```javascript
      else if (m.type === 'sessions') { renderSessions(m.sessions); if (currentSessionId === null) { showScreen('monitor'); if (window.refreshTermButton) window.refreshTermButton(); } }
```
app.js의 기존 `popstate` 핸들러는 상세(detail)만 처리하므로, 터미널은 term.js의 popstate가 담당(둘 다 등록되어도 각자 `currentSessionId`/`opened` 가드로 충돌 없음). 확인만.

- [ ] **Step 4: i18n.js — term.* 키(ko/en)**

ko:
```javascript
      'term.open': '⌨ 터미널',
      'term.title': '터미널',
      'term.warn': '이 터미널은 PC에서 실제 명령을 실행합니다. 신뢰하는 경우에만 사용하세요.',
```
en:
```javascript
      'term.open': '⌨ Terminal',
      'term.title': 'Terminal',
      'term.warn': 'This terminal runs real commands on the PC. Use only if you trust it.',
```

- [ ] **Step 5: app.css — 터미널 스타일 (파일 끝에 추가)**

```css
.term-btn { margin: 0 0 10px; padding: 8px 14px; background: #222941; color: #c9d1e4; border: 1px solid #2a3145; border-radius: 8px; cursor: pointer; }
.term-warn { color: #fbbf24; font-size: 12px; margin: 4px 0 8px; }
#termView { height: 70vh; width: 100%; }
#terminal .detail-head { margin-bottom: 8px; }
```

- [ ] **Step 6: sw.js — 캐시 버전 상향 + xterm 자산 등록**

`CACHE`를 `'agent-hub-v4'`로. `ASSETS`에 `'/js/xterm.js', '/js/addon-fit.js', '/css/xterm.css', '/js/term.js'` 추가.

- [ ] **Step 7: 검증**

`node --check` app.js/term.js/i18n.js → OK. PowerShell msbuild → 0 errors. `install/Debug/View/Htmls`에 term.js/xterm.js 복사 확인.

- [ ] **Step 8: 커밋**

```bash
git add AgentHub/View/Htmls/
git commit -m "feat(sp2): 모바일 터미널 화면(xterm.js) + 진입 버튼 + i18n/css/sw"
```

---

### Task 9: PC 콘솔 설정 탭 — 터미널 토글/셸/폴더

**Files:**
- Modify: `AgentHub/View/Htmls/host.html`, `js/host.js`, `js/i18n.js`

- [ ] **Step 1: host.html 설정 폼에 터미널 항목 추가**

`#settingsForm` 안(포트 입력 아래)에:
```html
        <hr />
        <label class="chk"><input type="checkbox" id="termEnabled" /> <span data-i18n="settings.termEnable">웹 터미널 허용(모바일에서 PC 명령 실행)</span></label>
        <p class="hint" data-i18n="settings.termWarn">승인된 기기가 이 PC에서 명령을 실행할 수 있게 됩니다. 필요할 때만 켜세요.</p>
        <label for="termShell" data-i18n="settings.termShell">기본 셸</label>
        <input type="text" id="termShell" placeholder="cmd.exe" />
        <label for="termCwd" data-i18n="settings.termCwd">시작 폴더 (비우면 사용자 홈)</label>
        <input type="text" id="termCwd" placeholder="%USERPROFILE%" />
        <button type="button" id="termSaveBtn" data-i18n="settings.termSave">터미널 설정 저장</button>
        <p class="hint" id="termHint"></p>
```

- [ ] **Step 2: host.js — 로드/저장**

설정 탭 로드시(기존 포트 로드 근처)에 터미널 config 로드:
```javascript
  try {
    const c = await (await fetch('/api/terminal/config')).json();
    $('#termEnabled').checked = !!c.enabled;
    $('#termShell').value = c.shell || '';
    $('#termCwd').value = c.workingDir || '';
  } catch (e) { /* noop */ }
```
저장 버튼:
```javascript
$('#termSaveBtn').addEventListener('click', async () => {
  const hint = $('#termHint');
  hint.textContent = t('settings.saving');
  try {
    const res = await (await fetch('/api/terminal/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ enabled: $('#termEnabled').checked, shell: $('#termShell').value, workingDir: $('#termCwd').value })
    })).json();
    hint.textContent = res.ok ? t('settings.saved').replace('{url}', '') : (t('settings.error'));
  } catch (e) { hint.textContent = t('settings.reqFail') + e.message; }
});
```
> `$`/`t`/`settings.saving` 등은 host.js/i18n의 기존 심볼. 필요한 i18n 키 추가(Step 3).

- [ ] **Step 3: i18n.js — settings.term* 키(ko/en)**

ko: `'settings.termEnable'`, `'settings.termWarn'`, `'settings.termShell'`, `'settings.termCwd'`, `'settings.termSave'` (위 한글 문구). en: 동일 키 영문.

- [ ] **Step 4: 검증** — node --check host.js/i18n.js, PowerShell msbuild → 0 errors.

- [ ] **Step 5: 커밋**

```bash
git add AgentHub/View/Htmls/host.html AgentHub/View/Htmls/js/host.js AgentHub/View/Htmls/js/i18n.js
git commit -m "feat(sp2): PC 콘솔 설정 탭에 터미널 토글/셸/폴더"
```

---

### Task 10: E2E 검증 + 빌드 게이트 + 마무리

- [ ] **Step 1: 전체 테스트** — `dotnet test AgentHub.Tests/AgentHub.Tests.csproj` → 전체 PASS(ConPtySession 통합 포함).
- [ ] **Step 2: 빌드 게이트** — PowerShell msbuild Restore+Build → 0 errors, `install/Debug/AgentHub.exe`.
- [ ] **Step 3: 실제 플로우(사용자 수동, verify)**: 앱 실행 → PC 콘솔 설정에서 "웹 터미널 허용" ON + 셸/폴더 지정 → 승인된 모바일에서 "⌨ 터미널" 버튼 노출 확인 → 진입 시 셸 프롬프트 표시 → 명령 입력/출력, `claude` 실행(TUI 렌더), resize, 뒤로가기 복귀 → 토글 OFF 시 세션 종료 확인. 미승인/토글OFF에서 denied 확인.
- [ ] **Step 4: 스펙 대비 점검** — `docs/superpowers/specs/2026-07-07-sp2-web-terminal-design.md` 요구 커버 확인.
- [ ] **Step 5: 브랜치 마무리** — `superpowers:finishing-a-development-branch`.

---

## Self-Review (계획 검증)

- **스펙 커버리지:** 설정=Task1, 게이트=Task2, ConPTY=Task3, WS 모듈=Task4, REST=Task5, 서버배선/정리=Task6, xterm 벤더=Task7, 모바일 UI=Task8, PC 설정 UI=Task9, 검증=Task10. 보안모델(토글+승인, loopback config, 토글 OFF 종료)=Task2/4/5/6. 커버됨.
- **플레이스홀더:** 없음(각 코드 스텝에 실제 코드). `DeviceRegistry.StatusOf`(string 반환)·`Forbidden()`(Task 반환)·`IsLoopback()`는 실측 확인됨 — 코드가 그에 맞게 작성됨.
- **타입 일관성:** `ConPtySession` 생성자/`Write`/`Resize(short,short)`/`Exited`/`Dispose`, `TerminalGate.IsAllowed(bool,string)`, WS 메시지 스키마(`ready/denied/exit`, `{t:"i",d}`/`{t:"r",cols,rows}`)가 Task 간 일치. 프론트 `openTerminal`/`refreshTermButton`/`showScreen('terminal')` 심볼 일치.
- **알려진 리스크:** ConPTY Dispose/파이프 소유 순서는 통합 테스트(Task3)가 1차 방어. `DeviceRegistry.StatusOf` 반환형은 Task4에서 실측 후 맞춤.
