# 모바일 세션 상세 — 답변 UX 개선 + 세션연결(재실행) 설계

- 날짜: 2026-07-22
- 상태: 설계 승인됨 → writing-plans로 이관 예정
- 대상: 모바일 세션 상세(detail) 화면의 답변 흐름 4가지 개선
- 관련 메모리: `console-input-injection`, `terminal-features-removed`, `mobile-sessions-broadcast-force-switch`, `codex-engine-parity`, `keep-usage-guide-in-sync`
- 관련 스펙: `2026-07-16-mobile-direct-console-input-injection-design.md`, `2026-07-13-remote-ask-open-window-design.md`

## 1. 배경 / 문제

모바일 세션 상세 하단에는 자유 텍스트를 원본 세션 콘솔에 직접 주입(`ConsoleInputInjector`)하는 입력 바가 있다. 실제 사용에서 다음 문제가 확인됐다.

1. **내 답변 식별성 부족** — 활동 피드에서 내가 보낸 답변(`user_prompt`)이 좌측 보더 색만 달라 한눈에 구분되지 않는다.
2. **한 줄 입력의 한계** — `<input type=text>`라 여러 줄 답변을 작성/확인하기 어렵다.
3. **전달 불확실 + 중복 전송** — 답변이 길거나 줄바꿈이 있으면 바로 전달되지 않는 경우가 있었고, 반복 전송하니 같은 내용이 한꺼번에 여러 번 전달됐다. (원인: 주입은 콘솔 입력 버퍼에 쓰는 것이라 claude가 프롬프트 대기 상태가 아니면 버퍼에 쌓였다가 나중에 한꺼번에 제출된다. 그동안 사용자는 실패로 보고 재전송해 중복이 누적됨.)
4. **직접입력 불가 세션의 막다른 길** — ConPTY(Windows Terminal 등)나 프로세스 종료(PID 미보고) 세션은 주입이 안 되는데, 현재는 안내 문구만 뜨고 모바일에서 이어갈 방법이 없다.

과거 “터미널 열기/이어받기”는 서버가 원본을 kill하고 `claude --resume`으로 새 PTY를 띄워 **웹 터미널로 라이브 attach**하는 경로였고, VPN 외부접속 대비 **보안상 전면 제거**(커밋 5d6ff10)되어 현재 서버에 resume 실행 경로가 없다.

## 2. 목표 / 비목표

### 목표
- 내가 보낸 답변 카드를 시각적으로 뚜렷이 구분한다.
- 입력 영역을 여러 줄(최대 6줄, 초과 시 스크롤) textarea로 바꾸고 Enter=전송 / Shift+Enter=줄바꿈 규칙을 둔다.
- 전송이 확인될 때만 입력창을 다시 활성화하고, 전송 중에는 입력창·버튼을 비활성화 + 로딩 표시해 **중복 전송을 원천 차단**한다.
- 직접입력이 안 되는 claude 세션에서는 입력 컨트롤을 숨기고 **‘세션연결’** 버튼을 노출한다. 탭하면 서버가 PC에서 해당 세션을 `claude --resume`으로 **고전 콘솔에 자동 재실행**해 주입 가능한 상태로 만들고, UI는 자동으로 일반 입력창으로 복귀해 **다른 세션과 똑같이 답변**할 수 있게 한다.

### 비목표 (YAGNI / 명시적 제외)
- 웹 터미널/라이브 attach 부활 (제거된 보안 결정 유지 — **라이브 연결 없음**).
- 임의 셸 실행. ‘세션연결’은 **해당 세션의 resume 실행만** 수행한다.
- Codex 세션연결. Codex는 콘솔 없는 Electron 앱이라 resume해도 주입 불가 → 기존 **안내 문구만** 유지.
- 외부망(LTE 등)에서의 재실행/주입 (기존과 동일하게 LAN 승인기기 한정).
- 주입이 claude에 “수용됐는지”까지의 확인. 확인 신호는 `injectResult.ok`(=`WriteConsoleInput` 성공)로 한정한다.

## 3. 변경 1 — 내 답변 카드 시각 구분 (클라이언트)

- 대상 이벤트: 활동 피드의 `user_prompt`(🧑). 이것이 “내가 얘기한 답변”이다.
- 디자인: 채팅 말풍선 느낌으로 **오른쪽 정렬 + accent(파랑 계열) 배경 카드**. 다크/라이트 테마 모두 대응. assistant 메시지·도구 이벤트는 현행 유지.
- 구현: `css/app.css`에 `.ev-user_prompt`(및 다크/라이트 오버라이드) 스타일 확장 + 필요 시 `evHtml`에 정렬용 클래스만 부여. 기존 이벤트 DOM 구조는 유지(수술적 변경, 원칙 3).

## 4. 변경 2 — 입력 영역 textarea (클라이언트)

- `index.html`의 `#injectInput`을 `<input type=text>` → `<textarea>`로 교체(클래스·id·placeholder 유지).
- 동작:
  - 초기 1줄 높이, 내용에 따라 auto-grow, **최대 6줄**까지 커지고 그 이상은 세로 스크롤(`overflow-y:auto`).
  - **Enter=전송, Shift+Enter=줄바꿈.** (기존 keydown 핸들러가 Enter→전송이므로 shiftKey 예외 추가)
  - font-size 16px 유지(iOS 포커스 확대 방지). 라인하이트 기준 6줄 max-height를 CSS로 지정.
- auto-grow: 입력 이벤트에서 `height=auto` 후 `scrollHeight`로 재설정하되 6줄 max에서 clamp.

## 5. 변경 3 — 전송 상태머신 + 로딩 (클라이언트)

- 상태: `idle → sending → idle`. 모듈 변수 `injectSending`(bool)로 관리.
- **전송 시작(`sendInject`)**:
  - `injectSending`이면 즉시 return(중복 차단). textarea 값이 비었으면 무시.
  - `injectSending=true`, textarea·전송 버튼 `disabled`, 전송 버튼을 **스피너로 교체**(또는 바에 로딩 표시). 안전망 타이머(약 12초) 시작.
- **회신(`handleInjectResult`)**:
  - `ok`: textarea 비우고 힌트 제거, `idle` 복귀(재활성화). 안전망 타이머 해제.
  - `noconsole`/`nopid`: 변경 4의 **‘세션연결’ 모드로 전환**(입력 숨김·버튼 노출) + 사유 힌트.
  - `failed`: 힌트 표시 후 `idle` 복귀(재시도 가능).
  - `engine`: 안전망(입력 바는 애초 codex에서 숨김).
- **안전망 타임아웃**: 회신이 오지 않으면 `idle` 복귀 + `inject.hintFailed` 힌트(무한 비활성 방지).
- 중복 원인 해소: 전송~회신 사이 추가 전송을 막아 콘솔 버퍼 중복 누적을 방지.

## 6. 변경 4 — 세션연결(재실행) (클라이언트 + 서버)

### 6.1 판별 (둘 다)
- **사전(스냅샷)**: `SessionSummary`에 `bool Injectable` 추가. 규칙: `Engine=="claude" && SessionPidRegistry.TryGet(id, pid)` 이면 true. (Codex/PID 없음 → false)
  - 상세 진입/스냅샷 갱신 시 `injectable==false && engine=="claude"`면 입력 컨트롤을 숨기고 **‘세션연결’ 버튼**을 노출.
  - 참고: 사전 판별은 “PID 존재” 수준이며 ConPTY(살아있지만 주입 불가)는 못 거른다. 그 경우는 아래 실패 전환이 담당.
- **실패 전환**: 전송이 `noconsole`/`nopid`로 실패하면 그 세션을 ‘세션연결’ 모드로 전환.

### 6.2 UI 상태(세 갈래) — claude 세션 상세 하단
1. **일반 입력**: `injectable==true`. textarea + 전송(변경 2·3·5).
2. **세션연결**: `injectable==false`(또는 실패 전환). 입력 컨트롤 숨김 + **‘세션연결’ 버튼** + 사유 힌트.
3. **연결 중**: ‘세션연결’ 탭 후. 버튼 자리에 “연결 중…” 로딩. 스냅샷에서 `injectable==true`가 되면 자동으로 (1)로 복귀.
- Codex: 위와 무관하게 기존 안내 문구만(세션연결 버튼 없음).

### 6.3 동작 흐름
1. 사용자가 ‘세션연결’ 탭 → (확인 대화 후) `send({ type:'reopen', sessionId })`.
2. 서버 `AgentMonitorModule`의 `reopen` 케이스: 승인기기 가드(기존) → `EngineOf==claude` 확인 → `AgentMonitorService.CwdOf(id)`로 작업 디렉터리 확보 → `SessionReopener.Reopen(sessionId, cwd)`.
3. `SessionReopener`: **conhost 강제**로 `claude --resume <sessionId>`를 새 콘솔 창으로 실행(주입 가능한 고전 콘솔 보장). 실행 결과를 `reopenResult`로 회신(`ok`/`reason`).
4. 재개된 세션이 훅을 통해 **같은 sessionId로 PID를 재보고**(`SessionPidRegistry.Record`) → 다음 `sessions` 스냅샷에서 `injectable=true`.
5. 클라이언트: `injectable=true` 감지 → UI가 자동으로 일반 입력창으로 복귀. 이후 다른 세션과 동일하게 답변.

### 6.4 서버 구성
- `AgentHub/Common/Models/SessionSummary.cs`: `public bool Injectable { get; set; }` 추가. 요약 빌더에서 위 규칙으로 채움.
- `AgentHub/Server/Socket/AgentMonitorModule.cs`: `WatchMessage`에 필드는 이미 `SessionId` 존재 → `reopen` 케이스 추가 + `reopenResult` 회신.
- `AgentHub/Server/Terminal/SessionReopener.cs`(신규, 네임스페이스 `AgentHub.Server.Terminal`): claude 전용. `Reopen(string sessionId, string cwd)` — conhost 강제 실행. 예외는 삼켜 `reason` 반환 + `LogService.Error`.
  - 실행 커맨드(구현 확정 대상): `conhost.exe <셸> ... claude --resume <id>` 형태로 **고전 콘솔 호스트를 강제**. cwd 지정. `UseShellExecute`로 새 창 표시.
- 승인기기 가드는 `OnMessageReceivedAsync` 상단 기존 로직 재사용.

### 6.5 보안 범위
- 임의 셸/명령 실행 아님 — **입력은 sessionId 하나**, 서버가 고정 커맨드(`claude --resume <검증된 sessionId>`)만 구성. sessionId는 알려진 세션 목록에 존재하는지 검증.
- LAN 승인기기 한정(기존 가드). 라이브 attach/스트리밍 없음(제거된 웹터미널과 무관).

## 7. i18n / 워딩

| 키 | ko | en |
|---|---|---|
| `session.connect` | 세션연결 | Connect session |
| `session.connecting` | 연결 중… | Connecting… |
| `session.connectDesc` | 이 세션은 직접 입력이 안 됩니다. ‘세션연결’을 누르면 PC에서 세션을 다시 열어 답변할 수 있어요. | This session can't take direct input. Tap "Connect session" to reopen it on the PC and answer here. |
| `session.reopenFailed` | 세션 재실행에 실패했습니다. PC에서 직접 열어 주세요. | Couldn't reopen the session. Please open it on the PC. |

- 기존 `inject.hintNoConsole`/`hintNoPid` 문구는 세션연결 안내와 맞게 소폭 조정(‘세션연결’ 버튼을 가리키도록).
- 기존 제거된 `term.*` 키는 되살리지 않는다(신규 `session.*` 키 사용).

## 8. 검증 계획 (goal-driven)

1. **내 답변 구분**: 답변 전송 후 피드에서 내 답변이 오른쪽 accent 카드로 표시. 다크/라이트 모두 확인.
2. **textarea**: 6줄까지 커지고 이후 스크롤. Enter 전송, Shift+Enter 줄바꿈, 여러 줄 답변 전송 성공.
3. **중복 방지**: 전송 중 입력·버튼 비활성 + 로딩. 연타/중복 Enter 무시. `ok` 회신 시에만 재활성·비움. 회신 지연 시 안전망으로 복구.
4. **세션연결(핵심)**: PID 없는(종료된) claude 세션 상세 진입 → ‘세션연결’ 노출 → 탭 → 서버가 conhost로 `claude --resume` 실행 → 같은 sessionId PID 재보고 → `injectable=true` → 입력창 복귀 → 답변 전송 성공.
5. **ConPTY 전환**: Windows Terminal 세션에 전송 → `noconsole` → 세션연결 모드로 전환.
6. **Codex**: 세션연결 버튼 없이 기존 안내만.
7. **스파이크(구현 중 실측)**:
   - (S1) Windows 11 기본 터미널이 Windows Terminal이어도 **conhost 강제 실행 콘솔이 주입 가능**한지.
   - (S2) `claude --resume`가 **같은 sessionId 유지**하는지(같은 카드 복귀 전제). 다르면 대안(예: 새 세션 카드로 안내) 확정.
- 빌드: `msbuild AgentHub.sln /t:Restore` → `/t:Build /p:Configuration=Debug /p:Platform="Any CPU"` 통과, 산출물 `install/Debug/AgentHub.exe`.

## 9. 문서 동기화 (CLAUDE.md 필수)

`docs/index.html` 사용 가이드에 반영:
- 내가 보낸 답변이 구분 표시된다는 점.
- 여러 줄 답변 입력과 Enter=전송 / Shift+Enter=줄바꿈 규칙, 전송 중 로딩·중복 방지.
- 직접입력이 안 되는 세션에서 ‘세션연결’을 누르면 PC에서 세션이 다시 열려 답변 가능해진다는 점, claude 전용·LAN 한정 제약.

## 10. 영향 파일 (예상)

- `AgentHub/View/Htmls/index.html` — textarea 교체, 세션연결 버튼 마크업.
- `AgentHub/View/Htmls/js/app.js` — 내 답변 클래스, textarea auto-grow/키 규칙, 전송 상태머신, injectable 기반 UI 분기, `reopen`/`reopenResult` 처리.
- `AgentHub/View/Htmls/css/app.css` — 내 답변 카드, textarea 6줄, 로딩/버튼 상태, 세션연결 버튼.
- `AgentHub/View/Htmls/js/i18n.js` — `session.*` 키(ko/en) + 힌트 문구 조정.
- `AgentHub/Common/Models/SessionSummary.cs` — `Injectable` 필드.
- `AgentHub/Server/Socket/AgentMonitorModule.cs` — `reopen`/`reopenResult`.
- `AgentHub/Server/Terminal/SessionReopener.cs` — 신규(conhost 강제 resume 실행).
- (요약 빌더가 있는 파일) — `Injectable` 채우기.
- `AgentHub/View/Htmls/sw.js` — 자산 해시/버전 갱신 필요 시(변경된 정적 자산 프리캐시).
- `docs/index.html` — 사용 가이드 갱신(필수).
