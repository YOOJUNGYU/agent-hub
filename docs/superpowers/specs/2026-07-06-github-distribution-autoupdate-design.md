# Agent Hub — GitHub 배포 · 자동 업데이트 · 다운로드 집계 설계

- 작성일: 2026-07-06
- 상태: 설계 확정 대기(사용자 검토)

## 1. 목표

GitHub를 통해 설치 exe를 배포하고, 사람들이 링크로 다운로드·설치하며, 변경사항이 자동으로 업데이트되고, 다운로드 수를 집계해서 볼 수 있게 한다.

### 브레인스토밍 확정 사항
- **배포 모델**: 저장소(`YOOJUNGYU/agent-hub`)를 **Public 전환**. 익명 다운로드 + 무제한 무료 Actions + Shields 배지 가능.
- **설치/업데이트 도구**: **Velopack** (Inno Setup 대체). GitHub Releases를 업데이트 소스로 직접 사용, 델타·자동 업데이트, 사용자별 설치.
- **권한 모델**: **비관리자(asInvoker)** 전환. 인증서를 `CurrentUser\Root`로, 설치를 `%LocalAppData%\AgentHub`로 → 관리자 없이 동작 + UAC 없는 조용한 자동 업데이트.
- **다운로드 집계**: README에 **Shields 총합 다운로드 배지**(최소 구성).

### 근거: 관리자 권한 불필요 검증
현 코드에서 관리자를 요구하는 지점은 **단 하나**(`EmbedIOServer` 인증서를 `LocalMachine\Root`에 등록) + Program Files 설치뿐. 그 외 HKLM/netsh/서비스/http.sys 사용 없음(코드 스캔 확인). 인증서를 `CurrentUser\Root`(관리자 불필요, 최초 1회 동의창)로, 설치를 LocalAppData로 옮기면 앱의 모든 동작이 비관리자로 성립한다. EmbedIO 자체 소켓 리스너의 `+:8080` 바인딩은 http.sys/URL ACL을 쓰지 않아 관리자 불필요. **유일한 예외**는 다른 기기(모바일) LAN 접속을 위한 Windows 방화벽 인바운드 규칙(1회 관리자) — 이는 권한 모델과 무관하게 필요하며 모바일 실접속과 함께 다음 단계로 둔다.

## 2. 컴포넌트 설계

### 2.1 시크릿 분리 (공개 전 선행)
- `Constants.SelfSigned.CertPassword` 하드코딩 제거.
- 첫 인증서 생성 시 **랜덤 비밀번호 생성** → 사용자 설정(`Settings.ServerCertPassword`, User scope, app.config in LocalAppData)에 저장하고 재사용. 소스/저장소에 비밀값 없음.
- 기존 커밋에 남은 옛 비밀번호: 방식 변경 후 어떤 인증서에도 쓰이지 않는 죽은 값(위험 낮음). 공개 직전 **히스토리 스쿼시**로 정리(선택, 아래 6절).

### 2.2 비관리자 전환
- `Properties/app.manifest`: `requireAdministrator` → `asInvoker`.
- `EmbedIOServer.GetSelfSignedCertificate`: `StoreLocation.LocalMachine` → `StoreLocation.CurrentUser`. (WebView2는 이미 `ServerCertificateErrorDetected=AlwaysAllow`로 통과하므로 등록 실패해도 데스크톱은 동작 — 등록은 브라우저 열기 UX용.)
- 인증서 파일/로그/설정은 설치 위치(LocalAppData) 하위라 쓰기 자유.

### 2.3 Velopack 패키징 + 자동 업데이트
- `Velopack` NuGet 추가(packages.config).
- `Program.Main` **최상단**에 `VelopackApp.Build().Run();` (설치/업데이트/제거 훅 — 반드시 다른 코드보다 먼저).
- 설치 산출물: `AgentHub-win-Setup.exe`(사용자별, `%LocalAppData%\AgentHub`).
- WebView2 런타임: `vpk pack --framework net48,webview2`로 미설치 시 자동 설치 처리.
- 자동 업데이트: `Common/Util/UpdateService.cs`
  - `UpdateManager(new GithubSource("https://github.com/YOOJUNGYU/agent-hub", null, false))`
  - 앱 시작 후 백그라운드에서 `CheckForUpdatesAsync()` → 있으면 `DownloadUpdatesAsync()`.
  - 완료 시 **트레이 알림(BalloonTip) "새 버전 준비됨 — 재시작 시 적용"**. 사용자가 수락하거나 다음 실행 시 `ApplyUpdatesAndRestart()`.
  - 비침습적: UI 블로킹/강제 재시작 없음. 개발 빌드(디버거 연결/미설치)에서는 조용히 skip.
- 기존 `install/Installer.iss`(Inno) **삭제**(역할 종료).

### 2.4 CI — GitHub Actions
- `.github/workflows/release.yml`, 트리거: 태그 `v*` 푸시.
- 러너: `windows-latest`.
- 단계:
  1. `actions/checkout`
  2. `microsoft/setup-msbuild`
  3. NuGet 복원(`nuget restore AgentHub.sln` 또는 `msbuild /t:restore`) — packages.config 프로젝트
  4. `msbuild AgentHub.sln /p:Configuration=Release`
  5. `dotnet tool install -g vpk`
  6. `vpk pack --packId AgentHub --packVersion <tag에서 파생> --packDir install\Release --mainExe AgentHub.exe --framework net48,webview2`
  7. `vpk upload github --repoUrl https://github.com/YOOJUNGYU/agent-hub --token ${{ secrets.GITHUB_TOKEN }} --publish --tag <tag> --releaseName "Agent Hub <ver>"`
- 공개 저장소 → 무제한 무료, 내장 `GITHUB_TOKEN`으로 릴리스 생성(별도 PAT 불필요).
- 버전: 태그 `vX.Y.Z` → `X.Y.Z`.

### 2.5 다운로드 배지 + 안내 (README)
- 배지: `![Downloads](https://img.shields.io/github/downloads/YOOJUNGYU/agent-hub/total)`
- 최신 다운로드 링크: `https://github.com/YOOJUNGYU/agent-hub/releases/latest`
- 설치 안내(다운로드 → 실행 → 최초 인증서 동의창 → 트레이 상주) + SmartScreen 경고 관련 한 줄 안내.

## 3. 파일 변경 요약
- 신규: `AgentHub/Common/Util/UpdateService.cs`, `.github/workflows/release.yml`
- 수정: `Program.cs`(VelopackApp + 업데이트 체크 트리거), `Server/EmbedIOServer.cs`(CurrentUser 스토어 + 생성 비번), `Common/Constants.cs`(하드코딩 비번 제거), `Properties/Settings.settings`+`Settings.Designer.cs`+`App.config`(`ServerCertPassword`), `Properties/app.manifest`(asInvoker), `AgentHub.csproj`+`packages.config`(Velopack 참조), `README.md`(배지·안내)
- 삭제: `install/Installer.iss`

## 4. 성공 기준 (검증)
1. `msbuild` Release 빌드 성공, `VelopackApp.Build().Run()`가 Main 최상단에 존재.
2. 로컬에서 `vpk pack`으로 `AgentHub-win-Setup.exe` 생성.
3. 설치본을 **비관리자 계정에서 설치·실행** → 트레이 상주, 대시보드 표시, 서버 활성(관리자 프롬프트 없음). 인증서 최초 동의창만.
4. 태그 `v0.0.1` 푸시 → Actions가 릴리스 자동 생성 + Setup.exe 자산 업로드.
5. 새 태그(`v0.0.2`) 릴리스 후, 구버전 앱 실행 시 자동으로 업데이트 감지·다운로드 → 트레이 알림 → 재시작 시 신버전 적용.
6. README 배지가 총 다운로드 수를 표시(공개 저장소).
7. 소스/저장소에 인증서 비밀번호 등 하드코딩 시크릿 없음.

## 5. 리스크 / 범위 밖
- **코드 서명 미적용** → SmartScreen "알 수 없는 게시자" 경고.
- **모바일 LAN 실접속**(방화벽 인바운드 + 모바일 기기의 인증서 신뢰/PWA 설치 조건) → 다음 단계.
- Velopack은 사용자별 설치 모델. 전사 배포(머신 단위 MSI)가 필요해지면 재검토.

## 6. 공개 전환 순서(런북)
1. 시크릿 분리(2.1) + 비관리자 전환(2.2) 구현·빌드 확인.
2. Velopack·CI·README(2.3–2.5) 구현.
3. (선택) `git` 히스토리 스쿼시로 옛 비밀번호 제거: 단일 커밋으로 재작성 후 force-push.
4. `gh repo edit YOOJUNGYU/agent-hub --visibility public --accept-visibility-change-consequences`.
5. 첫 태그 `v0.0.1` 푸시 → 릴리스 자동 생성 확인 → 배지·다운로드 확인.
