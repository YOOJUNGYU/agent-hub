# Codex 엔진 파리티 설계

- 날짜: 2026-07-14
- 대상: Agent Hub가 Claude Code와 동일하게 **Codex 세션**도 모니터링·원격 제어하도록 확장
- 대상 사용자: **내 PC 위주(현재 Codex 빌드, `[features] hooks = true`)** — hooks 의존 기능까지 완전 파리티 목표

## 목표

내 PC에서 돌고 있는 Codex 세션을, Claude 세션과 **똑같이** 폰/PC 콘솔에서:

- 실시간 모니터링(상태·현재 작업·경과·누적·마지막 멘트)
- 완료/대기 알림(푸시)
- 원격 권한 승인(PreToolUse)·원격 답변(AskUserQuestion류 PermissionRequest)
- 세션 터미널 이어받기(`codex resume`)

하나의 세션 목록에 **엔진 배지(Claude/Codex)**로 병합 표시한다.

## 핵심 조사 결과 (사실 기반)

이 PC의 Codex 빌드(`Codex Desktop`, cli_version 0.131.0)를 실측한 결과:

1. **세션 저장**: `~/.codex/sessions/YYYY/MM/DD/rollout-<ISO시각>-<uuid>.jsonl`.
   - 세션 id = UUID(파일명 + `session_meta.payload.id`에 동일).
2. **세션 인덱스**: `~/.codex/session_index.jsonl` = 줄당 `{id, thread_name, updated_at}`.
   - `thread_name`이 곧 제목 → Claude처럼 제목 추론 파싱 불필요.
3. **트랜스크립트 포맷(Claude와 완전히 다름)**: 줄 단위 JSON, `type`이 다음 중 하나:
   - `session_meta` → `payload.{id, cwd, cli_version, model_provider, ...}` (첫 줄).
   - `event_msg` → `payload.type`가 `task_started`/`user_message`/... (상태·이벤트).
   - `response_item` → `payload.type`가 `message`(role user/assistant/developer, content[].text) / `reasoning`(encrypted) 등.
4. **훅**: `~/.codex/hooks.json` + `config.toml`의 `[features] hooks = true`.
   - 지원 이벤트(실측 백업 기준): `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PermissionRequest`, `PostToolUse`, `Stop`.
   - 포맷: `{"hooks": {"<Event>": [{"hooks": [{"type":"command","command":"<단일 문자열>","timeout":<초>}]}]}}`
     - Claude와 달리 **`args` 배열/`matcher`/`async` 없음**. 명령은 단일 문자열(PowerShell 호출형 `& "node" "script"` 관찰).
   - **훅 I/O 계약은 Claude 호환**: `codex.exe`에 `hook_event_name`, `hookSpecificOutput`, `permissionDecision`, `tool_input`, `PreToolUse`, `PermissionRequest`, `SessionStart` 문자열이 존재.
     → stdin 페이로드 필드명과 출력(`hookSpecificOutput.permissionDecision` 등)이 Claude와 같음 → **기존 `agenthub-hook.js` 재사용 가능**(별도 어댑터 불필요 전망).
5. **충돌 인지**: `ClawdGuard`가 이미 clawd-on-desk(같은 Codex 훅을 가로채는 앱)를 감지·종료. 유지.

## 아키텍처 — 엔진 심(seam) 일반화

기존 코드에 이미 다엔진 심이 있음(`EngineSpec.For(key)`, `AgentMonitorService` 주석 "seam"). Claude 전용 4요소를 엔진별로 확장한다.

| 요소 | Claude(기존) | Codex(신규) |
|---|---|---|
| 세션 읽기 | `ClaudeSessionReader` | `CodexSessionReader` |
| 트랜스크립트 파싱 | `TranscriptParser` | `CodexTranscriptParser` |
| 훅 설치/제거 | `HookInstaller` → `~/.claude/settings.json` | `CodexHookInstaller` → `~/.codex/hooks.json` |
| 훅 스크립트 | `agenthub-hook.js` | **동일 스크립트 재사용**(계약 호환) |
| 터미널 이어받기 | `ClaudeEngine`(`claude --resume`) | `CodexEngine`(`codex resume <id>`) |

**데이터 모델**: `SessionSummary`에 `Engine` 필드("claude"|"codex") 추가 → 배지 표시 + 조회/이어받기 라우팅의 단일 근거.

## 컴포넌트 상세

### CodexSessionReader (`AgentHub.Server.Agents`)
- 책임: `ClaudeSessionReader`와 동일 인터페이스(정적 메서드) — `ListSessions/GetActivity/CwdOf/TitleOf/LastAssistantTextOf/Start/Stop`.
- 소스 루트: `~/.codex/sessions`(재귀, 날짜 폴더 중첩) + `~/.codex/session_index.jsonl`(제목/updated_at).
- `FileSystemWatcher`: 루트에 `*.jsonl`, `IncludeSubdirectories = true`, 300ms 디바운스 + 5초 폴백(기존과 동일 패턴).
- 세션 id → 파일 경로 캐시(`ConcurrentDictionary`), 24h 윈도우·MaxSessions 상한 동일.
- 잠긴 파일 대비 `FileShare.ReadWrite` 읽기(동일).

### CodexTranscriptParser (`AgentHub.Server.Agents`)
- 책임: Codex JSONL 라인들을 기존 `SessionSummary`·`ActivityEvent` 모델로 변환.
- 매핑:
  - 제목: `session_index.jsonl`의 `thread_name` 우선, 없으면 첫 user 메시지에서 유도, 최종 폴백 sessionId.
  - cwd/cli_version: `session_meta.payload`.
  - 상태(작업 중/대기/종료): `event_msg`(`task_started` 등)의 최신 상태 + 마지막 수정시각 기반(Claude 판정 규칙과 동치가 되도록 매핑).
  - 마지막 멘트: 마지막 `response_item` role=assistant의 텍스트.
  - PendingAsk/elicit: PermissionRequest 대기 상태를 트랜스크립트에서 추출 가능하면 반영(불가 시 훅 이벤트로만 처리).
- 누적 토큰/경과 등 Claude에서 뽑던 지표는 Codex 포맷에서 가능한 필드로 대응, 없으면 생략(배지·목록엔 영향 없음).

### AgentMonitorService (병합)
- `CurrentSessions()` = Claude + Codex 세션을 **updated 시각 내림차순 병합**.
- `Activity/CwdOf/TitleOf/LastAssistantTextOf(sessionId)`는 **엔진 라우팅**: 세션이 속한 리더로 위임(id→엔진 매핑 유지, 또는 양쪽 조회 폴백).
- `Start/Stop`은 두 리더를 함께 기동/정지하고, **둘 중 하나라도 변경**되면 `OnChanged` push(단일 게이트 유지).
- Broadcast* (ask/done/elicit/permission)는 변경 없음(엔진 무관).

### CodexHookInstaller (`AgentHub.Server.Hook`)
- 책임: `~/.codex/hooks.json`에 Agent Hub 훅을 멱등 설치/제거(백업·원자적 교체 = `HookInstaller` 패턴 재사용).
- 포맷 차이 반영: 이벤트별 `{"hooks":[{"type":"command","command":"<단일 문자열>","timeout":N}]}`.
  - 명령 문자열에 node 경로 + 스크립트 경로 + (PermissionRequest는) 대기창 초를 **한 문자열로** 합성.
  - **구현 시 확인 필요(스파이크)**: hooks.json `command`가 (a) 배열/셸 파싱 방식, (b) 추가 인자 전달 방식(Claude의 `args` 대체). PowerShell 호출형 `& "node" "script" "600"`이 인자로 전달되는지 첫 실측으로 확정.
- 마커로 자기 항목만 식별해 강제 갱신/제거(`HookConfigMerger`를 Codex 포맷용으로 확장하거나 소형 병합기 추가).
- 이벤트 매핑(Claude 대응):
  - `SessionStart` → session-pid 보고
  - `PreToolUse` → 원격 권한(블로킹)
  - `PermissionRequest` → 원격 답변(블로킹, AskUserQuestion류만)
  - `Stop` → 완료 알림
  - `Notification` 부재 시 대체 이벤트(예: `PostToolUse`/`UserPromptSubmit`)로 알림 대응 여부는 스파이크 후 확정.

### CodexEngine (`AgentHub.Server.Terminal`)
- `EngineSpec` 파생 추가, `EngineSpec.For("codex")`가 반환.
- `LaunchCommand`/`ResumeCommand(sessionId)` = `cmd.exe /c codex resume <id>`(정확한 구문 실측 확정).
- `ProjectDir(cwd)` = `~/.codex/sessions`(또는 조회에 쓰이는 경로) 대응.
- `SessionTerminalModule`이 세션 엔진으로 `EngineSpec.For(engine)` 선택.

### PID 종료 / 이어받기
- SessionStart 훅이 PID 보고 → `SessionPidRegistry`(영속 JSON) 재사용. `ProcessKiller` 워크업은 이미 셸 화이트리스트 기반이라 엔진 무관(node/codex 셸 심도 동일 처리 가능; 실측 시 codex 프로세스 트리 확인).

### UI (모바일 PWA + PC 콘솔)
- 세션 카드에 **엔진 배지**(Claude/Codex) 표시 — `SessionSummary.Engine` 사용.
- 목록은 병합된 단일 목록(시간순). 기존 sessions 브로드캐스트/강제전환 가드 로직 유지.
- 최소 변경: 배지 렌더링만 추가, 탭/필터 신설 없음.

### 사용 가이드 (필수)
- `docs/index.html`(= `/guide.html`, GitHub Pages 단일 소스)에 Codex 세션도 동일하게 보이고 제어된다는 점 반영.
- CLAUDE.md 규칙상 기능 변경과 **같은 작업에서** 갱신.

## 데이터 흐름 (변경 없음)

```
Codex 훅(hooks.json) --stdin--> agenthub-hook.js --loopback POST--> /api/hook/* --> Broadcast* --WS--> 폰
~/.codex/sessions/**  --FSWatcher--> CodexSessionReader --> AgentMonitorService(병합) --WS--> 폰/콘솔
```

기존 파이프 그대로, **소스만 2개**(Claude/Codex)로 늘어남.

## 검증 (원칙 4: 목표 주도)

1. **훅 계약 스파이크** → 확인: Codex가 stdin에 넣는 JSON 필드 + `hookSpecificOutput` 출력 존중 + hooks.json `command` 인자 전달 방식. (수동 codex 1턴 실행으로 캡처)
   - 성공 기준: PreToolUse에서 `agenthub-hook.js`가 반환한 `permissionDecision`이 실제로 존중됨.
2. **세션 파싱** → 확인: 실제 rollout 파일 몇 개로 `CodexSessionReader.ListSessions()`가 올바른 제목·cwd·상태·마지막 멘트를 반환(골든 테스트, `AgentHub.Tests`).
3. **병합** → 확인: Claude+Codex 세션이 시간순으로 한 목록에 나오고 각자 엔진 배지가 맞음.
4. **이어받기** → 확인: 폰에서 Codex 세션 터미널 attach 시 `codex resume`가 대화를 이어받아 xterm에 출력.
5. **원격 승인/답변** → 확인(계약 스파이크 통과 시): 폰에서 허용/거부·답변 선택이 Codex 진행에 반영.
6. **빌드**:
   ```powershell
   msbuild AgentHub.sln /t:Restore
   msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
   ```
   산출물 `install/Debug/AgentHub.exe`.

## 범위 밖(YAGNI)

- 표준(비-hooks) Codex 빌드 지원 — 대상은 내 PC 빌드.
- Claude/Codex 탭·필터 분리 UI — 병합+배지로 충분.
- 새 세션 생성 기능 — 기존 방침대로 "기존 세션 이어받기"만.
- Codex 고유 지표(토큰 등) 완전 재현 — 가능한 필드만.

## 주요 리스크

1. **hooks.json `command` 인자 전달 방식**(가장 큰 미확정). → 구현 1단계 스파이크로 확정. 만약 인자 전달이 불가하면 대기창 초를 스크립트가 서버에서 조회하도록 폴백.
2. **알림용 이벤트 매핑**: Claude의 `Notification`에 정확히 대응하는 Codex 이벤트가 없을 수 있음 → `Stop`(완료)만으로도 핵심 알림은 충족, 나머지는 스파이크 후 결정.
3. **동일 UUID 네임스페이스**: Claude/Codex 세션 id 충돌 가능성은 사실상 없음(둘 다 랜덤 UUID)이나, 조회는 엔진 명시로 안전하게.
