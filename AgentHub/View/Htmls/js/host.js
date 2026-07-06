// 호스트 콘솔(/host) — 서버 상태/URL, 접속 기기(/ws/host), 로그(C# push), 포트 설정
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];

const esc = s => (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const fmtTime = iso => { try { return new Date(iso).toLocaleString(); } catch (e) { return iso; } };

// ---- 탭 ----
$$('.tab').forEach(btn => btn.addEventListener('click', () => {
  $$('.tab').forEach(b => b.classList.remove('active'));
  $$('.view').forEach(v => v.classList.remove('active'));
  btn.classList.add('active');
  $('#' + btn.dataset.view).classList.add('active');
}));

// ---- 서버 상태 + 접속 URL ----
async function refreshStatus() {
  try {
    const s = await (await fetch('/api/server/status')).json();
    const badge = $('#statusBadge'), url = $('#serverUrl');
    if (s.active) {
      badge.textContent = '🟢 서버 활성';
      badge.className = 'badge on';
      url.textContent = s.url;
      url.href = s.url;
    } else {
      badge.textContent = '🔴 중지';
      badge.className = 'badge off';
      url.textContent = '';
      url.removeAttribute('href');
    }
  } catch (e) { /* ignore */ }
}

// ---- 접속한 모바일 기기 (WebSocket /ws/host) ----
function renderClients(list, count) {
  $('#clientCount').textContent = count != null ? count : (list ? list.length : 0);
  if (!list || !list.length) {
    $('#clientList').innerHTML = '<p class="hint">아직 접속한 모바일 기기가 없습니다. 접속 URL을 모바일에서 열어보세요.</p>';
    return;
  }
  $('#clientList').innerHTML =
    '<table class="tbl"><thead><tr><th>IP</th><th>기기 (User-Agent)</th><th>접속 시각</th></tr></thead><tbody>' +
    list.map(c => `<tr><td>${esc(c.ip)}</td><td class="ua">${esc(c.userAgent)}</td><td>${fmtTime(c.connectedAt)}</td></tr>`).join('') +
    '</tbody></table>';
}

// ---- 등록 기기 관리 (WebSocket /ws/host: devices) ----
const statusLabel = s => ({ pending: '대기', approved: '승인됨', revoked: '해제됨' }[s] || s);

function deviceActions(d) {
  const approve = `<button class="act approve" data-act="approve" data-id="${d.id}">승인</button>`;
  const revoke  = `<button class="act revoke" data-act="revoke" data-id="${d.id}">해제</button>`;
  const del     = `<button class="act delete" data-act="delete" data-id="${d.id}">삭제</button>`;
  if (d.status === 'pending')  return approve + del;
  if (d.status === 'approved') return revoke + del;
  return approve + del; // revoked → 재승인 가능
}

function renderDevices(list) {
  list = list || [];
  $('#pendingCount').textContent = list.filter(d => d.status === 'pending').length;
  $('#approvedCount').textContent = list.filter(d => d.status === 'approved').length;
  if (!list.length) {
    $('#deviceList').innerHTML = '<p class="hint">아직 인증을 요청한 기기가 없습니다.</p>';
    return;
  }
  $('#deviceList').innerHTML =
    '<table class="tbl"><thead><tr><th>이름</th><th>상태</th><th>IP</th><th>요청 시각</th><th>동작</th></tr></thead><tbody>' +
    list.map(d => `<tr>
      <td>${esc(d.name) || '<span class="hint">(이름 없음)</span>'}<div class="ua">${esc(d.userAgent)}</div></td>
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
window.addLog = function (evt) {
  const el = $('#logList');
  if (!el) return;
  const line = document.createElement('div');
  const msg = typeof evt === 'string' ? evt : (evt && evt.Message) || JSON.stringify(evt);
  line.textContent = `[${new Date().toLocaleTimeString()}] ${msg}`;
  el.appendChild(line);
  el.scrollTop = el.scrollHeight;
  while (el.childNodes.length > 500) el.removeChild(el.firstChild);
};

// ---- 설정(포트) ----
async function loadSettings() {
  try { const s = await (await fetch('/api/settings')).json(); $('#portInput').value = s.port; }
  catch (e) { /* ignore */ }
}
$('#settingsForm').addEventListener('submit', async e => {
  e.preventDefault();
  const port = parseInt($('#portInput').value, 10);
  const hint = $('#settingsHint');
  hint.innerHTML = '<span class="spinner"></span>저장 중…';
  try {
    const res = await (await fetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ port })
    })).json();
    if (res.ok) hint.textContent = `저장됨. 서버가 새 주소(${res.url})로 재시작됩니다…`;
    else hint.textContent = '오류: ' + (res.message || '실패');
  } catch (err) {
    hint.textContent = '요청 실패: ' + err.message;
  }
});

refreshStatus();
setInterval(refreshStatus, 5000);
loadSettings();
connect();
