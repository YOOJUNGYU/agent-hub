# Agent Hub 모니터링 UI 개편 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 검진/프린트 UI를 제거하고, EmbedIO가 HTTPS로 서빙하는 단일 반응형 PWA 대시보드에서 Claude 에이전트(mock)를 모니터링하며, 트레이 완전 종료·포트 설정·서버 상태/URL 표시·신규 아이콘을 갖춘 상태로 만든다.

**Architecture:** `FormMain`(WinForms 커스텀 크롬 + 트레이)이 WebView2 1개로 `https://127.0.0.1:{port}/`를 로드한다. `EmbedIOServer`가 자체서명 HTTPS 인증서로 정적 SPA(`View/Htmls`)와 `/api/*`(server status, mock agents, settings)를 서빙한다. 프런트는 빌드 단계 없는 바닐라 JS SPA(대시보드/로그/설정 탭) + PWA(manifest+SW).

**Tech Stack:** C# 8 / .NET Framework 4.8 / WinForms / WebView2 / EmbedIO / Newtonsoft.Json / Vanilla JS+HTML+CSS / GDI+(아이콘 생성).

## ★ 개정 (2026-07-06) — 두 화면 분리 + WebSocket (이 절이 아래 원안보다 우선)

**두 개의 서빙 화면 + WebSocket 실시간:**
- `/` → 모바일 Claude 에이전트 모니터 SPA (반응형, PWA). 실시간: `wss://…/ws/agents`.
- `/host` → WebView2 호스트 콘솔. 서버상태·접속 URL·**접속 모바일 목록**·로그·포트설정. 실시간: `wss://…/ws/host`.

**소켓 레이어 (신규 파일):**
- `AgentHub/Server/Socket/MonitorClientRegistry.cs` — 정적 싱글턴. `ConcurrentDictionary<string, MonitorClient>`. `Add(id,ip,ua)`, `Remove(id)`, `Snapshot()→List<MonitorClient>`, `event Action Changed`. `MonitorClient { Id, Ip, UserAgent, ConnectedAt(string ISO) }`.
- `AgentHub/Server/Socket/AgentMonitorModule.cs : WebSocketModule` — route `/ws/agents`. OnConnect: registry.Add + 초기 스냅샷 전송. OnDisconnect: registry.Remove. `Task BroadcastAgentsAsync(string json)`.
- `AgentHub/Server/Socket/HostMonitorModule.cs : WebSocketModule` — route `/ws/host`. OnConnect: 현재 클라이언트 목록 전송. registry.Changed 구독 → 목록 broadcast. `Task BroadcastClientsAsync()`.
- `AgentHub/Server/Agents/AgentMonitorService.cs` — mock 데이터 seam. `Start(AgentMonitorModule module)`/`Stop()`. 내부 `System.Threading.Timer`(2.5s)로 mock 에이전트 진행률 변화 → `module.BroadcastAgentsAsync(json)`. 실제 연동 시 이 클래스만 교체.

**Task 매핑 변경:**
- Task 4(EmbedIOServer): 정적 서빙을 `/`(mobile)와 `/host` 둘 다; WS 모듈 2개 등록 + `AgentMonitorService` 시작/정지를 Start/Stop에 연결.
- Task 5(ApiController): `/api/server/status`, `/api/settings`(GET/POST) 유지. `/api/agents`는 초기 스냅샷 fallback으로 유지(실시간은 WS). 클라이언트 목록은 `/ws/host`로.
- Task 6(프런트): `/` = 모바일 SPA(에이전트, WS 구독). 헤더는 간단한 서버 배지 정도.
- **신규 Task 6b(호스트 콘솔 `/host`)**: `host.html`+`js/host.js`. 서버상태+URL 링크, 접속 모바일 목록(`/ws/host`), 로그(`window.addLog`), 포트 설정 폼(`/api/settings`).
- Task 8(FormMain): 단일 WebView가 `EmbedIOServer.CurrentUrl + "/host"` 로드. 로그 push 대상=이 WebView. 나머지(트레이 완전종료/open-url/cert)는 원안과 동일.
- Task 2: 프린터 WS(`WebSocketPrinterModule`)뿐 아니라 `WebSocketTerminalModule`도 제거(용도 없음), 신규 소켓 모듈로 대체.

**신규/변경 파일 추가:** `Server/Socket/MonitorClientRegistry.cs`, `Server/Socket/AgentMonitorModule.cs`, `Server/Socket/HostMonitorModule.cs`, `Server/Agents/AgentMonitorService.cs`, `View/Htmls/host.html`, `View/Htmls/js/host.js`. csproj에 Compile/Content 등록.

> 실행은 인라인. 소켓/정적 서빙은 EmbedIO 실제 시그니처(IWebSocketContext에서 IP/UA 취득 방식, FileModule 기본문서)에 맞춰 빌드하며 확정.

## Global Constraints

- 루트 네임스페이스 `AgentHub`. 서드파티 `EmbedIO/`는 수정 금지.
- 한글은 UTF-8. 문자열 치환 시 인코딩 훼손 금지(Edit 도구/바이트 처리).
- **HTTPS 자체서명 인증서 발급 + `LocalMachine\Root` 등록은 유지**(HTTP 다운그레이드 금지) — PWA는 보안 컨텍스트 필요.
- 서버 바인딩은 이번 범위에서 `127.0.0.1` 유지(LAN 노출은 다음 단계).
- 프런트 프레임워크 없음(바닐라). 빌드 파이프라인 추가 금지.
- 검증은 `msbuild AgentHub.sln` 성공 + 실행 관찰(단위 테스트 하네스 없음).
- 커밋은 사용자가 요청할 때만. 계획의 각 "Commit" 스텝은 사용자 승인 시에만 수행.
- 빌드 명령:
  ```powershell
  $msb = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe'
  & $msb AgentHub.sln /t:Restore
  & $msb AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU" /v:minimal /nologo
  ```
  성공 판정: 에러 0, `install/Debug/AgentHub.exe` 생성.

---

## 파일 구조 (생성/수정/삭제)

**생성:**
- `AgentHub/View/Htmls/index.html` — SPA 셸(헤더 + 탭 + 뷰 컨테이너)
- `AgentHub/View/Htmls/css/app.css` — 반응형 스타일(다크 테마)
- `AgentHub/View/Htmls/js/app.js` — 라우팅/상태폴링/대시보드/설정/로그
- `AgentHub/View/Htmls/manifest.webmanifest` — PWA 매니페스트
- `AgentHub/View/Htmls/sw.js` — 최소 서비스워커
- `AgentHub/View/Htmls/icons/icon-192.png`, `icon-512.png` — PWA 아이콘
- `AgentHub/Common/Models/AgentStatus.cs` — mock 에이전트 모델
- `AgentHub/Common/Models/ServerStatusInfo.cs` — 서버 상태 응답 모델
- `tools/GenerateIcon/` (임시 스크립트) 또는 인라인 csx — 아이콘 생성(빌드 산출물 아님)

**수정:**
- `AgentHub/Server/EmbedIOServer.cs` — 인스턴스 보관 + Start/Stop/Restart + ServerPort + 정적 서빙 + 상태 조회 지원
- `AgentHub/Server/Controller/ApiController.cs` — 전면 재작성(status/agents/settings)
- `AgentHub/View/Forms/FormMain.cs` — 단일 WebView, 트레이 완전종료, open-url, cert 핸들러, 로그 대상 변경
- `AgentHub/View/Forms/FormMain.Designer.cs` — WebView 컨트롤 정리(단일화)
- `AgentHub/Common/Constants.cs` — Uris 정리(SPA URL), 프린트 URI 제거
- `AgentHub/Properties/Settings.settings` + `Settings.Designer.cs` + `App.config` — `ServerPort` 추가
- `AgentHub/Properties/Resources.resx` + `Resources.Designer.cs` — 신규 아이콘/이미지 교체
- `AgentHub/AgentHub.csproj` — 삭제 파일 제거 + 신규 Content 추가

**삭제:**
- `AgentHub/View/Forms/FormPrint.cs`/`.Designer.cs`/`.resx`
- `AgentHub/View/Prints/**`
- `AgentHub/Common/Helper/CheckupHelper.cs`
- `AgentHub/Common/Models/Checkup/**`
- `AgentHub/Common/Models/Patient.cs`, `AgentHub/Common/Models/Printer.cs`
- `AgentHub/View/Htmls/side_menu.html`, `server_log.html`, `server_setting.html`(신규 SPA로 대체)
- `AgentHub/View/bridges/SideMenuBridge.cs`
- `AgentHub/Server/Socket/WebSocketPrinterModule.cs`(프린터 전용)

---

### Task 1: 신규 허브 아이콘 생성 (GDI+)

**Files:**
- Create: `tools/generate-icon.ps1` (임시 생성 스크립트, 저장소 산출물 아님 — 실행 후 남겨두되 gitignore 무관)
- Produce: `AgentHub/Resources/trayicon_32x32.ico`(교체), `AgentHub/Resources/main_icon.png`(교체), `AgentHub/View/Htmls/icons/icon-192.png`, `AgentHub/View/Htmls/icons/icon-512.png`

**Interfaces:**
- Produces: `.ico`(16/32/48/256 멀티사이즈), `main_icon.png`(256), PWA `icon-192.png`/`icon-512.png`. 허브 모티브(라운드 사각 그라데이션 배경 + 중앙 노드 + 4개 연결 노드/선), 다크 배경에 청록/보라 계열.

- [ ] **Step 1: 아이콘 생성 스크립트 작성** (`tools/generate-icon.ps1`)

System.Drawing으로 각 크기 PNG를 그리고, PNG들을 ICO 컨테이너로 패킹한다. 핵심 그리기 함수:

```powershell
Add-Type -AssemblyName System.Drawing

function New-HubBitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  # 라운드 사각 그라데이션 배경
  $rect = New-Object System.Drawing.Rectangle(0,0,$size,$size)
  $c1 = [System.Drawing.Color]::FromArgb(24,28,42)
  $c2 = [System.Drawing.Color]::FromArgb(40,54,90)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,$c1,$c2,45)
  $r = [int]($size*0.18)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0,0,$r,$r,180,90); $path.AddArc($size-$r,0,$r,$r,270,90)
  $path.AddArc($size-$r,$size-$r,$r,$r,0,90); $path.AddArc(0,$size-$r,$r,$r,90,90); $path.CloseFigure()
  $g.FillPath($brush,$path)
  # 연결선 + 노드
  $cx = $size/2.0; $cy = $size/2.0; $R = $size*0.30; $nodeR = [Math]::Max(2,$size*0.07)
  $accent = [System.Drawing.Color]::FromArgb(56,208,214)   # teal
  $accent2 = [System.Drawing.Color]::FromArgb(150,120,255)  # violet
  $pen = New-Object System.Drawing.Pen($accent, [Math]::Max(1,$size*0.025))
  $angles = 0,90,180,270
  $pts = @()
  foreach ($a in $angles) {
    $rad = $a * [Math]::PI/180
    $pts += ,@(($cx + $R*[Math]::Cos($rad)), ($cy + $R*[Math]::Sin($rad)))
  }
  foreach ($p in $pts) { $g.DrawLine($pen, [single]$cx,[single]$cy,[single]$p[0],[single]$p[1]) }
  $nb = New-Object System.Drawing.SolidBrush($accent2)
  foreach ($p in $pts) { $g.FillEllipse($nb, [single]($p[0]-$nodeR),[single]($p[1]-$nodeR),[single]($nodeR*2),[single]($nodeR*2)) }
  $cb = New-Object System.Drawing.SolidBrush($accent)
  $cr = $nodeR*1.5
  $g.FillEllipse($cb, [single]($cx-$cr),[single]($cy-$cr),[single]($cr*2),[single]($cr*2))
  $g.Dispose()
  return $bmp
}
```

PNG 저장 + ICO 패킹(각 크기 PNG를 ICONDIR 헤더로 결합):

```powershell
$root = 'C:\GIT\PRIVATE\agent-hub\AgentHub'
(New-HubBitmap 256).Save("$root\Resources\main_icon.png", [System.Drawing.Imaging.ImageFormat]::Png)
New-Item -ItemType Directory -Force "$root\View\Htmls\icons" | Out-Null
(New-HubBitmap 192).Save("$root\View\Htmls\icons\icon-192.png", [System.Drawing.Imaging.ImageFormat]::Png)
(New-HubBitmap 512).Save("$root\View\Htmls\icons\icon-512.png", [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = 16,32,48,256
$pngStreams = foreach ($s in $sizes) {
  $ms = New-Object System.IO.MemoryStream
  (New-HubBitmap $s).Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  ,$ms.ToArray()
}
$fs = New-Object System.IO.FileStream("$root\Resources\trayicon_32x32.ico",'Create')
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16*$sizes.Count
for ($i=0;$i -lt $sizes.Count;$i++){
  $s=$sizes[$i]; $data=$pngStreams[$i]
  $bw.Write([Byte]($(if($s -ge 256){0}else{$s}))); $bw.Write([Byte]($(if($s -ge 256){0}else{$s})))
  $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
  $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset); $offset += $data.Length
}
foreach ($data in $pngStreams){ $bw.Write($data) }
$bw.Flush(); $fs.Close()
```

- [ ] **Step 2: 스크립트 실행**

Run: `pwsh -File tools/generate-icon.ps1`
Expected: 4개 파일 생성, 오류 없음.

- [ ] **Step 3: 산출물 확인**

Run: `pwsh -c "Get-Item AgentHub/Resources/trayicon_32x32.ico, AgentHub/Resources/main_icon.png, AgentHub/View/Htmls/icons/icon-192.png, AgentHub/View/Htmls/icons/icon-512.png | Select Name,Length"`
Expected: 4개 모두 존재, Length > 0. (`.ico`는 Windows 탐색기에서 아이콘으로 표시되는지 육안 확인)

- [ ] **Step 4: Commit** (사용자 승인 시)

```bash
git add AgentHub/Resources/trayicon_32x32.ico AgentHub/Resources/main_icon.png AgentHub/View/Htmls/icons tools/generate-icon.ps1
git commit -m "feat: add hub-motif app/tray/PWA icons"
```

---

### Task 2: 검진/프린트 코드 제거

**Files:**
- Delete: `AgentHub/View/Forms/FormPrint.cs`,`.Designer.cs`,`.resx`; `AgentHub/View/Prints/`(전체); `AgentHub/Common/Helper/CheckupHelper.cs`; `AgentHub/Common/Models/Checkup/`(전체); `AgentHub/Common/Models/Patient.cs`; `AgentHub/Common/Models/Printer.cs`; `AgentHub/View/bridges/SideMenuBridge.cs`; `AgentHub/Server/Socket/WebSocketPrinterModule.cs`; `AgentHub/View/Htmls/side_menu.html`
- Modify: `AgentHub/AgentHub.csproj`(해당 항목 제거), `AgentHub/Common/Constants.cs`(Prints URI 제거), `AgentHub/Common/Util/EtcUtil.cs`(프린터 전용 코드가 있으면 제거 — 참조 확인 후)

**Interfaces:**
- Produces: 컴파일 가능한 상태(검진/프린트 참조 0). `EtcUtil.GetPrinters` 등 프린터 전용 유틸은 참조 사라지면 제거, 그 외 공용 유틸은 유지.

- [ ] **Step 1: 참조 조사**

Run: `pwsh -c "Select-String -Path AgentHub\**\*.cs -Pattern 'Checkup|FormPrint|PrintRequest|Patient|Printer|SideMenuBridge|GetPrinters|WebSocketPrinterModule|Uris.Htmls.Prints|SideMenu' | Select Path,LineNumber,Line"`
Expected: 제거 대상 파일 및 이를 참조하는 지점(FormMain의 SideMenu/ChangeMenu, EmbedIOServer의 WebSocketPrinterModule 등록, ApiController의 print) 목록 확보. → Task 3~5,8에서 함께 정리.

- [ ] **Step 2: 파일 삭제**

```powershell
Remove-Item -Recurse -Force AgentHub/View/Forms/FormPrint.cs, AgentHub/View/Forms/FormPrint.Designer.cs, AgentHub/View/Forms/FormPrint.resx, AgentHub/View/Prints, AgentHub/Common/Helper/CheckupHelper.cs, AgentHub/Common/Models/Checkup, AgentHub/Common/Models/Patient.cs, AgentHub/Common/Models/Printer.cs, AgentHub/View/bridges/SideMenuBridge.cs, AgentHub/Server/Socket/WebSocketPrinterModule.cs, AgentHub/View/Htmls/side_menu.html
```

- [ ] **Step 3: csproj에서 제거된 항목 삭제**

`AgentHub/AgentHub.csproj`에서 위 파일들의 `<Compile>`, `<Content>`, `<EmbeddedResource>`(`FormPrint.resx` DependentUpon 포함), `View\Prints\*` `<Content>` 라인을 모두 제거. `side_menu.html`, `server_log.html`, `server_setting.html` Content 라인은 Task 6에서 index.html/app.js/app.css/manifest/sw로 교체하므로 여기서 `server_log.html`,`server_setting.html`도 함께 제거(파일은 Task 6에서 삭제).

- [ ] **Step 4: Constants.cs 정리**

`Constants.Uris.Htmls.Prints`(및 `GeneralFirstAdvice`) 블록 제거. `SideMenu`/`ServerLog`/`ServerSetting` URI는 Task 4에서 SPA URL로 대체하므로 이 스텝에서는 Prints만 제거.

- [ ] **Step 5: 빌드로 참조 오류 확인(의도된 실패 허용)**

Run: 빌드 명령(Global Constraints).
Expected: 아직 FormMain/ApiController/EmbedIOServer가 삭제 대상을 참조하면 **컴파일 에러** — Task 3~5,8에서 해소. (이 시점 실패는 정상; 다음 태스크에서 순차 해결)

- [ ] **Step 6: Commit** (사용자 승인 시) — 단, 빌드 통과는 Task 8 이후. 부분 커밋 대신 Task 8까지 묶어 커밋 권장.

---

### Task 3: ServerPort 설정 추가

**Files:**
- Modify: `AgentHub/Properties/Settings.settings`, `AgentHub/Properties/Settings.Designer.cs`, `AgentHub/App.config`

**Interfaces:**
- Produces: `Properties.Settings.Default.ServerPort` (int, 기본 8080). 다른 태스크가 이 값을 읽고 씀.

- [ ] **Step 1: Settings.settings에 항목 추가**

`<Settings>` 안에 추가:
```xml
<Setting Name="ServerPort" Type="System.Int32" Scope="User">
  <Value Profile="(Default)">8080</Value>
</Setting>
```

- [ ] **Step 2: Settings.Designer.cs에 프로퍼티 추가**

기존 프로퍼티(FormMainHeight 등)와 동일 패턴으로:
```csharp
[global::System.Configuration.UserScopedSettingAttribute()]
[global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
[global::System.Configuration.DefaultSettingValueAttribute("8080")]
public int ServerPort {
    get { return ((int)(this["ServerPort"])); }
    set { this["ServerPort"] = value; }
}
```

- [ ] **Step 3: App.config 기본값 추가**

`<AgentHub.Properties.Settings>` 안에:
```xml
<setting name="ServerPort" serializeAs="String">
  <value>8080</value>
</setting>
```

- [ ] **Step 4: 빌드 확인(부분)**

Run: 빌드 명령. Expected: Settings 관련 에러 없음(다른 태스크 미완으로 전체는 아직 실패 가능).

---

### Task 4: EmbedIOServer 리팩터 (인스턴스 보관 + Start/Stop/Restart + 포트 + 정적 서빙, HTTPS 유지)

**Files:**
- Modify: `AgentHub/Server/EmbedIOServer.cs`
- Modify: `AgentHub/Common/Constants.cs` (SPA URL 상수)

**Interfaces:**
- Consumes: `Properties.Settings.Default.ServerPort` (Task 3), `SelfSigned.*`(기존 인증서 상수 유지).
- Produces:
  - `EmbedIOServer.StartServer()` / `StopServer()` / `RestartServer()`
  - `EmbedIOServer.IsRunning` (bool), `EmbedIOServer.CurrentPort` (int), `EmbedIOServer.CurrentUrl` (string, 예 `https://127.0.0.1:8080`)
  - `ApiController`(Task 5)가 `WithWebApi("/api", ...)`로 등록됨.
  - 정적 SPA를 `/`에 서빙(`View/Htmls`).

- [ ] **Step 1: EmbedIOServer 재작성**

핵심: `WebServer`와 `CancellationTokenSource`를 정적 필드로 보관. 포트는 설정값 우선, 사용 중이면 8000~9000 폴백. 인증서 로직(자체서명 발급 + Root 등록)은 **그대로 유지**. 정적 파일 서빙 추가.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using EmbedIO;
using EmbedIO.Actions;
using EmbedIO.Files;
using EmbedIO.Security;
using EmbedIO.WebApi;
using Swan.Logging;
using AgentHub.Common.Util;
using AgentHub.Server.Controller;
using static AgentHub.Common.Constants;

namespace AgentHub.Server
{
    public static class EmbedIOServer
    {
        private static WebServer _server;
        private static CancellationTokenSource _cts;

        public static bool IsRunning => _server != null && _server.State == WebServerState.Listening;
        public static int CurrentPort { get; private set; }
        public static string CurrentUrl => $"https://127.0.0.1:{CurrentPort}";

        private static X509Certificate2 GetSelfSignedCertificate()
        {
            // === 기존 구현 그대로 유지 (자체서명 발급 + LocalMachine\Root 등록) ===
            // (기존 EmbedIOServer.cs의 GetSelfSignedCertificate 본문을 그대로 사용)
            var pfxFilePathName = Path.Combine(SelfSigned.CertFilePath, SelfSigned.PfxFileName);
            if (File.Exists(pfxFilePathName)) return new X509Certificate2(pfxFilePathName, SelfSigned.CertPassword);
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=AgentHub, O=AgentHub, C=KR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddIpAddress(IPAddress.Parse("127.0.0.1"));
            request.CertificateExtensions.Add(sanBuilder.Build());
            var certificate = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(10));
            var certRawData = certificate.Export(X509ContentType.Pfx, SelfSigned.CertPassword);
            if (!Directory.Exists(SelfSigned.CertFilePath)) Directory.CreateDirectory(SelfSigned.CertFilePath);
            File.WriteAllBytes(pfxFilePathName, certRawData);
            File.SetAttributes(pfxFilePathName, File.GetAttributes(pfxFilePathName) | FileAttributes.Hidden);
            var x509 = new X509Certificate2(certRawData, SelfSigned.CertPassword);
            File.WriteAllBytes(Path.Combine(SelfSigned.CertFilePath, SelfSigned.CrtFileName), x509.Export(X509ContentType.Cert));
            using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadWrite);
                store.Add(x509);
                store.Close();
            }
            return x509;
        }

        private static int ResolvePort()
        {
            var configured = Properties.Settings.Default.ServerPort;
            var active = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Select(p => p.Port).ToHashSet();
            if (configured >= 1024 && configured <= 65535 && !active.Contains(configured))
                return configured;
            for (var port = 8000; port <= 9000; port++)
                if (!active.Contains(port)) return port;
            throw new Exception("사용 가능한 포트를 찾을 수 없습니다.");
        }

        public static void StartServer()
        {
            try
            {
                CurrentPort = ResolvePort();
                var url = CurrentUrl;
                var certificate = GetSelfSignedCertificate();
                var htmlPath = Path.Combine(Application.StartupPath, "View", "Htmls");

                var options = new WebServerOptions()
                    .WithUrlPrefix(url)
                    .WithCertificate(certificate)
                    .WithMode(HttpListenerMode.EmbedIO);

                _cts = new CancellationTokenSource();
                _server = new WebServer(options)
                    .WithIPBanning(o => o.WithMaxRequestsPerSecond(100).WithRegexRules(100, 60, "HTTP exception 404"))
                    .WithLocalSessionManager()
                    .WithCors()
                    .WithWebApi("/api", m => m.WithController<ApiController>())
                    .WithModule(new FileModule("/", new FileSystemProvider(htmlPath, false))
                    {
                        DirectoryLister = null
                    }.WithContentCaching(false));
                // SPA 진입: 루트는 index.html (FileModule 기본 문서)
                _server.WithModule(new ActionModule("/", HttpVerbs.Any, ctx => ctx.SendStandardHtmlAsync(404)));

                _server.StateChanged += (s, e) => $"WebServer New State - {e.NewState}".Info("WebServer");
                _server.RunAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                throw;
            }
        }

        public static void StopServer()
        {
            try
            {
                _cts?.Cancel();
                _server?.Dispose();
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            finally { _server = null; _cts = null; }
        }

        public static void RestartServer()
        {
            StopServer();
            Thread.Sleep(200);
            StartServer();
        }
    }
}
```

> 주의: `FileModule` 기본 문서(index.html) 처리는 EmbedIO 버전 API에 맞춰 조정한다. `FileModule`이 `DefaultDocument`(기본 `index.html`)와 `DefaultExtension`을 제공하면 그것을 사용. 위 `ActionModule` 폴백이 라우팅을 가로채지 않도록 등록 순서 확인(정적 모듈을 마지막에, `/api`를 먼저).

- [ ] **Step 2: Constants.cs SPA URL 정리**

`Uris.Htmls`의 `SideMenu/ServerLog/ServerSetting`을 제거하고, WebView2가 로드할 서버 URL은 런타임 값(`EmbedIOServer.CurrentUrl`)을 사용하므로 상수 불요. (FormMain은 서버 시작 후 `EmbedIOServer.CurrentUrl`로 네비게이트 — Task 8)

- [ ] **Step 3: 빌드 확인(부분)**

Run: 빌드 명령. Expected: EmbedIOServer 자체 컴파일 에러 없음(ApiController는 Task 5, FileModule API는 실제 EmbedIO 시그니처로 맞춤). FormMain 미완으로 전체는 실패 가능.

---

### Task 5: ApiController 재작성 (status / agents(mock) / settings)

**Files:**
- Modify: `AgentHub/Server/Controller/ApiController.cs`
- Create: `AgentHub/Common/Models/AgentStatus.cs`, `AgentHub/Common/Models/ServerStatusInfo.cs`

**Interfaces:**
- Consumes: `EmbedIOServer.IsRunning/CurrentPort/CurrentUrl`(Task 4), `Properties.Settings.Default.ServerPort`(Task 3).
- Produces (HTTP, base `/api`):
  - `GET /api/server/status` → `{ active:bool, host:string, port:int, url:string }`
  - `GET /api/agents` → `AgentStatus[]`
  - `GET /api/settings` → `{ port:int }`
  - `POST /api/settings` `{ port:int }` → `{ ok:bool, port:int, url:string, message?:string }` (유효하면 저장 후 `RestartServer()`)

- [ ] **Step 1: 모델 생성**

`AgentStatus.cs`:
```csharp
namespace AgentHub.Common.Models
{
    public class AgentStatus
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }        // working | idle | error
        public string CurrentTask { get; set; }
        public int Progress { get; set; }          // 0-100
        public string UpdatedAt { get; set; }      // ISO 8601
    }
}
```
`ServerStatusInfo.cs`:
```csharp
namespace AgentHub.Common.Models
{
    public class ServerStatusInfo
    {
        public bool Active { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Url { get; set; }
    }
}
```

- [ ] **Step 2: ApiController 재작성**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using AgentHub.Common.Models;
using AgentHub.Common.Util;

namespace AgentHub.Server.Controller
{
    internal class ApiController : WebApiController
    {
        [Route(HttpVerbs.Get, "/server/status")]
        public ServerStatusInfo ServerStatus() => new ServerStatusInfo
        {
            Active = EmbedIOServer.IsRunning,
            Host = "127.0.0.1",
            Port = EmbedIOServer.CurrentPort,
            Url = EmbedIOServer.CurrentUrl
        };

        [Route(HttpVerbs.Get, "/agents")]
        public List<AgentStatus> Agents()
        {
            // mock 데이터 — 실제 연동 시 이 메서드만 교체
            var now = DateTime.UtcNow.ToString("o");
            return new List<AgentStatus>
            {
                new AgentStatus { Id="agent-1", Name="Claude Code", Status="working", CurrentTask="refactor EmbedIOServer.cs", Progress=72, UpdatedAt=now },
                new AgentStatus { Id="agent-2", Name="Docs Writer", Status="idle", CurrentTask="", Progress=0, UpdatedAt=now },
                new AgentStatus { Id="agent-3", Name="Test Runner", Status="error", CurrentTask="build failed: CS0246", Progress=0, UpdatedAt=now },
                new AgentStatus { Id="agent-4", Name="Reviewer", Status="working", CurrentTask="reviewing PR #12", Progress=34, UpdatedAt=now },
            };
        }

        [Route(HttpVerbs.Get, "/settings")]
        public object GetSettings() => new { port = Properties.Settings.Default.ServerPort };

        [Route(HttpVerbs.Post, "/settings")]
        public async Task<object> SaveSettings()
        {
            try
            {
                var body = await HttpContext.GetRequestDataAsync<PortSetting>();
                if (body == null || body.Port < 1024 || body.Port > 65535)
                    return new { ok = false, message = "포트는 1024~65535 범위여야 합니다." };

                Properties.Settings.Default.ServerPort = body.Port;
                Properties.Settings.Default.Save();

                // 응답을 먼저 보낸 뒤 재시작(현재 연결이 끊기므로 클라이언트가 새 URL로 재접속)
                _ = Task.Run(async () => { await Task.Delay(300); EmbedIOServer.RestartServer(); });
                return new { ok = true, port = body.Port, url = $"https://127.0.0.1:{body.Port}" };
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                return new { ok = false, message = ex.Message };
            }
        }

        public class PortSetting { public int Port { get; set; } }
    }
}
```

- [ ] **Step 3: 빌드 확인(부분)**

Run: 빌드 명령. Expected: ApiController/모델 컴파일 에러 없음.

---

### Task 6: 프런트엔드 SPA (index.html / app.css / app.js) — 반응형

**Files:**
- Create: `AgentHub/View/Htmls/index.html`, `css/app.css`, `js/app.js`
- Delete: `AgentHub/View/Htmls/server_log.html`, `server_setting.html`

**Interfaces:**
- Consumes: `/api/server/status`, `/api/agents`, `/api/settings`(Task 5). 데스크톱 로그 push는 전역 함수 `window.addLog(logEvent)`를 제공(FormMain이 호출 — Task 8).
- Produces: `/`에서 로드되는 SPA. 탭: `#dashboard`, `#logs`, `#settings`. 헤더에 상태 배지 + URL 링크.

- [ ] **Step 1: index.html 작성**

```html
<!DOCTYPE html>
<html lang="ko">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
  <meta name="theme-color" content="#181c2a" />
  <link rel="manifest" href="/manifest.webmanifest" />
  <link rel="apple-touch-icon" href="/icons/icon-192.png" />
  <link rel="icon" href="/icons/icon-192.png" />
  <link rel="stylesheet" href="/css/app.css" />
  <title>Agent Hub</title>
</head>
<body>
  <header class="app-header">
    <div class="brand"><span class="logo">◈</span> Agent Hub</div>
    <div class="server-status" id="serverStatus">
      <span class="badge" id="statusBadge">확인 중…</span>
      <a class="server-url" id="serverUrl" target="_blank" rel="noopener"></a>
    </div>
  </header>
  <nav class="tabs">
    <button class="tab active" data-view="dashboard">대시보드</button>
    <button class="tab" data-view="logs">로그</button>
    <button class="tab" data-view="settings">설정</button>
  </nav>
  <main>
    <section id="dashboard" class="view active">
      <div class="summary" id="summary"></div>
      <div class="agent-grid" id="agentGrid"></div>
    </section>
    <section id="logs" class="view">
      <div class="log-list" id="logList"></div>
    </section>
    <section id="settings" class="view">
      <form id="settingsForm" class="settings-form">
        <label for="portInput">서버 포트 (1024–65535)</label>
        <input type="number" id="portInput" min="1024" max="65535" required />
        <button type="submit">저장 후 재시작</button>
        <p class="hint" id="settingsHint"></p>
      </form>
    </section>
  </main>
  <script src="/js/app.js"></script>
</body>
</html>
```

- [ ] **Step 2: css/app.css 작성 (다크 테마 + 반응형)**

```css
:root{
  --bg:#12141f; --panel:#1b1f2e; --panel2:#232840; --fore:#e6e9f2; --muted:#8b90a6;
  --accent:#38d0d6; --violet:#9678ff; --ok:#3ad29f; --warn:#f5c451; --err:#ff6b7a;
  --border:#2c3350; --radius:14px;
}
*{box-sizing:border-box}
body{margin:0;font-family:'Segoe UI','NanumSquareR',sans-serif;background:var(--bg);color:var(--fore)}
.app-header{display:flex;flex-wrap:wrap;gap:.5rem 1rem;align-items:center;justify-content:space-between;
  padding:.9rem 1.2rem;background:linear-gradient(90deg,#181c2a,#22284a);border-bottom:1px solid var(--border)}
.brand{font-weight:700;font-size:1.15rem;letter-spacing:.02em}
.brand .logo{color:var(--accent)}
.server-status{display:flex;align-items:center;gap:.6rem;flex-wrap:wrap}
.badge{padding:.25rem .7rem;border-radius:999px;font-size:.85rem;background:var(--panel2);border:1px solid var(--border)}
.badge.on{color:#0c1020;background:var(--ok);border-color:transparent}
.badge.off{color:#fff;background:var(--err);border-color:transparent}
.server-url{color:var(--accent);text-decoration:none;font-size:.9rem;word-break:break-all}
.server-url:hover{text-decoration:underline}
.tabs{display:flex;gap:.25rem;padding:.5rem .8rem;background:var(--panel);border-bottom:1px solid var(--border);position:sticky;top:0;z-index:2}
.tab{flex:0 0 auto;background:transparent;border:none;color:var(--muted);padding:.6rem 1rem;border-radius:10px;font-size:1rem;cursor:pointer}
.tab.active{color:var(--fore);background:var(--panel2)}
main{padding:1rem 1.2rem;max-width:1100px;margin:0 auto}
.view{display:none}
.view.active{display:block}
.summary{display:flex;gap:.8rem;flex-wrap:wrap;margin-bottom:1rem}
.stat{flex:1 1 120px;background:var(--panel);border:1px solid var(--border);border-radius:var(--radius);padding:.9rem 1rem}
.stat .n{font-size:1.8rem;font-weight:700}
.stat .l{color:var(--muted);font-size:.85rem}
.agent-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:1rem}
.card{background:var(--panel);border:1px solid var(--border);border-radius:var(--radius);padding:1rem}
.card .top{display:flex;justify-content:space-between;align-items:center;margin-bottom:.5rem}
.card .name{font-weight:700}
.pill{font-size:.75rem;padding:.15rem .55rem;border-radius:999px}
.pill.working{background:rgba(56,208,214,.18);color:var(--accent)}
.pill.idle{background:rgba(139,144,166,.18);color:var(--muted)}
.pill.error{background:rgba(255,107,122,.18);color:var(--err)}
.task{color:var(--muted);font-size:.9rem;min-height:1.2em;margin:.3rem 0}
.bar{height:6px;background:var(--panel2);border-radius:999px;overflow:hidden}
.bar > i{display:block;height:100%;background:linear-gradient(90deg,var(--accent),var(--violet))}
.log-list{font-family:Consolas,monospace;font-size:.82rem;background:#0d0f17;border:1px solid var(--border);border-radius:var(--radius);padding:.8rem;max-height:70vh;overflow:auto;white-space:pre-wrap}
.settings-form{display:flex;flex-direction:column;gap:.7rem;max-width:420px}
.settings-form input{background:var(--panel);border:1px solid var(--border);color:var(--fore);padding:.7rem;border-radius:10px;font-size:1rem}
.settings-form button{background:var(--accent);color:#0c1020;border:none;padding:.8rem;border-radius:10px;font-weight:700;font-size:1rem;cursor:pointer}
.hint{color:var(--muted);font-size:.85rem;min-height:1.2em}
@media (max-width:600px){
  .app-header{flex-direction:column;align-items:flex-start}
  main{padding:.8rem}
  .agent-grid{grid-template-columns:1fr}
  .tab{flex:1 1 0;text-align:center}
}
```

- [ ] **Step 3: js/app.js 작성**

```javascript
const $ = (s, r=document) => r.querySelector(s);
const $$ = (s, r=document) => [...r.querySelectorAll(s)];

// ---- 탭 라우팅 ----
$$('.tab').forEach(btn => btn.addEventListener('click', () => {
  $$('.tab').forEach(b => b.classList.remove('active'));
  $$('.view').forEach(v => v.classList.remove('active'));
  btn.classList.add('active');
  $('#' + btn.dataset.view).classList.add('active');
}));

// ---- 서버 상태 ----
async function refreshStatus(){
  try{
    const r = await fetch('/api/server/status'); const s = await r.json();
    const badge = $('#statusBadge'), url = $('#serverUrl');
    if(s.active){ badge.textContent='🟢 서버 활성'; badge.className='badge on';
      url.textContent=s.url; url.href=s.url; }
    else { badge.textContent='🔴 중지'; badge.className='badge off'; url.textContent=''; url.removeAttribute('href'); }
  }catch(e){ const b=$('#statusBadge'); b.textContent='🔴 연결 안 됨'; b.className='badge off'; }
}

// ---- 대시보드 ----
function renderAgents(list){
  const working = list.filter(a=>a.status==='working').length;
  const error = list.filter(a=>a.status==='error').length;
  $('#summary').innerHTML =
    `<div class="stat"><div class="n">${list.length}</div><div class="l">전체 에이전트</div></div>`+
    `<div class="stat"><div class="n">${working}</div><div class="l">작업 중</div></div>`+
    `<div class="stat"><div class="n">${error}</div><div class="l">오류</div></div>`;
  $('#agentGrid').innerHTML = list.map(a=>`
    <div class="card">
      <div class="top"><span class="name">${esc(a.name)}</span><span class="pill ${a.status}">${label(a.status)}</span></div>
      <div class="task">${esc(a.currentTask||'—')}</div>
      <div class="bar"><i style="width:${a.progress||0}%"></i></div>
    </div>`).join('');
}
const label = s => ({working:'작업 중',idle:'대기',error:'오류'}[s]||s);
const esc = s => (s||'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
async function refreshAgents(){
  try{ const r=await fetch('/api/agents'); renderAgents(await r.json()); }catch(e){}
}

// ---- 로그 (데스크톱 WebView가 addLog 호출) ----
window.addLog = function(ev){
  const el=$('#logList'); if(!el) return;
  const line=document.createElement('div');
  const msg = typeof ev==='string'?ev:(ev && ev.Message)||JSON.stringify(ev);
  line.textContent=`[${new Date().toLocaleTimeString()}] ${msg}`;
  el.appendChild(line); el.scrollTop=el.scrollHeight;
  while(el.childNodes.length>500) el.removeChild(el.firstChild);
};

// ---- 설정 ----
async function loadSettings(){
  try{ const r=await fetch('/api/settings'); const s=await r.json(); $('#portInput').value=s.port; }catch(e){}
}
$('#settingsForm').addEventListener('submit', async e=>{
  e.preventDefault();
  const port=parseInt($('#portInput').value,10);
  const hint=$('#settingsHint');
  hint.textContent='저장 중…';
  try{
    const r=await fetch('/api/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({port})});
    const res=await r.json();
    if(res.ok){ hint.textContent=`저장됨. 새 주소로 재시작합니다: ${res.url} (잠시 후 재접속)`;
      setTimeout(()=>{ location.href=res.url; }, 1500); }
    else hint.textContent='오류: '+(res.message||'실패');
  }catch(err){ hint.textContent='요청 실패: '+err.message; }
});

// ---- PWA ----
if('serviceWorker' in navigator){ navigator.serviceWorker.register('/sw.js').catch(()=>{}); }

// ---- 초기화 + 폴링 ----
refreshStatus(); refreshAgents(); loadSettings();
setInterval(refreshStatus, 5000);
setInterval(refreshAgents, 5000);
```

- [ ] **Step 4: 기존 HTML 삭제**

```powershell
Remove-Item -Force AgentHub/View/Htmls/server_log.html, AgentHub/View/Htmls/server_setting.html
```

- [ ] **Step 5: csproj에 신규 Content 등록**

`AgentHub.csproj`의 `View\Htmls` Content 그룹에 추가(CopyToOutputDirectory=Always): `View\Htmls\index.html`, `View\Htmls\css\app.css`, `View\Htmls\js\app.js`, `View\Htmls\manifest.webmanifest`, `View\Htmls\sw.js`, `View\Htmls\icons\icon-192.png`, `View\Htmls\icons\icon-512.png`. 기존 `common.css`는 유지하거나 미사용 시 제거.

---

### Task 7: PWA (manifest + service worker)

**Files:**
- Create: `AgentHub/View/Htmls/manifest.webmanifest`, `AgentHub/View/Htmls/sw.js`

**Interfaces:**
- Consumes: `icons/icon-192.png`,`icon-512.png`(Task 1). `index.html`이 manifest 링크 + `app.js`가 SW 등록(Task 6).
- Produces: 설치 가능(installable) 조건 충족(HTTPS+manifest+SW).

- [ ] **Step 1: manifest.webmanifest**

```json
{
  "name": "Agent Hub",
  "short_name": "Agent Hub",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "background_color": "#12141f",
  "theme_color": "#181c2a",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png", "purpose": "any maskable" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png", "purpose": "any maskable" }
  ]
}
```

- [ ] **Step 2: sw.js (최소 캐시)**

```javascript
const CACHE = 'agent-hub-v1';
const ASSETS = ['/', '/index.html', '/css/app.css', '/js/app.js', '/icons/icon-192.png', '/icons/icon-512.png'];
self.addEventListener('install', e => { e.waitUntil(caches.open(CACHE).then(c => c.addAll(ASSETS)).then(()=>self.skipWaiting())); });
self.addEventListener('activate', e => { e.waitUntil(caches.keys().then(ks => Promise.all(ks.filter(k=>k!==CACHE).map(k=>caches.delete(k)))).then(()=>self.clients.claim())); });
self.addEventListener('fetch', e => {
  const u = new URL(e.request.url);
  if (u.pathname.startsWith('/api/')) return; // API는 항상 네트워크
  e.respondWith(caches.match(e.request).then(r => r || fetch(e.request)));
});
```

- [ ] **Step 3: 빌드에 포함 확인** — Task 6 Step 5의 csproj Content에 이미 포함.

---

### Task 8: FormMain 리팩터 (단일 WebView + 트레이 완전종료 + open-url + cert 핸들러 + 로그 대상)

**Files:**
- Modify: `AgentHub/View/Forms/FormMain.cs`, `AgentHub/View/Forms/FormMain.Designer.cs`

**Interfaces:**
- Consumes: `EmbedIOServer.StartServer/StopServer/CurrentUrl`(Task 4). SPA의 `window.addLog`(Task 6).
- Produces: 단일 `webViewMain`이 `EmbedIOServer.CurrentUrl` 로드. 트레이 컨텍스트 메뉴(열기/완전 종료). `_isExiting` 플래그.

- [ ] **Step 1: Designer 정리 — 단일 WebView**

`FormMain.Designer.cs`에서 `webViewLeft`, `webViewCenter`, `webViewServer` 3개 대신 콘텐츠 영역을 채우는 `webViewMain` 1개만 남긴다(기존 `webViewServer`를 `webViewMain`으로 개명·확장하고 나머지 두 개 및 관련 패널/사이드 영역 선언·초기화 제거). 커스텀 타이틀바/버튼/리사이즈 패널은 유지.

- [ ] **Step 2: FormMain.cs — 초기화 재작성**

`InitializeControl`를 단일 WebView + 서버 우선 시작으로 변경:
```csharp
private async void InitializeControl()
{
    ApiLogger.Initialize();
    Logger.RegisterLogger(this);
    SetVersionInfo();
    InitTrayMenu();
    LoadSetting();
    ControlBox = false;
    ActiveControl = lblTitle;

    EmbedIOServer.StartServer();          // 서버 먼저
    _stopwatch.Start(); _timer.Tick += Timer_Tick; _timer.Start();

    await webViewMain.EnsureCoreWebView2Async();
    webViewMain.CoreWebView2.ServerCertificateErrorDetected += (s, e) =>
    {
        // 로컬 자체서명 인증서 허용
        e.Action = Microsoft.Web.WebView2.Core.CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
    };
    webViewMain.CoreWebView2.NewWindowRequested += (s, e) =>
    {
        e.Handled = true;
        System.Diagnostics.Process.Start(e.Uri);   // 외부 링크는 시스템 브라우저로
    };
    webViewMain.CoreWebView2.Navigate(EmbedIOServer.CurrentUrl);

#if DEBUG
    SetShowWindow(true);
#else
    SetShowWindow(false);
#endif
}
```
> `ChangeMenu`, `webViewServer_NavigationCompleted`(서버 시작을 여기서 하던 로직) 제거. 서버 시작은 `InitializeControl`로 이동.

- [ ] **Step 3: 트레이 컨텍스트 메뉴 + 완전 종료**

```csharp
private bool _isExiting;

private void InitTrayMenu()
{
    var menu = new ContextMenu();
    menu.MenuItems.Add(new MenuItem("열기", (s, e) => SetShowWindow(true)));
    menu.MenuItems.Add("-");
    menu.MenuItems.Add(new MenuItem("완전 종료", (s, e) => ExitApplication()));

    _notify = new NotifyIcon
    {
        Icon = Properties.Resources.trayicon_32x32,
        Visible = true,
        ContextMenu = menu,
        Text = ProgramInfo.KoreanName
    };
    _notify.DoubleClick += Notify_DoubleClick;
}

private void ExitApplication()
{
    _isExiting = true;
    try { EmbedIOServer.StopServer(); } catch { }
    if (_notify != null) { _notify.Visible = false; _notify.Dispose(); _notify = null; }
    Application.Exit();
}
```

- [ ] **Step 4: OnClosing — 완전 종료 시 통과**

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    if (!_isExiting)
    {
        e.Cancel = true;
        SetShowWindow(false);
    }
    base.OnClosing(e);
}
```

- [ ] **Step 5: 로그 대상 WebView 변경**

`WriteLog`의 `webViewServer.CoreWebView2?.ExecuteScriptAsync(...)`를 `webViewMain.CoreWebView2?.ExecuteScriptAsync($"addLog({JsonConvert.SerializeObject(logEvent)})")`로 변경. SPA의 `window.addLog`가 수신.

- [ ] **Step 6: 미사용 참조 정리**

`using AgentHub.View.bridges;` 제거, `ChangeMenu`/`webViewServer_NavigationCompleted`/`Notify_DoubleClick` 외 사이드메뉴 관련 코드 제거. `AddHostObjectToScript("bridge", ...)` 제거.

---

### Task 9: csproj 동기화 + 전체 빌드 + 검증

**Files:**
- Modify: `AgentHub/AgentHub.csproj` (누락/삭제 항목 최종 정리)

- [ ] **Step 1: csproj 최종 점검**

Run: `pwsh -c "Select-String -Path AgentHub\AgentHub.csproj -Pattern 'FormPrint|Prints|Checkup|Patient|Printer|SideMenu|server_log|server_setting|WebSocketPrinterModule'"`
Expected: 출력 없음(제거된 항목이 csproj에 남지 않음). index.html/app.css/app.js/manifest/sw/icons Content 존재 확인.

- [ ] **Step 2: 정리 대상 잔재 grep**

Run: `pwsh -c "Select-String -Path AgentHub\**\*.cs -Pattern 'Checkup|FormPrint|Patient|Printer|SideMenuBridge|GetPrinters'"`
Expected: 출력 없음.

- [ ] **Step 3: Restore + Build**

Run: 빌드 명령(Global Constraints).
Expected: 에러 0, `install/Debug/AgentHub.exe` 생성. (경고는 기존 EmbedIO nullable 경고만 허용)

- [ ] **Step 4: 실행 검증 (수동, 스펙 성공 기준)**

`install/Debug/AgentHub.exe` 실행 후 확인:
1. WebView에 대시보드 표시 + 상단 🟢 상태 + 클릭 가능한 `https://127.0.0.1:{port}` 링크.
2. 링크 클릭 → 시스템 브라우저로 동일 대시보드(HTTPS) 접속.
3. 탭 전환(대시보드/로그/설정) 동작. 로그 탭에 요청 로그 유입.
4. 설정 탭에서 포트 변경·저장 → 서버 재시작 + 새 URL 안내 + 재접속.
5. 트레이 우클릭 → "완전 종료" → 프로세스 종료(작업관리자 확인), 재실행 시 중복 경고 없음.
6. 창 폭을 좁히면 카드 1열로 반응형 전환.
7. 브라우저 DevTools > Application > Manifest에 Agent Hub 인식, SW 등록, "installable".

- [ ] **Step 5: Commit** (사용자 승인 시)

```bash
git add -A
git commit -m "feat: replace checkup/print UI with responsive PWA agent monitoring dashboard

- single WebView2 loads EmbedIO-served SPA over HTTPS
- tray context menu with full exit
- configurable server port with live restart
- server status + clickable URL in header
- mock /api/agents, /api/server/status, /api/settings
- PWA manifest + service worker; new hub-motif icons
- remove checkup/print forms, models, pages, printer WS module"
```

---

## Self-Review (작성자 점검 결과)

- **스펙 커버리지**: 트레이 완전종료(T8), 아이콘(T1), 포트 설정(T3+T5+T4 restart), 서버 상태/URL(T5 status + T6 header + T8 open-url), 검진/프린트 제거(T2+T9), 반응형(T6 CSS), HTTPS 유지(T4), PWA(T7) — 모두 태스크 존재.
- **플레이스홀더**: 없음(코드 제시). 단 `FileModule`/기본문서 API는 실제 EmbedIO 시그니처에 맞춰 T4 Step1 주석대로 조정(버전 차이 대응) — 실행 시 확정.
- **타입 일관성**: `EmbedIOServer.CurrentUrl/CurrentPort/IsRunning/StartServer/StopServer/RestartServer`가 T4 정의 → T5/T8 사용 일치. `window.addLog`가 T6 정의 → T8 호출 일치. `PortSetting.Port`(T5) ↔ app.js `{port}`(T6) 일치.
- **주의(실행 중 확정 필요)**: EmbedIO 정적 서빙의 기본 문서/폴백 API, WebView2 `ServerCertificateErrorDetected` enum 정확 명칭, `Process.Start(uri)`의 .NET FW 4.8 동작(정상). 이들은 빌드/실행에서 검증.
