# SP3 질문 알림(LAN 전용) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Claude가 입력을 기다릴 때(Notification 훅)를 감지해 연결된 모바일에 LAN WebSocket으로 알림을 띄우고, "답변하기"로 SP2 터미널에 딥링크한다. 외부로 나가지 않는다.

**Architecture:** Claude Code `Notification` 훅 → `agenthub-hook.js`(로컬) → `POST /api/hook/notification`(loopback) → `AgentMonitorService.BroadcastAsk` → 기존 `/ws/agents`로 `{type:"ask"}` broadcast(승인기기) → PWA가 Notification API + 배너 표시 → "답변하기" → SP2 `openTerminal()`. 훅 설치는 순수 `HookConfigMerger`(멱등 병합) + `HookInstaller`(백업 I/O), PC 콘솔 토글로 관리.

**Tech Stack:** C# 8 / .NET Framework 4.8, EmbedIO(기존), Newtonsoft.Json(기존), Node(훅 스크립트, 환경 존재), xUnit. **새 NuGet 없음.**

## Global Constraints

- 루트 네임스페이스 `AgentHub.*`. 서드파티 `EmbedIO/` 수정 금지. 한글(UTF-8) 인코딩 훼손 금지(Edit/바이트만).
- **새 NuGet 의존성 금지** (Web Push 폐기 확정).
- **외부 송신 절대 금지**: 모든 통신은 loopback 또는 LAN WebSocket. 훅→서버는 `https://127.0.0.1:<port>` loopback.
- 빌드는 **PowerShell**: `& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"` (git bash는 `/t:` `/p:` 훼손). 0 errors(기존 AppSettingsProvider.cs CS0168 경고 무방).
- 테스트: `dotnet test AgentHub.Tests/AgentHub.Tests.csproj`. 순수 파일은 소스 링크.
- `HookConfigMerger`는 순수(Newtonsoft만, I/O·로깅·WinForms 금지) — 소스 링크 테스트.
- 실측 확인: `AgentMonitorModule.BroadcastMessageAsync(string)` public이며 승인기기에만 broadcast. `AgentMonitorService`에 `_module`/`_sendGate` 존재. `ApiController`에 `IsLoopback()`(bool)·`Forbidden()`(Task). `DeviceRegistry.StatusOf`→string. node = `C:\Program Files\nodejs\node.exe`.
- Htmls 자산은 csproj `<Content Include="..."><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>`로 등록(기존 패턴). 신규 `.cs`는 `<Compile Include="..." />`.
- 작업 브랜치: `feature/sp3-question-notify` (생성됨, 스펙 커밋 존재).
- Notification 필터: `notification_type`가 비-actionable(auth_success, agent_completed, elicitation_complete, elicitation_response)이면 무시, 그 외(permission_prompt/idle_prompt/agent_needs_input/elicitation_dialog/미지정)는 알림.

---

### Task 1: `HookConfigMerger` (순수) + 테스트

settings.json(JSON)에 우리 Notification 훅을 멱등 추가/제거/조회.

**Files:**
- Create: `AgentHub/Server/Hook/HookConfigMerger.cs`
- Test: `AgentHub.Tests/HookConfigMergerTests.cs`

**Interfaces:**
- Produces:
  - `static bool HookConfigMerger.IsInstalled(string json, string marker)`
  - `static string HookConfigMerger.AddNotificationHook(string json, Newtonsoft.Json.Linq.JObject hookEntry, string marker)` — 멱등(마커 있으면 무변경)
  - `static string HookConfigMerger.RemoveNotificationHook(string json, string marker)`

- [ ] **Step 1: 실패 테스트 작성**

`AgentHub.Tests/HookConfigMergerTests.cs`:
```csharp
using AgentHub.Server.Hook;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentHub.Tests
{
    public class HookConfigMergerTests
    {
        private static JObject Entry() => new JObject
        {
            ["matcher"] = "",
            ["hooks"] = new JArray { new JObject
            {
                ["type"] = "command",
                ["command"] = "C:/n/node.exe",
                ["args"] = new JArray { "C:/app/hook/agenthub-hook.js" },
                ["async"] = true,
                ["timeout"] = 5
            }}
        };

        // clawd 항목이 이미 있는 기존 settings
        private const string Existing = "{\"hooks\":{\"Notification\":[{\"matcher\":\"\",\"hooks\":[{\"type\":\"command\",\"command\":\"clawd-hook.js\"}]}]}}";

        [Fact]
        public void Add_is_idempotent_and_preserves_existing()
        {
            var once = HookConfigMerger.AddNotificationHook(Existing, Entry(), "agenthub-hook.js");
            Assert.Contains("clawd-hook.js", once);            // 기존 보존
            Assert.Contains("agenthub-hook.js", once);          // 우리 것 추가
            var twice = HookConfigMerger.AddNotificationHook(once, Entry(), "agenthub-hook.js");
            var arr = (JArray)JObject.Parse(twice)["hooks"]["Notification"];
            Assert.Equal(2, arr.Count);                         // 중복 추가 안 됨(clawd 1 + 우리 1)
        }

        [Fact]
        public void Add_creates_structure_from_empty()
        {
            var res = HookConfigMerger.AddNotificationHook("{}", Entry(), "agenthub-hook.js");
            Assert.True(HookConfigMerger.IsInstalled(res, "agenthub-hook.js"));
        }

        [Fact]
        public void Remove_removes_only_ours()
        {
            var added = HookConfigMerger.AddNotificationHook(Existing, Entry(), "agenthub-hook.js");
            var removed = HookConfigMerger.RemoveNotificationHook(added, "agenthub-hook.js");
            Assert.DoesNotContain("agenthub-hook.js", removed);
            Assert.Contains("clawd-hook.js", removed);          // clawd 보존
        }

        [Fact]
        public void IsInstalled_false_on_empty_or_broken()
        {
            Assert.False(HookConfigMerger.IsInstalled("", "agenthub-hook.js"));
            Assert.False(HookConfigMerger.IsInstalled("not json", "agenthub-hook.js"));
            Assert.False(HookConfigMerger.IsInstalled("{}", "agenthub-hook.js"));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — `dotnet test AgentHub.Tests/AgentHub.Tests.csproj --filter HookConfigMergerTests` → FAIL(형식 없음).

- [ ] **Step 3: 구현**

`AgentHub/Server/Hook/HookConfigMerger.cs`:
```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentHub.Server.Hook
{
    /// <summary>~/.claude/settings.json의 Notification 훅을 멱등 추가/제거/조회(순수, Newtonsoft만).</summary>
    public static class HookConfigMerger
    {
        public static bool IsInstalled(string json, string marker)
        {
            var arr = NotificationArray(Parse(json), create: false);
            return arr != null && arr.ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string AddNotificationHook(string json, JObject hookEntry, string marker)
        {
            var root = Parse(json) ?? new JObject();
            var arr = NotificationArray(root, create: true);
            if (arr.ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                arr.Add(hookEntry);
            return root.ToString(Formatting.Indented);
        }

        public static string RemoveNotificationHook(string json, string marker)
        {
            var root = Parse(json);
            var arr = NotificationArray(root, create: false);
            if (arr == null) return json;
            for (int i = arr.Count - 1; i >= 0; i--)
                if (arr[i].ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    arr.RemoveAt(i);
            return root.ToString(Formatting.Indented);
        }

        private static JArray NotificationArray(JObject root, bool create)
        {
            if (root == null) return null;
            var hooks = root["hooks"] as JObject;
            if (hooks == null) { if (!create) return null; hooks = new JObject(); root["hooks"] = hooks; }
            var arr = hooks["Notification"] as JArray;
            if (arr == null) { if (!create) return null; arr = new JArray(); hooks["Notification"] = arr; }
            return arr;
        }

        private static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); } catch { return null; }
        }
    }
}
```

- [ ] **Step 4: 소스 링크 + 통과**

`AgentHub.Tests/AgentHub.Tests.csproj`에 추가: `<Compile Include="..\AgentHub\Server\Hook\HookConfigMerger.cs" Link="Linked\HookConfigMerger.cs" />`. `AgentHub/AgentHub.csproj`에 `<Compile Include="Server\Hook\HookConfigMerger.cs" />`. `dotnet test` → 전체 PASS.

- [ ] **Step 5: 커밋**
```bash
git add AgentHub/Server/Hook/HookConfigMerger.cs AgentHub.Tests/HookConfigMergerTests.cs AgentHub.Tests/AgentHub.Tests.csproj AgentHub/AgentHub.csproj
git commit -m "feat(sp3): HookConfigMerger 멱등 훅 병합 + 테스트"
```

---

### Task 2: `agenthub-hook.js` + endpoint.txt 기록 + csproj

**Files:**
- Create: `AgentHub/hook/agenthub-hook.js`
- Modify: `AgentHub/Server/EmbedIOServer.cs` (서버 시작 시 endpoint.txt 기록)
- Modify: `AgentHub/AgentHub.csproj` (hook 스크립트 Content 복사)

**Interfaces:**
- Produces: `<StartupPath>\hook\endpoint.txt`(현재 loopback 포트) — Task 3 HookInstaller가 스크립트 경로를, 훅 스크립트가 endpoint.txt를 사용.

- [ ] **Step 1: 훅 스크립트 작성**

`AgentHub/hook/agenthub-hook.js`:
```javascript
// Agent Hub 알림 훅: Claude Code Notification 이벤트를 로컬 Agent Hub 서버로 전달한다.
// 외부로 나가지 않는다(오직 127.0.0.1 loopback). async fire-and-forget.
const fs = require('fs');
const path = require('path');
const https = require('https');

let raw = '';
process.stdin.on('data', d => (raw += d));
process.stdin.on('error', () => process.exit(0));
process.stdin.on('end', () => {
  let p;
  try { p = JSON.parse(raw || '{}'); } catch (e) { process.exit(0); }
  let port;
  try { port = fs.readFileSync(path.join(__dirname, 'endpoint.txt'), 'utf8').trim(); } catch (e) { process.exit(0); }
  if (!port) process.exit(0);

  const body = JSON.stringify({
    session_id: p.session_id,
    cwd: p.cwd,
    message: p.message,
    notification_type: p.notification_type
  });
  const req = https.request({
    host: '127.0.0.1', port: Number(port), path: '/api/hook/notification', method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
    rejectUnauthorized: false, timeout: 3000
  }, res => { res.on('data', () => {}); res.on('end', () => process.exit(0)); });
  req.on('error', () => process.exit(0));
  req.on('timeout', () => { try { req.destroy(); } catch (e) {} process.exit(0); });
  req.write(body); req.end();
});
setTimeout(() => process.exit(0), 4000); // 안전망
```

- [ ] **Step 2: EmbedIOServer가 endpoint.txt 기록**

`StartServer()`에서 포트 확정(`CurrentPort`) 직후, 아래를 추가(파일 I/O 실패는 로그만):
```csharp
                try
                {
                    var hookDir = Path.Combine(Application.StartupPath, "hook");
                    if (!Directory.Exists(hookDir)) Directory.CreateDirectory(hookDir);
                    File.WriteAllText(Path.Combine(hookDir, "endpoint.txt"), CurrentPort.ToString());
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
```
(`System.IO`·`System.Windows.Forms.Application`은 이미 EmbedIOServer에서 사용 중 — 확인.)

- [ ] **Step 3: csproj에 훅 스크립트 등록**

`AgentHub/AgentHub.csproj`에 (기존 Content 패턴):
```xml
    <Content Include="hook\agenthub-hook.js">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
```

- [ ] **Step 4: 빌드 + 검증** — `node --check AgentHub/hook/agenthub-hook.js` → OK. PowerShell msbuild → 0 errors. 앱 실행 없이, `install/Debug/hook/agenthub-hook.js` 복사 확인(빌드 후).

- [ ] **Step 5: 커밋**
```bash
git add AgentHub/hook/agenthub-hook.js AgentHub/Server/EmbedIOServer.cs AgentHub/AgentHub.csproj
git commit -m "feat(sp3): agenthub-hook.js + 서버 시작 시 endpoint.txt 기록"
```

---

### Task 3: `HookInstaller` (I/O) + 설치/제거/상태 REST

**Files:**
- Create: `AgentHub/Server/Hook/HookInstaller.cs`
- Modify: `AgentHub/Server/Controller/ApiController.cs`

**Interfaces:**
- Produces: `static bool HookInstaller.Install()`, `static bool HookInstaller.Uninstall()`, `static bool HookInstaller.IsInstalled()`
- REST(loopback): `POST /api/hook/install`, `POST /api/hook/uninstall`, `GET /api/hook/status`

- [ ] **Step 1: `HookInstaller` 구현**

`AgentHub/Server/Hook/HookInstaller.cs`:
```csharp
using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using AgentHub.Common.Util;

namespace AgentHub.Server.Hook
{
    /// <summary>~/.claude/settings.json에 Agent Hub Notification 훅을 백업·멱등 설치/제거(I/O).</summary>
    public static class HookInstaller
    {
        private const string Marker = "agenthub-hook.js";

        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        private static string ScriptPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hook", "agenthub-hook.js");

        public static bool IsInstalled()
        {
            try { return HookConfigMerger.IsInstalled(ReadSettings(), Marker); }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Install()
        {
            try
            {
                var entry = new JObject
                {
                    ["matcher"] = "",
                    ["hooks"] = new JArray { new JObject
                    {
                        ["type"] = "command",
                        ["command"] = ResolveNode(),
                        ["args"] = new JArray { ScriptPath },
                        ["async"] = true,
                        ["timeout"] = 5
                    }}
                };
                var merged = HookConfigMerger.AddNotificationHook(ReadSettings(), entry, Marker);
                WriteSettingsWithBackup(merged);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Uninstall()
        {
            try
            {
                var removed = HookConfigMerger.RemoveNotificationHook(ReadSettings(), Marker);
                WriteSettingsWithBackup(removed);
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        private static string ReadSettings()
            => File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";

        private static void WriteSettingsWithBackup(string content)
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(SettingsPath))
                File.Copy(SettingsPath, SettingsPath + ".agenthub.bak", true);
            var tmp = SettingsPath + ".agenthub.tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            File.Move(tmp, SettingsPath);
        }

        private static string ResolveNode()
        {
            var pf = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(pf))
            {
                var p = Path.Combine(pf, "nodejs", "node.exe");
                if (File.Exists(p)) return p;
            }
            try
            {
                var psi = new ProcessStartInfo("where", "node")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var proc = Process.Start(psi))
                {
                    var outp = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                    var first = (outp ?? "").Split('\n')[0].Trim();
                    if (!string.IsNullOrEmpty(first) && File.Exists(first)) return first;
                }
            }
            catch { /* fall through */ }
            return "node"; // PATH 폴백
        }
    }
}
```

- [ ] **Step 2: REST 엔드포인트 추가 (`ApiController`)**
```csharp
        [Route(HttpVerbs.Get, "/hook/status")]
        public Task HookStatus()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { installed = AgentHub.Server.Hook.HookInstaller.IsInstalled() }));
        }

        [Route(HttpVerbs.Post, "/hook/install")]
        public Task HookInstall()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = AgentHub.Server.Hook.HookInstaller.Install() }));
        }

        [Route(HttpVerbs.Post, "/hook/uninstall")]
        public Task HookUninstall()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = AgentHub.Server.Hook.HookInstaller.Uninstall() }));
        }
```
`AgentHub/AgentHub.csproj`에 `<Compile Include="Server\Hook\HookInstaller.cs" />` 추가.

- [ ] **Step 3: 빌드** → 0 errors.

- [ ] **Step 4: 커밋**
```bash
git add AgentHub/Server/Hook/HookInstaller.cs AgentHub/Server/Controller/ApiController.cs AgentHub/AgentHub.csproj
git commit -m "feat(sp3): HookInstaller(백업·멱등) + /api/hook install/uninstall/status"
```

---

### Task 4: `/api/hook/notification` 수신 + `BroadcastAsk`

**Files:**
- Modify: `AgentHub/Server/Controller/ApiController.cs`
- Modify: `AgentHub/Server/Agents/AgentMonitorService.cs`

**Interfaces:**
- Produces: `static void AgentMonitorService.BroadcastAsk(string project, string message, string sessionId)`
- REST(loopback): `POST /api/hook/notification`

- [ ] **Step 1: `BroadcastAsk` 추가 (`AgentMonitorService`)**

기존 `_sendGate`를 재사용해 OnChanged의 send와 직렬화(같은 컨텍스트 동시 SendAsync 방지):
```csharp
        public static async void BroadcastAsk(string project, string message, string sessionId)
        {
            var msg = Json.Serialize(new
            {
                type = "ask",
                project,
                message,
                sessionId,
                at = DateTime.UtcNow.ToString("o")
            });
            await _sendGate.WaitAsync();
            try { if (_module != null) await _module.BroadcastMessageAsync(msg); }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _sendGate.Release(); }
        }
```

- [ ] **Step 2: `/api/hook/notification` 추가 (`ApiController`)**

`using Newtonsoft.Json.Linq;` 추가(없으면). 메서드:
```csharp
        [Route(HttpVerbs.Post, "/hook/notification")]
        public async Task HookNotification()
        {
            if (!IsLoopback()) { await Forbidden(); return; }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            try
            {
                var o = JObject.Parse(raw);
                var ntype = ((string)o["notification_type"] ?? "").ToLowerInvariant();
                // 비-actionable 타입만 무시, 그 외(및 미지정)는 알림
                var skip = ntype == "auth_success" || ntype == "agent_completed"
                        || ntype == "elicitation_complete" || ntype == "elicitation_response";
                if (!skip)
                {
                    var cwd = (string)o["cwd"] ?? "";
                    var project = LastSegment(cwd);
                    var message = (string)o["message"] ?? "입력이 필요합니다";
                    AgentMonitorService.BroadcastAsk(project, message, (string)o["session_id"]);
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            await SendJsonAsync(Json.Serialize(new { ok = true }));
        }

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var t = path.Replace('\\', '/').TrimEnd('/');
            var i = t.LastIndexOf('/');
            return i >= 0 ? t.Substring(i + 1) : t;
        }
```
(`LastSegment`가 이미 컨트롤러에 있으면 재정의하지 말 것.)

- [ ] **Step 3: 빌드** → 0 errors.

- [ ] **Step 4: 커밋**
```bash
git add AgentHub/Server/Controller/ApiController.cs AgentHub/Server/Agents/AgentMonitorService.cs
git commit -m "feat(sp3): /api/hook/notification 수신 + BroadcastAsk(ask WS 알림)"
```

---

### Task 5: 프론트엔드 — 알림 권한 + ask 배너 + 딥링크

**Files:**
- Modify: `AgentHub/View/Htmls/index.html`, `js/app.js`, `js/i18n.js`, `css/app.css`, `sw.js`

**Interfaces:**
- Consumes(WS): `{type:"ask", project, message, sessionId, at}`
- Uses: `window.openTerminal()` (SP2), `showScreen`, `t`

- [ ] **Step 1: `index.html` — 알림 버튼 + 배너**

모니터 화면의 `#termBtn` 옆(또는 위)에:
```html
      <button id="notifyBtn" class="term-btn" data-i18n="notify.on">🔔 알림 켜기</button>
```
`<main>` 안(헤더 아래, screens 위)에 배너:
```html
    <div id="askBanner" class="ask-banner" hidden>
      <div class="ask-text"><b id="askProject"></b> <span id="askMsg"></span></div>
      <div class="ask-actions">
        <button id="askAnswer" data-i18n="ask.answer">답변하기</button>
        <button id="askDismiss" data-i18n="ask.dismiss">닫기</button>
      </div>
    </div>
```

- [ ] **Step 2: `js/app.js` — 권한/수신/배너/딥링크**

(a) `ws.onmessage` 스위치에 추가:
```javascript
      else if (m.type === 'ask') { handleAsk(m); }
```
(b) 파일 하단에 추가:
```javascript
    function refreshNotifyBtn() {
      const b = document.getElementById('notifyBtn');
      if (!b || !('Notification' in window)) { if (b) b.hidden = true; return; }
      b.hidden = (Notification.permission === 'granted');
    }
    document.getElementById('notifyBtn') && document.getElementById('notifyBtn').addEventListener('click', async () => {
      if (!('Notification' in window)) return;
      try { await Notification.requestPermission(); } catch (_) {}
      refreshNotifyBtn();
    });

    let lastAsk = null;
    function handleAsk(m) {
      lastAsk = m;
      if (('Notification' in window) && Notification.permission === 'granted') {
        try { new Notification(t('ask.title'), { body: (m.project ? '[' + m.project + '] ' : '') + (m.message || ''), tag: m.sessionId || 'ask' }); } catch (_) {}
      }
      const banner = document.getElementById('askBanner');
      document.getElementById('askProject').textContent = m.project || '';
      document.getElementById('askMsg').textContent = m.message || '';
      banner.hidden = false;
    }
    document.getElementById('askAnswer') && document.getElementById('askAnswer').addEventListener('click', () => {
      document.getElementById('askBanner').hidden = true;
      if (window.openTerminal) window.openTerminal();
    });
    document.getElementById('askDismiss') && document.getElementById('askDismiss').addEventListener('click', () => {
      document.getElementById('askBanner').hidden = true;
    });
```
(c) 모니터 진입 시 `refreshNotifyBtn()` 호출 — 기존 `sessions` 핸들러의 monitor 표시 분기에 `refreshNotifyBtn()` 추가(SP2의 `refreshTermButton()` 옆). 최초 로드 시에도 1회 호출.

- [ ] **Step 3: `js/i18n.js` — notify/ask 키(ko/en)**

ko: `'notify.on':'🔔 알림 켜기'`, `'ask.title':'Claude가 입력을 기다립니다'`, `'ask.answer':'답변하기'`, `'ask.dismiss':'닫기'`.
en: `'notify.on':'🔔 Enable alerts'`, `'ask.title':'Claude is waiting for your input'`, `'ask.answer':'Answer'`, `'ask.dismiss':'Dismiss'`.

- [ ] **Step 4: `css/app.css` — 배너 스타일(파일 끝)**
```css
.ask-banner { margin: 0 0 10px; padding: 10px 12px; background: #3a2f14; border: 1px solid #6b5a1e; border-radius: 10px; display: flex; flex-direction: column; gap: 8px; }
.ask-text { color: #fde68a; font-size: 13px; word-break: break-word; }
.ask-actions { display: flex; gap: 8px; }
.ask-actions button { flex: 1; padding: 8px; border-radius: 8px; border: none; cursor: pointer; }
#askAnswer { background: #7aa2ff; color: #0b0f1a; font-weight: 600; }
#askDismiss { background: #2a3145; color: #c9d1e4; }
```

- [ ] **Step 5: `sw.js` — 캐시 버전 상향** — `CACHE`를 `'agent-hub-v6'`로.

- [ ] **Step 6: 검증** — `node --check` app.js/i18n.js → OK. PowerShell msbuild → 0 errors. 요소 ID(notifyBtn/askBanner/askProject/askMsg/askAnswer/askDismiss)와 JS 참조 일치, esc 불필요(textContent 사용) 확인.

- [ ] **Step 7: 커밋**
```bash
git add AgentHub/View/Htmls/
git commit -m "feat(sp3): 모바일 알림 권한 + ask 배너 + 터미널 딥링크"
```

---

### Task 6: PC 콘솔 — 훅 설치/제거 UI

**Files:**
- Modify: `AgentHub/View/Htmls/host.html`, `js/host.js`, `js/i18n.js`

- [ ] **Step 1: `host.html` 설정 폼에 훅 항목 추가** (터미널 항목 아래)
```html
        <hr />
        <label class="chk"><input type="checkbox" id="hookEnabled" /> <span data-i18n="settings.hookEnable">질문 알림 훅 설치 (Claude 입력 대기 시 연결된 폰에 알림)</span></label>
        <p class="hint" data-i18n="settings.hookNote">설치 시 ~/.claude/settings.json에 항목을 추가합니다(기존 훅 보존, 백업 생성). 앱 업데이트 후 경로가 바뀌면 다시 설치하세요.</p>
        <p class="hint" id="hookHint"></p>
```

- [ ] **Step 2: `js/host.js` — 상태 로드 + 토글**

`loadSettings()`에 추가:
```javascript
  try { const h = await (await fetch('/api/hook/status')).json(); $('#hookEnabled').checked = !!h.installed; } catch (e) {}
```
토글 핸들러:
```javascript
$('#hookEnabled').addEventListener('change', async () => {
  const hint = $('#hookHint');
  hint.textContent = t('settings.saving');
  try {
    const url = $('#hookEnabled').checked ? '/api/hook/install' : '/api/hook/uninstall';
    const res = await (await fetch(url, { method: 'POST' })).json();
    hint.textContent = res.ok ? t('settings.saved').replace('{url}', '') : t('settings.error');
    if (!res.ok) $('#hookEnabled').checked = !$('#hookEnabled').checked; // 실패 시 되돌림
  } catch (e) { hint.textContent = t('settings.reqFail') + e.message; $('#hookEnabled').checked = !$('#hookEnabled').checked; }
});
```

- [ ] **Step 3: `js/i18n.js` — settings.hookEnable/hookNote(ko/en)**

- [ ] **Step 4: 검증** — `node --check host.js/i18n.js` → OK. PowerShell msbuild → 0 errors.

- [ ] **Step 5: 커밋**
```bash
git add AgentHub/View/Htmls/host.html AgentHub/View/Htmls/js/host.js AgentHub/View/Htmls/js/i18n.js
git commit -m "feat(sp3): PC 콘솔 질문 알림 훅 설치/제거 토글"
```

---

### Task 7: E2E 검증 + 빌드 게이트 + 마무리

- [ ] **Step 1: 전체 테스트** — `dotnet test AgentHub.Tests/AgentHub.Tests.csproj` → 전체 PASS(HookConfigMerger 포함).
- [ ] **Step 2: 빌드 게이트** — PowerShell msbuild Restore+Build → 0 errors, `install/Debug/AgentHub.exe` + `install/Debug/hook/agenthub-hook.js`.
- [ ] **Step 3: 실제 플로우(사용자 수동, verify)**: PC 콘솔에서 "질문 알림 훅 설치" ON → `~/.claude/settings.json`에 항목 추가 확인(clawd 항목 보존) → 폰(PWA)에서 "🔔 알림 켜기" 허용 → 실제 claude가 권한을 물을 때 폰에 알림+배너 → "답변하기" → 터미널에서 응답 → claude 진행. 훅 제거 시 알림 안 옴. 외부 트래픽 0(방화벽/netstat로 확인 권장).
- [ ] **Step 4: 스펙 대비 점검** — `docs/superpowers/specs/2026-07-08-sp3-question-notify-design.md` 커버 확인.
- [ ] **Step 5: 브랜치 마무리** — `superpowers:finishing-a-development-branch`.

---

## Self-Review (계획 검증)

- **스펙 커버리지:** 훅 병합=Task1, 훅 스크립트+endpoint=Task2, 설치 I/O+REST=Task3, 수신+ask broadcast=Task4, 프론트 알림/배너/딥링크=Task5, PC 콘솔 UI=Task6, 검증=Task7. 외부 금지(전부 loopback/LAN), 필터(notification_type), settings.json 백업·멱등 모두 반영.
- **플레이스홀더:** 없음. `AgentMonitorModule.BroadcastMessageAsync`(public)·`_sendGate`·`IsLoopback()`·`Forbidden()`·node 경로 실측 확인됨.
- **타입 일관성:** `BroadcastAsk(project,message,sessionId)`, `HookConfigMerger.Add/Remove/IsInstalled(string,...)`, `HookInstaller.Install/Uninstall/IsInstalled()`, WS `{type:"ask",project,message,sessionId,at}`, 프론트 `handleAsk`/`refreshNotifyBtn`/`openTerminal` 심볼 일치.
- **알려진 한계(재확인):** 닫힌 앱 푸시 불가(연결 중 알림), 답변은 웹 터미널 세션만, 앱 업데이트 시 훅 경로 재설치 필요 — 모두 스펙에 명시.
