// 호스트 콘솔(/host) — 서버 상태/URL, 접속 기기(/ws/host), 로그(C# push), 포트 설정, 언어
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const t = (k, v) => window.I18n.t(k, v);

const esc = s => (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const fmtTime = iso => { try { return new Date(iso).toLocaleString(); } catch (e) { return iso; } };

// 마지막 데이터(언어 변경 시 재렌더용)
let lastClients = null, lastClientCount = null, lastDevices = null;

const LOG_MAX_ENTRIES = 10000;
const LOG_OVERSCAN = 16;
let logStore = [];
let logRenderQueued = false;
let logStickToBottom = true;
let logResizeObserver = null;

// ---- 탭 ----
$$('.tab').forEach(btn => btn.addEventListener('click', () => {
  $$('.tab').forEach(b => b.classList.remove('active'));
  $$('.view').forEach(v => v.classList.remove('active'));
  btn.classList.add('active');
  $('#' + btn.dataset.view).classList.add('active');
  if (btn.dataset.view === 'logs') scheduleLogRender(logStickToBottom);
}));

// ---- 서버 상태 + 접속 URL ----
async function refreshStatus() {
  try {
    const s = await (await fetch('/api/server/status')).json();
    const badge = $('#statusBadge'), box = $('#serverUrls');
    if (s.active) {
      badge.textContent = t('server.active');
      badge.className = 'badge on';
      // 엔드포인트 목록(LAN + VPN)을 label 배지와 함께 여러 줄로. 옛 서버(필드 없음)는 url 한 줄로 폴백.
      const eps = (s.endpoints && s.endpoints.length) ? s.endpoints
                : (s.url ? [{ url: s.url, kind: 'lan' }] : []);
      box.innerHTML = eps.map(e =>
        `<span class="ep"><a class="server-url" href="${esc(e.url)}" target="_blank" rel="noopener">${esc(e.url)}</a>`
        + `<span class="ep-label ${esc(e.kind)}">${esc(t('endpoint.' + e.kind))}</span></span>`
      ).join('');
    } else {
      badge.textContent = t('server.stopped');
      badge.className = 'badge off';
      box.innerHTML = '';
    }
  } catch (e) { /* ignore */ }
}

// ---- 접속한 모바일 기기 (WebSocket /ws/host) ----
function renderClients(list, count) {
  lastClients = list; lastClientCount = count;
  $('#clientCount').textContent = count != null ? count : (list ? list.length : 0);
  if (!list || !list.length) {
    $('#clientList').innerHTML = `<p class="hint">${t('clients.empty')}</p>`;
    return;
  }
  $('#clientList').innerHTML =
    `<table class="tbl"><thead><tr><th>${t('clients.col.ip')}</th><th>${t('clients.col.device')}</th><th>${t('clients.col.connected')}</th></tr></thead><tbody>` +
    list.map(c => `<tr><td>${esc(c.ip)}</td><td class="ua">${esc(c.userAgent)}</td><td>${fmtTime(c.connectedAt)}</td></tr>`).join('') +
    '</tbody></table>';
}

// ---- 등록 기기 관리 (WebSocket /ws/host: devices) ----
const statusLabel = s => t('status.' + s);

function deviceActions(d) {
  const approve = `<button class="act approve" data-act="approve" data-id="${d.id}">${t('act.approve')}</button>`;
  const revoke  = `<button class="act revoke" data-act="revoke" data-id="${d.id}">${t('act.revoke')}</button>`;
  const del     = `<button class="act delete" data-act="delete" data-id="${d.id}">${t('act.delete')}</button>`;
  if (d.status === 'pending')  return approve + del;
  if (d.status === 'approved') return revoke + del;
  return approve + del; // revoked → 재승인 가능
}

function renderDevices(list) {
  lastDevices = list;
  list = list || [];
  $('#pendingCount').textContent = list.filter(d => d.status === 'pending').length;
  $('#approvedCount').textContent = list.filter(d => d.status === 'approved').length;
  if (!list.length) {
    $('#deviceList').innerHTML = `<p class="hint">${t('devices.empty')}</p>`;
    return;
  }
  $('#deviceList').innerHTML =
    `<table class="tbl"><thead><tr><th>${t('devices.col.name')}</th><th>${t('devices.col.status')}</th><th>${t('devices.col.ip')}</th><th>${t('devices.col.requested')}</th><th>${t('devices.col.actions')}</th></tr></thead><tbody>` +
    list.map(d => `<tr>
      <td>${esc(d.name) || `<span class="hint">${t('devices.noname')}</span>`}<div class="ua">${esc(d.userAgent)}</div></td>
      <td><span class="pill ${d.status}">${statusLabel(d.status)}</span></td>
      <td>${esc(d.ip)}</td>
      <td>${fmtTime(d.requestedAt)}</td>
      <td class="actions">${deviceActions(d)}</td>
    </tr>`).join('') +
    '</tbody></table>';
}

// 액션 버튼 (이벤트 위임)
$('#deviceList').addEventListener('click', async e => {
  const btn = e.target.closest('button.act');
  if (!btn) return;
  const { act, id } = btn.dataset;
  btn.disabled = true;
  try {
    if (act === 'delete') await fetch('/api/devices/' + id, { method: 'DELETE' });
    else await fetch('/api/devices/' + id + '/' + act, { method: 'POST' });
    // 결과는 /ws/host의 devices broadcast로 자동 반영됨
  } catch (err) { btn.disabled = false; }
});

let ws;
function connect() {
  const url = (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host + '/ws/host';
  ws = new WebSocket(url);
  ws.onclose = () => setTimeout(connect, 3000);
  ws.onerror = () => { try { ws.close(); } catch (e) { /* noop */ } };
  ws.onmessage = ev => {
    try {
      const m = JSON.parse(ev.data);
      if (m.type === 'clients') renderClients(m.clients, m.count);
      else if (m.type === 'devices') renderDevices(m.devices);
    } catch (e) { /* ignore */ }
  };
}

// ---- 로그 (FormMain이 window.addLog로 push) ----
function getLogViewport() {
  const el = $('#logList');
  if (!el) return null;
  if (!el._virtualLogReady) {
    el.innerHTML = '<div class="log-virtual-spacer"><div class="log-virtual-window"></div></div>';
    el._virtualLogReady = true;
    el._logSpacer = $('.log-virtual-spacer', el);
    el._logWindow = $('.log-virtual-window', el);
    el.addEventListener('scroll', () => {
      logStickToBottom = isLogAtBottom(el);
      scheduleLogRender(false);
    });
    if ('ResizeObserver' in window) {
      logResizeObserver = new ResizeObserver(() => scheduleLogRender(logStickToBottom));
      logResizeObserver.observe(el);
    } else {
      window.addEventListener('resize', () => scheduleLogRender(logStickToBottom));
    }
  }
  return { el, spacer: el._logSpacer, win: el._logWindow };
}

function getLogRowHeight(el) {
  const style = getComputedStyle(el);
  const lineHeight = parseFloat(style.lineHeight);
  const fontSize = parseFloat(style.fontSize);
  return Math.max(16, Number.isFinite(lineHeight) ? lineHeight : fontSize * 1.45);
}

function isLogAtBottom(el) {
  return el.scrollTop + el.clientHeight >= el.scrollHeight - getLogRowHeight(el) * 2;
}

function scheduleLogRender(scrollToBottom) {
  if (scrollToBottom) logStickToBottom = true;
  if (logRenderQueued) return;
  logRenderQueued = true;
  requestAnimationFrame(renderLogs);
}

function renderLogs() {
  logRenderQueued = false;
  const viewport = getLogViewport();
  if (!viewport) return;

  const { el, spacer, win } = viewport;
  const rowHeight = getLogRowHeight(el);
  const total = logStore.length;
  const totalHeight = total * rowHeight;
  const start = Math.max(0, Math.floor(el.scrollTop / rowHeight) - LOG_OVERSCAN);
  const visibleRows = Math.ceil(el.clientHeight / rowHeight) + LOG_OVERSCAN * 2;
  const end = Math.min(total, start + visibleRows);
  const frag = document.createDocumentFragment();

  spacer.style.height = totalHeight + 'px';
  win.style.transform = `translateY(${start * rowHeight}px)`;

  for (let i = start; i < end; i++) {
    const line = document.createElement('div');
    line.className = 'log-line';
    line.textContent = logStore[i];
    line.title = logStore[i];
    frag.appendChild(line);
  }

  win.replaceChildren(frag);
  if (logStickToBottom) {
    el.scrollTop = el.scrollHeight;
  }
}

window.addLog = function (evt) {
  const viewport = getLogViewport();
  const el = viewport && viewport.el;
  const wasAtBottom = !el || isLogAtBottom(el);
  const msg = typeof evt === 'string' ? evt : (evt && evt.Message) || JSON.stringify(evt);
  logStore.push(`[${new Date().toLocaleTimeString()}] ${msg}`);

  if (logStore.length > LOG_MAX_ENTRIES) {
    const removed = logStore.length - LOG_MAX_ENTRIES;
    logStore.splice(0, removed);
    if (el && !wasAtBottom) {
      el.scrollTop = Math.max(0, el.scrollTop - removed * getLogRowHeight(el));
    }
  }

  scheduleLogRender(wasAtBottom);
};

// ---- 설정(포트) ----
async function loadSettings() {
  try { const s = await (await fetch('/api/settings')).json(); $('#portInput').value = s.port; }
  catch (e) { /* ignore */ }
  try {
    const a = await (await fetch('/api/autostart')).json();
    $('#autoStart').checked = !!a.enabled;
  } catch (e) { /* noop */ }
}
// Windows 시작 시 자동 실행 토글 — 변경 즉시 저장.
$('#autoStart').addEventListener('change', () => {
  fetch('/api/autostart', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled: $('#autoStart').checked })
  }).catch(function () {});
});
$('#settingsForm').addEventListener('submit', async e => {
  e.preventDefault();
  const port = parseInt($('#portInput').value, 10);
  const hint = $('#settingsHint');
  hint.innerHTML = `<span class="spinner"></span>${t('settings.saving')}`;
  try {
    const res = await (await fetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ port })
    })).json();
    if (res.ok) hint.textContent = t('settings.saved', { url: res.url });
    else hint.textContent = t('settings.error') + (res.message || '');
  } catch (err) {
    hint.textContent = t('settings.reqFail') + err.message;
  }
});
// 콘솔에서 사용자가 고른 표시 언어를 서버에 저장 → PWA가 /server/status의 Lang으로 이 값을 따라간다.
function saveConsoleLang() {
  try {
    fetch('/api/server/lang', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ lang: I18n.getLang() })
    }).catch(function () {});
  } catch (e) {}
}

// ---- 언어 변경 시 동적 콘텐츠 재렌더 ----
document.addEventListener('i18n:changed', () => {
  saveConsoleLang();
  refreshStatus();
  if (lastClients !== null) renderClients(lastClients, lastClientCount);
  if (lastDevices !== null) renderDevices(lastDevices);
});

refreshStatus();
setInterval(refreshStatus, 5000);
loadSettings();
connect();
saveConsoleLang(); // 최초 로드 시 현재 콘솔 언어를 서버에 반영
