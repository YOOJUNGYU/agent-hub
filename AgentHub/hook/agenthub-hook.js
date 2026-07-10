// Agent Hub 훅: Claude Code 이벤트를 로컬 Agent Hub 서버(127.0.0.1 loopback)로 전달한다. 외부로 나가지 않는다.
//  - Notification : 알림(fire-and-forget).
//  - PreToolUse   : 위험 도구 권한을 폰에서 원격 승인. 서버 응답({decision})을 받아 permissionDecision을 반환(블로킹).
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

  // 세션↔PID 지도용 보고(모바일이 세션을 가져올 때 원본 프로세스 종료에 사용).
  // process.ppid = 이 훅을 띄운 claude 프로세스 PID.
  if (p.hook_event_name === 'SessionStart') {
    post(port, '/api/hook/session-pid', { session_id: p.session_id, pid: process.ppid }, 2500, () => process.exit(0));
    setTimeout(() => process.exit(0), 3000);
    return;
  }
  post(port, '/api/hook/session-pid', { session_id: p.session_id, pid: process.ppid }, 2000, () => {}); // fire-and-forget

  if (p.hook_event_name === 'PermissionRequest') {
    // AskUserQuestion(질문+답변 목록)만 폰으로 넘겨 원격 답변받는다. 그 외 권한요청은
    // 출력 없이 통과시켜 기존 PreToolUse 권한 흐름을 그대로 둔다.
    if (p.tool_name !== 'AskUserQuestion') { process.exit(0); return; }
    post(port, '/api/hook/elicit', {
      session_id: p.session_id, cwd: p.cwd, tool_input: p.tool_input
    }, 118000, data => {
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
    setTimeout(() => process.exit(0), 119000); // 안전망(훅 timeout 120s 이내)
    return;
  }

  if (p.hook_event_name === 'PreToolUse') {
    // 권한 요청 → 서버가 폰 응답을 기다렸다가 {decision:"allow"|"deny"|"ask"} 반환.
    post(port, '/api/hook/permission', {
      session_id: p.session_id, cwd: p.cwd,
      tool_name: p.tool_name, tool_input: p.tool_input,
      permission_mode: p.permission_mode
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

  // Notification: 알림만(fire-and-forget).
  post(port, '/api/hook/notification', {
    session_id: p.session_id, cwd: p.cwd, message: p.message, notification_type: p.notification_type
  }, 3000, () => process.exit(0));
  setTimeout(() => process.exit(0), 4000); // 안전망
});
