// 모바일 모니터 — 기기 인증(토큰) + WebSocket(/ws/agents) 실시간
const $ = (s, r = document) => r.querySelector(s);

// ---- 기기 토큰 ----
const TOKEN_KEY = 'agenthub.deviceToken';
function genUuid() {
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80;
  const h = [...b].map(x => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`;
}
function getToken() {
  let t = localStorage.getItem(TOKEN_KEY);
  if (!t) {
    t = (crypto.randomUUID ? crypto.randomUUID() : genUuid());
    localStorage.setItem(TOKEN_KEY, t);
  }
  return t;
}
const token = getToken();

// ---- 화면 전환 ----
function showScreen(name) {
  ['authRequest', 'authPending', 'monitor'].forEach(id => {
    $('#' + id).hidden = (id !== name);
  });
}

// ---- 렌더 ----
const label = s => ({ working: '작업 중', idle: '대기', error: '오류' }[s] || s);
const esc = s => (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

function render(list) {
  list = list || [];
  const working = list.filter(a => a.status === 'working').length;
  const error = list.filter(a => a.status === 'error').length;
  $('#summary').innerHTML =
    `<div class="stat"><div class="n">${list.length}</div><div class="l">전체 에이전트</div></div>` +
    `<div class="stat"><div class="n">${working}</div><div class="l">작업 중</div></div>` +
    `<div class="stat"><div class="n">${error}</div><div class="l">오류</div></div>`;
  $('#agentGrid').innerHTML = list.map(a => `
    <div class="card">
      <div class="top"><span class="name">${esc(a.name)}</span><span class="pill ${a.status}">${label(a.status)}</span></div>
      <div class="task">${esc(a.currentTask) || '&mdash;'}</div>
      <div class="bar"><i style="width:${a.progress || 0}%"></i></div>
    </div>`).join('');
}

function setBadge(on) {
  const b = $('#wsBadge');
  b.textContent = on ? '🟢 실시간 연결됨' : '🔴 연결 끊김';
  b.className = 'badge ' + (on ? 'on' : 'off');
}

// ---- auth 상태 → 화면 ----
function applyAuth(status) {
  if (status === 'approved') showScreen('monitor');
  else if (status === 'pending') showScreen('authPending');
  else showScreen('authRequest'); // none | revoked
}

// ---- 인증 요청 ----
$('#requestBtn').addEventListener('click', async () => {
  const name = $('#deviceName').value.trim();
  const hint = $('#requestHint');
  hint.textContent = '요청 전송 중…';
  try {
    const res = await (await fetch('/api/devices/request', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Device-Token': token },
      body: JSON.stringify({ name })
    })).json();
    if (res.ok) { applyAuth(res.status); hint.textContent = ''; }
    else hint.textContent = '요청 실패: ' + (res.message || '오류');
  } catch (e) {
    hint.textContent = '요청 실패: ' + e.message;
  }
});

// ---- WebSocket ----
let ws;
function connect() {
  const url = (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host
    + '/ws/agents?token=' + encodeURIComponent(token);
  ws = new WebSocket(url);
  ws.onopen = () => setBadge(true);
  ws.onclose = () => { setBadge(false); setTimeout(connect, 3000); };
  ws.onerror = () => { try { ws.close(); } catch (e) { /* noop */ } };
  ws.onmessage = ev => {
    try {
      const m = JSON.parse(ev.data);
      if (m.type === 'auth') applyAuth(m.status);
      else if (m.type === 'agents') { showScreen('monitor'); render(m.agents); }
    } catch (e) { /* ignore malformed */ }
  };
}

showScreen('authPending'); // 최초: WS 응답 전까지 대기 표시
connect();

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => {});
}
