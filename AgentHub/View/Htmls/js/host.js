// 호스트 콘솔(/host) — 서버 상태/URL, 접속 기기(/ws/host), 로그(C# push), 포트 설정, 언어
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const t = (k, v) => window.I18n.t(k, v);

const esc = s => (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const fmtTime = iso => { try { return new Date(iso).toLocaleString(); } catch (e) { return iso; } };

// 마지막 데이터(언어 변경 시 재렌더용)
let lastClients = null, lastClientCount = null, lastDevices = null;
let lastEndpoints = [];   // QR 모달이 쓰는 접속 주소(LAN·VPN)
let qrEndpointIndex = 0;

const LOG_MAX_ENTRIES = 10000;
const LOG_OVERSCAN = 16;
let logStore = [];
let logRenderQueued = false;
let logStickToBottom = true;
let logResizeObserver = null;

// ---- 좌측 메뉴(LNB) ----
function showView(view) {
  $$('.tab').forEach(b => b.classList.toggle('active', b.dataset.view === view));
  $$('.view').forEach(v => v.classList.toggle('active', v.id === view));
  if (view === 'logs') scheduleLogRender(logStickToBottom);
}
$$('.tab').forEach(btn => btn.addEventListener('click', () => showView(btn.dataset.view)));

// ---- 테마(라이트 기본 / 다크) ----
// localStorage('agenthub.theme')를 오피스·대화창과 공유한다. 같은 출처의 iframe은 storage 이벤트로 즉시 따라 바뀐다.
(function initTheme() {
  const btn = $('#themeBtn');
  const apply = th => {
    document.documentElement.setAttribute('data-theme', th);
    btn.textContent = th === 'dark' ? '🌙' : '☀️';
  };
  apply(document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light');
  btn.addEventListener('click', () => {
    const next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    apply(next);
    try { localStorage.setItem('agenthub.theme', next); } catch (e) { /* 저장 불가 — 이 화면에서만 적용 */ }
  });
})();

// ---- PC / 폰 ----
// 같은 화면(PWA)을 PC와 폰이 함께 쓴다. 기기 승인·로그·설정은 PC(이 컴퓨터, loopback)에서만 —
// 서버도 해당 API·소켓을 loopback으로 막으므로 폰에서는 메뉴만 숨긴다.
const isPc = ['127.0.0.1', 'localhost', '[::1]'].includes(location.hostname);
if (!isPc) {
  document.body.classList.add('remote');
  const bell = $('#bellBtn');
  // 폰 알림(앱이 꺼져 있어도 오는 웹 푸시)은 권한을 받은 뒤 앱(/)이 구독하고 다시 이 화면으로 돌아온다.
  if ('Notification' in window && Notification.permission === 'default') {
    bell.hidden = false;
    bell.addEventListener('click', async () => {
      if ((await Notification.requestPermission()) === 'granted') location.href = '/';
      else bell.hidden = true;
    });
  }
}

// ---- 오피스 연결 상태 → 상단 바 칩 (오피스 iframe이 postMessage로 알린다) ----
window.addEventListener('message', e => {
  if (e.origin !== location.origin || !e.data || e.data.type !== 'agenthub:officeStatus') return;
  const b = $('#officeBadge');
  b.hidden = false;
  b.textContent = (e.data.ok ? '🐾 ' : '⚠ ') + e.data.text;
  b.classList.toggle('on', !!e.data.ok);
  b.classList.toggle('off', !e.data.ok);
});

// ---- 오피스(픽셀 캐릭터) ----
// 트레이 알림 클릭은 /host?open=<세션ID>로 들어온다 → 오피스가 그 세션 대화창을 연다.
(function initOffice() {
  const open = new URLSearchParams(location.search).get('open');
  $('#officeFrame').src = '/office/index.html' + (open ? '?open=' + encodeURIComponent(open) : '');
})();

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
      setQrEndpoints(eps);
    } else {
      badge.textContent = t('server.stopped');
      badge.className = 'badge off';
      box.innerHTML = '';
      setQrEndpoints([]);
    }
  } catch (e) { /* ignore */ }
}

// ---- 휴대폰 연결 QR ----
// 콘솔 자신은 https://127.0.0.1:포트로 열려 있어 location.origin으로 QR을 만들면 폰에서 절대 열리지 않는다.
// 반드시 서버가 알려준 LAN·VPN 주소를 쓴다.
function certUrlOf(endpointUrl) {
  return endpointUrl.replace(/\/+$/, '') + '/api/cert';
}

function setQrEndpoints(eps) {
  lastEndpoints = eps || [];
  if (qrEndpointIndex >= lastEndpoints.length) qrEndpointIndex = 0;
  const btn = $('#qrBtn');
  if (btn) btn.disabled = lastEndpoints.length === 0;
  if (!$('#qrModal').hidden) renderQr();
}

// QR은 항상 흰 바탕·검은 모듈로 그린다(다크 테마에서 반전되면 스캐너가 못 읽는다).
function drawQr(box, text) {
  box.innerHTML = '';
  try {
    const q = qrcode(0, 'M');
    q.addData(text);
    q.make();
    box.innerHTML = q.createSvgTag({ cellSize: 4, margin: 4, scalable: true });
  } catch (e) {
    // 라이브러리 로드 실패 등 → 모달이 빈 화면이 되지 않도록 주소만이라도 남긴다.
    box.textContent = text;
    box.classList.add('qr-fallback');
  }
}

function renderQr() {
  const ep = lastEndpoints[qrEndpointIndex];
  if (!ep) return;

  // 엔드포인트가 하나뿐이면 고를 것이 없으므로 탭을 숨긴다.
  const tabs = $('#qrEndpoints');
  tabs.hidden = lastEndpoints.length < 2;
  tabs.innerHTML = lastEndpoints.map((e, i) =>
    `<button type="button" class="qr-ep${i === qrEndpointIndex ? ' active' : ''}" data-i="${i}">${esc(t('endpoint.' + e.kind))}</button>`
  ).join('');
  $$('.qr-ep', tabs).forEach(b => b.addEventListener('click', () => {
    qrEndpointIndex = Number(b.dataset.i);
    renderQr();
  }));

  const certUrl = certUrlOf(ep.url);
  drawQr($('#qrConnect'), ep.url);
  drawQr($('#qrCert'), certUrl);
  $('#qrConnectUrl').textContent = ep.url;
  $('#qrCertUrl').textContent = certUrl;
}

$('#qrBtn').addEventListener('click', () => {
  if (!lastEndpoints.length) return;
  $('#qrModal').hidden = false;
  renderQr();
});
$('#qrCloseBtn').addEventListener('click', () => { $('#qrModal').hidden = true; });
$('#qrModal').addEventListener('click', e => {   // 바깥 여백 클릭으로 닫기
  if (e.target === $('#qrModal')) $('#qrModal').hidden = true;
});
document.addEventListener('keydown', e => {
  if (e.key === 'Escape' && !$('#qrModal').hidden) $('#qrModal').hidden = true;
});

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
  const pending = list.filter(d => d.status === 'pending').length;
  $('#pendingCount').textContent = pending;
  const lb = $('#lnbPending'); lb.textContent = pending; lb.hidden = pending === 0; // 메뉴에 인증 대기 수
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
      else if (m.type === 'logs') { logStore = (m.lines || []).slice(); scheduleLogRender(true); } // 접속 시 최근 로그(재연결 시 중복 방지로 교체)
      else if (m.type === 'log') window.addLog(m.line);
    } catch (e) { /* ignore */ }
  };
}

// ---- 로그 (서버가 /ws/host로 push: 접속 시 'logs', 이후 'log') ----
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
  // 서버가 보내는 줄은 이미 순번·시각을 담고 있다.
  logStore.push(typeof evt === 'string' ? evt : `[${new Date().toLocaleTimeString()}] ${(evt && evt.Message) || JSON.stringify(evt)}`);

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
if (isPc) { // 기기·로그·설정 데이터는 PC에서만(서버가 loopback으로 제한)
  loadSettings();
  connect();
  saveConsoleLang(); // 최초 로드 시 현재 콘솔 언어를 서버에 반영
}

// 이 화면에서 바로 설치할 수 있도록 서비스워커도 등록한다(PC는 /host로 바로 열린다 — 첫 화면 /를 거치지 않음).
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(function () {});

// ---- 앱(PWA) 설치 유도 팝업 — 브라우저로 열면 먼저 띄운다 ----
// 설치 가능(beforeinstallprompt) → [앱으로 설치]가 바로 설치 창을 연다.
// iOS(설치 API 없음)·그 밖의 브라우저 → 메뉴에서 설치하는 방법을 보여 준다.
// 설치된 앱으로 열었거나 한 번이라도 설치했으면(agenthub.installed), '나중에' 뒤 하루 동안은 띄우지 않는다.
(function () {
  var INSTALLED = 'agenthub.installed', SNOOZE = 'agenthub.installSnoozeUntil', DAY = 24 * 3600 * 1000;
  function get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
  function set(k, v) { try { localStorage.setItem(k, v); } catch (e) {} }
  var standalone = (window.matchMedia && matchMedia('(display-mode: standalone)').matches) || navigator.standalone === true;
  if (standalone) { set(INSTALLED, '1'); return; }
  var modal = document.getElementById('installModal');
  if (!modal || get(INSTALLED) === '1' || Number(get(SNOOZE) || 0) > Date.now()) return;

  var isIOS = /iphone|ipad|ipod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  var steps = document.getElementById('installSteps');
  var go = document.getElementById('installGo');
  var deferred = null, mode = null;

  function show(m) {
    mode = m;
    steps.hidden = m === 'prompt';
    if (m === 'ios') steps.textContent = t('install.iosSteps');
    else if (m === 'manual') steps.textContent = t('install.manualSteps');
    go.textContent = t(m === 'prompt' ? 'install.go' : 'install.ok');
    modal.hidden = false;
    go.focus();
  }
  function close() { modal.hidden = true; }

  window.addEventListener('beforeinstallprompt', function (e) { e.preventDefault(); deferred = e; show('prompt'); });
  window.addEventListener('appinstalled', function () { set(INSTALLED, '1'); close(); });
  // 설치 가능 신호를 잠깐 기다렸다가, 끝내 없으면 방법 안내로 띄운다(늦게 오면 위에서 설치 버튼으로 바뀜).
  setTimeout(function () { if (!mode) show(isIOS ? 'ios' : 'manual'); }, isIOS ? 300 : 1500);

  go.addEventListener('click', async function () {
    if (mode === 'prompt' && deferred) {
      deferred.prompt();
      try { var r = await deferred.userChoice; if (r && r.outcome === 'accepted') set(INSTALLED, '1'); } catch (_) {}
      deferred = null;
    } else {
      set(SNOOZE, String(Date.now() + DAY)); // 방법을 봤으니 하루는 다시 묻지 않음
    }
    close();
  });
  document.getElementById('installLater').addEventListener('click', function () { set(SNOOZE, String(Date.now() + DAY)); close(); });
  modal.addEventListener('keydown', function (e) { if (e.key === 'Escape') { set(SNOOZE, String(Date.now() + DAY)); close(); } });
  document.addEventListener('i18n:changed', function () { if (mode) show(mode); });
})();

// ---- 좁은 화면(모바일): 햄버거로 LNB 서랍 열고 닫기 ----
// 메뉴를 고르거나, 바깥(어두운 영역)을 누르거나, Esc, 넓은 화면으로 바뀌면 닫는다.
(function initNavDrawer() {
  const btn = $('#menuBtn');
  const scrim = $('#navScrim');
  const set = open => {
    document.body.classList.toggle('nav-open', open);
    btn.setAttribute('aria-expanded', String(open));
    scrim.hidden = !open;
  };
  btn.addEventListener('click', () => set(!document.body.classList.contains('nav-open')));
  scrim.addEventListener('click', () => set(false));
  document.addEventListener('keydown', e => { if (e.key === 'Escape') set(false); });
  $$('.lnb .tab').forEach(b => b.addEventListener('click', () => set(false)));
  matchMedia('(min-width: 761px)').addEventListener('change', e => { if (e.matches) set(false); });
})();
