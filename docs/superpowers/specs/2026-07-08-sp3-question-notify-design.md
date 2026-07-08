# SP3 — 질문 알림 (LAN 전용) + 답변 딥링크

- 작성일: 2026-07-08
- 상태: 설계 확정 대기 (사용자 리뷰 예정)
- 선행/후행: SP1·SP2(완료) → **SP3** → SP4(사용 가이드) → SP5(정적 홈페이지)

## 1. 목적

Claude가 **권한/입력을 기다릴 때**(질문이 왔을 때)를 감지해 **연결된 모바일에 알림**을 띄우고, 사용자가 **SP2 웹 터미널로 딥링크**해 답변을 입력할 수 있게 한다. **모든 통신은 LAN 내부(로컬 서버 + WebSocket)로만** 하고 **외부(푸시 서비스 등)로 절대 나가지 않는다.**

## 2. 확정된 결정 (브레인스토밍)

- **외부 금지 (하드 제약):** Web Push/VAPID/외부 푸시 서비스 **폐기**. 감지·전달·답변 모두 LAN 내부.
- **한계(수용됨):** 순수 LAN에서는 **앱이 완전히 닫히면 깨우는 푸시 불가**(SW가 소켓 유지 불가, OS 웨이크업은 외부 서비스 필요). SP3는 **"PWA가 열려 있거나 백그라운드로 살아있는 동안" 알림**이다.
- **감지:** Claude Code **`Notification` 훅**을 `~/.claude/settings.json`에 **additive** 등록(훅은 전 소스에서 병합 + 동일 명령 dedup → clawd 등 기존 훅 보존). `notification_type`로 "입력 필요"만 필터.
- **답변:** **SP2 웹 터미널 세션에 주입**(알림 → 터미널 딥링크 → 타이핑). 외부 CLI 세션은 알림만.

## 3. 데이터 흐름

```
Claude Code: 권한/입력 대기
  → Notification 훅 (async, 로컬) → agenthub-hook.js
      stdin JSON(session_id, cwd, message, notification_type) 읽기
      → POST https://127.0.0.1:<port>/api/hook/notification  (loopback, self-signed 무시)
        → 서버: notification_type 필터 → 승인·연결된 기기에 /ws/agents 로 {type:"ask", ...} broadcast
          → 폰(PWA 활성): {type:"ask"} 수신 → Notification API 알림 + 화면 배너
            → "답변하기" 탭 → 터미널 화면 열기(SP2) → 사용자가 답변 타이핑 → 소켓 → exe → claude
```

포트는 서버 시작 시 Agent Hub가 기록한 파일(`<StartupPath>\hook\endpoint.txt`)을 훅이 읽는다.

## 4. 컴포넌트 설계

### 4.1 `agenthub-hook.js` (신규, `AgentHub/hook/agenthub-hook.js`, 출력 복사)
- Node 스크립트(node는 환경에 존재). stdin에서 훅 JSON 파싱.
- 같은 폴더의 `endpoint.txt`(포트 또는 loopback URL) 읽어 `https://127.0.0.1:<port>/api/hook/notification`로 POST(node `https`, `rejectUnauthorized:false` — loopback 자체서명).
- 실패해도 조용히 종료(훅은 `async:true` fire-and-forget). 어떤 것도 외부로 보내지 않음.

### 4.2 엔드포인트 파일 기록 (`EmbedIOServer`)
- 서버 시작(포트 확정) 시 `<StartupPath>\hook\endpoint.txt`에 현재 loopback 포트 기록. (앱 업데이트로 경로가 바뀌면 훅 재설치 필요 — 한계로 명시.)

### 4.3 `HookConfigMerger` (신규, 순수, `AgentHub.Server.Hook`)
- settings.json(JSON 문자열/`JObject`) 대상 순수 함수:
  - `AddNotificationHook(json, command, argsMarker)` — `hooks.Notification` 배열에 우리 항목을 **멱등** 추가(마커로 기존 우리 항목 감지, 없을 때만 추가). 기존 항목 보존.
  - `RemoveNotificationHook(json, marker)` — 마커가 포함된 우리 항목만 제거.
  - `IsInstalled(json, marker)` — 설치 여부.
  - 마커 = 훅 command/args에 포함되는 고유 문자열(예: `agenthub-hook.js`).
- 로깅/IO 비의존 → 테스트 소스 링크. Newtonsoft `JObject` 사용.

### 4.4 `HookInstaller` (신규, I/O, `AgentHub.Server.Hook`)
- `~/.claude/settings.json` 읽기 → `HookConfigMerger` 적용 → **백업(.agenthub.bak) 후** 원자적 쓰기(임시파일→교체). 파일 없으면 최소 골격 생성.
- 등록 command: 현재 `node` 경로 + `<StartupPath>\hook\agenthub-hook.js`, `notification_type` 무관 전체 수신(서버에서 필터), `async:true`, `timeout` 5.
- `Install()` / `Uninstall()` / `Status()`.

### 4.5 REST (`ApiController`)
- `POST /api/hook/notification` (**loopback 전용**) — 바디 `{session_id, cwd, message, notification_type}`. 필터 통과 시 `AgentMonitorService.BroadcastAsk(project, message, sessionId)`.
- `POST /api/hook/install` / `POST /api/hook/uninstall` / `GET /api/hook/status` (**loopback 전용**) — `HookInstaller` 호출.

### 4.6 알림 broadcast (`AgentMonitorService` + `AgentMonitorModule`)
- `AgentMonitorService.BroadcastAsk(project, message, sessionId)` → `{ type:"ask", project, message, sessionId, at }` 직렬화 → `AgentMonitorModule`의 승인기기 broadcast 재사용(기존 `BroadcastMessageAsync` + 승인 필터).
- 필터: `notification_type` ∈ {permission_prompt, idle_prompt, agent_needs_input, elicitation_dialog} 만 알림(auth_success/agent_completed 등 무시). 서버에서 판정.

### 4.7 프론트엔드 (모바일 PWA)
- **"🔔 알림 켜기" 버튼**(모니터 화면) → `Notification.requestPermission()`. 상태에 따라 버튼 텍스트/숨김.
- `/ws/agents` `{type:"ask"}` 수신 → (권한 허용 시) `new Notification("Claude 입력 대기", { body: message, tag: sessionId })` + **화면 상단 배너**(프로젝트·메시지 + "답변하기"/"닫기").
- "답변하기" → SP2 `openTerminal()` 호출(터미널 화면으로). (해당 세션이 웹 터미널에서 돌고 있으면 프롬프트가 거기 보임.)
- i18n(ko/en) 키 추가. `sw.js` 캐시 버전 상향.
- (알림은 페이지 컨텍스트에서 표시 — SW push 아님. 앱이 살아있는 동안만.)

### 4.8 PC 콘솔 (host)
- 설정 탭에 **"모바일 질문 알림 훅 설치/제거" 토글 + 상태 표시**. `GET/POST /api/hook/(status|install|uninstall)` 사용.
- 안내: "Claude가 입력을 기다릴 때 연결된 폰에 알림이 갑니다. (설치 시 ~/.claude/settings.json에 항목 추가, 기존 훅 보존)".

## 5. 보안/프라이버시
- 모든 엔드포인트 loopback 전용(`/hook/*`) 또는 승인기기 broadcast. 외부 송신 경로 없음.
- 알림 본문은 Claude가 준 message(코드 조각 가능) — 승인·연결된 기기에만. 기기 revoke 시 broadcast 대상에서 제외(기존 승인 필터).
- settings.json 편집은 백업 + 멱등 + 우리 항목만 조작(마커).

## 6. 에러 처리
- 훅 POST 실패: 조용히 무시(로그 없음, 사용자 흐름 방해 금지).
- settings.json 파싱 실패: 백업 유지, 에러 반환(설치 실패 안내), 원본 미훼손.
- 알림 권한 거부: 배너만 표시(OS 알림 없이).
- 서버 예외: `LogService.Instance.Error`.

## 7. 테스트/검증
- **`HookConfigMerger` 단위**: add 멱등(2회 호출=1개), remove(우리 것만), isInstalled, 기존 clawd 항목 보존, 깨진/빈 JSON 처리 — xUnit(소스 링크).
- **빌드 게이트**: PowerShell msbuild 0 errors.
- **E2E(사용자 수동)**: 훅 설치 → 실제 claude가 권한 물을 때 폰(PWA 열림)에 알림/배너 → "답변하기" → 터미널에서 y/n 입력 → claude 진행. 훅 제거 시 알림 안 옴. clawd 훅 정상 유지 확인.

## 8. 범위 밖 / 한계
- 닫힌 앱 웨이크업 푸시(외부 서비스 필요) — 불가, 범위 밖.
- 알림→특정 ConPtySession 자동 매핑(세션 id↔PTY) — 하지 않음. "답변하기"는 터미널 화면을 열 뿐, 사용자가 자기 세션 프롬프트에 입력.
- 외부 CLI 세션(웹 터미널 밖) 답변 주입 — 불가(알림만).

## 9. 변경/신규 파일 (요약)
- 신규: `AgentHub/hook/agenthub-hook.js`, `AgentHub/Server/Hook/HookConfigMerger.cs`, `AgentHub/Server/Hook/HookInstaller.cs`, `AgentHub.Tests/HookConfigMergerTests.cs`.
- 수정: `AgentHub/Server/EmbedIOServer.cs`(endpoint.txt 기록), `AgentHub/Server/Controller/ApiController.cs`(hook 엔드포인트), `AgentHub/Server/Agents/AgentMonitorService.cs`(+`AgentMonitorModule` broadcast 재사용, BroadcastAsk), `AgentHub/AgentHub.csproj`(신규 .cs + hook/agenthub-hook.js Content), `AgentHub/View/Htmls/index.html`·`js/app.js`·`js/i18n.js`·`css/app.css`·`sw.js`, PC 콘솔 `host.html`·`js/host.js`.
- 서드파티 `EmbedIO/` 미수정. 새 NuGet 없음(Web Push 폐기).
