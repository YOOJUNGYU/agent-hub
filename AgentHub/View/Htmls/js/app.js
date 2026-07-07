// 모바일 모니터 — 기기 인증(토큰) + WebSocket(/ws/agents) 실시간 + 언어(i18n)
const $ = (s, r = document) => r.querySelector(s);
const t = (k, v) => window.I18n.t(k, v);

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
  ['authRequest', 'authPending', 'monitor', 'detail'].forEach(id => {
    $('#' + id).hidden = (id !== name);
  });
}

// ---- 렌더 ----
let wsConnected = false;

function setBadge(on) {
  wsConnected = on;
  const b = $('#wsBadge');
  b.textContent = on ? t('ws.connected') : t('ws.disconnected');
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
  hint.textContent = t('auth.sending');
  try {
    const res = await (await fetch('/api/devices/request', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Device-Token': token },
      body: JSON.stringify({ name })
    })).json();
    if (res.ok) { applyAuth(res.status); hint.textContent = ''; }
    else hint.textContent = t('auth.reqFail') + (res.message || t('auth.reqErr'));
  } catch (e) {
    hint.textContent = t('auth.reqFail') + e.message;
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
      else if (m.type === 'sessions') { renderSessions(m.sessions); if (currentSessionId === null) showScreen('monitor'); }
      else if (m.type === 'activity') { renderActivity(m.sessionId, m.events); }
    } catch (e) { /* ignore malformed */ }
  };
}

// ---- 세션 리스트 / 상세 활동 피드 ----
let currentSessionId = null;
let sessionsById = {};

function renderSessions(sessions) {
  const list = $('#sessionList');
  const sum = $('#summary');
  if (!sessions || sessions.length === 0) {
    list.innerHTML = '<div class="empty" data-i18n="monitor.empty">최근 활동한 세션이 없습니다.</div>';
    sum.textContent = '';
    if (window.I18n) I18n.apply();
    return;
  }
  sessionsById = {}; (sessions || []).forEach(s => { sessionsById[s.id] = s; });
  const active = sessions.filter(s => s.status === 'active').length;
  sum.textContent = t('summary.count') + ': ' + sessions.length + ' · active ' + active;
  list.innerHTML = sessions.map(cardHtml).join('');
  list.querySelectorAll('.session-card').forEach(el =>
    el.addEventListener('click', () => openDetail(el.getAttribute('data-id'))));
}

function cardHtml(s) {
  const badge = '<span class="badge-status ' + esc(s.status) + '">' + esc(s.status) + '</span>';
  return '<div class="session-card" data-id="' + esc(s.id) + '">'
    + '<div class="card-top">' + badge + '<span class="card-title">' + esc(s.title) + '</span></div>'
    + '<div class="card-meta">' + esc(s.project || '') + (s.gitBranch ? ' · ' + esc(s.gitBranch) : '') + '</div>'
    + '<div class="card-task">' + esc(s.currentTask || '') + '</div>'
    + '<div class="card-time">' + rel(s.lastActivityAt) + '</div>'
    + '</div>';
}

function openDetail(id) {
  currentSessionId = id;
  document.getElementById('detailTitle').textContent = (sessionsById[id] && sessionsById[id].title) || '';
  $('#activityFeed').innerHTML =
    '<div class="loading"><span class="spinner"></span></div>';
  showScreen('detail');
  send({ type: 'watch', sessionId: id });
}

function renderActivity(sessionId, events) {
  if (sessionId !== currentSessionId) return;
  const feed = $('#activityFeed');
  if (!events || events.length === 0) { feed.innerHTML = '<div class="empty">—</div>'; return; }
  feed.innerHTML = events.map(evHtml).join('');
  feed.scrollTop = feed.scrollHeight;
}

function evHtml(e) {
  const icon = ({message:'💬', thinking:'💭', tool_use:'🔧', tool_result:'↩︎', user_prompt:'🧑', mode_change:'⚙︎'})[e.kind] || '•';
  const body = e.text && e.kind !== 'thinking'
    ? '<div class="ev-text">' + esc(e.text) + '</div>' : '';
  return '<div class="ev ev-' + esc(e.kind) + '">'
    + '<div class="ev-head"><span class="ev-icon">' + icon + '</span>'
    + '<span class="ev-summary">' + esc(e.summary || e.toolName || e.kind) + '</span></div>'
    + body + '</div>';
}

function send(obj) { try { ws && ws.readyState === 1 && ws.send(JSON.stringify(obj)); } catch (_) {} }
function esc(s) { return (s == null ? '' : String(s)).replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }
function rel(iso) {
  if (!iso) return '';
  const d = new Date(iso); const s = Math.floor((Date.now() - d.getTime())/1000);
  if (s < 60) return s + 's'; if (s < 3600) return Math.floor(s/60) + 'm';
  if (s < 86400) return Math.floor(s/3600) + 'h'; return Math.floor(s/86400) + 'd';
}

// ---- 언어 변경 시 동적 콘텐츠 재렌더 ----
document.addEventListener('i18n:changed', () => {
  setBadge(wsConnected);
});

document.getElementById('backBtn').addEventListener('click', () => {
  send({ type: 'unwatch' }); currentSessionId = null; showScreen('monitor');
});

showScreen('authPending'); // 최초: WS 응답 전까지 대기 표시
connect();

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => {});
}
