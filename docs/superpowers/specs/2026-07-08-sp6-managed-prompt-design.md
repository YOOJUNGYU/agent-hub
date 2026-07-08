# SP6 — 관리 세션: 프롬프트 전송 + AskUserQuestion 답변 (Claude)

- 작성일: 2026-07-08
- 상태: 설계 확정 대기 (사용자 리뷰 예정)
- 선행: SP1~SP5(완료). 후행: SP7(Codex 파리티), SP8(외부 종료 세션 이어가기 등).

## 1. 목적

모바일에서 **Claude 세션에 프롬프트를 보내고**, **AskUserQuestion(다지선다)에 옵션을 선택해 답변**할 수 있게 한다. 검증 결과 라이브 외부 세션에 안전한 입력 주입이 불가하므로, **Agent Hub가 실행·소유(PTY)하는 "관리 세션"** 에 한해 확실히 동작시킨다. 외부에서 띄운 라이브 세션은 보기+알림만.

## 2. 검증된 사실 (조사 결과 · 설계 근거)

- `claude -p --resume <id> "<prompt>"` (그리고 `codex exec resume <id> "<prompt>"`)로 세션에 프롬프트 추가는 가능하나, **그 세션이 다른 곳에서 라이브면 트랜스크립트가 뒤섞여 손상**된다(락 없음). → 라이브 외부 세션엔 안전하지 않음.
- **AskUserQuestion 답변을 외부에서 넣는 깔끔한 API 없음** — 그 세션의 stdin/PTY 제어만 가능.
- 안전한 것: **우리가 PTY로 실행한 관리 세션**(프롬프트·답변 확실), **완전 종료된 세션의 재개**(후속 SP8).
- AskUserQuestion tool_use input 구조 확인: `{questions:[{question,header,multiSelect,options:[{label,description}]}]}`. 답변은 선택 label로 tool_result 기록.

## 3. 관리 세션 모델

- **관리 세션** = Agent Hub가 `ConPtySession`으로 실행한 `claude` 프로세스(특정 폴더). PTY를 소유하므로 stdin write·키입력이 확실.
- SP2의 `ConPtySession`/`TerminalModule`(웹 터미널) 인프라를 **일반화**해 재사용. 게이트는 **기존 "웹 터미널 허용" 토글 + 승인기기**(동일 RCE 표면).
- **상관(correlation)**: `claude`를 폴더 C에서 실행 → `~/.claude/projects/<enc(C)>/`에 새 트랜스크립트가 생김 → 실행 시각 이후 mtime이 가장 최신인 파일의 `sessionId`를 이 관리 세션에 매핑. (경합 시 최신 우선, 확정 실패 시 재폴링.)
- `ManagedSessionRegistry`: `sessionId ↔ ConPtySession ↔ cwd ↔ contextId`. 세션 종료/소켓 종료 시 정리.

## 4. 엔진 추상화 (SP7 대비)

```
EngineSpec {
  key: "claude" | "codex"
  launchCommand(cwd): 실행 커맨드 (claude / codex.exe)
  projectDirFor(cwd): 트랜스크립트 디렉터리
  answerKeystrokes(optionIndex, multiSelect): AskUserQuestion 선택용 키 시퀀스
}
```
SP6은 `claude`만 구현. Codex는 SP7에서 EngineSpec 추가(커맨드·세션dir·파서만).

## 5. 서버 설계

### 5.1 `ManagedSessionRegistry` (신규, `AgentHub.Server.Terminal`)
- `Start(engine, cwd)` → `ConPtySession` 생성(engine.launchCommand) → 등록 → 백그라운드로 sessionId 상관.
- `Prompt(sessionId, text)` → 매핑된 세션에 `text + "\r"` write.
- `Answer(sessionId, optionIndex, multiSelect)` → `engine.answerKeystrokes` 시퀀스 write.
- `Get(sessionId)`, `Remove`, `DisposeAll`.
- I/O·PTY 레이어(로깅 허용). 순수 보조(키 시퀀스 계산 등)는 분리해 테스트.

### 5.2 WS 메시지 (`AgentMonitorModule` `/ws/agents` 확장 — 승인기기 게이트 재사용)
클라→서버:
- `{type:"startSession", engine:"claude", cwd}` — 관리 세션 시작.
- `{type:"prompt", sessionId, text}` — 프롬프트 전송.
- `{type:"answer", sessionId, optionIndex}` — AskUserQuestion 답변.
서버→클라:
- 기존 `sessions`/`activity`에, 관리 세션 여부(`managed:true`)와 대기 중 AskUserQuestion(`pendingAsk:{question,options,multiSelect}`)을 포함.
- `{type:"started", sessionId}` 또는 실패 통지.
- 모든 관리 명령은 **터미널 허용 토글 ON + 승인기기**일 때만 수행.

### 5.3 AskUserQuestion 감지 (`TranscriptParser` 확장)
- 세션 트랜스크립트에서 **마지막 assistant `tool_use` name=="AskUserQuestion"** 이고, 그 뒤 대응 tool_result가 아직 없으면 → `PendingAsk{question, header, multiSelect, options[]}` 추출(첫 question 기준, 다중 질문은 후속).
- SessionSummary에 `Managed`(bool), `PendingAsk`(nullable) 필드 추가.

### 5.4 AskUserQuestion 키입력 (best-effort)
- Claude CLI 메뉴는 화살표 선택. `answerKeystrokes(i)` = `"\x1b[B" * i + "\r"`(Down×i + Enter) 가정(커서 최상단 시작). multiSelect는 스페이스 토글 + Enter — SP6은 단일 선택 우선.
- **취약성 명시**: 커서 위치·렌더 타이밍에 따라 어긋날 수 있음. 실패 시 폴백은 "웹 터미널에서 직접 선택". 통합/E2E로 실제 검증.

## 6. 프론트엔드 (모바일 PWA)

- 모니터에 **"+ 새 세션"** 버튼(터미널 허용 시) → 폴더 입력(또는 설정된 기본 폴더 목록) + 엔진(SP6은 Claude 고정) → `startSession`.
- **세션 카드/상세**: `managed` 세션이면
  - 상세 하단에 **프롬프트 입력창 + 보내기** → `{type:"prompt"}`.
  - `pendingAsk` 있으면 질문 + **옵션 버튼 리스트** → 탭 시 `{type:"answer", optionIndex}` + 배너/카드에 강조(SP3 ask 배너를 옵션형으로 확장).
- 외부(비관리) 세션: 프롬프트/답변 UI 숨김, "외부 실행 세션 — 보기 전용" 안내.
- i18n(ko/en), sw 캐시 상향.

## 7. 보안
- 관리 세션 시작·프롬프트·답변 모두 **터미널 허용 토글 + 승인기기** 게이트(SP2와 동일 표면). 토글 OFF 시 관리 세션 전부 종료(기존 DisableAll 확장).
- cwd 입력 검증(존재하는 폴더). 임의 명령이 아닌 고정 엔진 커맨드만 실행.

## 8. 테스트/검증
- 순수: `answerKeystrokes(i)` 시퀀스, `TranscriptParser`의 PendingAsk 추출(fixture: AskUserQuestion tool_use 유/무, 답변 후) — xUnit.
- 빌드 게이트: PowerShell msbuild.
- E2E(사용자 수동): 새 Claude 세션 시작 → 폰에서 프롬프트 전송돼 Claude가 응답 → Claude가 AskUserQuestion 낼 때 옵션 버튼 → 탭 → 선택 반영. 토글 OFF 시 종료. 외부 세션엔 UI 안 뜸.

## 9. 범위 밖 (후속)
- Codex 파리티(EngineSpec 추가) → SP7.
- 외부 종료 세션 "이어가기(resume)" → SP8.
- multiSelect AskUserQuestion 완전 지원, 다중 질문 순차 응답 → 후속.
- 라이브 외부 세션 주입 → 하지 않음(안전 불가).

## 10. 변경/신규 파일 (요약)
- 신규: `AgentHub/Server/Terminal/ManagedSessionRegistry.cs`, `AgentHub/Server/Terminal/EngineSpec.cs`(+ Claude 구현), `AgentHub.Tests/*`(answerKeystrokes·PendingAsk).
- 수정: `AgentHub/Server/Terminal/TranscriptParser.cs`(PendingAsk), `AgentHub/Common/Models/SessionSummary.cs`(Managed/PendingAsk), `AgentHub/Server/Socket/AgentMonitorModule.cs`(startSession/prompt/answer), `AgentHub/Server/Agents/AgentMonitorService.cs`·`ClaudeSessionReader.cs`(managed 표식), `AgentHub/Server/Socket/TerminalModule.cs`(엔진 실행 재사용/DisableAll 연동), 프론트 `index.html`·`js/app.js`·`js/i18n.js`·`css/app.css`·`sw.js`.
- `EmbedIO/` 미수정. 새 NuGet 없음.
