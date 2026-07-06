# GitHub 배포 · 자동 업데이트 · 다운로드 집계 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agent Hub를 GitHub Releases로 배포하고, Velopack으로 사용자별 설치 + 조용한 자동 업데이트를 제공하며, 저장소를 공개해 다운로드 배지를 노출한다.

**Architecture:** 앱을 비관리자(asInvoker)로 전환(인증서 CurrentUser\Root, LocalAppData 설치)하고, Velopack으로 패키징한다. `Program.Main` 최상단 `VelopackApp.Build().Run()`, 백그라운드 `UpdateService`가 GitHub Releases에서 업데이트를 받아 재시작 시 적용. GitHub Actions가 태그 푸시에 릴리스를 자동 생성.

**Tech Stack:** C# 8 / .NET Framework 4.8 / WinForms / Velopack / GitHub Actions / vpk CLI / Shields.io.

## Global Constraints

- 저장소 공개 전 소스/히스토리에 시크릿(인증서 비밀번호) 잔존 금지.
- 앱은 관리자 권한 없이 동작해야 함(`asInvoker`, `CurrentUser\Root`, LocalAppData 쓰기).
- Velopack 사용자별 설치 모델 유지. Inno Setup 제거.
- 업데이트 UX: **조용히 다운로드 → 트레이 알림 → 재시작 시 적용**. 강제 재시작 금지. 미설치(개발) 환경에선 skip.
- 서드파티 `EmbedIO/` 수정 금지. 한글 UTF-8 인코딩 보존.
- 커밋/공개/force-push는 사용자 승인 시에만.
- 빌드: `msbuild AgentHub.sln /t:Restore` → `/t:Build /p:Configuration=Release`. 검증은 빌드+`vpk pack`+실행 관찰.
- 저장소: `https://github.com/YOOJUNGYU/agent-hub` (기본 브랜치 main).

---

## 파일 구조
- 수정: `AgentHub/Properties/Settings.settings`·`Settings.Designer.cs`·`App.config` (`ServerCertPassword`)
- 수정: `AgentHub/Common/Constants.cs` (하드코딩 `CertPassword` 제거)
- 수정: `AgentHub/Server/EmbedIOServer.cs` (비번 생성/설정 기반 + `CurrentUser\Root`)
- 수정: `AgentHub/Properties/app.manifest` (`asInvoker`)
- 수정: `AgentHub/Program.cs` (`VelopackApp.Build().Run()` + 업데이트 트리거)
- 수정: `AgentHub/View/Forms/FormMain.cs` (업데이트 체크 호출 + 트레이 알림 + "지금 업데이트" 메뉴)
- 수정: `AgentHub/AgentHub.csproj`·`AgentHub/packages.config` (Velopack 참조)
- 신규: `AgentHub/Common/Util/UpdateService.cs`
- 신규: `.github/workflows/release.yml`
- 수정: `README.md` (배지·설치 안내)
- 삭제: `install/Installer.iss`

---

### Task 1: 인증서 비밀번호 시크릿 분리

**Files:**
- Modify: `AgentHub/Properties/Settings.settings`, `Settings.Designer.cs`, `App.config`, `Common/Constants.cs`, `Server/EmbedIOServer.cs`

**Interfaces:**
- Produces: `Properties.Settings.Default.ServerCertPassword` (string, 기본 ""), `EmbedIOServer` 내부 `GetCertPassword()`.

- [ ] **Step 1: Settings에 ServerCertPassword 추가**

`Settings.settings` `<Settings>`에:
```xml
<Setting Name="ServerCertPassword" Type="System.String" Scope="User">
  <Value Profile="(Default)" />
</Setting>
```
`Settings.Designer.cs`에 프로퍼티 추가:
```csharp
[global::System.Configuration.UserScopedSettingAttribute()]
[global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
[global::System.Configuration.DefaultSettingValueAttribute("")]
public string ServerCertPassword {
    get { return ((string)(this["ServerCertPassword"])); }
    set { this["ServerCertPassword"] = value; }
}
```
`App.config` `<AgentHub.Properties.Settings>`에:
```xml
<setting name="ServerCertPassword" serializeAs="String">
  <value />
</setting>
```

- [ ] **Step 2: Constants에서 하드코딩 비번 제거**

`Common/Constants.cs`의 `SelfSigned` 클래스에서 아래 줄 삭제:
```csharp
public static string CertPassword => "OvnECM(*@!$(cnTei@#%NoE";
```
(CertFilePath, PfxFileName, CrtFileName는 유지)

- [ ] **Step 3: EmbedIOServer에 비번 생성기 추가 + 참조 교체**

`EmbedIOServer`에 메서드 추가:
```csharp
private static string GetCertPassword()
{
    var pw = Properties.Settings.Default.ServerCertPassword;
    if (string.IsNullOrEmpty(pw))
    {
        pw = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        Properties.Settings.Default.ServerCertPassword = pw;
        Properties.Settings.Default.Save();
    }
    return pw;
}
```
`GetSelfSignedCertificate` 안의 `SelfSigned.CertPassword` 3곳을 지역 변수로 교체:
```csharp
var certPw = GetCertPassword();
// new X509Certificate2(pfxFilePathName, certPw)
// certificate.Export(X509ContentType.Pfx, certPw)
// new X509Certificate2(certRawData, certPw)
```
(캐시된 인증서 로드 시에도 `certPw` 사용)

- [ ] **Step 4: 빌드 확인**

Run: `msbuild AgentHub.sln /p:Configuration=Debug /v:minimal /nologo`
Expected: 에러 0. `grep -rn CertPassword AgentHub --include=*.cs` → `GetCertPassword`/`certPw`만, 하드코딩 문자열 없음.

---

### Task 2: 비관리자(asInvoker) 전환

**Files:**
- Modify: `AgentHub/Properties/app.manifest`, `Server/EmbedIOServer.cs`

**Interfaces:**
- Consumes: `GetCertPassword()` (Task 1).
- Produces: 관리자 없이 실행 가능한 앱.

- [ ] **Step 1: 매니페스트 asInvoker**

`Properties/app.manifest`에서 활성 요소를 변경:
```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
```
(기존 `requireAdministrator` 라인을 asInvoker로. 주석 블록은 그대로 둠)

- [ ] **Step 2: 인증서 스토어 CurrentUser로**

`EmbedIOServer.GetSelfSignedCertificate`의 store 등록부:
```csharp
using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
{
    store.Open(OpenFlags.ReadWrite);
    store.Add(x509Certificate2);
    store.Close();
}
```

- [ ] **Step 3: 빌드 + 비관리자 실행 검증**

Run: 빌드 후 `install/Debug/AgentHub.exe` 실행(비관리자).
Expected: UAC 프롬프트 없음(최초 인증서 동의창만), 서버 활성, `https://127.0.0.1:8080/host` 200. (관리자 승격 요구가 사라졌는지 확인)

---

### Task 3: Velopack 통합 (패키징 + 자동 업데이트)

**Files:**
- Modify: `AgentHub/packages.config`, `AgentHub/AgentHub.csproj`, `Program.cs`, `View/Forms/FormMain.cs`
- Create: `AgentHub/Common/Util/UpdateService.cs`

**Interfaces:**
- Produces: `UpdateService.CheckAndDownloadAsync() : Task<bool>`, `UpdateService.ApplyAndRestart()`. `VelopackApp.Build().Run()` in Main.

- [ ] **Step 1: Velopack NuGet 추가**

최신 안정 버전을 설치(정확 버전은 설치 시 확정):
```powershell
nuget install Velopack -OutputDirectory packages -ExcludeVersion:$false
```
`packages.config`에 항목 추가(설치된 버전으로):
```xml
<package id="Velopack" version="<installed-version>" targetFramework="net48" />
```
`AgentHub.csproj` `<ItemGroup>`(참조)에 HintPath 추가(설치 버전 경로로):
```xml
<Reference Include="Velopack">
  <HintPath>..\packages\Velopack.<ver>\lib\netstandard2.0\Velopack.dll</HintPath>
</Reference>
```
> 실제 lib 경로/의존 DLL(예: NuGet 종속성)은 설치 결과에 맞춰 참조 추가. 빌드 에러가 나면 누락 참조를 채운다.

- [ ] **Step 2: UpdateService 작성** (`Common/Util/UpdateService.cs`)

```csharp
using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace AgentHub.Common.Util
{
    public static class UpdateService
    {
        private const string RepoUrl = "https://github.com/YOOJUNGYU/agent-hub";
        private static UpdateManager _mgr;
        private static UpdateInfo _pending;

        /// <summary>업데이트 확인 후 조용히 다운로드. 준비되면 true.</summary>
        public static async Task<bool> CheckAndDownloadAsync()
        {
            try
            {
                _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
                if (!_mgr.IsInstalled) return false;           // 개발/미설치 환경 skip
                var info = await _mgr.CheckForUpdatesAsync();
                if (info == null) return false;
                await _mgr.DownloadUpdatesAsync(info);
                _pending = info;
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                return false;
            }
        }

        /// <summary>다운로드된 업데이트를 적용하고 재시작.</summary>
        public static void ApplyAndRestart()
        {
            if (_mgr != null && _pending != null)
                _mgr.ApplyUpdatesAndRestart(_pending);
        }
    }
}
```
> Velopack 버전에 따라 `ApplyUpdatesAndRestart`/`DownloadUpdatesAsync`/`CheckForUpdatesAsync` 시그니처가 다를 수 있음 — 설치 버전의 API에 맞춰 조정(빌드로 확정).

- [ ] **Step 3: Program.cs에 VelopackApp 부트스트랩**

`Main` **최상단**(다른 어떤 코드보다 먼저):
```csharp
[STAThread]
private static void Main()
{
    Velopack.VelopackApp.Build().Run();   // 설치/업데이트/제거 훅 — 반드시 최상단

    SetProcessDpiAwareness(ProcessDpiAwareness.ProcessSystemDpiAware);
    // ...기존 로직...
}
```
파일 상단 `using Velopack;`는 FQN(`Velopack.VelopackApp`)로 대체하거나 using 추가.

- [ ] **Step 4: FormMain에서 업데이트 체크 + 트레이 알림 + 메뉴**

`InitializeControl` 끝(네비게이트 이후)에 백그라운드 체크:
```csharp
_ = UpdateService.CheckAndDownloadAsync().ContinueWith(t =>
{
    if (t.IsFaulted || !t.Result || IsDisposed) return;
    try
    {
        BeginInvoke((Action)(() =>
        {
            _updateReady = true;
            _notify?.ShowBalloonTip(5000, "Agent Hub",
                "새 버전이 준비되었습니다. 재시작 시 적용됩니다.", ToolTipIcon.Info);
        }));
    }
    catch (Exception ex) { LogService.Instance.Error(ex); }
});
```
필드 추가: `private bool _updateReady;`
트레이 메뉴(`InitTrayMenu`)에 항목 추가(열기와 완전 종료 사이):
```csharp
menu.MenuItems.Add(new MenuItem("지금 업데이트 후 재시작", (s, e) =>
{
    if (_updateReady) UpdateService.ApplyAndRestart();
}));
```
`ExitApplication`은 그대로(완전 종료 시 다음 실행에서 신버전 적용됨).

- [ ] **Step 5: Inno 스크립트 삭제**

```powershell
Remove-Item install\Installer.iss
```

- [ ] **Step 6: 빌드 + 로컬 pack 검증**

```powershell
msbuild AgentHub.sln /t:Restore
msbuild AgentHub.sln /p:Configuration=Release /p:Platform="Any CPU" /v:minimal /nologo
dotnet tool install -g vpk
vpk pack -u AgentHub -v 0.0.1 -p install\Release -e AgentHub.exe --framework net48,webview2
```
Expected: 빌드 에러 0, `Releases\AgentHub-win-Setup.exe` 생성. (설치·실행은 별도 관찰)

---

### Task 4: GitHub Actions 릴리스 워크플로

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- 트리거: 태그 `v*` 푸시. 산출물: GitHub Release + Setup.exe 자산.

- [ ] **Step 1: 워크플로 작성**

```yaml
name: release
on:
  push:
    tags: ['v*']
permissions:
  contents: write
jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: microsoft/setup-msbuild@v2
      - uses: nuget/setup-nuget@v2
      - name: Restore NuGet
        run: nuget restore AgentHub.sln
      - name: Build (Release)
        run: msbuild AgentHub.sln /p:Configuration=Release /p:Platform="Any CPU"
      - name: Install vpk
        run: dotnet tool install -g vpk
      - name: Derive version
        id: ver
        shell: pwsh
        run: echo "version=$($env:GITHUB_REF_NAME -replace '^v','')" >> $env:GITHUB_OUTPUT
      - name: Pack (Velopack)
        run: vpk pack -u AgentHub -v ${{ steps.ver.outputs.version }} -p install\Release -e AgentHub.exe --framework net48,webview2
      - name: Upload to GitHub Release
        run: vpk upload github --repoUrl https://github.com/YOOJUNGYU/agent-hub --token ${{ secrets.GITHUB_TOKEN }} --publish --releaseName "Agent Hub ${{ steps.ver.outputs.version }}" --tag ${{ github.ref_name }}
```

- [ ] **Step 2: YAML 문법 점검**

Run: `pwsh -c "python -c \"import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml'))\""` (파이썬 있으면) 또는 육안 점검.
Expected: 파싱 오류 없음. (실동작 검증은 태그 푸시 후 Actions 로그 — Task 6.)

---

### Task 5: README 배지 + 설치 안내

**Files:**
- Modify: `README.md`

- [ ] **Step 1: 배지·다운로드·설치 섹션 추가**

`README.md` 상단(제목 아래)에:
```markdown
[![Downloads](https://img.shields.io/github/downloads/YOOJUNGYU/agent-hub/total)](https://github.com/YOOJUNGYU/agent-hub/releases)
[![Latest Release](https://img.shields.io/github/v/release/YOOJUNGYU/agent-hub)](https://github.com/YOOJUNGYU/agent-hub/releases/latest)
```
"설치" 섹션 추가:
```markdown
## 설치 (Install)

1. [최신 릴리스](https://github.com/YOOJUNGYU/agent-hub/releases/latest)에서 `AgentHub-win-Setup.exe` 다운로드.
2. 실행하면 `%LocalAppData%\AgentHub`에 설치되고 트레이에 상주합니다(관리자 권한 불필요).
3. 최초 실행 시 로컬 HTTPS용 자체서명 인증서 설치 동의창이 한 번 표시됩니다.
4. 이후 새 버전은 실행 중 자동으로 다운로드되어 **재시작 시 적용**됩니다.

> 코드 서명 미적용이라 다운로드/최초 실행 시 SmartScreen "알 수 없는 게시자" 경고가 보일 수 있습니다. "추가 정보 → 실행"으로 진행하세요.
```

- [ ] **Step 2: 확인**

Run: `grep -n 'shields.io\|releases/latest' README.md`
Expected: 배지·링크 존재.

---

### Task 6: 공개 전환 · 히스토리 스쿼시 · 첫 릴리스 (⚠️ 되돌리기 어려움 — 사용자 승인 후)

**Files:** git 히스토리, GitHub 저장소 설정

- [ ] **Step 1: 최종 빌드 검증 + 커밋** (사용자 승인 시)

전체 빌드 에러 0 확인 후 변경사항 커밋.

- [ ] **Step 2: 히스토리 스쿼시(옛 비밀번호 제거)** (사용자 승인 필수)

현재 히스토리를 단일 커밋으로 재작성(옛 커밋의 하드코딩 비번 흔적 제거):
```bash
git checkout --orphan clean-main
git add -A
git commit -m "Agent Hub: AI agent monitoring hub (initial public)"
git branch -D main
git branch -m main
git push -f origin main
```
Expected: 원격 main이 단일 클린 커밋으로 대체. `git log` 1개.

- [ ] **Step 3: 저장소 공개 전환** (사용자 승인 필수)

```bash
gh repo edit YOOJUNGYU/agent-hub --visibility public --accept-visibility-change-consequences
```
Expected: `gh repo view --json visibility` → PUBLIC.

- [ ] **Step 4: 첫 릴리스 태그 푸시**

```bash
git tag v0.0.1
git push origin v0.0.1
```
Expected: Actions `release` 워크플로 실행 → 릴리스 `v0.0.1` 생성 + `AgentHub-win-Setup.exe` 자산. `gh run watch`로 확인.

- [ ] **Step 5: 최종 검증**

- 릴리스 페이지에 Setup.exe 존재.
- README 배지가 렌더링(총 다운로드/최신 버전).
- (가능하면) 다른 PC/계정에서 Setup.exe 다운로드·설치·실행 → 트레이 상주.

---

## Self-Review (작성자 점검)
- **스펙 커버리지**: 시크릿 분리(T1), 비관리자(T2), Velopack 패키징+자동업데이트(T3), CI(T4), 배지·안내(T5), 공개·스쿼시·릴리스(T6) — 스펙 2.1~2.5 + 6절 런북 모두 태스크로 매핑.
- **플레이스홀더**: Velopack 버전/lib 경로/일부 API 시그니처는 "설치 버전에 맞춰 확정"으로 명시(외부 패키지라 실측 필요) — 실행 시 빌드로 확정.
- **타입 일관성**: `UpdateService.CheckAndDownloadAsync()`/`ApplyAndRestart()`가 T3에서 정의→FormMain(T4 아님, T3 Step4)에서 사용 일치. `ServerCertPassword`(T1)↔`GetCertPassword`(T1) 일치.
- **되돌리기 어려운 단계**(T6 공개/force-push)는 사용자 승인 게이트 명시.
