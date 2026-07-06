// 모바일 Claude 에이전트 모니터 — WebSocket(/ws/agents) 실시간
const $ = (s, r = document) => r.querySelector(s);

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

let ws;
function connect() {
  const url = (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host + '/ws/agents';
  ws = new WebSocket(url);
  ws.onopen = () => setBadge(true);
  ws.onclose = () => { setBadge(false); setTimeout(connect, 3000); };
  ws.onerror = () => { try { ws.close(); } catch (e) { /* noop */ } };
  ws.onmessage = ev => {
    try {
      const m = JSON.parse(ev.data);
      if (m.type === 'agents') render(m.agents);
    } catch (e) { /* ignore malformed */ }
  };
}

// 초기 로드(폴백) — WebSocket 연결 시 스냅샷을 다시 받는다.
fetch('/api/agents').then(r => r.json()).then(d => render(d.agents)).catch(() => {});
connect();

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => {});
}
