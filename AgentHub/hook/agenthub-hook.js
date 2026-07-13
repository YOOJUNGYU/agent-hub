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

// PermissionRequest(AskUserQuestion) 전용: deadline까지 서버 접속을 폴링하며 답변을 대기한다.
// 서버 미기동/연결거부/연결끊김(서버 재시작)은 deadline 전이면 재시도한다(앱을 창 안에 켜면 붙는다).
function awaitElicit(p) {
  const windowSec = Number(process.argv[2]) || 600;
  const budgetMs = (windowSec - 5) * 1000;     // Claude 훅 timeout보다 짧게(먼저 스스로 종료)
  const deadline = Date.now() + budgetMs;
  const safety = setTimeout(() => process.exit(0), budgetMs); // 절대 안전망

  function finish(data) {
    clearTimeout(safety);
    try {
      const r = JSON.parse((data || '{}').replace(/^\uFEFF/, '')); // 선행 BOM 제거
      if (r.updatedInput) {
        process.stdout.write(JSON.stringify({
          hookSpecificOutput: {
            hookEventName: 'PermissionRequest',
            decision: { behavior: 'allow', updatedInput: r.updatedInput }
          }
        }));
      }
      // updatedInput 없음(무응답/타임아웃) → 출력 없음 = 기존 흐름(PC 프롬프트)으로 폴백.
    } catch (e) {}
    process.exit(0);
  }

  function attempt() {
    const now = Date.now();
    if (now >= deadline) { finish(null); return; }
    const port = readPort();                    // 매 시도 재읽기(앱이 켜지며 기록/변경될 수 있음)
    if (!port) { setTimeout(attempt, 700); return; }
    const remaining = deadline - now;
    const body = JSON.stringify({
      session_id: p.session_id, cwd: p.cwd, tool_input: p.tool_input, waitMs: remaining
    });
    let settled = false;
    const req = https.request({
      host: '127.0.0.1', port: Number(port), path: '/api/hook/elicit', method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
      rejectUnauthorized: false, timeout: remaining + 2000
    }, res => {
      let data = '';
      res.on('data', d => (data += d));
      res.on('end', () => { if (!settled) { settled = true; finish(data); } });
    });
    req.on('error', e => {
      if (settled) return; settled = true;
      // 접속 불가/연결 끊김(서버 미기동·재시작)은 deadline 전이면 재시도.
      const retryable = e && (e.code === 'ECONNREFUSED' || e.code === 'ECONNRESET'
        || e.code === 'ENOENT' || e.code === 'ECONNABORTED');
      if (retryable && Date.now() < deadline) setTimeout(attempt, 700);
      else finish(null);
    });
    req.on('timeout', () => { try { req.destroy(); } catch (e) {} if (!settled) { settled = true; finish(null); } });
    req.write(body); req.end();
  }
  attempt();
}

let raw = '';
process.stdin.on('data', d => (raw += d));
process.stdin.on('error', () => process.exit(0));
process.stdin.on('end', () => {
  let p;
  try { p = JSON.parse(raw || '{}'); } catch (e) { process.exit(0); }
  // AskUserQuestion 원격 답변은 앱이 꺼져 있어도 창 안에 앱을 켜면 받도록 폴링한다(포트 없어도 진행).
  const isElicit = p.hook_event_name === 'PermissionRequest' && p.tool_name === 'AskUserQuestion';
  const port = readPort();
  if (!port && !isElicit) process.exit(0);

  // 세션↔PID 지도용 보고(모바일이 세션을 가져올 때 원본 프로세스 종료에 사용).
  // process.ppid = 이 훅을 띄운 claude 프로세스 PID.
  if (p.hook_event_name === 'SessionStart') {
    post(port, '/api/hook/session-pid', { session_id: p.session_id, pid: process.ppid }, 2500, () => process.exit(0));
    setTimeout(() => process.exit(0), 3000);
    return;
  }
  if (port) post(port, '/api/hook/session-pid', { session_id: p.session_id, pid: process.ppid }, 2000, () => {}); // fire-and-forget(포트 없으면 생략)

  if (p.hook_event_name === 'PermissionRequest') {
    // AskUserQuestion만 폰으로 넘겨 원격 답변받는다. 그 외 권한요청은 출력 없이 통과.
    if (!isElicit) { process.exit(0); return; }
    awaitElicit(p);
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

  if (p.hook_event_name === 'Stop') {
    // 세션이 응답(턴)을 끝냄 → '작업 완료' 알림(fire-and-forget).
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
