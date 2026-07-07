# SP1 — 실제 에이전트 활동 피드 (Agent Activity Feed)

- 작성일: 2026-07-07
- 상태: 설계 확정 대기 (사용자 리뷰 예정)
- 선행/후행: SP1 → SP2(웹 터미널) → SP3(질문 알림/답변) → SP4(사용 가이드) → SP5(정적 홈페이지)

## 1. 목적

현재 모바일 모니터는 `AgentMonitorService`의 **mock 데이터**만 보여준다(실행 중 여부 정도). SP1은 이를 **PC에서 실제로 돌아가는 Claude Code 세션의 활동**으로 대체한다.
- 최근 세션 목록을 카드로 보여준다(활성/유휴/종료 상태 배지).
- 카드를 탭하면 해당 세션의 **실시간 활동 피드**(현재 작업 + 최근 이벤트 스트림)로 들어가 본다.

읽기 전용이며 어떤 입력도 주입하지 않는다(입력/답변은 SP2·SP3에서 다룸).

## 2. 데이터 소스

Claude Code는 세션마다 트랜스크립트 JSONL을 남긴다:

```
~/.claude/projects/<encoded-project-path>/<session-id>.jsonl
```

- 한 줄 = 이벤트 1건. 확인된 `type`: `assistant`, `user`, `attachment`, `mode`, `permission-mode`, `last-prompt`, `ai-title`, `file-history-snapshot` 등.
- 활용 필드:
  - `timestamp` (모든 이벤트, ISO8601)
  - `sessionId`, `cwd`(프로젝트 경로), `gitBranch`, `version`
  - `aiTitle` / `slug` (사람이 읽는 세션 제목)
  - `assistant` 이벤트: `message.content[]` 블록 = `text` | `thinking` | `tool_use`(`name`,`input`)
  - `user` 이벤트: 사용자 프롬프트 문자열, 또는 `tool_result`(`content`)

접근 방식은 **트랜스크립트 tailing**(설계 논의에서 확정한 안 A). Claude Code 설정을 건드리지 않고, Agent Hub 실행 전에 시작된 세션도 포함해 모든 세션을 읽을 수 있다. 훅 기반(안 B)은 SP3의 "입력 필요" 신호에서 별도로 도입한다.

## 3. 컴포넌트 설계

### 3.1 `ClaudeSessionReader` (신규, `AgentHub.Server.Agents`)

`AgentMonitorService`의 mock 본문을 대체하되 동일한 "seam" 역할(서비스가 소켓 모듈로 push)을 유지한다.

**탐색(Discover)**
- `~/.claude/projects/*/*.jsonl` 스캔.
- 파일 `mtime`이 **윈도우(기본 24시간)** 이내인 세션만 유지.
- 마지막 활동 시각 desc 정렬, **상한 기본 30개**.

**요약(Summarize) → `SessionSummary`**
```
Id            // session-id
Title         // aiTitle (없으면 slug, 둘 다 없으면 첫 사용자 프롬프트 요약)
Project       // cwd의 마지막 경로 세그먼트 (전체 cwd는 별도 필드)
Cwd
GitBranch
Status        // active | idle | ended
CurrentTask   // 최신 tool_use(name + 짧은 대상) 또는 최신 assistant text 요약
ToolName      // 최신 tool_use 이름 (없으면 null)
LastActivityAt
MessageCount
```

**상태 판정 휴리스틱**
- `active`: 마지막 이벤트가 **60초** 이내 **또는** 마지막 블록이 대응 `tool_result`가 아직 없는 `tool_use`(실행 중).
- `idle`: 마지막 턴이 assistant `text`로 끝나 사용자 입력 대기.
- `ended`: 마지막 활동이 유휴 윈도우(기본 30분)를 초과.

**상세 피드 → `ActivityEvent`**
```
Kind     // message | thinking | tool_use | tool_result | user_prompt | mode_change
Ts
ToolName // tool_use/tool_result 시
Summary  // 한 줄 요약 (예: "Edit  FormMain.cs", "Bash  msbuild ...")
Text     // 본문(길면 상세 화면에서 잘라 표시). thinking은 접힌 상태 기본
```
- 한 세션의 상세 요청 시 **최근 이벤트 N개(기본 200)** 를 파싱해 시간순 리스트로 반환.

**효율(Efficiency)**
- 파일별 캐시 키 `(size, mtime)` — 변경 없으면 재파싱 생략.
- **증분 tail**: 파일별 마지막 읽은 byte offset을 기록해, 4MB 트랜스크립트를 매 변경마다 전량 재파싱하지 않고 새로 추가된 꼬리만 파싱.
- 요약용 제목/`cwd` 등 헤더 필드는 파일 앞부분에서 1회 캐시.

### 3.2 실시간 감시: `FileSystemWatcher`

- 대상: `~/.claude/projects` (하위 디렉터리 포함, `*.jsonl`).
- 변경 이벤트 → 해당 파일 꼬리 재파싱 → (1) 목록 요약 갱신 push, (2) 그 세션을 구독 중인 클라이언트에 활동 델타 push.
- 디바운스(예: 300ms)로 연속 쓰기 폭주 완화.
- 폴백: 감시 실패/이벤트 유실 대비, 저빈도 폴링 타이머(예: 5초)로 목록 재요약(기존 타이머 자리 재사용).

### 3.3 전송: 기존 WebSocket 확장 (새 서버 없음)

`/ws/agents` 재사용, 스키마만 확장. 기기 승인 게이팅은 기존 `AgentMonitorModule` 로직 그대로.

- 목록 메시지: `{ type:"sessions", sessions:[SessionSummary...] }` (기존 `type:"agents"` 대체).
- 드릴인 구독: 클라이언트 → `{ type:"watch", sessionId }` / `{ type:"unwatch" }`.
  - `AgentMonitorModule`이 연결(context)별 구독 세션을 추적.
  - 서버 → `{ type:"activity", sessionId, events:[ActivityEvent...] }` (구독 직후 스냅샷 1회 + 이후 델타).
  - 현재 `OnMessageReceivedAsync`는 no-op이므로 여기서 watch/unwatch를 파싱하도록 구현.
- REST 폴백(승인 기기 전용, 기존 `/api/agents` 패턴):
  - `GET /api/sessions` → 요약 목록.
  - `GET /api/sessions/{id}` → 상세 활동(최근 N).

### 3.4 프론트엔드 (`AgentHub/View/Htmls`, 자체 코드)

- `index.html` 모니터 화면을 **세션 리스트**로: 제목, `프로젝트·브랜치`, 상태 배지, 현재 작업, 상대 시간.
- 카드 탭 → **상세 뷰**: 활동 피드(시간순, 아이콘+요약, 길면 펼침) + 뒤로가기.
- `app.js`: `sessions`/`activity` 메시지 처리, watch/unwatch 전송, 상세↔목록 라우팅.
- `app.css`: 리스트/상세/배지 스타일. 슬림 스크롤바 등 기존 전역 스타일 준수.
- i18n(`i18n.js`): 상태 배지/버튼/빈 상태 문구 ko·en 키 추가.
- PC 콘솔(`host.js`/`host.html`)은 `/ws/host`만 사용하고 `/ws/agents`(에이전트 목록)를 소비하지 않으므로 **SP1에서 변경 대상 아님**. (PC 콘솔에도 세션 목록을 노출할지는 후속 과제로 남김.)

## 4. 데이터 흐름

```
JSONL 파일 변경
  → FileSystemWatcher (디바운스)
    → ClaudeSessionReader: 꼬리 증분 파싱 → SessionSummary / ActivityEvent 갱신
      → AgentMonitorModule.Broadcast: 승인 기기에 {type:"sessions"} push
      → 구독자에게 {type:"activity", sessionId} push
클라이언트 최초 접속: auth → 승인 시 sessions 스냅샷
클라이언트 카드 탭: {type:"watch", sessionId} → activity 스냅샷 + 델타 수신
```

## 5. 에러 처리

- JSONL 라인 파싱 실패: 해당 라인만 건너뜀(부분 쓰기 중일 수 있음). 세션 전체 실패로 확산 금지.
- 파일 잠금/읽기 실패: 로그 후 다음 tick에서 재시도. `FileShare.ReadWrite`로 열기.
- `~/.claude/projects` 부재: 빈 목록 반환(신규 사용자). 오류 아님.
- Watcher 예외/버퍼 오버플로: 폴링 폴백이 목록 정합성 보장.
- 모든 예외는 기존 `LogService.Instance.Error` 경유.

## 6. 테스트 / 검증 (원칙 4)

- **파서 단위 검증**: 실제 트랜스크립트 샘플(고정 fixture)로 `ClaudeSessionReader`가
  - 제목/프로젝트/브랜치를 뽑는지,
  - 상태 휴리스틱(active/idle/ended)을 올바르게 판정하는지,
  - `tool_use` → CurrentTask 요약이 맞는지 확인.
- **증분 tail 검증**: fixture에 라인 append 후 새 이벤트만 반환되는지.
- **엔드투엔드**: 앱 실행 → 실제 Claude Code 세션 하나를 돌리며 모바일(또는 브라우저)에서 목록·상세 갱신 확인. `verify` 스킬로 실제 플로우 구동.
- **빌드 게이트**: `msbuild AgentHub.sln /t:Restore` → `/t:Build /p:Configuration=Debug /p:Platform="Any CPU"` 통과, 산출물 `install/Debug/AgentHub.exe`.

## 7. 결정된 기본값 / 가정

- 크로스 프로젝트 가시성 **허용**: 승인된 기기는 그 PC의 모든 프로젝트 세션·활동(코드/프롬프트/도구 호출 포함)을 본다. 이미 기기 승인 뒤에 있으므로 수용.
- 기본값: 활동 윈도우 24h, 세션 상한 30, active=60초 이내, ended=유휴 30분 초과, 상세 최근 이벤트 200. (추후 필요 시 설정화)

## 8. 범위 밖 (다른 SP)

- 입력/답변 주입, 웹 터미널 → SP2.
- 질문 감지 + PWA 푸시 알림(닫힌 앱 포함) → SP3 (여기서 Claude Code `Notification` 훅 도입).
- 사용 가이드 화면 → SP4. 정적 홈페이지 → SP5.

## 9. 변경 대상 파일 (요약)

- 신규: `AgentHub/Server/Agents/ClaudeSessionReader.cs`, `AgentHub/Common/Models/SessionSummary.cs`, `AgentHub/Common/Models/ActivityEvent.cs`
- 수정: `AgentHub/Server/Agents/AgentMonitorService.cs`(reader로 위임), `AgentHub/Server/Socket/AgentMonitorModule.cs`(watch/unwatch + activity push), `AgentHub/Server/Controller/ApiController.cs`(`/sessions` 엔드포인트), `AgentHub/View/Htmls/index.html`·`js/app.js`·`js/i18n.js`·`css/app.css`. (PC 콘솔 `host.*`는 대상 아님.)
- 서드파티 `EmbedIO/`는 수정하지 않는다.
