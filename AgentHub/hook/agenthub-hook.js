// Agent Hub 알림 훅: Claude Code Notification 이벤트를 로컬 Agent Hub 서버로 전달한다.
// 외부로 나가지 않는다(오직 127.0.0.1 loopback). async fire-and-forget.
const fs = require('fs');
const path = require('path');
const https = require('https');

let raw = '';
process.stdin.on('data', d => (raw += d));
process.stdin.on('error', () => process.exit(0));
process.stdin.on('end', () => {
  let p;
  try { p = JSON.parse(raw || '{}'); } catch (e) { process.exit(0); }
  let port;
  try { port = fs.readFileSync(path.join(__dirname, 'endpoint.txt'), 'utf8').trim(); } catch (e) { process.exit(0); }
  if (!port) process.exit(0);

  const body = JSON.stringify({
    session_id: p.session_id,
    cwd: p.cwd,
    message: p.message,
    notification_type: p.notification_type
  });
  const req = https.request({
    host: '127.0.0.1', port: Number(port), path: '/api/hook/notification', method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
    rejectUnauthorized: false, timeout: 3000
  }, res => { res.on('data', () => {}); res.on('end', () => process.exit(0)); });
  req.on('error', () => process.exit(0));
  req.on('timeout', () => { try { req.destroy(); } catch (e) {} process.exit(0); });
  req.write(body); req.end();
});
setTimeout(() => process.exit(0), 4000); // 안전망
