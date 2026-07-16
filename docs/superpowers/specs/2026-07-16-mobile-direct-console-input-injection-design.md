# 모바일 자유 텍스트 → 원본 세션 콘솔 직접 주입 (설계)

- 날짜: 2026-07-16
- 범위: **Claude 세션 전용** (Codex 데스크톱 앱은 콘솔이 없어 이 방식 적용 불가 — 아래 "비목표" 참조)
- 관련 메모리: `console-input-injection`, `session-terminal-resume-model`, `mobile-sessions-broadcast-force-switch`

## 1. 배경 / 문제

세션이 **선택형이 아니라 자유 텍스트 답변**을 요구할 때(예: "이 spec을 검토하고 수정할 부분을 알려달라") 모바일에서 진행할 수단이 없다. 기존 원격 제어 경로 `/ws/session`은 원본 프로세스를 kill하고 `claude --resume`으로 새 PTY를 띄우는 방식이라, "원본을 유지한 채 답변만 넣고 싶다"는 요구와 맞지 않는다.

## 2. 목표

- 모바일 **세션 상세(detail) 화면 하단에 텍스트 입력창 + 전송 버튼**을 추가한다.
- 입력한 텍스트가 EmbedIO 소켓을 통해 agent-hub.exe로 전달되고, agent-hub이 **해당 세션의 원본 프로세스를 종료하지 않고** 그 콘솔 입력 버퍼에 텍스트를 주입한다.
- 전송 = **텍스트 + Enter(제출)**. 즉 답변이 바로 제출된다.
- 이 입력은 범용이라 선택형 프롬프트에도 번호/텍스트를 타이핑해 답하는 폴백으로 쓸 수 있다.

## 3. 비목표 (YAGNI / 명시적 제외)

- **Codex 미지원.** 사용자 환경의 Codex는 Windows Store로 설치된 **Electron 데스크톱 앱**(`OpenAI.Codex_*`)이다. Electron 앱은 콘솔이 없어 `AttachConsole`/`WriteConsoleInput` 대상이 될 수 없다. Codex 세션에서는 입력창을 숨기고 안내만 표시한다. (Electron 창 UI 오토메이션은 별도 스파이크 주제로 남긴다.)
- resume-PTY 경로(`/ws/session`, `SessionTerminalModule`)는 **건드리지 않는다.** 신규 경로는 그와 완전히 분리된다.
- 여러 줄 입력·슬래시 명령 특수 처리 등은 이번 범위 밖(그냥 문자열을 그대로 주입).

## 4. 검증된 사실 (구현 전 스파이크로 실측)

2026-07-16 스파이크(`AttachConsole` + `WriteConsoleInput`)로 다음을 확인:

1. **고전 conhost 콘솔**에 주입 성공 — 주입한 명령이 실제 실행됨(마커 파일 생성).
2. **Windows Terminal/ConPTY**에서는 `AttachConsole`이 `err=6`(invalid handle)로 실패 → 주입 불가. (과거 "불가" 판정의 원인)
3. 사용자의 **실행 중 claude 세션 전부가 주입 가능**(부모 pwsh.exe·claude.exe 모두 `AttachConsole` 성공) — 고전 conhost 환경.
4. **raw-mode claude 실물**에 `AGENTHUB_INJECT_TEST` 주입 → 세션 입력줄에 그대로 표시됨(사용자 눈으로 확인).
5. **한글 주입 성공** — 단, `CharSet.Unicode`/`WriteConsoleInputW`(2바이트 `char`)가 **필수**. 기본 `CharSet.Ansi`면 `char UnicodeChar`가 1바이트로 잘려 ASCII만 우연히 통과하고 한글은 깨진다.

### 환경 전제와 한계

- 이 기능은 **고전 conhost에서만 동작.** ConPTY(Windows Terminal/VS Code 통합 터미널)에서 실행한 세션은 `AttachConsole` 실패로 주입되지 않는다 → 해당 경우 실패를 모바일에 명확히 회신한다.
- PID 출처는 기존 `SessionPidRegistry`(훅 `agenthub-hook.js`가 모든 이벤트에서 `process.ppid` 보고). `AttachConsole`은 pid가 속한 콘솔에 붙으므로, 보고된 PID가 claude 본체든 부모 pwsh든 같은 콘솔이면 주입이 도달한다.

## 5. 아키텍처 & 데이터 흐름

```
[모바일 detail 하단 입력창]
   │ send {type:'inject', sessionId, text}
   ▼  (/ws/agents  WebSocket, 기존 소켓 재사용)
AgentMonitorModule.OnMessageReceivedAsync  (msg.Type == "inject")
   │ 1) 승인 기기 확인 (기존 가드)
   │ 2) AgentMonitorService.EngineOf(sessionId) == "claude" 확인
   │ 3) SessionPidRegistry.TryGet(sessionId, out pid)
   ▼
ConsoleInputInjector.Inject(pid, text, appendEnter: true)
   FreeConsole → AttachConsole(pid) → GetStdHandle(STD_INPUT_HANDLE)
   → WriteConsoleInputW(문자별 KeyDown/KeyUp, 끝에 Enter) → FreeConsole
   ▼
원본 claude 콘솔 입력 버퍼에 유니코드 문자 입력 → claude가 읽어 제출
   │
   ▼  send {type:'injectResult', sessionId, ok, reason?}
[모바일] 성공 시 입력창 비움 / 실패 시 사유 표시
```

`/ws/session`이 아니라 `/ws/agents`를 쓰는 이유: detail 화면이 이미 `/ws/agents`에 붙어 `watch`를 보내고 있고, `/ws/agents`에는 원본 kill 부수효과가 없다. (`/ws/session`은 kill→resume 경로)

## 6. 구성 요소

### A. `ConsoleInputInjector` (신규)

- 파일: `AgentHub/Server/Terminal/ConsoleInputInjector.cs`, 네임스페이스 `AgentHub.Server.Terminal`
- 공개 API:
  ```csharp
  // 성공 시 Ok, 실패 사유를 구분해 반환.
  public enum InjectResult { Ok, NoConsole, Failed }
  public static InjectResult Inject(int pid, string text, bool appendEnter);
  ```
- 구현(스파이크에서 검증된 로직):
  - P/Invoke: `AttachConsole`, `FreeConsole`, `GetStdHandle`, `WriteConsoleInputW`(**`CharSet.Unicode`**), `VkKeyScan`.
  - 구조체 `INPUT_RECORD` / `KEY_EVENT_RECORD` 모두 **`CharSet.Unicode`**. `KEY_EVENT_RECORD`는 `LayoutKind.Explicit` (bKeyDown@0, wRepeatCount@4, wVirtualKeyCode@6, wVirtualScanCode@8, UnicodeChar@10, dwControlKeyState@12).
  - 문자마다 KeyDown+KeyUp 2개 레코드. `vk = ('\r'이면 VK_RETURN(0x0D), 아니면 VkKeyScan 하위바이트, 매핑 불가(한글 등)면 0)`, `UnicodeChar = 문자`.
  - `appendEnter`면 마지막에 `\r` 추가.
  - 절차: `FreeConsole()` → `AttachConsole(pid)`(실패 시 `GetLastError`==6 등 → `NoConsole`/`Failed` 구분 후 반환) → `GetStdHandle(STD_INPUT_HANDLE)` → `WriteConsoleInputW` → `finally`에서 `FreeConsole()`.
  - **동시성**: `AttachConsole`/`FreeConsole`는 프로세스 전역 상태. `static readonly object _gate`로 전 구간을 `lock`해 직렬화. agent-hub은 콘솔 없는 WinExe라 attach/free가 다른 기능과 충돌하지 않음.
  - 예외는 삼켜서 `Failed` 반환 + `LogService.Instance.Error`.

### B. `/ws/agents`의 `inject` 케이스

- 파일: `AgentHub/Server/Socket/AgentMonitorModule.cs`
- `WatchMessage`에 필드 추가: `public string Text { get; set; }`
- `OnMessageReceivedAsync`의 dispatch 체인(기존 `watch`/`unwatch`/`permissionDecision`/`elicitAnswer` 뒤)에 추가:
  ```csharp
  else if (msg.Type == "inject" && !string.IsNullOrEmpty(msg.SessionId) && msg.Text != null)
  {
      string reason = null; bool ok = false;
      if (AgentMonitorService.EngineOf(msg.SessionId) != "claude")
          reason = "engine";                  // Codex 등 미지원
      else if (!SessionPidRegistry.TryGet(msg.SessionId, out var pid))
          reason = "nopid";                    // PID 미보고(세션 종료 등)
      else
      {
          var r = ConsoleInputInjector.Inject(pid, msg.Text, appendEnter: true);
          ok = r == InjectResult.Ok;
          if (!ok) reason = r == InjectResult.NoConsole ? "noconsole" : "failed"; // noconsole=ConPTY 등
      }
      await SendSafe(context, Json.Serialize(new { type = "injectResult",
          sessionId = msg.SessionId, ok, reason }));
  }
  ```
- 승인 기기 가드는 핸들러 상단의 기존 로직(`_tokens`/`DeviceStatus.Approved`)이 이미 적용됨.

### C. 모바일 detail 화면

- 파일: `AgentHub/View/Htmls/index.html`, `AgentHub/View/Htmls/js/app.js`
- **UI**: detail 화면 하단에 입력 바(텍스트 `input` + 전송 `button`) 추가. 기존 detail 레이아웃/스타일을 따른다.
- **엔진 가드**: 현재 세션의 `engine`이 `codex`면 입력 바를 숨기고 "Codex 세션은 직접 입력을 지원하지 않습니다" 안내를 표시. `engine`은 `sessions` 브로드캐스트의 `SessionSummary.Engine`에서 얻는다(현재 열람 중 세션 요약을 참조).
- **전송**: 버튼 클릭/Enter 키 →
  ```js
  send({ type: 'inject', sessionId: currentSessionId, text: value });
  ```
  (텍스트만 전송; Enter 제출은 백엔드가 `appendEnter:true`로 처리)
- **결과 처리**: `app.js`의 메시지 dispatch(`m.type` 분기, 현재 `auth/sessions/activity/ask/done/elicit/permission/answerBlocked`)에 `injectResult` 추가:
  - `ok` → 입력창 비우기(+간단한 성공 피드백).
  - `!ok` → `reason`별 안내: `engine`(미지원)·`nopid`(세션을 찾을 수 없음)·`noconsole`(이 세션은 Windows Terminal/ConPTY라 직접 입력 불가)·`failed`(주입 실패).
- **화면 가드**: 신규 입력 UI는 detail 화면 내부 요소이므로 별도 화면 추가 아님. `sessions` 브로드캐스트 수신 시 화면 강제 전환 가드(`currentSessionId`/terminal 표시 조건)는 기존 그대로 유효(메모리 `mobile-sessions-broadcast-force-switch` 준수 — 신규 화면을 추가하지 않으므로 가드 변경 불필요).

### D. 사용 가이드 동기화 (필수)

- 파일: `docs/index.html`
- 모바일에서 세션 상세 하단 입력창으로 **자유 텍스트 답변을 원본 세션에 직접 전송**하는 기능을 문서화. 제약(고전 conhost 세션만, Codex 미지원)도 함께 명시.

## 7. 에러 처리 & 상태 회신

`injectResult.reason` 값:

| reason | 의미 | 모바일 안내 |
|---|---|---|
| (ok=true) | 주입 성공 | 입력창 비움 |
| `engine` | Codex 등 비-Claude 세션 | 미지원(입력창은 애초에 숨김; 안전망) |
| `nopid` | PID 미보고(세션 종료/훅 미실행) | "세션을 찾을 수 없습니다" |
| `noconsole` | AttachConsole 실패(ConPTY 등) | "이 세션은 직접 입력을 지원하지 않는 터미널에서 실행 중입니다" |
| `failed` | WriteConsoleInput/기타 실패 | "전송에 실패했습니다" |

## 8. 동시성 / 안전

- 주입 전 구간을 `ConsoleInputInjector`의 전역 lock으로 직렬화(AttachConsole 프로세스 전역 상태 보호).
- agent-hub은 콘솔 없는 WinExe → `FreeConsole`/`AttachConsole`이 자체 UI/로직과 충돌하지 않음. 주입은 밀리초 단위로 짧다.
- 승인되지 않은 기기의 메시지는 기존 가드로 차단.

## 9. 테스트 / 검증

- **단위 테스트 한계**: P/Invoke 콘솔 주입은 실제 콘솔이 필요해 CI 단위 테스트에 부적합. 문자→`INPUT_RECORD` 매핑(특히 `'\r'`→VK_RETURN, 한글→vk 0 + UnicodeChar 보존) 정도만 순수 함수로 분리해 단위 테스트 가능하면 추가.
- **통합 검증 절차(수동, 스파이크 재사용)**:
  1. `conhost cmd`를 띄우고 자식 cmd PID로 `echo ...> marker` 주입 → 마커 파일 생성 확인(ASCII).
  2. 동일하게 **한글** 명령 주입 → 한글 파일명 생성 확인(유니코드 경로 검증).
  3. 실제 claude 세션에 `noenter`로 문자열 주입 → 입력줄 표시 확인(raw-mode).
- **엔드투엔드**: 모바일 detail에서 한글 답변 전송 → PC claude 세션에 답변이 입력·제출되는지 확인. Codex 세션에서는 입력창이 숨는지 확인. ConPTY 세션에서는 `noconsole` 안내가 뜨는지 확인.
- 빌드: `msbuild AgentHub.sln /t:Restore` → `/t:Build /p:Configuration=Debug /p:Platform="Any CPU"` 통과, 산출물 `install/Debug/AgentHub.exe`.

## 10. 리스크 / 열린 질문

- **ConPTY 세션**: 사용자 기본이 고전 conhost로 확인됐으나, 특정 세션을 Windows Terminal에서 띄우면 주입 불가. `noconsole` 회신으로 명확히 안내하는 것으로 처리(자동 우회 없음).
- **PID 정확성**: `SessionPidRegistry`가 보고한 PID의 프로세스가 이미 죽었으면 `AttachConsole` 실패 → `nopid`/`noconsole`로 회신. (재사용된 PID가 다른 콘솔에 붙을 극히 드문 경우는 감수.)
- **claude 입력 상태 의존**: claude가 프롬프트 입력 대기 상태가 아니라 작업 중이면, 주입된 문자는 입력 버퍼에 쌓였다가 다음 입력 시점에 반영된다(콘솔 기본 동작). 이는 실제 키보드 입력과 동일한 동작이라 수용.

## 11. 구현 순서 (검증 포인트 포함)

1. `ConsoleInputInjector` 구현 → verify: 통합 검증 절차 1·2(ASCII·한글 마커 파일 생성).
2. `WatchMessage.Text` + `inject`/`injectResult` 케이스 → verify: 소켓으로 `inject` 보내면 claude 세션에 반영, `injectResult` 회신 확인.
3. 모바일 detail 입력 UI + 결과 처리 + 엔진 가드 → verify: 모바일에서 한글 답변 전송 성공, Codex 입력창 숨김.
4. `docs/index.html` 가이드 갱신 → verify: 가이드에 신규 기능·제약 반영.
5. 전체 빌드 통과 확인.
