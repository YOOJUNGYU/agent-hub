// Agent Hub 훅: Claude/Codex CLI 이벤트를 로컬 Agent Hub 서버(127.0.0.1 loopback)로 전달한다. 외부로 나가지 않는다.
//  - Notification      : 알림(fire-and-forget).
//  - SessionEnd        : 세션 종료 → 세션↔PID 지도에서 제거(fire-and-forget).
//  - PermissionRequest : 승인이 필요한 도구 호출을 폰에서 원격 허용/거부(블로킹). AskUserQuestion은 질문 답변 흐름.
//  - PreToolUse        : Codex 전용(Codex hooks.json이 이 이벤트로 권한을 넘긴다). Claude는 등록하지 않는다.
// WSL에서 실행되는 Claude/Codex도 이 스크립트를 Windows node.exe로(인터롭) 실행한다 → HTTP는 항상 Windows
// 네트워크에서 나가므로 127.0.0.1로 서버에 닿는다(WSL에서 직접 127.0.0.1은 닿지 않음).
const fs = require('fs');
const path = require('path');
const https = require('https');

function readPort() {
  try { return fs.readFileSync(path.join(__dirname, 'endpoint.txt'), 'utf8').trim(); } catch (e) { return ''; }
}

function post(port, apiPath, payload, timeoutMs, onDone) {
  const body = JSON.stringify(payload);
  const req = https.request({
    host: '127.0.0.1', port: Number(port), path: apiPath, method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
    rejectUnauthorized: false, timeout: timeoutMs
  }, res => {
    let data = '';
    res.on('data', d => (data += d));
    res.on('end', () => onDone(data));
  });
  req.on('error', () => onDone(null));
  req.on('timeout', () => { try { req.destroy(); } catch (e) {} onDone(null); });
  req.write(body); req.end();
}

let raw = '';
process.stdin.on('data', d => (raw += d));
process.stdin.on('error', () => process.exit(0));
process.stdin.on('end', () => {
  let p;
  try { p = JSON.parse(raw || '{}'); } catch (e) { process.exit(0); }
  const port = readPort();
  if (!port) process.exit(0);

  if (p.hook_event_name === 'SessionEnd') {
    // 세션 종료 → PID 지도에서 제거(아래 PID 보고보다 먼저 처리해야 지운 직후 다시 등록되지 않는다).
    post(port, '/api/hook/session-end', { session_id: p.session_id }, 2500, () => process.exit(0));
    setTimeout(() => process.exit(0), 3000);
    return;
  }

  // 세션↔PID 지도용 보고(콘솔 주입 대상 판별). process.ppid = 이 훅을 띄운 CLI 프로세스 PID.
  // WSL 세션도 보고한다: 훅이 인터롭으로 실행되므로 ppid는 그 터미널의 wsl.exe(= 콘솔에 붙은 Windows
  // 프로세스)가 되고, 그 콘솔 입력 버퍼에 쓰면 wsl.exe가 WSL 안 CLI의 stdin으로 그대로 전달한다.
  // 인터롭 부모는 터미널(WSL 세션)마다 다르므로 여러 세션이 떠 있어도 서로 섞이지 않는다.
  if (p.hook_event_name === 'SessionStart') {
    post(port, '/api/hook/session-pid', { session_id: p.session_id, pid: process.ppid }, 2500, () => process.exit(0));
    setTimeout(() => process.exit(0), 3000);
    return;
  }
  post(port, '/api/hook/session-pid', { session_id: p.session_id, pid: process.ppid }, 2000, () => {}); // fire-and-forget

  if (p.hook_event_name === 'PermissionRequest') {
    // agent-hub.exe(서버)는 상시 실행 전제. 폰의 PWA가 닫혀 있어도 서버가 요청을 붙들고 대기하므로,
    // 창(argv[2] 초, 기본 600) 동안 HTTP 요청을 열어 둔다 → push 받고 PWA를 열어 답할 시간 확보.
    const windowSec = Number(process.argv[2]) || 600;
    const budgetMs = Math.max((windowSec - 5) * 1000, 1000);

    if (p.tool_name !== 'AskUserQuestion') {
      // 승인이 필요한 도구 호출(Bash·Write·WebFetch·MCP 등 전부) → 폰에서 허용/거부.
      // 서버가 '지금 폰으로 답할 상황이 아니다'(PC 사용 중 + 폰 미연결)라고 보면 즉시 ask를 돌려주고,
      // 그러면 출력 없이 통과 = 기존 흐름(PC 터미널 프롬프트)로 폴백한다.
      post(port, '/api/hook/permission', {
        session_id: p.session_id, cwd: p.cwd,
        tool_name: p.tool_name, tool_input: p.tool_input,
        permission_mode: p.permission_mode, waitMs: budgetMs
      }, budgetMs + 2000, data => {
        try {
          const r = JSON.parse((data || '{}').replace(/^﻿/, '')); // 선행 BOM 제거(서버 응답에 BOM이 붙어도 안전)
          if (r.decision === 'allow' || r.decision === 'deny') {
            process.stdout.write(JSON.stringify({
              hookSpecificOutput: {
                hookEventName: 'PermissionRequest',
                decision: r.decision === 'allow'
                  ? { behavior: 'allow' }
                  : { behavior: 'deny', reason: 'Agent Hub 원격 응답(거부)' }
              }
            }));
          }
        } catch (e) {}
        process.exit(0);
      });
      setTimeout(() => process.exit(0), budgetMs + 4000); // 안전망(Claude 훅 timeout 이내)
      return;
    }

    post(port, '/api/hook/elicit', {
      session_id: p.session_id, cwd: p.cwd, tool_input: p.tool_input, waitMs: budgetMs
    }, budgetMs + 2000, data => {
      try {
        const r = JSON.parse((data || '{}').replace(/^﻿/, '')); // 선행 BOM 제거(서버 응답에 BOM이 붙어도 안전)
        if (r.updatedInput) {
          // 폰에서 고른 답을 마치 사용자가 답한 것처럼 주입.
          process.stdout.write(JSON.stringify({
            hookSpecificOutput: {
              hookEventName: 'PermissionRequest',
              decision: { behavior: 'allow', updatedInput: r.updatedInput }
            }
          }));
        }
        // updatedInput 없음(무응답/타임아웃/미승인) → 출력 없음 = 기존 흐름(PC 프롬프트)으로 폴백.
      } catch (e) {}
      process.exit(0);
    });
    setTimeout(() => process.exit(0), budgetMs + 4000); // 안전망(Claude 훅 timeout 이내)
    return;
  }

  if (p.hook_event_name === 'PreToolUse') {
    // Codex 전용 경로(Codex hooks.json은 PreToolUse로 권한을 넘긴다). Claude는 PermissionRequest만 쓴다.
    // waitMs로 서버 대기를 훅 HTTP 타임아웃(118s) 안쪽으로 묶는다.
    post(port, '/api/hook/permission', {
      session_id: p.session_id, cwd: p.cwd,
      tool_name: p.tool_name, tool_input: p.tool_input,
      permission_mode: p.permission_mode, waitMs: 110000
    }, 118000, data => {
      try {
        const r = JSON.parse((data || '{}').replace(/^﻿/, '')); // 선행 BOM 제거(서버 응답에 BOM이 붙어도 안전)
        if (r.decision === 'allow' || r.decision === 'deny') {
          process.stdout.write(JSON.stringify({
            hookSpecificOutput: {
              hookEventName: 'PreToolUse',
              permissionDecision: r.decision,
              permissionDecisionReason: 'Agent Hub 원격 응답'
            }
          }));
        }
        // 그 외(ask/무응답) → 출력 없음 = 기존 권한 흐름(PC 터미널)으로 넘어감.
      } catch (e) {}
      process.exit(0);
    });
    setTimeout(() => process.exit(0), 119000); // 안전망(훅 timeout 120s 이내)
    return;
  }

  if (p.hook_event_name === 'Stop') {
    // 세션이 턴을 끝냄 → '완료/마지막 멘트' 알림(fire-and-forget).
    post(port, '/api/hook/stop', { session_id: p.session_id, cwd: p.cwd }, 3000, () => process.exit(0));
    setTimeout(() => process.exit(0), 4000); // 안전망
    return;
  }

  // Notification: 알림만(fire-and-forget).
  post(port, '/api/hook/notification', {
    session_id: p.session_id, cwd: p.cwd, message: p.message, notification_type: p.notification_type
  }, 3000, () => process.exit(0));
  setTimeout(() => process.exit(0), 4000); // 안전망
});
