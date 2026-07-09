# 모바일 PWA 질문/답변(Elicitation) 설계

작성일: 2026-07-09
참고 구현: `C:\GIT\PRIVATE\clawd-on-desk` (elicitation 버블)

## 배경 / 목표

Claude가 `AskUserQuestion` 도구로 사용자에게 "질문 + 선택 답변 목록"을 제시할 때, 모바일 PWA에서
clawd-on-desk와 동일한 방식으로 질문·옵션을 보여주고, 사용자가 답을 선택하면 그 답이 Claude로
되돌아가 마치 사용자가 직접 답한 것처럼 세션이 진행되도록 한다.

**핵심 제약**: 현재 "답변하기" 버튼은 세션 터미널(`claude --resume`)로 연결된다. 이 터미널 연결을 제거하고,
터미널 없이 훅 응답만으로 답을 주입한다.

## 조사로 확정된 사실

- Claude Code에서 `AskUserQuestion`을 훅으로 제어하는 건 **공식 미지원**이나, `PermissionRequest` 훅이
  AskUserQuestion에서도 (비공식적으로) 발생하며 `updatedInput.answers`로 답을 주입할 수 있다.
  **clawd-on-desk가 실제 운영에서 쓰는 방식이 바로 이것**이다.
- agent-hub에는 이미 블로킹 훅(`/hook/permission`, PreToolUse) + `PermissionRegistry.AwaitDecision`
  인프라, `EngineSpec.AnswerKeystrokes`, `TranscriptParser.ExtractPendingAsk`(부분 파싱)가 존재한다.
- clawd-on-desk 스키마: `tool_input.questions[]` = `{ header, question, multiSelect, options:[{label, description}] }`.
  답변 결과: `updatedInput = { ...tool_input, answers: { [question텍스트]: 선택라벨(문자열/배열) } }`.

## 결정 사항

1. **답변 전달 = `PermissionRequest` 훅 + `updatedInput`** (clawd-on-desk 방식). 터미널/resume/프로세스 종료 없음.
2. **agent-hub 단독 실행 전제** — clawd-on-desk와 동시 실행하지 않으므로 훅 이중 응답 방어 로직 불필요.
3. PWA UI는 clawd-on-desk와 동일 수준: 다중 질문(스텝), radio/checkbox, 옵션 description, "기타" 자유입력.
4. 터미널 무한 로딩은 **방어적 폴백**(오버레이 타임아웃/ready 기반 해제) 먼저 적용, 근본원인은 이후 라이브 재현으로.

## 아키텍처 / 데이터 흐름

```
Claude(AskUserQuestion)
  → PermissionRequest 훅 발생
  → agenthub-hook.js: hook_event_name==="PermissionRequest" && tool_name==="AskUserQuestion" 일 때만
      POST /api/hook/elicit { session_id, cwd, tool_input }  (블로킹, ~118s)
  → ApiController.HookElicit: id 발급 → AskRegistry에 pending 등록
      → AgentMonitorService.BroadcastElicit(id, project, questions) → 승인된 폰에 {type:"elicit", ...} push
      → await AskRegistry.AwaitAnswer(id, timeout)
  → 폰(PWA): elicit 카드 렌더 → 사용자가 옵션 선택/기타 입력 → WS {type:"elicitAnswer", id, answers}
  → AgentMonitorModule: AskRegistry.Resolve(id, answersJson)
  → HookElicit: answers로 updatedInput 조립 → JSON 반환
  → agenthub-hook.js: { hookSpecificOutput:{ hookEventName:"PermissionRequest",
        decision:{ behavior:"allow", updatedInput:{ questions, answers } } } } 를 stdout 출력
  → Claude가 그 답을 채택하고 진행 (사용자에게 재질문 안 함)
```

타임아웃/무응답/미승인 시: 훅이 출력 없이 종료 → Claude 정상 흐름(PC 터미널 프롬프트)으로 폴백.

## 변경 파일

| 파일 | 변경 |
|---|---|
| `AgentHub/hook/agenthub-hook.js` | `PermissionRequest` 분기 추가. AskUserQuestion 한정, `/api/hook/elicit` 호출, updatedInput 응답 출력. |
| `AgentHub/Server/Hook/HookInstaller.cs` | `PermissionRequest` 훅 설치/제거(matcher ""). |
| `AgentHub/Server/Hook/AskRegistry.cs` (신규) | pending 질문의 답변(JSON 문자열) 대기/해제. `PermissionRegistry` 패턴 일반화. |
| `AgentHub/Server/Controller/ApiController.cs` | `POST /hook/elicit` 엔드포인트: tool_input 파싱 → broadcast → await → updatedInput 반환. |
| `AgentHub/Server/Agents/AgentMonitorService.cs` | `BroadcastElicit(id, project, questionsJson, sessionId)`. |
| `AgentHub/Server/Socket/AgentMonitorModule.cs` | `elicitAnswer` 수신 → `AskRegistry.Resolve`. |
| `AgentHub/View/Htmls/index.html` | elicit 화면/모달 마크업. |
| `AgentHub/View/Htmls/js/app.js` | `elicit` 처리·렌더·답변 전송. `askAnswer`→`openSessionTerminal` 제거. |
| `AgentHub/View/Htmls/css/app.css` | elicit 카드 스타일. |
| `AgentHub/View/Htmls/js/i18n.js` | elicit 관련 문자열(ko/en). |
| `AgentHub/View/Htmls/js/term.js` | 로딩 오버레이 타임아웃 폴백. |
| `docs/index.html` | 사용 가이드 갱신(질문 답변 흐름). |

## 답변 스키마 (서버→훅 반환)

- 단일선택: `answers[question] = 선택한 옵션 label` (문자열)
- 다중선택: `answers[question] = [label, ...]` (배열; 필요 시 쉼표 문자열)
- "기타": 사용자가 입력한 자유 텍스트를 label 대신 사용

## 리스크

- **비공식 의존**: PermissionRequest 훅이 AskUserQuestion에서 발생하는 동작에 의존. Claude Code 버전에 따라
  달라질 수 있음 → 라이브 검증 필수. 미발생 시 기능이 조용히 폴백(터미널 프롬프트)되므로 데이터 손상은 없음.
- PermissionRequest 훅은 모든 권한 요청에 발생 → AskUserQuestion 외에는 훅이 **출력 없이 통과**해야 함(기존 PreToolUse 권한 흐름 불변).

## 검증

1. 빌드 통과(msbuild Restore/Build).
2. 라이브: 폰 승인 → PC에서 AskUserQuestion 유발 → 폰에 카드 표시 → 답 선택 → Claude가 그 답으로 진행.
3. 터미널: 세션 터미널 진입 시 오버레이가 (정상)첫 출력 또는 (비정상)타임아웃에 반드시 해제됨.
