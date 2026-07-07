# 기기 등록·인증 시스템 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 승인된 기기만 Agent Hub 모니터에 접속하도록 기기 등록·인증 체계를 구축한다.

**Architecture:** 모바일 브라우저가 최초 접속 시 랜덤 토큰(UUID)을 생성해 `localStorage`에 저장하고, 이 토큰으로 인증을 요청한다. 서버는 토큰의 SHA-256 해시와 상태(pending/approved/revoked)만 JSON 파일(`%LOCALAPPDATA%\AgentHub\devices.json`)에 영속 저장한다. 승인은 PC(loopback) 전용이며, 승인/해제/삭제는 해당 기기의 WebSocket으로 실시간 push되어 즉시 반영된다.

**Tech Stack:** C# 8 / .NET Framework 4.8 / WinForms, EmbedIO(서드파티), Newtonsoft.Json, HTML + Vanilla JS.

## Global Constraints

- 자체 코드 루트 네임스페이스는 `AgentHub`. 새 코드도 `AgentHub.*`.
- 서드파티 `EmbedIO/` 디렉터리와 `EmbedIO` 네임스페이스 소스는 **수정 금지**.
- C# 소스·HTML·리소스의 **한글(UTF-8)** 문자열 인코딩을 훼손하지 말 것. 편집은 Edit 도구로 국소 변경.
- JSON 직렬화는 항상 `AgentHub.Common.Util.Json`(camelCase) 사용.
- 빌드 검증 커맨드:
  ```powershell
  msbuild AgentHub.sln /t:Restore
  msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
  ```
- 상태 문자열은 소문자 `none` / `pending` / `approved` / `revoked` (프론트엔드와 일치).
- 커밋은 각 태스크에서 **해당 파일만 명시적으로 `git add`** 한다(작업 트리에 무관한 미커밋 변경분이 섞이지 않도록).

---

## File Structure

**신규**
- `AgentHub/Common/Models/Device.cs` — 기기 레코드(`Device`), 안전 투영(`DeviceView`), 상태 상수(`DeviceStatus`), 요청 DTO(`DeviceRequestBody`).
- `AgentHub/Server/Devices/DeviceRegistry.cs` — 정적·thread-safe 저장소: JSON 로드/원자적 저장, 토큰 해시, 상태 전이, 이벤트.

**변경**
- `AgentHub/Server/Socket/AgentMonitorModule.cs` — 토큰 게이트, auth 상태 push, 토큰↔소켓 매핑, 승인 브로드캐스트 필터.
- `AgentHub/Server/Socket/HostMonitorModule.cs` — devices 목록 broadcast 추가.
- `AgentHub/Server/Controller/ApiController.cs` — 기기 엔드포인트 + loopback 가드 + `/agents` 게이트.
- `AgentHub/Server/EmbedIOServer.cs` — `LocalUrl`, `/host` loopback 가드, DeviceRegistry 로드.
- `AgentHub/View/Forms/FormMain.cs` — WebView2를 127.0.0.1로 로드, 신규 pending 시 풍선알림.
- `AgentHub/View/Htmls/index.html`, `AgentHub/View/Htmls/js/app.js` — 요청/대기/모니터 화면.
- `AgentHub/View/Htmls/host.html`, `AgentHub/View/Htmls/js/host.js` — 기기 관리 UI.
- `AgentHub/View/Htmls/css/app.css` — 스타일 추가.

---

## Task 0: 작업 브랜치 생성

- [ ] **Step 1: 현재 main에서 feature 브랜치 생성**

작업 트리에 대규모 미커밋 변경분이 있으므로, 그대로 두고 브랜치만 전환한다.

```bash
git checkout -b feature/device-auth
```

Expected: `Switched to a new branch 'feature/device-auth'` (미커밋 변경분은 유지됨).

---

## Task 1: 기기 모델 + DeviceRegistry (영속 저장소)

**Files:**
- Create: `AgentHub/Common/Models/Device.cs`
- Create: `AgentHub/Server/Devices/DeviceRegistry.cs`

**Interfaces:**
- Produces:
  - `AgentHub.Common.Models.DeviceStatus`: `const string None="none", Pending="pending", Approved="approved", Revoked="revoked"`.
  - `AgentHub.Common.Models.Device` { `string Id, TokenHash, Name, Ip, UserAgent, Status, RequestedAt, ApprovedAt, LastSeenAt` }.
  - `AgentHub.Common.Models.DeviceView` { `string Id, Name, Ip, UserAgent, Status, RequestedAt, ApprovedAt, LastSeenAt` }.
  - `AgentHub.Common.Models.DeviceRequestBody` { `string Name` }.
  - `AgentHub.Server.Devices.DeviceRegistry`:
    - `event Action Changed;`
    - `event Action<string,string> StatusChanged;` (tokenHash, status)
    - `void Load();`
    - `string HashToken(string token);`
    - `string StatusOf(string token);`
    - `string StatusByHash(string hash);`
    - `Device FindByToken(string token);`
    - `string Request(string token, string name, string ip, string userAgent);` (반환: tokenHash)
    - `bool Approve(string id); bool Revoke(string id); bool Delete(string id);`
    - `void MarkSeen(string token);`
    - `List<DeviceView> Snapshot();`

- [ ] **Step 1: `Device.cs` 작성**

```csharp
namespace AgentHub.Common.Models
{
    /// <summary>기기 상태 문자열(프론트엔드와 일치).</summary>
    public static class DeviceStatus
    {
        public const string None = "none";
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Revoked = "revoked";
    }

    /// <summary>등록/승인 대상 기기(영속). TokenHash는 비밀 — 클라이언트에 전송하지 않는다.</summary>
    public class Device
    {
        public string Id { get; set; }         // 공개 GUID (승인/삭제 대상 지정)
        public string TokenHash { get; set; }  // 토큰 SHA-256 (비밀)
        public string Name { get; set; }
        public string Ip { get; set; }
        public string UserAgent { get; set; }
        public string Status { get; set; }     // none/pending/approved/revoked
        public string RequestedAt { get; set; }
        public string ApprovedAt { get; set; }
        public string LastSeenAt { get; set; }
    }

    /// <summary>콘솔/응답에 노출하는 안전한 투영(토큰/해시 제외).</summary>
    public class DeviceView
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public string UserAgent { get; set; }
        public string Status { get; set; }
        public string RequestedAt { get; set; }
        public string ApprovedAt { get; set; }
        public string LastSeenAt { get; set; }
    }

    /// <summary>POST /api/devices/request 요청 본문.</summary>
    public class DeviceRequestBody
    {
        public string Name { get; set; }
    }
}
```

- [ ] **Step 2: `DeviceRegistry.cs` 작성**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AgentHub.Common.Models;
using AgentHub.Common.Util;

namespace AgentHub.Server.Devices
{
    /// <summary>
    /// 등록 기기 저장소(정적, thread-safe, 파일 영속).
    /// 토큰 원문은 저장하지 않고 SHA-256 해시만 보관한다. 조회 키는 TokenHash.
    /// </summary>
    public static class DeviceRegistry
    {
        private static readonly ConcurrentDictionary<string, Device> ByHash =
            new ConcurrentDictionary<string, Device>();
        private static readonly object SaveLock = new object();
        private static string _filePath;

        /// <summary>목록 변경(추가/상태변경/삭제) 시 — 호스트 콘솔 갱신용.</summary>
        public static event Action Changed;

        /// <summary>특정 기기 상태 변경(tokenHash, status) — 모바일 소켓 push용.</summary>
        public static event Action<string, string> StatusChanged;

        public static void Load()
        {
            _filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentHub", "devices.json");
            try
            {
                if (!File.Exists(_filePath)) return;
                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                var list = Json.Deserialize<List<Device>>(json) ?? new List<Device>();
                ByHash.Clear();
                foreach (var d in list)
                    if (!string.IsNullOrEmpty(d.TokenHash)) ByHash[d.TokenHash] = d;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        public static string HashToken(string token)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token ?? ""));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string StatusOf(string token)
            => string.IsNullOrEmpty(token) ? DeviceStatus.None : StatusByHash(HashToken(token));

        public static string StatusByHash(string hash)
            => ByHash.TryGetValue(hash ?? "", out var d) ? d.Status : DeviceStatus.None;

        public static Device FindByToken(string token)
            => string.IsNullOrEmpty(token) ? null
               : (ByHash.TryGetValue(HashToken(token), out var d) ? d : null);

        /// <summary>인증 요청 등록(또는 기존 갱신). 이미 승인된 기기는 승인 유지.</summary>
        public static string Request(string token, string name, string ip, string userAgent)
        {
            var hash = HashToken(token);
            var now = DateTime.UtcNow.ToString("o");
            var d = ByHash.AddOrUpdate(hash,
                _ => new Device
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TokenHash = hash,
                    Name = name,
                    Ip = ip,
                    UserAgent = userAgent,
                    Status = DeviceStatus.Pending,
                    RequestedAt = now,
                    LastSeenAt = now
                },
                (_, existing) =>
                {
                    existing.Name = name;
                    existing.Ip = ip;
                    existing.UserAgent = userAgent;
                    if (existing.Status != DeviceStatus.Approved)
                    {
                        existing.Status = DeviceStatus.Pending;
                        existing.RequestedAt = now;
                    }
                    existing.LastSeenAt = now;
                    return existing;
                });
            Save();
            Changed?.Invoke();
            StatusChanged?.Invoke(hash, d.Status);
            return hash;
        }

        public static bool Approve(string id) => SetStatusById(id, DeviceStatus.Approved);
        public static bool Revoke(string id) => SetStatusById(id, DeviceStatus.Revoked);

        public static bool Delete(string id)
        {
            var entry = ByHash.FirstOrDefault(kv => kv.Value.Id == id);
            if (entry.Value == null) return false;
            if (!ByHash.TryRemove(entry.Key, out _)) return false;
            Save();
            Changed?.Invoke();
            StatusChanged?.Invoke(entry.Key, DeviceStatus.Revoked); // 접속 차단
            return true;
        }

        public static void MarkSeen(string token)
        {
            var d = FindByToken(token);
            if (d == null) return;
            d.LastSeenAt = DateTime.UtcNow.ToString("o"); // 빈번 → 저장 생략(상태변화 아님)
        }

        public static List<DeviceView> Snapshot()
            => ByHash.Values.OrderBy(d => d.RequestedAt).Select(ToView).ToList();

        private static DeviceView ToView(Device d) => new DeviceView
        {
            Id = d.Id, Name = d.Name, Ip = d.Ip, UserAgent = d.UserAgent,
            Status = d.Status, RequestedAt = d.RequestedAt,
            ApprovedAt = d.ApprovedAt, LastSeenAt = d.LastSeenAt
        };

        private static bool SetStatusById(string id, string status)
        {
            var d = ByHash.Values.FirstOrDefault(x => x.Id == id);
            if (d == null) return false;
            d.Status = status;
            if (status == DeviceStatus.Approved) d.ApprovedAt = DateTime.UtcNow.ToString("o");
            Save();
            Changed?.Invoke();
            StatusChanged?.Invoke(d.TokenHash, status);
            return true;
        }

        private static void Save()
        {
            lock (SaveLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var json = Json.Serialize(ByHash.Values.ToList());
                    var tmp = _filePath + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
                    else File.Move(tmp, _filePath);
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
        }
    }
}
```

- [ ] **Step 3: `.csproj`에 신규 파일 포함 확인**

`AgentHub/AgentHub.csproj`가 SDK 스타일이 아니라 옛 형식이면 `<Compile Include="...">` 항목을 추가해야 한다. 먼저 형식을 확인한다.

Run:
```powershell
Select-String -Path AgentHub/AgentHub.csproj -Pattern '<Compile Include' -SimpleMatch | Select-Object -First 1
```
Expected: `<Compile Include=...` 가 출력되면 **옛 형식** → 다음 두 항목을 `<ItemGroup>` 내 다른 `<Compile>`들과 같은 위치에 추가한다:
```xml
<Compile Include="Common\Models\Device.cs" />
<Compile Include="Server\Devices\DeviceRegistry.cs" />
```
출력이 없으면 SDK 스타일(자동 포함)이므로 이 단계는 생략.

- [ ] **Step 4: 빌드로 컴파일 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: `Build succeeded`. (로직 동작은 Task 3에서 실행 중 서버로 검증.)

- [ ] **Step 5: 커밋**

```bash
git add AgentHub/Common/Models/Device.cs AgentHub/Server/Devices/DeviceRegistry.cs AgentHub/AgentHub.csproj
git commit -m "feat(auth): add Device model and persistent DeviceRegistry"
```

---

## Task 2: AgentMonitorModule 토큰 게이트 + HostMonitorModule devices broadcast

**Files:**
- Modify: `AgentHub/Server/Socket/AgentMonitorModule.cs`
- Modify: `AgentHub/Server/Socket/HostMonitorModule.cs`

**Interfaces:**
- Consumes: `DeviceRegistry.StatusOf/HashToken/StatusByHash/MarkSeen/StatusChanged`, `DeviceStatus`, `MonitorClientRegistry.Add/Remove`, `AgentMonitorService.CurrentAgentsMessage()`.
- Produces: `AgentMonitorModule.BroadcastMessageAsync(string)` 는 이제 **승인된 소켓에만** 전송(기존 시그니처 유지 — `AgentMonitorService.Tick`이 그대로 호출).

- [ ] **Step 1: `AgentMonitorModule.cs` 전체 교체**

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Agents;
using AgentHub.Server.Devices;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 모바일 모니터용 WebSocket (route: /ws/agents?token=...).
    /// 접속 시 토큰 상태를 판별해 auth 메시지를 보낸다. 승인된 경우에만 레지스트리 등록 +
    /// 에이전트 스냅샷 전송. 승인/해제는 DeviceRegistry.StatusChanged 구독으로 실시간 push.
    /// </summary>
    public class AgentMonitorModule : WebSocketModule
    {
        // contextId -> tokenHash
        private readonly ConcurrentDictionary<string, string> _tokens =
            new ConcurrentDictionary<string, string>();

        public AgentMonitorModule(string urlPath) : base(urlPath, true)
        {
            DeviceRegistry.StatusChanged += OnDeviceStatusChanged;
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            var token = GetToken(context);
            var status = DeviceRegistry.StatusOf(token);
            if (!string.IsNullOrEmpty(token))
                _tokens[context.Id] = DeviceRegistry.HashToken(token);

            await SendAsync(context, AuthMessage(status));

            if (status == DeviceStatus.Approved)
                await ActivateAsync(context, token);
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            _tokens.TryRemove(context.Id, out _);
            MonitorClientRegistry.Remove(context.Id);
            return Task.CompletedTask;
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
            => Task.CompletedTask; // 조회 전용

        /// <summary>서비스에서 호출 — 승인된 소켓에만 broadcast.</summary>
        public Task BroadcastMessageAsync(string message)
            => BroadcastAsync(message, ctx =>
                _tokens.TryGetValue(ctx.Id, out var h)
                && DeviceRegistry.StatusByHash(h) == DeviceStatus.Approved);

        private async void OnDeviceStatusChanged(string hash, string status)
        {
            foreach (var ctx in ActiveContexts)
            {
                if (!_tokens.TryGetValue(ctx.Id, out var h) || h != hash) continue;
                try
                {
                    await SendAsync(ctx, AuthMessage(status));
                    if (status == DeviceStatus.Approved)
                        await ActivateAsync(ctx, null);
                    else
                        MonitorClientRegistry.Remove(ctx.Id);
                }
                catch { /* per-socket 실패 무시 */ }
            }
        }

        private async Task ActivateAsync(IWebSocketContext context, string tokenForSeen)
        {
            if (tokenForSeen != null) DeviceRegistry.MarkSeen(tokenForSeen);
            var ip = context.RemoteEndPoint?.Address?.ToString() ?? "unknown";
            var ua = context.Headers?["User-Agent"] ?? "unknown";
            MonitorClientRegistry.Add(context.Id, ip, ua);
            await SendAsync(context, AgentMonitorService.CurrentAgentsMessage());
        }

        private static string GetToken(IWebSocketContext ctx)
        {
            var q = ctx.RequestUri?.Query;
            if (string.IsNullOrEmpty(q)) return null;
            foreach (var pair in q.TrimStart('?').Split('&'))
            {
                var i = pair.IndexOf('=');
                if (i > 0 && pair.Substring(0, i) == "token")
                    return Uri.UnescapeDataString(pair.Substring(i + 1));
            }
            return null;
        }

        private static string AuthMessage(string status)
            => Json.Serialize(new { type = "auth", status });

        protected override void Dispose(bool disposing)
        {
            if (disposing) DeviceRegistry.StatusChanged -= OnDeviceStatusChanged;
            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 2: `HostMonitorModule.cs`에 devices broadcast 추가**

`ClientsMessage()` 아래에 devices 메시지를 추가하고, 접속/변경 시 함께 보낸다. `DeviceRegistry.Changed`도 구독한다.

전체 교체:
```csharp
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using AgentHub.Common.Util;
using AgentHub.Server.Devices;

namespace AgentHub.Server.Socket
{
    /// <summary>
    /// 호스트 콘솔(/host)용 WebSocket (route: /ws/host).
    /// 접속한 라이브 클라이언트 목록(clients)과 등록 기기 목록(devices)을 실시간 전달한다.
    /// </summary>
    public class HostMonitorModule : WebSocketModule
    {
        public HostMonitorModule(string urlPath) : base(urlPath, true)
        {
            MonitorClientRegistry.Changed += OnRegistryChanged;
            DeviceRegistry.Changed += OnDevicesChanged;
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            await SendAsync(context, ClientsMessage());
            await SendAsync(context, DevicesMessage());
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
            => Task.CompletedTask;

        private async void OnRegistryChanged()
        {
            try { await BroadcastAsync(ClientsMessage()); }
            catch { /* broadcast 실패 무시 */ }
        }

        private async void OnDevicesChanged()
        {
            try { await BroadcastAsync(DevicesMessage()); }
            catch { /* broadcast 실패 무시 */ }
        }

        private static string ClientsMessage() => Json.Serialize(new
        {
            type = "clients",
            count = MonitorClientRegistry.Count,
            clients = MonitorClientRegistry.Snapshot()
        });

        private static string DevicesMessage() => Json.Serialize(new
        {
            type = "devices",
            devices = DeviceRegistry.Snapshot()
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MonitorClientRegistry.Changed -= OnRegistryChanged;
                DeviceRegistry.Changed -= OnDevicesChanged;
            }
            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 3: 빌드 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: `Build succeeded`.

- [ ] **Step 4: 커밋**

```bash
git add AgentHub/Server/Socket/AgentMonitorModule.cs AgentHub/Server/Socket/HostMonitorModule.cs
git commit -m "feat(auth): gate /ws/agents by device token and broadcast device list"
```

---

## Task 3: ApiController 기기 엔드포인트 + loopback 가드 + /agents 게이트

**Files:**
- Modify: `AgentHub/Server/Controller/ApiController.cs`

**Interfaces:**
- Consumes: `DeviceRegistry.*`, `DeviceStatus`, `DeviceRequestBody`, `IPAddress.IsLoopback`.
- Produces (REST):
  - `GET /api/devices/status` → `{ status }`
  - `POST /api/devices/request` (헤더 `X-Device-Token`, body `{ name }`) → `{ ok, status }`
  - `GET /api/devices` (loopback) → `{ devices:[DeviceView] }`
  - `POST /api/devices/{id}/approve` (loopback) → `{ ok }`
  - `POST /api/devices/{id}/revoke` (loopback) → `{ ok }`
  - `DELETE /api/devices/{id}` (loopback) → `{ ok }`
  - `GET /api/agents` — 승인된 토큰만 200, 아니면 401 `{ ok:false, status }`

- [ ] **Step 1: using 추가**

파일 상단 using 블록에 다음을 추가한다(기존 순서 유지, 알파벳/기존 스타일에 맞춰 삽입):
```csharp
using System.Net;
using AgentHub.Server.Devices;
```

- [ ] **Step 2: 기존 `/agents` 엔드포인트를 게이트 버전으로 교체**

기존:
```csharp
        // 실시간은 WebSocket(/ws/agents). 이 엔드포인트는 초기 로드/폴백용 스냅샷.
        [Route(HttpVerbs.Get, "/agents")]
        public Task Agents() => SendJsonAsync(AgentMonitorService.CurrentAgentsSnapshot());
```
교체:
```csharp
        // 실시간은 WebSocket(/ws/agents). 이 엔드포인트는 승인된 기기용 스냅샷 폴백.
        [Route(HttpVerbs.Get, "/agents")]
        public Task Agents()
        {
            var status = DeviceRegistry.StatusOf(DeviceToken());
            if (status != DeviceStatus.Approved)
            {
                HttpContext.Response.StatusCode = 401;
                return SendJsonAsync(Json.Serialize(new { ok = false, status }));
            }
            return SendJsonAsync(AgentMonitorService.CurrentAgentsSnapshot());
        }
```

- [ ] **Step 3: 기기 엔드포인트 추가**

`Agents()` 엔드포인트 바로 아래에 추가:
```csharp
        // ---- 기기 인증 (모바일) ----

        [Route(HttpVerbs.Get, "/devices/status")]
        public Task DeviceStatusEndpoint()
            => SendJsonAsync(Json.Serialize(new { status = DeviceRegistry.StatusOf(DeviceToken()) }));

        [Route(HttpVerbs.Post, "/devices/request")]
        public async Task DeviceRequest()
        {
            var token = DeviceToken();
            if (string.IsNullOrEmpty(token))
            {
                HttpContext.Response.StatusCode = 400;
                await SendJsonAsync(Json.Serialize(new { ok = false, message = "토큰이 없습니다." }));
                return;
            }
            var raw = await HttpContext.GetRequestBodyAsStringAsync();
            var body = Json.Deserialize<DeviceRequestBody>(raw) ?? new DeviceRequestBody();
            var ip = HttpContext.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
            var ua = HttpContext.Request.Headers["User-Agent"] ?? "unknown";
            DeviceRegistry.Request(token, (body.Name ?? "").Trim(), ip, ua);
            await SendJsonAsync(Json.Serialize(new { ok = true, status = DeviceRegistry.StatusOf(token) }));
        }

        // ---- 기기 관리 (PC/loopback 전용) ----

        [Route(HttpVerbs.Get, "/devices")]
        public Task Devices()
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { devices = DeviceRegistry.Snapshot() }));
        }

        [Route(HttpVerbs.Post, "/devices/{id}/approve")]
        public Task ApproveDevice(string id)
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = DeviceRegistry.Approve(id) }));
        }

        [Route(HttpVerbs.Post, "/devices/{id}/revoke")]
        public Task RevokeDevice(string id)
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = DeviceRegistry.Revoke(id) }));
        }

        [Route(HttpVerbs.Delete, "/devices/{id}")]
        public Task DeleteDevice(string id)
        {
            if (!IsLoopback()) return Forbidden();
            return SendJsonAsync(Json.Serialize(new { ok = DeviceRegistry.Delete(id) }));
        }
```

- [ ] **Step 4: 헬퍼 추가**

`SendJsonAsync` 헬퍼 바로 아래에 추가:
```csharp
        private string DeviceToken() => HttpContext.Request.Headers["X-Device-Token"];

        private bool IsLoopback()
            => IPAddress.IsLoopback(HttpContext.Request.RemoteEndPoint.Address);

        private Task Forbidden()
        {
            HttpContext.Response.StatusCode = 403;
            return SendJsonAsync(Json.Serialize(new { ok = false, message = "forbidden" }));
        }
```

- [ ] **Step 5: 빌드 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: `Build succeeded`.

- [ ] **Step 6: 실행 중 서버로 통합 검증 (PowerShell, localhost=loopback)**

앱을 실행한다(Visual Studio F5 또는 `install/Debug/AgentHub.exe`). 콘솔 상단의 서버 URL에서 포트를 확인한다(DEBUG 기본 8000). 새 PowerShell에서:

```powershell
$port = 8000   # 콘솔에 표시된 실제 포트로
$base = "https://127.0.0.1:$port/api"
$tok  = [guid]::NewGuid().ToString("N")
$hdr  = @{ "X-Device-Token" = $tok }

# 1) 미등록 → none
Invoke-RestMethod "$base/devices/status" -Headers $hdr -SkipCertificateCheck

# 2) 요청 → pending
Invoke-RestMethod "$base/devices/request" -Method Post -Headers $hdr -ContentType 'application/json' -Body '{"name":"테스트폰"}' -SkipCertificateCheck

# 3) 목록에서 방금 요청한 기기 id 확인 (loopback이라 허용됨)
$dev = (Invoke-RestMethod "$base/devices" -SkipCertificateCheck).devices | Where-Object { $_.name -eq '테스트폰' }
$dev.id; $dev.status   # status=pending

# 4) 미승인 상태에서 /agents → 401
try { Invoke-RestMethod "$base/agents" -Headers $hdr -SkipCertificateCheck } catch { $_.Exception.Response.StatusCode }  # Unauthorized

# 5) 승인 → approved
Invoke-RestMethod "$base/devices/$($dev.id)/approve" -Method Post -SkipCertificateCheck
Invoke-RestMethod "$base/devices/status" -Headers $hdr -SkipCertificateCheck   # status=approved

# 6) 승인 후 /agents → 200 (agents 배열)
(Invoke-RestMethod "$base/agents" -Headers $hdr -SkipCertificateCheck).agents.Count   # > 0

# 7) 해제 → revoked, /agents 다시 401
Invoke-RestMethod "$base/devices/$($dev.id)/revoke" -Method Post -SkipCertificateCheck
try { Invoke-RestMethod "$base/agents" -Headers $hdr -SkipCertificateCheck } catch { $_.Exception.Response.StatusCode }  # Unauthorized

# 8) 삭제 → 목록에서 사라짐
Invoke-RestMethod "$base/devices/$($dev.id)" -Method Delete -SkipCertificateCheck
(Invoke-RestMethod "$base/devices" -SkipCertificateCheck).devices | Where-Object { $_.id -eq $dev.id }   # 빈 결과
```
Expected: 각 단계 주석의 값과 일치.

- [ ] **Step 7: 커밋**

```bash
git add AgentHub/Server/Controller/ApiController.cs
git commit -m "feat(auth): add device endpoints, loopback guard, gate /api/agents"
```

---

## Task 4: EmbedIOServer — LocalUrl + /host loopback 가드 + DeviceRegistry 로드

**Files:**
- Modify: `AgentHub/Server/EmbedIOServer.cs`

**Interfaces:**
- Consumes: `DeviceRegistry.Load()`, `RequestHandlerPassThroughException`(EmbedIO), `IPAddress.IsLoopback`.
- Produces: `EmbedIOServer.LocalUrl` (string, `https://127.0.0.1:{port}`).

- [ ] **Step 1: using 추가**

상단 using 블록에 추가:
```csharp
using System.Text;
using AgentHub.Server.Devices;
```

- [ ] **Step 2: `LocalUrl` 프로퍼티 추가**

`CurrentUrl` 프로퍼티 바로 아래에 추가:
```csharp
        /// <summary>PC(호스트 콘솔) 전용 loopback URL. WebView2가 이 주소로 /host를 로드한다.</summary>
        public static string LocalUrl => $"https://127.0.0.1:{CurrentPort}";
```

- [ ] **Step 3: StartServer에서 DeviceRegistry 로드**

`StartServer()` 본문 `try` 블록 첫 줄(`CurrentPort = ResolvePort();` 위)에 추가:
```csharp
                DeviceRegistry.Load();
```

- [ ] **Step 4: /host loopback 가드 모듈 등록**

`_server = new WebServer(options)` 빌더 체인에서 `.WithModule(new HostMonitorModule("/ws/host"))` 다음, `.WithStaticFolder(...)` **이전**에 삽입:
```csharp
                    // /host, /host.html 는 PC(loopback)에서만 접근 허용. 그 외는 정적 폴더로 통과.
                    .WithAction("/host", HttpVerbs.Any, GuardHostAsync)
                    .WithAction("/host.html", HttpVerbs.Any, GuardHostAsync)
```

- [ ] **Step 5: 가드 핸들러 메서드 추가**

클래스 내 `StopServer()` 위(또는 아무 정적 메서드 위치)에 추가:
```csharp
        /// <summary>loopback이면 정적 폴더로 통과, 아니면 403.</summary>
        private static Task GuardHostAsync(EmbedIO.IHttpContext ctx)
        {
            if (IPAddress.IsLoopback(ctx.Request.RemoteEndPoint.Address))
                throw new EmbedIO.RequestHandlerPassThroughException();
            ctx.Response.StatusCode = 403;
            return ctx.SendStringAsync("Forbidden", "text/plain", Encoding.UTF8);
        }
```

- [ ] **Step 6: 빌드 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: `Build succeeded`.

- [ ] **Step 7: 검증**

앱 실행 후 PowerShell(=loopback)에서 `/host`가 통과(200/HTML)하는지 확인:
```powershell
$port = 8000  # 실제 포트
(Invoke-WebRequest "https://127.0.0.1:$port/host" -SkipCertificateCheck).StatusCode  # 200
```
LAN 기기(휴대폰)에서 `https://<PC-LAN-IP>:$port/host` 접속 시 **Forbidden(403)** 확인은 Task 9 E2E에서 수행.

- [ ] **Step 8: 커밋**

```bash
git add AgentHub/Server/EmbedIOServer.cs
git commit -m "feat(auth): load DeviceRegistry, add LocalUrl, restrict /host to loopback"
```

---

## Task 5: FormMain — WebView2를 127.0.0.1로 로드 + 신규 요청 풍선알림

**Files:**
- Modify: `AgentHub/View/Forms/FormMain.cs`

**Interfaces:**
- Consumes: `EmbedIOServer.LocalUrl`, `DeviceRegistry.StatusChanged`, `DeviceStatus`.

- [ ] **Step 1: using 추가**

상단 using 블록에 추가:
```csharp
using AgentHub.Common.Models;
using AgentHub.Server.Devices;
```

- [ ] **Step 2: 콘솔 로드 URL을 LocalUrl로 변경 (2곳)**

`InitializeControl()` 내 `Restarted` 핸들러:
```csharp
                        webViewServer.CoreWebView2?.Navigate($"{EmbedIOServer.CurrentUrl}/host");
```
→
```csharp
                        webViewServer.CoreWebView2?.Navigate($"{EmbedIOServer.LocalUrl}/host");
```

그리고 최초 로드:
```csharp
            core.Navigate($"{EmbedIOServer.CurrentUrl}/host");
```
→
```csharp
            core.Navigate($"{EmbedIOServer.LocalUrl}/host");
```

- [ ] **Step 3: 신규 pending 시 트레이 풍선알림 구독 추가**

`InitializeControl()` 내 `EmbedIOServer.StartServer();` 호출 다음 줄에 추가:
```csharp
            DeviceRegistry.StatusChanged += (hash, status) =>
            {
                if (status != DeviceStatus.Pending || IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() => _notify?.ShowBalloonTip(
                        5000, "Agent Hub", "새 기기 인증 요청이 도착했습니다.", ToolTipIcon.Info)));
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            };
```

- [ ] **Step 4: 빌드 확인**

Run:
```powershell
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: `Build succeeded`.

- [ ] **Step 5: 검증**

앱 실행 → 콘솔이 정상 로드되는지 확인(주소가 127.0.0.1). PowerShell로 pending 요청을 만들면 트레이 풍선알림이 뜨는지 확인:
```powershell
$port = 8000
$tok = [guid]::NewGuid().ToString("N")
Invoke-RestMethod "https://127.0.0.1:$port/api/devices/request" -Method Post -Headers @{ "X-Device-Token"=$tok } -ContentType 'application/json' -Body '{"name":"알림테스트"}' -SkipCertificateCheck
```
Expected: 트레이에 "새 기기 인증 요청이 도착했습니다." 풍선알림.

- [ ] **Step 6: 커밋**

```bash
git add AgentHub/View/Forms/FormMain.cs
git commit -m "feat(auth): load host console via loopback and toast on new device request"
```

---

## Task 6: 모바일 화면 — index.html + app.js (요청/대기/모니터)

**Files:**
- Modify: `AgentHub/View/Htmls/index.html`
- Modify: `AgentHub/View/Htmls/js/app.js`

**Interfaces:**
- Consumes (WS `/ws/agents?token=`): `{type:"auth", status}`, `{type:"agents", agents}`.
- Consumes (REST): `POST /api/devices/request` (헤더 `X-Device-Token`, body `{name}`).

- [ ] **Step 1: `index.html` 본문 교체**

`<body>` 내부를 아래로 교체(헤더/스크립트/링크는 유지, main 내용과 화면 구조 추가):
```html
<body>
  <header class="app-header">
    <div class="brand"><span class="logo">◈</span> Agent Hub</div>
    <div class="server-status">
      <span class="badge" id="wsBadge"><span class="spinner"></span>연결 중…</span>
    </div>
  </header>
  <main>
    <!-- 인증 요청 화면 -->
    <section id="authRequest" class="screen" hidden>
      <div class="auth-card">
        <h2>기기 인증이 필요합니다</h2>
        <p class="hint">이 기기의 접속을 허용하려면 PC(Agent Hub)에서 승인해야 합니다. 아래에 기기 이름을 입력하고 인증을 요청하세요.</p>
        <input type="text" id="deviceName" maxlength="40" placeholder="기기 이름 (예: 내 아이폰)" />
        <button id="requestBtn">인증 요청 보내기</button>
        <p class="hint" id="requestHint"></p>
      </div>
    </section>

    <!-- 승인 대기 화면 -->
    <section id="authPending" class="screen" hidden>
      <div class="auth-card">
        <div class="loading"><span class="spinner"></span></div>
        <h2>승인 대기 중…</h2>
        <p class="hint">PC(Agent Hub)에서 이 기기를 승인하면 자동으로 모니터 화면이 표시됩니다.</p>
      </div>
    </section>

    <!-- 모니터 화면 -->
    <section id="monitor" class="screen" hidden>
      <div class="summary" id="summary"></div>
      <div class="agent-grid" id="agentGrid">
        <div class="loading"><span class="spinner"></span><span>에이전트 정보를 불러오는 중…</span></div>
      </div>
    </section>
  </main>
  <script src="/js/app.js"></script>
</body>
```

- [ ] **Step 2: `app.js` 전체 교체**

```javascript
// 모바일 모니터 — 기기 인증(토큰) + WebSocket(/ws/agents) 실시간
const $ = (s, r = document) => r.querySelector(s);

// ---- 기기 토큰 ----
const TOKEN_KEY = 'agenthub.deviceToken';
function genUuid() {
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80;
  const h = [...b].map(x => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`;
}
function getToken() {
  let t = localStorage.getItem(TOKEN_KEY);
  if (!t) {
    t = (crypto.randomUUID ? crypto.randomUUID() : genUuid());
    localStorage.setItem(TOKEN_KEY, t);
  }
  return t;
}
const token = getToken();

// ---- 화면 전환 ----
function showScreen(name) {
  ['authRequest', 'authPending', 'monitor'].forEach(id => {
    $('#' + id).hidden = (id !== name);
  });
}

// ---- 렌더 ----
const label = s => ({ working: '작업 중', idle: '대기', error: '오류' }[s] || s);
const esc = s => (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

function render(list) {
  list = list || [];
  const working = list.filter(a => a.status === 'working').length;
  const error = list.filter(a => a.status === 'error').length;
  $('#summary').innerHTML =
    `<div class="stat"><div class="n">${list.length}</div><div class="l">전체 에이전트</div></div>` +
    `<div class="stat"><div class="n">${working}</div><div class="l">작업 중</div></div>` +
    `<div class="stat"><div class="n">${error}</div><div class="l">오류</div></div>`;
  $('#agentGrid').innerHTML = list.map(a => `
    <div class="card">
      <div class="top"><span class="name">${esc(a.name)}</span><span class="pill ${a.status}">${label(a.status)}</span></div>
      <div class="task">${esc(a.currentTask) || '&mdash;'}</div>
      <div class="bar"><i style="width:${a.progress || 0}%"></i></div>
    </div>`).join('');
}

function setBadge(on) {
  const b = $('#wsBadge');
  b.textContent = on ? '🟢 실시간 연결됨' : '🔴 연결 끊김';
  b.className = 'badge ' + (on ? 'on' : 'off');
}

// ---- auth 상태 → 화면 ----
function applyAuth(status) {
  if (status === 'approved') showScreen('monitor');
  else if (status === 'pending') showScreen('authPending');
  else showScreen('authRequest'); // none | revoked
}

// ---- 인증 요청 ----
$('#requestBtn').addEventListener('click', async () => {
  const name = $('#deviceName').value.trim();
  const hint = $('#requestHint');
  hint.textContent = '요청 전송 중…';
  try {
    const res = await (await fetch('/api/devices/request', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Device-Token': token },
      body: JSON.stringify({ name })
    })).json();
    if (res.ok) { applyAuth(res.status); hint.textContent = ''; }
    else hint.textContent = '요청 실패: ' + (res.message || '오류');
  } catch (e) {
    hint.textContent = '요청 실패: ' + e.message;
  }
});

// ---- WebSocket ----
let ws;
function connect() {
  const url = (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host
    + '/ws/agents?token=' + encodeURIComponent(token);
  ws = new WebSocket(url);
  ws.onopen = () => setBadge(true);
  ws.onclose = () => { setBadge(false); setTimeout(connect, 3000); };
  ws.onerror = () => { try { ws.close(); } catch (e) { /* noop */ } };
  ws.onmessage = ev => {
    try {
      const m = JSON.parse(ev.data);
      if (m.type === 'auth') applyAuth(m.status);
      else if (m.type === 'agents') { showScreen('monitor'); render(m.agents); }
    } catch (e) { /* ignore malformed */ }
  };
}

showScreen('authPending'); // 최초: WS 응답 전까지 대기 표시
connect();

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => {});
}
```

- [ ] **Step 3: 검증(빌드 불필요 — 정적 자산)**

앱 실행 상태에서 브라우저(또는 시크릿 창)로 `https://127.0.0.1:$port/` 접속:
- localStorage가 비어있으면 잠시 "승인 대기 중" 후 **인증 요청 화면**이 뜬다(WS가 none 전송).
- 이름 입력 후 "인증 요청 보내기" → **승인 대기 화면**으로 전환.
- PowerShell 또는 콘솔에서 해당 기기를 승인하면(다음 태스크 콘솔 UI 또는 `/approve` API) → **모니터 화면**으로 자동 전환.

> 브라우저 캐시로 옛 app.js가 남을 수 있으니 강력 새로고침(Ctrl+Shift+R) 사용.

- [ ] **Step 4: 커밋**

```bash
git add AgentHub/View/Htmls/index.html AgentHub/View/Htmls/js/app.js
git commit -m "feat(auth): mobile auth request/pending/monitor screens with device token"
```

---

## Task 7: 호스트 콘솔 — host.html + host.js 기기 관리 UI

**Files:**
- Modify: `AgentHub/View/Htmls/host.html`
- Modify: `AgentHub/View/Htmls/js/host.js`

**Interfaces:**
- Consumes (WS `/ws/host`): 기존 `{type:"clients", count, clients}` + 신규 `{type:"devices", devices}`.
- Consumes (REST): `POST /api/devices/{id}/approve`, `POST /api/devices/{id}/revoke`, `DELETE /api/devices/{id}`, `GET /api/devices`(폴백).

- [ ] **Step 1: `host.html` — 탭과 섹션 추가**

`<nav class="tabs">`를 교체(맨 앞에 "기기 관리" 탭 추가):
```html
  <nav class="tabs">
    <button class="tab active" data-view="devices">기기 관리</button>
    <button class="tab" data-view="clients">연결된 기기</button>
    <button class="tab" data-view="logs">로그</button>
    <button class="tab" data-view="settings">설정</button>
  </nav>
```

`<main>` 첫 자식으로 devices 섹션 추가하고, 기존 clients 섹션의 `active`는 제거한다.
기존:
```html
  <main>
    <section id="clients" class="view active">
```
교체:
```html
  <main>
    <section id="devices" class="view active">
      <div class="summary">
        <div class="stat"><div class="n" id="pendingCount">0</div><div class="l">인증 대기</div></div>
        <div class="stat"><div class="n" id="approvedCount">0</div><div class="l">승인된 기기</div></div>
      </div>
      <div id="deviceList">
        <div class="loading"><span class="spinner"></span><span>기기 정보를 불러오는 중…</span></div>
      </div>
    </section>
    <section id="clients" class="view">
```

- [ ] **Step 2: `host.js` — devices 렌더 + 액션 추가**

`renderClients` 함수 정의 **아래**에 devices 렌더러와 액션 핸들러를 추가:
```javascript
// ---- 등록 기기 관리 (WebSocket /ws/host: devices) ----
const statusLabel = s => ({ pending: '대기', approved: '승인됨', revoked: '해제됨' }[s] || s);

function deviceActions(d) {
  const approve = `<button class="act approve" data-act="approve" data-id="${d.id}">승인</button>`;
  const revoke  = `<button class="act revoke" data-act="revoke" data-id="${d.id}">해제</button>`;
  const del     = `<button class="act delete" data-act="delete" data-id="${d.id}">삭제</button>`;
  if (d.status === 'pending')  return approve + del;
  if (d.status === 'approved') return revoke + del;
  return approve + del; // revoked → 재승인 가능
}

function renderDevices(list) {
  list = list || [];
  $('#pendingCount').textContent = list.filter(d => d.status === 'pending').length;
  $('#approvedCount').textContent = list.filter(d => d.status === 'approved').length;
  if (!list.length) {
    $('#deviceList').innerHTML = '<p class="hint">아직 인증을 요청한 기기가 없습니다.</p>';
    return;
  }
  $('#deviceList').innerHTML =
    '<table class="tbl"><thead><tr><th>이름</th><th>상태</th><th>IP</th><th>요청 시각</th><th>동작</th></tr></thead><tbody>' +
    list.map(d => `<tr>
      <td>${esc(d.name) || '<span class="hint">(이름 없음)</span>'}<div class="ua">${esc(d.userAgent)}</div></td>
      <td><span class="pill ${d.status}">${statusLabel(d.status)}</span></td>
      <td>${esc(d.ip)}</td>
      <td>${fmtTime(d.requestedAt)}</td>
      <td class="actions">${deviceActions(d)}</td>
    </tr>`).join('') +
    '</tbody></table>';
}

// 액션 버튼 (이벤트 위임)
$('#deviceList').addEventListener('click', async e => {
  const btn = e.target.closest('button.act');
  if (!btn) return;
  const { act, id } = btn.dataset;
  btn.disabled = true;
  try {
    if (act === 'delete') await fetch('/api/devices/' + id, { method: 'DELETE' });
    else await fetch('/api/devices/' + id + '/' + act, { method: 'POST' });
    // 결과는 /ws/host의 devices broadcast로 자동 반영됨
  } catch (err) { btn.disabled = false; }
});
```

`ws.onmessage` 핸들러의 메시지 분기에 devices를 추가:
```javascript
      if (m.type === 'clients') renderClients(m.clients, m.count);
```
→
```javascript
      if (m.type === 'clients') renderClients(m.clients, m.count);
      else if (m.type === 'devices') renderDevices(m.devices);
```

- [ ] **Step 3: 검증(정적 자산)**

앱 실행 → 콘솔 "기기 관리" 탭에서:
- 모바일(또는 PowerShell)로 요청한 pending 기기가 실시간으로 목록에 표시.
- "승인" 클릭 → 상태가 승인됨으로 바뀌고, 해당 모바일이 즉시 모니터 화면으로 전환.
- "해제" → 모바일 즉시 차단(요청/대기 화면). "삭제" → 목록에서 제거 + 모바일 차단.

- [ ] **Step 4: 커밋**

```bash
git add AgentHub/View/Htmls/host.html AgentHub/View/Htmls/js/host.js
git commit -m "feat(auth): host console device management (approve/revoke/delete)"
```

---

## Task 8: 스타일 — css/app.css

**Files:**
- Modify: `AgentHub/View/Htmls/css/app.css`

- [ ] **Step 1: 인증 화면·기기 관리 스타일 추가**

`app.css` 맨 끝에 추가(기존 변수/색상 톤에 맞춤 — 다크 배경 `#181c2a` 계열):
```css
/* ---- 기기 인증 화면 ---- */
.screen[hidden] { display: none; }
.auth-card {
  max-width: 420px;
  margin: 12vh auto 0;
  padding: 28px 24px;
  background: #20243a;
  border: 1px solid #2c3150;
  border-radius: 14px;
  text-align: center;
}
.auth-card h2 { margin: 8px 0 12px; font-size: 1.2rem; color: #e6e9f2; }
.auth-card .hint { color: #9aa3c0; font-size: .9rem; line-height: 1.5; }
.auth-card input[type="text"] {
  width: 100%; box-sizing: border-box; margin: 16px 0 12px;
  padding: 12px 14px; font-size: 1rem;
  background: #171a2b; color: #e6e9f2;
  border: 1px solid #2c3150; border-radius: 10px;
}
.auth-card button {
  width: 100%; padding: 12px; font-size: 1rem; font-weight: 600;
  color: #fff; background: #4f6ccf;
  border: 0; border-radius: 10px; cursor: pointer;
}
.auth-card button:hover { background: #5b78d8; }
.auth-card .loading { justify-content: center; margin-bottom: 4px; }

/* ---- 기기 관리 액션/상태 ---- */
.tbl td.actions { white-space: nowrap; }
.act {
  margin: 0 2px; padding: 5px 10px; font-size: .82rem; font-weight: 600;
  border: 0; border-radius: 7px; cursor: pointer; color: #fff;
}
.act:disabled { opacity: .5; cursor: default; }
.act.approve { background: #2e9e5b; }
.act.revoke  { background: #b7861f; }
.act.delete  { background: #b3423a; }
.pill.pending  { background: #3a3550; color: #d9c98a; }
.pill.approved { background: #1f4a34; color: #7fe0a6; }
.pill.revoked  { background: #4a2320; color: #e0908a; }
```

- [ ] **Step 2: 검증**

콘솔·모바일 화면에서 인증 카드, 상태 pill, 액션 버튼 색상이 정상 표시되는지 육안 확인(라이트/다크 톤 깨짐 없음).

- [ ] **Step 3: 커밋**

```bash
git add AgentHub/View/Htmls/css/app.css
git commit -m "style(auth): styles for auth screens and device management"
```

---

## Task 9: 전체 E2E 시나리오 검증

**Files:** (없음 — 검증 전용)

- [ ] **Step 1: 클린 빌드**

Run:
```powershell
msbuild AgentHub.sln /t:Restore
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Expected: `Build succeeded`.

- [ ] **Step 2: 저장 파일 초기화 후 앱 실행**

기존 저장을 지우고 시작(선택):
```powershell
Remove-Item "$env:LOCALAPPDATA\AgentHub\devices.json" -ErrorAction SilentlyContinue
```
`install/Debug/AgentHub.exe` 실행. 콘솔이 `https://127.0.0.1:{port}/host`로 로드되는지 확인.

- [ ] **Step 3: 모바일 E2E (같은 Wi-Fi의 휴대폰)**

콘솔 상단의 LAN 접속 URL(`https://<PC-LAN-IP>:{port}`)을 휴대폰 브라우저에서 연다(자체서명 인증서 경고 수락).
1. **요청 화면** 표시 → 이름 입력 → "인증 요청 보내기" → **대기 화면**.
2. PC: 트레이 풍선알림 + 콘솔 "기기 관리"에 pending 기기 실시간 표시.
3. PC에서 **승인** → 휴대폰이 즉시 **모니터 화면**으로 전환, 에이전트 실시간 갱신.
4. PC에서 **해제** → 휴대폰 즉시 요청/대기 화면으로 차단.
5. PC에서 **재승인** → 다시 모니터 표시(복원).
6. PC에서 **삭제** → 휴대폰 차단, 새로고침 시 요청 화면.

- [ ] **Step 4: /host loopback 차단 확인 (휴대폰)**

휴대폰 브라우저에서 `https://<PC-LAN-IP>:{port}/host` 접속 → **Forbidden(403)** 표시(콘솔은 열리지 않음).

- [ ] **Step 5: 영속성 확인**

승인된 기기가 있는 상태에서 앱 완전 종료 후 재실행 → 휴대폰이 재접속 시 재요청 없이 바로 모니터가 뜨는지 확인(`devices.json`에서 승인 상태 복원).

- [ ] **Step 6: 최종 커밋(문서/잔여)**

변경 문서가 있다면 커밋:
```bash
git add docs/superpowers
git commit -m "docs(auth): device authentication spec and plan"
```

---

## Self-Review 결과

- **스펙 커버리지**: 요청 화면(Task 6) · 실시간 수신(Task 2·5·7) · 승인 게이트(Task 2·3) · 해제/삭제 차단(Task 2·3·7) · DB 없는 저장(Task 1) — 모두 태스크로 매핑됨.
- **Loopback 승인 전용**: /host(Task 4) + admin API(Task 3) 이중 방어.
- **타입 일관성**: `DeviceStatus` 상수, `DeviceView` 투영, `Request→StatusChanged→OnDeviceStatusChanged` push 경로, WS `{type:"auth"|"agents"|"devices"}` 스키마가 C#/JS 양쪽에서 일치.
- **인코딩**: 모든 C# 편집은 Edit 도구로 국소 변경, 신규 파일은 UTF-8. 저장 파일은 `UTF8Encoding(false)`(BOM 없음).
