# Agent Hub

[![Downloads](https://img.shields.io/github/downloads/YOOJUNGYU/agent-hub/total)](https://github.com/YOOJUNGYU/agent-hub/releases)
[![Latest Release](https://img.shields.io/github/v/release/YOOJUNGYU/agent-hub)](https://github.com/YOOJUNGYU/agent-hub/releases/latest)

> 내 PC에서 동작하는 **AI 에이전트들의 작업 상황을 실시간으로 모니터링**하고,
> 같은 네트워크(LAN)에 연결된 **모바일 기기에서 브라우저로 확인**할 수 있게 해 주는
> Windows 트레이 애플리케이션입니다.

## 설치 (Install)

1. [최신 릴리스](https://github.com/YOOJUNGYU/agent-hub/releases/latest)에서 **`AgentHub-win-Setup.exe`**를 다운로드합니다.
2. 실행하면 `%LocalAppData%\AgentHub`에 설치되고 트레이에 상주합니다(**관리자 권한 불필요**).
3. 최초 실행 시 로컬 HTTPS용 자체서명 인증서 설치 동의창이 **한 번** 표시됩니다.
4. 이후 새 버전은 실행 중 자동으로 다운로드되어 **재시작 시 적용**됩니다. (트레이 우클릭 → "지금 업데이트 후 재시작"으로 즉시 적용도 가능)

> ⚠️ 코드 서명이 없어 다운로드·최초 실행 시 SmartScreen "알 수 없는 게시자" 경고가 보일 수 있습니다. **추가 정보 → 실행**으로 진행하세요.

C#(.NET Framework 4.8, WinForms)로 작성된 호스트 프로그램이 내장 웹서버([EmbedIO](https://github.com/unosquare/embedio))를
띄우고, HTML + **바닐라 JavaScript**로 만든 프런트엔드 화면을 제공합니다.
데스크톱에서는 WebView2로, 모바일에서는 같은 네트워크의 브라우저로 동일한 화면에 접속합니다.

---

## 목적 (Purpose)

- **AI 에이전트 모니터링** — 내 PC에 설치/실행 중인 AI 에이전트들의 작업 진행 상황, 로그, 상태를 한곳에서 수집·표시합니다.
- **웹 기반 FE 화면** — EmbedIO가 정적 HTML/CSS/JS와 REST API·WebSocket을 서빙하여 실시간 대시보드를 제공합니다.
- **모바일 모니터링** — 별도 앱 설치 없이, 같은 공유기(동일 네트워크망)에 연결된 스마트폰 브라우저로 PC의 에이전트 작업 현황을 확인합니다.

> 이 저장소는 이전에 작성했던 데스크톱 에이전트 프로젝트의 기본 골격(WinForms 호스트 + EmbedIO + WebView2 + WebSocket)을
> 그대로 복사하여 시작했으며, AI 에이전트 모니터링 목적에 맞게 재구성해 나갈 예정입니다.
> (기존 프로젝트에 남아 있던 특정 회사·제품 브랜딩과 네임스페이스는 모두 `AgentHub` 기준으로 정리되었습니다.)

---

## 아키텍처 (Architecture)

```
                            ┌─────────────────────────────────────────────┐
                            │            내 PC (Windows Host)              │
                            │                                             │
   AI Agents ──작업/로그──▶ │  AgentHub.exe (WinForms 트레이 앱)           │
                            │   ├─ EmbedIO 내장 웹서버 (HTTPS)             │
                            │   │    ├─ /api/       REST API (ApiController)│
                            │   │    ├─ /terminal   WebSocket (실시간 로그) │
                            │   │    └─ /printer    WebSocket               │
                            │   ├─ 정적 자산 서빙 (HTML / CSS / Vanilla JS) │
                            │   └─ WebView2 (데스크톱 내장 브라우저 화면)   │
                            └──────────────┬──────────────────────────────┘
                                           │ 동일 네트워크망 (LAN / Wi-Fi)
                        ┌──────────────────┴───────────────────┐
                        │                                       │
                 ┌──────────────┐                       ┌──────────────┐
                 │  데스크톱     │                       │   모바일      │
                 │  (WebView2)  │                       │  (브라우저)   │
                 └──────────────┘                       └──────────────┘
```

- **호스트**: `AgentHub.exe` — 트레이에 상주하며 서버 수명주기를 관리하는 WinForms 앱.
- **웹서버**: EmbedIO 기반 HTTPS 서버. 자체 서명 인증서를 런타임에 생성하여 사용합니다.
  - `DEBUG` 빌드는 `8000` 포트 고정, `RELEASE` 빌드는 `8000~9000` 범위에서 사용 가능한 포트를 자동 선택합니다.
- **프런트엔드**: `View/Htmls`의 HTML + CSS + 바닐라 JS. 빌드 도구(번들러) 없이 그대로 서빙합니다.
- **실시간 통신**: WebSocket 모듈(`/terminal`, `/printer`)로 로그/이벤트를 스트리밍합니다.

---

## 기술 스택 (Tech Stack)

| 구분 | 사용 기술 |
|------|-----------|
| 언어 / 런타임 | C# 8, .NET Framework 4.8 |
| UI 호스트 | Windows Forms, WebView2 |
| 내장 웹서버 | EmbedIO (WebApi, WebSockets, IP Banning, Session) |
| 프런트엔드 | HTML5, CSS3, Vanilla JavaScript |
| 직렬화 | Newtonsoft.Json |
| 로깅 | NLog, Serilog |
| 설치 배포 | Inno Setup (`install/Installer.iss`) |

---

## 프로젝트 구조 (Project Structure)

```
agent-hub/
├─ AgentHub.sln                     # 솔루션
├─ AgentHub/                        # 메인 애플리케이션 프로젝트 (namespace: AgentHub)
│  ├─ Program.cs                    # 진입점 (트레이 앱 시작, 중복 실행 방지)
│  ├─ App.config
│  ├─ Common/
│  │  ├─ Constants.cs               # 앱 상수 (이름, URI, 메시지, 인증서 설정)
│  │  ├─ Enums.cs / EnumDescriptions.cs
│  │  ├─ Helper/                    # 도메인 헬퍼
│  │  ├─ Models/                    # 데이터 모델
│  │  └─ Util/                      # 로깅·설정·Win32 등 유틸리티
│  ├─ Server/
│  │  ├─ EmbedIOServer.cs           # EmbedIO 서버 구성·시작, 자체 서명 인증서 발급
│  │  ├─ Controller/ApiController.cs# REST API (/api/)
│  │  └─ Socket/                    # WebSocket 모듈 (/terminal, /printer)
│  ├─ View/
│  │  ├─ Forms/                     # WinForms 창 (FormMain, FormPrint, FormToast)
│  │  ├─ bridges/                   # WebView2 ↔ C# 브릿지
│  │  ├─ Htmls/                     # 프런트엔드 (HTML / CSS / Vanilla JS / 폰트)
│  │  └─ Prints/                    # 인쇄용 템플릿
│  ├─ Properties/                   # AssemblyInfo, Settings, Resources
│  └─ Resources/                    # 아이콘·이미지
├─ EmbedIO/                         # 내장 웹서버 라이브러리(서드파티, 원본 유지)
├─ install/
│  └─ Installer.iss                 # Inno Setup 설치 스크립트 (빌드 산출물은 git 제외)
├─ .gitignore
└─ README.md
---

## 빌드 및 실행 (Build & Run)

### 요구 사항
- Windows 10/11
- Visual Studio 2019 이상 (.NET Framework 4.8 개발 도구 포함)
- [WebView2 런타임](https://developer.microsoft.com/microsoft-edge/webview2/)

### 빌드
```powershell
# NuGet 복원 후 빌드
msbuild AgentHub.sln /t:Restore
msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
```
Visual Studio에서는 `AgentHub.sln`을 열고 시작 프로젝트를 `AgentHub`로 설정한 뒤 F5로 실행합니다.

### 실행
- 빌드 산출물: `install/Debug/AgentHub.exe` (또는 `install/Release/`).
- 실행 시 트레이에 상주하며, 내장 웹서버가 자동으로 시작됩니다.
- 데스크톱 화면은 WebView2로 표시됩니다.

---

## 로드맵 / 참고 (Roadmap & Notes)

- [ ] **LAN·모바일 접속 활성화** — 현재 서버는 `https://127.0.0.1:{port}`(로컬호스트)로 바인딩되어 있어
      같은 네트워크의 모바일에서는 접속되지 않습니다. 모바일 모니터링을 위해서는
      바인딩 주소를 `0.0.0.0` 또는 PC의 LAN IP로 변경하고, 자체 서명 인증서의 SAN에
      PC IP를 추가하며, Windows 방화벽에서 해당 포트를 허용해야 합니다.
- [ ] AI 에이전트 상태/작업 수집 파이프라인 정의 및 API·WebSocket 스키마 설계.
- [ ] 모바일 화면 반응형 UI 정리.

> ⚠️ 인증서 비밀번호 등 민감 값이 소스에 포함되어 있던 부분은 향후 설정/시크릿으로 분리 예정입니다.
