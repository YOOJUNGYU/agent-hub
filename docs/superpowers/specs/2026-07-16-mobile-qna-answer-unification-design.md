# 모바일 Q&A 답변 통합 — 터미널 열기 제거 + 만료 질문 콘솔 주입 제출 (설계)

- 날짜: 2026-07-16
- 관련 메모리: `console-input-injection`, `askuserquestion-picker-injection`, `mobile-sessions-broadcast-force-switch`
- 선행 기능: 모바일 콘솔 직접 주입(`docs/superpowers/specs/2026-07-16-mobile-direct-console-input-injection-design.md`)

## 1. 배경 / 문제 (근본 원인 조사 결과)

Q&A(AskUserQuestion) 답변에는 두 개의 별개 상태가 있다:
- **라이브 elicit** (`AskRegistry._pending`, 서버 메모리): PC 훅이 답을 기다리는 창(~593초) 동안에만 존재. 이때만 폰 답변 폼→`elicitAnswer`→훅이 Claude에 주입.
- **pendingAsk** (`SessionSummary.PendingAsk`, 트랜스크립트 파생, 지속): "미답 질문 있음" 신호. 창이 만료돼도 남음.

**"터미널 열기"**는 별도 패널(`#askExpired`)이며 `openDetail` 1200ms 뒤 **pendingAsk는 있는데 라이브 elicit 폼이 없을 때** 뜬다:
- (a) **레이스**: `watch` 재전송 elicit이 1200ms보다 늦게 도착 → 그 사이 터미널 패널이 먼저 뜸(질문은 아직 라이브인데).
- (b) **만료**: 훅 창이 지나 `AskRegistry`에 항목 없음 → 재전송 불가 → 터미널 패널만 남음.
- (c) **연속 Q&A**: 제출 후 클라이언트 `elicit` 객체가 null로 정리되지 않아(취소 때만) `showAskExpired`의 `if(elicit) return` 가드가 꼬임. 연속 질문은 라이브 broadcast 수신에만 의존하고 pendingAsk로 폼을 다시 띄우지 않음.

## 2. 목표

- **Q&A에서 "터미널 열기"로 가지 않는다.** 미답 질문이 있으면 **항상 답변 폼**을 보여준다.
- 라이브 질문은 기존 훅 경로(`elicitAnswer`)로 정확히 제출.
- **만료 질문**은 **콘솔 주입**으로 제출(선행 기능 재사용). 검증된 키 시퀀스(`askuserquestion-picker-injection`):
  - 나열된 옵션(단일선택): 그 옵션 **번호** 주입 → 즉시 제출.
  - 직접입력(Other): `(옵션수+1)`("Type something") 주입 → 편집모드 → **텍스트 주입** → (별도) **Enter 주입**(반드시 분리).
- **연속 Q&A**가 pendingAsk 기준으로 매번 폼을 다시 띄워 확실히 동작.

## 3. 비목표

- 만료 질문의 **다중선택/다중질문** 완전 지원은 이번 범위 밖(라이브 경로는 기존대로 완전 지원). pendingAsk는 단일 질문만 담으므로(트랜스크립트 파서가 첫 질문만 추출), 만료 다중질문은 첫 질문만 폼에 표시. 만료 다중선택은 best-effort(번호 토글 후 Enter, 미검증) — 안 되면 후속.
- Codex/ConPTY: 콘솔 주입 불가 → 만료 질문 제출 시 실패 회신 + 안내(선행 기능과 동일 사유).
- detail 헤더의 일반 "⌨ 터미널 열기" 버튼(`openSessionTermBtn`)은 유지(Q&A 전용 아님). 제거 대상은 **askExpired 패널의 터미널 열기**뿐.

## 4. 검증된 사실 (스파이크, 2026-07-16)

`askuserquestion-picker-injection` 메모리 참조:
- 나열 옵션 번호 주입 → 즉시 선택·제출(예: `2`→2번). ✅
- Other: `(옵션수+1)` → 편집모드; 텍스트 주입 → 필드에 표시; **별도** Enter → 그 텍스트로 제출. ✅
- **주의**: Other에서 텍스트와 Enter를 같은 버스트로 넣으면 Enter가 먼저 처리돼 **빈 값 제출**. 반드시 분리(텍스트 → 지연 → Enter). 한글 OK.

## 5. 아키텍처 & 데이터 흐름

```
[모바일 detail] 미답 질문 존재?
   ├─ 라이브 elicit(id) 수신/재전송 → 그 폼(정확한 옵션) 우선
   └─ 없으면 pendingAsk로 폼 구성(id=null, "만료/주입 모드")
        │  (openDetail 즉시 / sessions 스냅샷에서 갱신 — 1200ms 대기·터미널 패널 없음)
        ▼ 사용자가 옵션 선택 또는 직접입력 후 제출
   ├─ id 있음(라이브)  → send {type:'elicitAnswer', id, answers}        (기존 훅 경로)
   └─ id 없음(만료)    → send {type:'pickerAnswer', sessionId, indices, text}
                              ▼ (/ws/agents)
                        AgentMonitorModule: engine=claude & PID 확인
                              ▼
                        ConsoleInputInjector.InjectPickerAnswer(pid, indices, text, optionCount)
                          - text 있음(Other): (optionCount+1) → 지연 → text → 지연 → Enter
                          - 단일 index i:      (i+1)  (즉시 제출)
                          - 다중 indices:      각 (i+1) 토글 → 지연 → Enter  (best-effort)
                              ▼
                        send {type:'pickerAnswerResult', sessionId, ok, reason}
```

## 6. 구성 요소

### A. 백엔드 — `ConsoleInputInjector.InjectPickerAnswer` (기존 클래스 확장)

- 파일: `AgentHub/Server/Terminal/ConsoleInputInjector.cs`
- 신규 공개 메서드: `Result InjectPickerAnswer(int pid, int[] indices, string text, int optionCount)`
  - **전체 시퀀스를 `_gate` lock으로 한 번에 직렬화**(중간에 다른 주입 끼어들기 방지).
  - private 원자 헬퍼 `WriteOnce(int pid, string payload, bool appendEnter)`(attach→write→free, lock 없음)를 재사용. 기존 `Inject`도 이 헬퍼를 lock으로 감싸도록 리팩터(중복 제거).
  - 단계 사이 지연 상수 `PickerStepDelayMs = 500` (`System.Threading.Thread.Sleep`).
  - 분기:
    - `text`가 비지 않음(Other): `WriteOnce((optionCount+1)번호, enter:false)` → Sleep → `WriteOnce(text, enter:false)` → Sleep → `WriteOnce("", enter:true)`.
    - `indices.Length == 1` (단일): `WriteOnce((indices[0]+1)번호, enter:false)` (즉시 제출).
    - `indices.Length > 1` (다중, best-effort): 각 `WriteOnce((i+1), enter:false)`+짧은 Sleep → 마지막 `WriteOnce("", enter:true)`.
  - AttachConsole 실패 → `NoConsole`, 예외 → `Failed`(+로그), 성공 → `Ok`.

### B. 백엔드 — `/ws/agents` `pickerAnswer` 케이스

- 파일: `AgentHub/Server/Socket/AgentMonitorModule.cs`
- `WatchMessage`에 필드 추가: `public int[] Indices { get; set; }` (0-based 선택 인덱스), `public int OptionCount { get; set; }`. (`Text`는 기존 필드 재사용.)
- 신규 케이스(`inject` 케이스 인근):
  ```csharp
  else if (msg.Type == "pickerAnswer" && !string.IsNullOrEmpty(msg.SessionId))
  {
      bool ok = false; string reason;
      if (AgentMonitorService.EngineOf(msg.SessionId) != "claude") reason = "engine";
      else if (!SessionPidRegistry.TryGet(msg.SessionId, out var pid)) reason = "nopid";
      else
      {
          var r = ConsoleInputInjector.InjectPickerAnswer(pid, msg.Indices ?? new int[0], msg.Text, msg.OptionCount);
          ok = r == ConsoleInputInjector.Result.Ok;
          reason = ok ? null : (r == ConsoleInputInjector.Result.NoConsole ? "noconsole" : "failed");
      }
      await SendSafe(context, Json.Serialize(new { type = "pickerAnswerResult", sessionId = msg.SessionId, ok, reason }));
  }
  ```

### C. 프론트 — Q&A 폼 통합 (app.js / index.html)

- 파일: `AgentHub/View/Htmls/js/app.js`, `index.html`, `css/app.css`, `js/i18n.js`
- **askExpired 제거**: `scheduleAskExpiredGuidance`/`showAskExpired`/`closeAskExpired`/`askExpiredOpenTerm` 핸들러와 `#askExpired` DOM 제거. `openDetail`/`backToList`의 호출부도 제거.
- **pendingAsk로 폼 구성**: 라이브 `elicit`이 없고 `sessionsById[currentSessionId].pendingAsk`가 있으면, pendingAsk로 기존 `elicit` 폼 객체를 구성해 표시:
  ```js
  elicit = { id: null, sessionId: id, fromPending: true, step: 0, answers: {},
    questions: [{ header: pa.header, question: pa.question, multiSelect: pa.multiSelect,
                  options: (pa.options||[]).map(l => ({ label: l })) }] };
  ```
  그리고 기존 `renderElicitStep()` 재사용.
- **표시 시점/우선순위**:
  - 라이브 `elicit`(id≠null)이 활성이면 유지(pendingAsk로 덮어쓰지 않음).
  - pendingAsk 폼 표시 중 라이브 elicit 도착 → `handleElicit`이 교체(정확한 id·옵션).
  - **사용자가 폼을 열어 답하는 중이면** 스냅샷으로 재렌더하지 않음(입력 방해 금지). 폼이 닫혀 있고 미답 pendingAsk가 있을 때만 표시.
  - `openDetail`: pendingAsk 있으면 즉시 폼 표시(1200ms 대기·터미널 없음). 라이브가 오면 교체.
  - `sessions` 스냅샷 수신 시(detail 화면, 폼 미표시): pendingAsk 있으면 폼 표시(연속 Q&A 대응).
  - pendingAsk가 사라지면(답변됨) fromPending 폼 닫기.
- **제출**(`elicitNext` 최종 단계):
  - `elicit.id`(라이브) → 기존 `send({type:'elicitAnswer', id, answers})`.
  - `elicit.id == null`(만료/pending) → 답을 인덱스/텍스트로 변환해 `send({type:'pickerAnswer', sessionId, indices, text, optionCount})`.
    - 각 선택 라벨 → `options`에서 인덱스 찾기. 라벨이 옵션에 없으면(기타 직접입력) → `text`에 커스텀 문자열, `indices=[]`.
    - `optionCount = 실제 옵션 수`(pendingAsk.options.length).
  - 제출 후 **`elicit` 상태 정리**(`elicit = null`, 오버레이 닫기) → 연속 Q&A가 다음 pendingAsk로 새 폼을 띄움. (clawd answerBlocked 재시도 보존이 필요하면 라이브 경로에서만 유지.)
- **결과 처리**: `pickerAnswerResult` 수신 → ok면 폼 정리, 실패면 reason별 안내(`inject.hint*` 재사용: noconsole/nopid/engine/failed).
- **엔진 가드**: Codex 세션은 애초에 pendingAsk 폼 대신 안내(선행 기능의 detail 입력 가드와 일관).

### D. i18n / CSS
- 만료 질문 폼에 작은 힌트(선택) — "이 질문은 답변 창이 지나 콘솔로 전달합니다" 정도. i18n 키 추가(ko/en). 과하면 생략.
- askExpired 관련 i18n 키(`askExpired.*`)는 미사용화(제거 또는 잔존 무해).

### E. 가이드 동기화 (`docs/index.html`)
- Q&A 답변 설명에서 "만료 시 터미널로 답하라"류 문구 제거. "미답 질문은 항상 폰에서 답변 폼으로 제출(라이브는 즉시 반영, 만료는 콘솔로 전달; Claude+conhost 한정)"로 갱신. ko/en.

## 7. 에러 처리
`pickerAnswerResult.reason`: `engine`(Codex)·`nopid`·`noconsole`(ConPTY)·`failed`. 프론트는 선행 기능의 `inject.hint*` 문구 재사용해 안내.

## 8. 동시성 / 안전
- `InjectPickerAnswer`는 `_gate` lock을 시퀀스 전체 동안 보유(Thread.Sleep 포함). Other는 최대 ~1.5s 보유 — 드문 경로라 허용. 기존 `Inject`와 상호 배제.
- 승인 기기 가드는 핸들러 상단 기존 로직이 담당.

## 9. 테스트 / 검증
- P/Invoke·소켓·PWA는 빌드+통합검증(스펙 관행). 순수 로직만 단위테스트:
  - **라벨→인덱스 매핑**(프론트) 또는 **InjectPickerAnswer의 시퀀스 구성**(백엔드에서 순수 분리 가능하면) 단위테스트 고려.
- 수동 E2E:
  1. 라이브 질문: 폼→옵션 선택/직접입력→즉시 반영(기존 훅).
  2. 만료 질문(창 경과): 폼→옵션 선택→그 세션에 번호 주입·제출; 직접입력→텍스트 주입·제출(한글).
  3. 연속 Q&A: Q1 답변 후 Q2가 폼으로 자동 표시·제출.
  4. "터미널 열기"가 Q&A 흐름에서 더는 안 뜸.
  5. Codex/Windows Terminal 세션: 만료 제출 시 안내.
- 빌드: `& "…VS2019…MSBuild.exe" AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"` 0 오류; `node --check` app.js/i18n.js.

## 10. 리스크 / 열린 질문
- **만료 다중선택**: 번호 토글→Enter 미검증. 안 되면 후속 스파이크. (단일선택·Other는 검증됨.)
- **만료 다중질문**: pendingAsk가 첫 질문만 담음 → 만료 상태에선 첫 질문만 제출 가능. 라이브면 전체 지원.
- **번호 매핑 안정성**: Other는 `(옵션수+1)`이 "Type something"이라는 Claude Code UI 순서에 의존(스파이크로 확인, UI 변경 시 취약).
- **지연 상수**(500ms): 느린 환경에서 부족하면 조정.
