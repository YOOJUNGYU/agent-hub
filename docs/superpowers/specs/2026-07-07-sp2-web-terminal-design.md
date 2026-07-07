# SP2 — 웹 터미널 (ConPTY로 로컬 셸/claude 제어)

- 작성일: 2026-07-07
- 상태: 설계 확정 대기 (사용자 리뷰 예정)
- 선행/후행: SP1(완료) → **SP2** → SP3(질문 알림/답변, SP2 터미널을 답변 substrate로 활용) → SP4 → SP5

## 1. 목적

호스팅되는 웹페이지(모바일 PWA)에서 **로컬 PC의 셸을 실제 터미널로 제어**하고, 그 안에서 `claude` CLI를 직접 실행할 수 있게 한다. ConPTY 기반이라 컬러/커서 등 TUI가 정상 렌더된다. 이는 SP3(모바일에서 Claude 질문에 답변)의 기반 substrate이기도 하다.

임의 명령 실행 = **RCE 표면**이므로 접근 제어가 설계의 핵심이다.

## 2. 확정된 결정 (브레인스토밍)

- **접근 제어**: 승인 기기(`Approved`) **AND** 호스트가 켠 마스터 토글(`TerminalEnabled`, 기본 OFF).
- **터미널 형태**: 범용 셸 1개(연결당 PTY 1개). 여러 탭/자동-claude 세션은 범위 밖.
- **셸/시작 폴더**: **PC(exe)에서 설정**하고, 모바일은 그 설정대로 실행된 셸에 접속만 한다.
- **터미널 화면 위치**: **모바일 PWA에만**. PC 콘솔은 토글/설정만 담당(PC엔 이미 실제 cmd가 있음).
- **PTY 백엔드**: **ConPTY (P/Invoke)** — 외부/네이티브 의존성 0, Win10 1809+/Win11 보장.
- **xterm.js**: dist를 로컬 벤더링(CDN 금지, CSP 안전). 외부 다운로드는 `curl --ssl-no-revoke`로 가능(이 환경은 schannel 폐기검사만 이슈).

## 3. 보안 모델 (핵심)

- **마스터 토글** `TerminalEnabled` (기본 **false**) — PC 콘솔에서만 변경.
- `/ws/term` 접속 게이트: `TerminalEnabled == true` **그리고** 토큰의 기기 상태 `== Approved`. 하나라도 불충족 → `{type:"denied", reason}` 전송 후 소켓 종료(PTY 미생성).
- 토글을 **OFF로 변경 시 활성 터미널 세션 전부 즉시 종료**(PTY kill + 소켓 close). 설정 변경 이벤트를 `TerminalModule`이 구독.
- 설정/토글 변경 REST는 **loopback 전용**(`IsLoopback()` 게이트) — 모바일이 토글을 못 켠다.
- 연결당 PTY 1개. 연결 종료 시 PTY 프로세스 kill + 읽기 스레드/핸들 정리.
- 안내 문구로 위험 명시(모바일 터미널 진입 시 경고 한 줄).
- (범위 밖) 명령 화이트리스트/샌드박싱은 하지 않는다 — 토글+승인 게이트로 관리.

## 4. 컴포넌트 설계

### 4.1 `ConPtySession` (신규, `AgentHub.Server.Terminal`)

Windows Pseudo Console(ConPTY)를 P/Invoke로 감싼다. 표준 MS ConPTY 샘플 패턴.

- 생성: 입력/출력 파이프 2쌍 생성 → `CreatePseudoConsole(size, inputRead, outputWrite, 0, out hPC)` → `STARTUPINFOEX`에 `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`(0x00020016) 설정 → `CreateProcess(shell, cwd, EXTENDED_STARTUPINFO_PRESENT)`.
- P/Invoke: `kernel32`의 `CreatePipe`, `CreatePseudoConsole`, `ResizePseudoConsole`, `ClosePseudoConsole`, `InitializeProcThreadAttributeList`, `UpdateProcThreadAttribute`, `DeleteProcThreadAttributeList`, `CreateProcess`, `CloseHandle`, `TerminateProcess`, `WaitForSingleObject`.
- API:
  - `ConPtySession(string shell, string cwd, int cols, int rows, Action<byte[],int> onOutput)` — spawn + 출력 읽기 스레드 시작(`outputRead`에서 blocking read → `onOutput`).
  - `void Write(byte[] data)` — `inputWrite`에 기록(PTY stdin).
  - `void Resize(int cols, int rows)` — `ResizePseudoConsole`.
  - `event Action Exited` — 자식 프로세스 종료 감지.
  - `void Dispose()` — 프로세스 kill, 핸들/파이프/attr list 정리, hPC close, 읽기 스레드 종료.
- 순수 Windows interop; EmbedIO/WinForms 비의존(로깅은 `LogService` 허용).

### 4.2 `TerminalModule : WebSocketModule` (신규, `AgentHub.Server.Socket`, route `/ws/term`)

- `OnClientConnectedAsync`: 토큰 파싱(?token=) → 게이트 확인.
  - 불충족 → `{type:"denied", reason:"disabled"|"unauthorized"}` 전송 후 close.
  - 충족 → `AppSettings`의 `TerminalShell`/`TerminalWorkingDir`로 `ConPtySession` 생성(초기 cols/rows는 클라 첫 resize 전 기본 80x24). 세션을 contextId에 매핑. `{type:"ready"}` 전송. PTY `onOutput` → 해당 소켓에 **binary** 프레임 전송. `Exited` → `{type:"exit"}` 전송 후 close.
- `OnMessageReceivedAsync`: **text** 프레임 = JSON.
  - `{t:"i", d:"..."}` → `session.Write(UTF8(d))` (키 입력).
  - `{t:"r", cols, rows}` → `session.Resize(cols,rows)`.
- `OnClientDisconnectedAsync`: 세션 Dispose + 매핑 제거.
- `DisableAll()` — 토글 OFF 시 호출: 모든 세션 Dispose + 소켓 close.
- 게이트 재확인: 출력 전송 시에도 승인 취소되면 중단(연결 종료).

### 4.3 설정 (`Properties.Settings` + `AppSettingsProvider`)

- `TerminalEnabled` (bool, 기본 false)
- `TerminalShell` (string, 기본 `"cmd.exe"`)
- `TerminalWorkingDir` (string, 기본 `""` → 빈 값이면 런타임에 `%USERPROFILE%`로 해석)
- 변경 시 `TerminalModule`에 통지(토글 OFF면 `DisableAll()`).

### 4.4 REST (`ApiController`, loopback 전용)

- `GET /api/terminal/config` (loopback) → `{ enabled, shell, workingDir }`
- `POST /api/terminal/config` (loopback) → 저장 + 토글 변경 반영. 바디 `{ enabled, shell, workingDir }`.
- `GET /api/terminal/status` (공개, 비민감) → `{ enabled }` — 모바일이 터미널 버튼 노출 여부 판단용.

### 4.5 프론트엔드 (모바일 PWA, `View/Htmls`)

- **xterm.js 벤더링**: `js/xterm.js`, `css/xterm.css`, `js/addon-fit.js`(로컬 파일). `index.html`에서 로컬 참조.
- 모니터 화면에 **"터미널" 버튼** — `GET /api/terminal/status`가 `enabled:true`일 때만 노출.
- **터미널 화면**(`#terminal` screen): xterm 인스턴스 + FitAddon. `/ws/term?token=` 접속.
  - WS `ready` → xterm 표시. 서버 binary → `term.write(uint8)`. `term.onData` → `{t:"i",d}` 전송. resize(FitAddon) → `{t:"r",cols,rows}` 전송.
  - `denied` → 안내 후 목록 복귀. `exit` → "세션 종료" 표시 + 목록 복귀.
  - 뒤로가기: SP1과 동일한 History API 패턴(pushState/popstate)으로 목록 복귀, WS close.
  - 진입 시 위험 안내 한 줄(“이 터미널은 PC에서 명령을 실행합니다”).
- **알려진 한계(모바일 키보드)**: 모바일 브라우저의 가상 키보드는 xterm.js와 궁합이 브라우저별로 편차가 크다(키보드가 안 뜨거나 입력이 어색할 수 있음). 대응으로 화면에 **숨은 `<textarea>` 또는 "키보드 열기" 버튼**을 두어 포커스를 유도하고, 입력을 xterm으로 전달한다. 완전한 매끄러움은 보장하지 않으며(브라우저 의존), SP3의 "질문 답변"은 이 원터미널 입력 대신 **간단한 답변 입력 필드**를 별도 제공하는 방향으로 보완할 수 있다.
- i18n(ko/en) 키 추가. `sw.js` 캐시 버전 상향 + xterm 자산 캐시 목록 추가.

### 4.6 PC 콘솔 (host)

- 설정 탭에 **"웹 터미널 허용" 토글** + **기본 셸**(cmd.exe/PowerShell 선택 또는 입력) + **시작 폴더** 입력.
- `GET/POST /api/terminal/config`(loopback) 사용. 저장 시 즉시 반영(토글 OFF면 활성 세션 종료).
- 안내: 토글 옆에 “승인된 기기가 이 PC에서 명령을 실행할 수 있게 됩니다” 경고.

### 4.7 서버 배선 (`EmbedIOServer`)

- `.WithModule(new TerminalModule("/ws/term"))` 등록.
- 서버 stop/restart 시 활성 터미널 세션 정리.

## 5. 데이터 흐름

```
[모바일] 터미널 버튼(enabled일 때) → /ws/term?token= 접속
  게이트(Enabled && Approved) 통과 → ConPtySession spawn(설정 셸/폴더)
  PTY stdout(ANSI bytes) → WS binary → xterm.write
  xterm.onData(키) → WS text {t:"i",d} → PTY stdin
  FitAddon resize → WS text {t:"r",cols,rows} → ResizePseudoConsole
[PC] 설정 탭 토글 OFF → POST /api/terminal/config → TerminalModule.DisableAll() → 전 세션 종료
```

## 6. 에러 처리

- ConPTY 생성 실패(구버전 Windows 등): `denied`/에러 메시지, `LogService.Error`.
- PTY 프로세스 조기 종료: `Exited` → 소켓에 `exit` 통지 후 close.
- 소켓 예외: per-socket try/catch, 세션 Dispose.
- 잘못된 JSON 입력: 무시.
- 모든 예외 `LogService.Instance.Error` 경유.

## 7. 테스트 / 검증

- **ConPtySession 단위(수동/통합)**: 헤드리스로 `cmd.exe /c echo hello` 류를 spawn해 출력 왕복 확인(가능하면 자동, 어려우면 수동 E2E).
- **게이트 로직 단위**: Enabled/Approved 조합에 따른 허용/거부 판정(순수 함수로 분리해 테스트).
- **빌드 게이트**: msbuild(=VS2019 MSBuild.exe, PowerShell로 실행) 0 errors.
- **E2E(사용자 수동)**: 토글 ON → 모바일에서 터미널 진입 → `claude` 실행/키입력/리사이즈/뒤로가기/토글 OFF 시 종료 확인.

## 8. 범위 밖 (다른 SP / 후속)

- 질문 감지·PWA 푸시·모바일 답변 UX → SP3 (SP2 터미널을 답변 입력 경로로 활용).
- 여러 탭/세션, 자동-claude 세션, 명령 화이트리스트/감사 로그 → 후속 과제.
- PC 콘솔 내 터미널 뷰 → 후속(현재는 설정/토글만).

## 9. 변경/신규 파일 (요약)

- 신규: `AgentHub/Server/Terminal/ConPtySession.cs`, `AgentHub/Server/Terminal/ConPtyInterop.cs`(P/Invoke 분리 가능), `AgentHub/Server/Socket/TerminalModule.cs`, `AgentHub/View/Htmls/js/xterm.js`·`js/addon-fit.js`·`css/xterm.css`(벤더), `AgentHub/View/Htmls/js/term.js`(터미널 화면 로직).
- 수정: `AgentHub/Server/EmbedIOServer.cs`(모듈 등록/정리), `AgentHub/Server/Controller/ApiController.cs`(터미널 config/status 엔드포인트), `AgentHub/Properties/Settings.settings`·`Settings.Designer.cs`(+`AppSettingsProvider`), `AgentHub/View/Htmls/index.html`·`js/app.js`·`js/i18n.js`·`css/app.css`·`sw.js`, PC 콘솔 `host.html`·`host.js`(설정 탭 터미널 항목).
- 서드파티 `EmbedIO/`는 수정하지 않는다.
