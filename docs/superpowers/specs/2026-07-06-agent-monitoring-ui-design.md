# Agent Hub — 모니터링 UI 개편 설계

- 작성일: 2026-07-06
- 상태: 설계 확정(인라인 실행)

## 설계 개정 (2026-07-06 추가 요구 — 두 화면 분리 + WebSocket)

사용자 추가 설명으로 **두 화면의 목적이 다름**이 명확해졌고, 실시간 모니터링은 **WebSocket** 기반으로 한다.

- **exe WebView2 = 호스트 콘솔 (`/host`)**: 이 PC에서 Agent Hub가 정상 동작 중인지, **어떤 모바일 기기가 접속했는지**(IP/UA/접속시각), 서버 상태·접속 URL(클릭)·로그·포트 설정. 운영자(PC 사용자)용.
- **호스팅 SPA = 모바일 모니터 (`/`)**: 같은 망의 모바일에서 내 PC의 Claude 에이전트 진행상황을 실시간 확인. 조회 전용, 반응형, PWA 설치.
- **전송**: 실시간 진행상황은 API 폴링이 아닌 **WebSocket**. 서버가 HTTPS이므로 `wss://` 로 업그레이드(동일 포트).

### 소켓 레이어 구조 (Server/Socket, Server/Agents)
- `MonitorClientRegistry`(정적 싱글턴, thread-safe): 접속한 모바일 모니터 클라이언트 추적 `{ Id, Ip, UserAgent, ConnectedAt }`. 변경 시 `Changed` 이벤트.
- `AgentMonitorModule : WebSocketModule` (route `/ws/agents`): 모바일 클라이언트 대상. OnConnect→레지스트리 등록 + 초기 스냅샷 전송, OnDisconnect→해제. `BroadcastAgents(json)`.
- `HostMonitorModule : WebSocketModule` (route `/ws/host`): 호스트 콘솔 대상. OnConnect→현재 클라이언트 목록+서버상태 전송. 레지스트리 `Changed` 구독→클라이언트 목록 broadcast.
- `AgentMonitorService`(Server/Agents): 데이터 seam. 타이머로 **mock** 에이전트 상태를 생성/변화시켜 `AgentMonitorModule`로 push. 실제 연동 시 이 서비스만 교체.

### 화면별 책임 재배치 (원 요구사항 매핑)
- 서버 상태 + 접속 URL 링크(원 요구 4): **호스트 콘솔 헤더**(PC에서 URL을 보고 모바일로 공유·접속). 데스크톱 링크 클릭→시스템 브라우저.
- 포트 설정(원 요구 3), 로그 뷰: **호스트 콘솔**.
- Claude 에이전트 모니터(원 요구 5) + 반응형(원 요구 6) + PWA: **모바일 SPA(`/`)**.
- 트레이 완전종료(원 요구 1), 아이콘(원 요구 2): FormMain/리소스(공통).

> 아래 3~5절의 단일 SPA/`/api/agents` 폴링 서술은 본 개정으로 대체된다: 화면은 `/`(모바일)와 `/host`(콘솔) 2개, 에이전트/클라이언트 실시간 데이터는 WebSocket. `/api`는 서버 상태·설정·초기 스냅샷 용도로만 유지.

---

## (원안) — 참고용, 위 개정이 우선

- 작성일: 2026-07-06
- 상태: 설계 확정 대기(사용자 검토 중)

## 1. 목표

기존 건강검진/프린트용 데스크톱 에이전트 UI를, **로컬 PC에서 동작하는 Claude AI 에이전트들을 모니터링**하는 화면으로 개편한다. 이번 이터레이션 범위:

1. 트레이 아이콘 우클릭 **완전 종료** 기능
2. 프로젝트에 맞는 **신규 아이콘**(허브/노드 연결 모티브)
3. 설정창의 **포트 변경** 기능
4. 메인 화면 상단 **EmbedIO 서버 활성 상태 + 접속 URL 링크**
5. **검진/프린트 화면 전면 제거** 후 에이전트 모니터링 대시보드 신규 작성
6. **반응형** (데스크톱/모바일 폭 대응)

### 범위 결정 (브레인스토밍 확정)
- 모니터링 데이터: **mock 데이터 먼저** — 대시보드 UI + API 계약(스키마)만 구현, 실제 데이터 연동은 다음 단계.
- 모바일 접속: **반응형 UI + PWA 설치 지원**까지. 서버 바인딩은 localhost 유지(실제 LAN 노출/방화벽은 다음 단계)이되, **HTTPS 자체서명 인증서 발급·신뢰 등록은 반드시 유지**한다 — PWA 설치는 보안 컨텍스트(HTTPS)를 요구하기 때문. PWA 매니페스트+서비스워커를 추가해 **설치 가능**하게 만든다.
- 프레임워크: **바닐라 JS(빌드 단계 없음)**. EmbedIO가 손으로 작성한 정적 파일을 그대로 서빙. 이 범위엔 프레임워크 불필요(참고: `ysr-server`의 `ysr.server.page`는 Vue+빌드지만, 여기선 mock 대시보드 규모라 바닐라가 더 단순).
- 셸 구조: **단일 반응형 SPA를 EmbedIO가 서빙**, 데스크톱 WebView2와 (추후)모바일이 같은 URL 로드.
- 로그 탭: **유지**.
- 아이콘: **허브(중앙 노드 + 연결 노드) 모티브**.

### 참고 프로젝트 (`C:\GIT\PRIVATE\MyWork\ysr-server`)
동일 계보의 기존 로컬 서버. 아래 패턴을 참고/재사용:
- `Ysr.Server.EmbedIO/Server.cs`: 인증서를 `LocalMachine\Root`에 등록 + `netsh http add sslcert`로 SSL 바인딩 + `WithAutoRegisterCertificate()`; `ActionModule("/")`로 정적 파일 해석 + MIME + `index.html` 폴백; `WebServer` 인스턴스 보관 + `StartServer/StopServer`.
- `AppSettingsProvider.cs`/`Constants.cs`: agent-hub와 동일한 설정 저장 패턴.

## 2. 아키텍처 개요

### 현재
- `FormMain`이 WebView2 3개(`webViewLeft`=사이드메뉴, `webViewServer`=로그, `webViewCenter`=설정)로 **로컬 파일(file://)** 로드.
- `SideMenuBridge`(WebView2 host object)로 메뉴 전환.
- EmbedIO 서버는 `/api`(프린터/프린트), `/terminal`, `/printer`(WS)만 제공 — **정적 HTML 미서빙** → 모바일 접속 시 표시할 화면 없음.
- 포트: `EmbedIOServer`에서 DEBUG=8000 고정, RELEASE=8000~9000 자동. 설정 아님.
- 닫기(X): `OnClosing`에서 취소 후 창 숨김. 트레이 컨텍스트 메뉴는 생성되지만 **항목 없음**.

### 변경 후
```
FormMain (WinForms 커스텀 크롬: 타이틀바/최소·최대·닫기/드래그/리사이즈/트레이)
  └─ WebView2 1개  ──loads──▶  https://127.0.0.1:{port}/   (EmbedIO 정적 서빙)
                                   └─ index.html (반응형 SPA)
                                        ├─ 공통 상단 헤더: 서버 상태 배지 + 접속 URL 링크
                                        ├─ 탭: 대시보드 / 로그 / 설정 (클라이언트 라우팅)
EmbedIO WebServer
  ├─ StaticFilesModule  → View/Htmls (SPA + css/js)
  ├─ /api/server/status → { active, url, host, port }
  ├─ /api/agents        → mock 에이전트 목록 (모니터링 데이터 계약)
  ├─ /api/settings      → GET 현재 설정 / POST 저장(포트 등) → 서버 재시작
  └─ /terminal (WS)     → 기존 유지
```

`SideMenuBridge` 및 다중 WebView 전환 로직 제거. 설정 저장은 host object 브리지 대신 **HTTP API**로 통일(추후 모바일에서도 동작).

## 3. 컴포넌트 상세

### 3.1 트레이 완전 종료 (`FormMain`)
- `InitTrayMenu()`에서 `ContextMenu`에 항목 추가: **열기**(`SetShowWindow(true)`), **완전 종료**.
- 완전 종료 흐름: `_isExiting = true` 설정 → `_notify.Visible = false; _notify.Dispose()` → `Application.Exit()`.
- `OnClosing`: `if (!_isExiting) { e.Cancel = true; SetShowWindow(false); }` — 완전 종료 시에는 취소하지 않고 실제 종료.
- 종료 시 EmbedIO 서버도 정상 정지(아래 3.3의 stop 사용).

### 3.2 서버 상태 헤더 + 접속 URL (SPA 헤더 + API)
- `GET /api/server/status` → `{ "active": true, "host": "127.0.0.1", "port": 8000, "url": "https://127.0.0.1:8000" }`.
- SPA 헤더가 주기적으로(예: 5초) 상태 조회 → 배지(🟢 활성 / 🔴 중지) + 활성 시 URL을 `<a target="_blank">`로 표시.
- 데스크톱 WebView2에서 URL 클릭 → `CoreWebView2.NewWindowRequested` 가로채 `Process.Start(url)`로 시스템 브라우저 오픈.

### 3.3 포트 설정 + 서버 재시작
- `Settings`에 `ServerPort`(Int32, User scope, 기본 `8080`) 추가(`Settings.settings` + Designer + app.config 기본).
- `EmbedIOServer` 리팩터:
  - 시작 포트 결정: `ServerPort` 값 사용(0 또는 사용중이면 8000~9000 자동 폴백).
  - `WebServer` 인스턴스와 `CancellationTokenSource`를 정적 필드로 보관.
  - `StartServer()` / `StopServer()` / `RestartServer()` 제공. `RunAsync(cts.Token)`.
- `GET /api/settings` → `{ "port": 8080 }`. `POST /api/settings` `{ "port": 9000 }` → 유효성 검사(1024–65535) → 저장 → `RestartServer()` → 새 `{ url }` 반환.
- 설정 페이지: 포트 입력 + 저장. 저장 성공 시 "새 주소로 재접속" 안내(재시작으로 포트가 바뀌면 현재 URL도 변경되므로).

### 3.4 대시보드 (mock)
- `GET /api/agents` → mock 배열:
  ```json
  [ { "id":"agent-1", "name":"Claude Code", "status":"working|idle|error",
      "currentTask":"...", "progress":72, "updatedAt":"2026-07-06T12:00:00Z" } ]
  ```
- 상단 요약: 전체/작업중/오류 카운트. 하단: 에이전트 카드 그리드. 상태별 색상 배지.
- 실제 연동은 이 엔드포인트 구현만 교체하면 되도록 프런트는 계약에만 의존.

### 3.5 로그 탭 (유지)
- 기존 EmbedIO 요청 로그를 SPA '로그' 탭에 표시. 데스크톱은 기존 `webView.CoreWebView2.ExecuteScriptAsync("addLog(...)")` push 방식 유지(단일 WebView 대상).
- `FormMain`의 `ILogger` 구현/`ApiLogger`/필터 로직 유지, 대상 WebView만 단일 인스턴스로 변경.

### 3.6 신규 아이콘
- GDI+(System.Drawing)로 프로그램 생성(외부 의존성 없음). 허브 모티브: 라운드 사각 그라데이션 배경 + 중앙 노드 + 3~4개 연결 노드/선.
- 16/32/48/256px 멀티사이즈 `.ico` 생성 → `Resources/trayicon_32x32.ico` 및 `main_icon.png` 교체. `Properties/Resources` 참조 유지.

### 3.7 정적 서빙 & 인증서 (HTTPS 유지)
- EmbedIO로 `View/Htmls`를 정적 서빙. `FileModule`/`WithStaticFolder` 사용, 또는 `ysr-server`의 `ActionModule("/")` 방식(파일 해석 + MIME 매핑 + `index.html` 폴백) 참고. PWA 자산(`manifest.webmanifest`, `sw.js`, 아이콘)도 함께 서빙.
- **HTTPS/인증서 유지**: 기존 `EmbedIOServer.GetSelfSignedCertificate()`의 자체서명 인증서 발급 + `LocalMachine\Root` 등록을 그대로 유지(제거/HTTP 다운그레이드 금지). PWA 설치가 HTTPS를 요구하므로 필수.
- WebView2 `CoreWebView2.ServerCertificateErrorDetected`에서 자체서명 localhost 인증서 허용(신뢰 스토어 등록되어 있어도 안전장치).
- 시작 순서: 서버 먼저 시작 → WebView2를 `https://127.0.0.1:{port}/`로 네비게이트.

### 3.8 PWA 설치 지원
- `manifest.webmanifest`: `name`/`short_name`(Agent Hub), `start_url` `/`, `display: standalone`, `theme_color`/`background_color`(다크 테마), 아이콘(192/512 PNG — 3.6 아이콘에서 함께 생성).
- `sw.js`: 최소 서비스워커(install/activate + 정적 자산 캐시 폴백). `index.html`에서 `navigator.serviceWorker.register('/sw.js')` 등록.
- `index.html` `<head>`에 manifest 링크 + `theme-color` + apple-touch-icon. 설치 가능 조건(HTTPS + manifest + SW) 충족.
- 데스크톱 WebView2에서는 설치 UI가 없어도 정상 동작. 실제 모바일 설치는 LAN 노출(다음 단계) 후 가능하지만, 자산·조건은 이번에 모두 갖춘다.

## 4. 제거 목록 (검진/프린트)
- 파일: `View/Forms/FormPrint.*`, `View/Prints/**`, `Common/Helper/CheckupHelper.cs`, `Common/Models/Checkup/**`, `Common/Models/Patient.cs`, `Common/Models/Printer.cs`, `View/Htmls/side_menu.html`, `View/bridges/SideMenuBridge.cs`.
- 코드: `ApiController`의 `/printer`·`/print` 라우트, `EtcUtil.GetPrinters`(프린터 전용 시), `Constants.Uris.Htmls.Prints`(+ SideMenu/ServerLog/ServerSetting URI는 단일 SPA로 대체), `WebSocketPrinterModule`(프린터 전용 → 제거) 및 `EmbedIOServer`의 해당 등록.
- csproj의 위 항목 `<Compile>/<Content>/<EmbeddedResource>` 엔트리.
- 판단 애매한 항목(예: `EtcUtil`의 다른 유틸과 섞인 코드)은 삭제 전 확인하고, 실제로 참조가 사라진 것만 제거(원칙 3: Surgical).

## 5. 반응형 방침
- CSS 변수 기반 다크 테마 유지. 레이아웃은 flex/grid.
- 카드 그리드: `repeat(auto-fill, minmax(280px, 1fr))`. 모바일 폭(≤600px)에서 1열, 헤더/탭 스택.
- 터치 타깃 최소 44px, `viewport` 메타 유지.

## 6. 성공 기준 (검증)
1. `msbuild AgentHub.sln`가 에러 0으로 빌드 → `install/Debug/AgentHub.exe` 생성.
2. 실행 시 트레이 상주, WebView2에 대시보드가 뜨고 상단에 🟢 상태 + 클릭 가능한 URL 표시.
3. 트레이 우클릭 → **완전 종료**로 프로세스 완전 종료(재실행 시 중복 실행 경고 없음).
4. 설정에서 포트 변경·저장 → 서버가 새 포트로 재시작되고 헤더 URL이 갱신됨.
5. 브라우저에서 헤더 URL 접속 시 동일 대시보드 표시(정적 서빙 확인).
6. 창/브라우저 폭을 모바일 크기로 줄여도 레이아웃이 1열로 정상 표시.
7. 검진/프린트 관련 파일·라우트·메뉴가 코드베이스에 남아있지 않음(grep 확인).
8. HTTPS로 서빙되고 자체서명 인증서가 신뢰 스토어에 등록됨(기존 유지). `manifest.webmanifest`+`sw.js`가 서빙되고 브라우저 개발자도구 기준 PWA 설치 조건(installable)을 만족.

## 7. 다음 단계(범위 밖, 기록용)
- 실제 에이전트 데이터 소스 연동(`/api/agents` 구현 교체).
- LAN 바인딩(0.0.0.0) + 인증서 SAN에 PC IP + 방화벽 → 실제 모바일 접속.
- 인증서 비밀번호 등 시크릿 분리.
