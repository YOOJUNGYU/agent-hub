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
  ['authRequest', 'authPending', 'monitor', 'detail', 'terminal'].forEach(id => {
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
      else if (m.type === 'sessions') {
        renderSessions(m.sessions);
        if (currentSessionId === null && document.getElementById('terminal').hidden) { showScreen('monitor'); if (window.refreshTermButton) window.refreshTermButton(); refreshNotifyBtn(); }
      }
      else if (m.type === 'activity') { renderActivity(m.sessionId, m.events); }
      else if (m.type === 'ask') { handleAsk(m); }
      else if (m.type === 'elicit') { handleElicit(m); }
      else if (m.type === 'permission') { handlePermission(m); }
    } catch (e) { /* ignore malformed */ }
  };
}

// ---- 세션 리스트 / 상세 활동 피드 ----
let currentSessionId = null;
let sessionsById = {};
let firstActivityRender = false; // 상세 진입 직후 첫 렌더는 무조건 최하단

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
  firstActivityRender = true;
  document.getElementById('detailTitle').textContent = (sessionsById[id] && sessionsById[id].title) || '';
  $('#activityFeed').innerHTML =
    '<div class="loading"><span class="spinner"></span></div>';
  showScreen('detail');
  // 히스토리 항목 추가 → 기기 뒤로가기가 앱 종료 대신 popstate로 목록 복귀
  history.pushState({ screen: 'detail', id }, '');
  send({ type: 'watch', sessionId: id });
}

// 상세 → 목록 복귀 (기기 back / 화면 버튼 공통 경로)
function backToList() {
  send({ type: 'unwatch' });
  currentSessionId = null;
  showScreen('monitor');
}

function renderActivity(sessionId, events) {
  if (sessionId !== currentSessionId) return;
  const feed = $('#activityFeed');
  if (!events || events.length === 0) { feed.innerHTML = '<div class="empty">—</div>'; return; }
  // 재렌더 전 스크롤 상태 캡처: 최하단 근처(40px 이내)를 보고 있었는지.
  // 이벤트는 시간순 append라 위쪽 콘텐츠 오프셋은 유지되므로, 위를 보고 있으면 prevTop 복원으로 위치 유지.
  const atBottom = (feed.scrollHeight - feed.scrollTop - feed.clientHeight) < 40;
  const prevTop = feed.scrollTop;
  feed.innerHTML = events.map(evHtml).join('');
  if (firstActivityRender || atBottom) feed.scrollTop = feed.scrollHeight; // 첫 진입/최하단 → 자동 최하단
  else feed.scrollTop = prevTop;                                          // 과거 보는 중 → 위치 유지
  firstActivityRender = false;
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

// ---- 세션 터미널 열기 (claude --resume attach) ----
document.getElementById('openSessionTermBtn') && document.getElementById('openSessionTermBtn').addEventListener('click', () => {
  if (!currentSessionId || !window.openSessionTerminal) return;
  if (!confirm(t('term.confirmOpen'))) return;
  window.openSessionTerminal(currentSessionId, sessionsById[currentSessionId] && sessionsById[currentSessionId].title);
});

// ---- 알림 권한 ----
function refreshNotifyBtn() {
  const b = document.getElementById('notifyBtn');
  if (!b || !('Notification' in window)) { if (b) b.hidden = true; return; }
  b.hidden = (Notification.permission === 'granted');
}
document.getElementById('notifyBtn') && document.getElementById('notifyBtn').addEventListener('click', async () => {
  if (!('Notification' in window)) return;
  try { await Notification.requestPermission(); } catch (_) {}
  refreshNotifyBtn();
});

// ---- ask 배너(질문 알림) ----
function handleAsk(m) {
  if (('Notification' in window) && Notification.permission === 'granted') {
    var title = t('ask.title');
    var opts = { body: (m.project ? '[' + m.project + '] ' : '') + (m.message || ''), tag: m.sessionId || 'ask' };
    if (navigator.serviceWorker && navigator.serviceWorker.ready) {
      navigator.serviceWorker.ready
        .then(function (reg) { return reg.showNotification(title, opts); })
        .catch(function () { try { new Notification(title, opts); } catch (e) {} });
    } else {
      try { new Notification(title, opts); } catch (e) {}
    }
  }
  const banner = document.getElementById('askBanner');
  document.getElementById('askProject').textContent = m.project || '';
  document.getElementById('askMsg').textContent = m.message || '';
  banner.hidden = false;
}
document.getElementById('askDismiss') && document.getElementById('askDismiss').addEventListener('click', () => {
  document.getElementById('askBanner').hidden = true;
});

// ---- elicit 오버레이(AskUserQuestion 질문+답변 선택) ----
// 서버 PermissionRequest 훅이 질문을 push하면 옵션을 골라 바로 답변한다(터미널 불필요).
let elicit = null; // { id, questions:[{header,question,multiSelect,options:[{label,description}]}], step, answers:{} }
const ELICIT_OTHER = '__other__';

function handleElicit(m) {
  const qs = Array.isArray(m.questions) ? m.questions.filter(q => q && q.question) : [];
  if (qs.length === 0) return;
  elicit = { id: m.id, questions: qs, step: 0, answers: {} };
  document.getElementById('askBanner').hidden = true; // 같은 질문의 알림 배너가 오버레이 뒤에 남지 않도록
  if (('Notification' in window) && Notification.permission === 'granted') {
    const opts = { body: (m.project ? '[' + m.project + '] ' : '') + qs[0].question, tag: 'elicit-' + m.id, requireInteraction: true };
    if (navigator.serviceWorker && navigator.serviceWorker.ready)
      navigator.serviceWorker.ready.then(r => r.showNotification(t('elicit.title'), opts)).catch(() => { try { new Notification(t('elicit.title'), opts); } catch (e) {} });
    else try { new Notification(t('elicit.title'), opts); } catch (e) {}
  }
  renderElicitStep();
  document.getElementById('elicit').hidden = false;
}

function renderElicitStep() {
  if (!elicit) return;
  const q = elicit.questions[elicit.step];
  const total = elicit.questions.length;
  const multi = !!q.multiSelect;
  document.getElementById('elicitProgress').textContent = total > 1 ? (elicit.step + 1) + ' / ' + total : '';
  document.getElementById('elicitHeader').textContent = q.header || (t('elicit.question') + ' ' + (elicit.step + 1));
  document.getElementById('elicitQuestion').textContent = q.question;
  document.getElementById('elicitHint').textContent = multi ? t('elicit.chooseMulti') : t('elicit.chooseOne');
  const prev = elicit.answers[q.question]; // 이전 선택 복원용(라벨 문자열 또는 배열)
  const prevSet = prev == null ? [] : (Array.isArray(prev) ? prev : String(prev).split(', '));
  const opts = Array.isArray(q.options) ? q.options : [];
  const type = multi ? 'checkbox' : 'radio';
  let html = '';
  opts.forEach((op, i) => {
    const label = op && op.label != null ? String(op.label) : '';
    const desc = op && op.description ? '<div class="elicit-opt-desc">' + esc(op.description) + '</div>' : '';
    const checked = prevSet.indexOf(label) >= 0 ? ' checked' : '';
    html += '<label class="elicit-opt"><input type="' + type + '" name="elicitOpt" value="' + i + '"' + checked + '>'
      + '<span class="elicit-opt-body"><span class="elicit-opt-label">' + esc(label) + '</span>' + desc + '</span></label>';
  });
  // "기타" 자유 입력(터미널 UI가 자동 제공하는 Other를 클라이언트에서 주입)
  const otherChecked = prevSet.some(v => opts.every(o => (o && String(o.label)) !== v)) ? ' checked' : '';
  html += '<label class="elicit-opt"><input type="' + type + '" name="elicitOpt" value="' + ELICIT_OTHER + '"' + otherChecked + '>'
    + '<span class="elicit-opt-body"><span class="elicit-opt-label">' + esc(t('elicit.other')) + '</span></span></label>'
    + '<textarea id="elicitOther" class="elicit-other" rows="2" data-i18n-ph="elicit.otherPh"' + (otherChecked ? '' : ' hidden') + '></textarea>';
  const box = document.getElementById('elicitOptions');
  box.innerHTML = html;
  if (otherChecked) {
    const custom = prevSet.filter(v => opts.every(o => (o && String(o.label)) !== v))[0] || '';
    const ta = document.getElementById('elicitOther'); if (ta) ta.value = custom;
  }
  box.querySelectorAll('input[name="elicitOpt"]').forEach(inp => inp.addEventListener('change', onElicitOptChange));
  const back = document.getElementById('elicitBack');
  const next = document.getElementById('elicitNext');
  back.textContent = elicit.step === 0 ? t('elicit.cancel') : t('elicit.back');
  next.textContent = elicit.step === total - 1 ? t('elicit.submit') : t('elicit.next');
  if (window.I18n) I18n.apply();
}

function onElicitOptChange() {
  const ta = document.getElementById('elicitOther');
  if (!ta) return;
  const checked = Array.from(document.querySelectorAll('input[name="elicitOpt"]:checked'));
  const otherOn = checked.some(c => c.value === ELICIT_OTHER);
  ta.hidden = !otherOn;
  if (otherOn) ta.focus();
}

function collectElicitAnswer() {
  const q = elicit.questions[elicit.step];
  const opts = Array.isArray(q.options) ? q.options : [];
  const checked = Array.from(document.querySelectorAll('input[name="elicitOpt"]:checked'));
  if (checked.length === 0) return null;
  const labels = [];
  checked.forEach(c => {
    if (c.value === ELICIT_OTHER) {
      const ta = document.getElementById('elicitOther');
      const v = ta && ta.value.trim();
      if (v) labels.push(v);
    } else {
      const op = opts[Number(c.value)];
      if (op && op.label != null) labels.push(String(op.label));
    }
  });
  if (labels.length === 0) return null;
  return q.multiSelect ? labels.join(', ') : labels[0];
}

function closeElicit() {
  document.getElementById('elicit').hidden = true;
  elicit = null;
}

document.getElementById('elicitNext') && document.getElementById('elicitNext').addEventListener('click', () => {
  if (!elicit) return;
  const ans = collectElicitAnswer();
  if (ans == null) return; // 최소 하나는 선택
  const q = elicit.questions[elicit.step];
  elicit.answers[q.question] = ans;
  if (elicit.step < elicit.questions.length - 1) { elicit.step++; renderElicitStep(); return; }
  send({ type: 'elicitAnswer', id: elicit.id, answers: elicit.answers });
  closeElicit();
});
document.getElementById('elicitBack') && document.getElementById('elicitBack').addEventListener('click', () => {
  if (!elicit) return;
  if (elicit.step === 0) { closeElicit(); return; } // 취소(무응답 → 서버 타임아웃 후 PC 프롬프트로 폴백)
  elicit.step--; renderElicitStep();
});

// ---- 권한 요청(PreToolUse) 원격 승인 ----
let currentPermId = null;
function handlePermission(m) {
  currentPermId = m.id;
  if (('Notification' in window) && Notification.permission === 'granted') {
    var title = t('perm.title');
    var opts = { body: (m.project ? '[' + m.project + '] ' : '') + (m.detail || m.tool || ''), tag: 'perm-' + m.id, requireInteraction: true };
    if (navigator.serviceWorker && navigator.serviceWorker.ready)
      navigator.serviceWorker.ready.then(function (r) { return r.showNotification(title, opts); }).catch(function () { try { new Notification(title, opts); } catch (e) {} });
    else try { new Notification(title, opts); } catch (e) {}
  }
  document.getElementById('permProject').textContent = m.project || '';
  document.getElementById('permTool').textContent = m.tool || '';
  document.getElementById('permDetail').textContent = m.detail || '';
  document.getElementById('permBanner').hidden = false;
}
function sendPermission(decision) {
  if (currentPermId) send({ type: 'permissionDecision', id: currentPermId, decision });
  currentPermId = null;
  document.getElementById('permBanner').hidden = true;
}
document.getElementById('permAllow') && document.getElementById('permAllow').addEventListener('click', () => sendPermission('allow'));
document.getElementById('permDeny') && document.getElementById('permDeny').addEventListener('click', () => sendPermission('deny'));

// ---- 언어 변경 시 동적 콘텐츠 재렌더 ----
document.addEventListener('i18n:changed', () => {
  setBadge(wsConnected);
});

// 화면 "← 목록" 버튼: 히스토리를 되돌려(popstate) 기기 back과 동일 경로로 처리
document.getElementById('backBtn').addEventListener('click', () => {
  if (currentSessionId !== null) history.back();
});

// 기기 뒤로가기(popstate): 상세 화면이면 목록으로 복귀(앱 종료 방지)
window.addEventListener('popstate', () => {
  if (currentSessionId !== null) backToList();
});

showScreen('authPending'); // 최초: WS 응답 전까지 대기 표시
connect();
refreshNotifyBtn();

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => {});
  // 새 서비스워커가 제어를 넘겨받으면(=새 버전 활성화) 한 번만 새로고침해 최신 화면 반영.
  var _reloading = false;
  navigator.serviceWorker.addEventListener('controllerchange', function () {
    if (_reloading) return;
    _reloading = true;
    location.reload();
  });
}

// ---- 인증서 메뉴(헤더) + PWA 설치 유도 ----
(function () {
  var isStandalone = (window.matchMedia && matchMedia('(display-mode: standalone)').matches) || window.navigator.standalone === true;
  var isIOS = /iphone|ipad|ipod/i.test(navigator.userAgent);
  var certBtn = document.getElementById('certBtn');
  var certPanel = document.getElementById('certPanel');
  var installBtn = document.getElementById('installBtn');

  // 이미 앱(PWA)으로 설치·실행 중이면 인증서/설치 유도 숨김 (인증서는 이미 신뢰됨)
  if (isStandalone) {
    if (certBtn) certBtn.hidden = true;
    if (installBtn) installBtn.hidden = true;
    if (certPanel) certPanel.hidden = true;
    return;
  }

  // 인증서 메뉴 토글(헤더 드롭다운)
  if (certBtn && certPanel) {
    certBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      var open = certPanel.hidden;
      certPanel.hidden = !open;
      certBtn.setAttribute('aria-expanded', String(open));
    });
    document.addEventListener('click', function (e) {
      if (!certPanel.hidden && !certPanel.contains(e.target) && e.target !== certBtn) {
        certPanel.hidden = true; certBtn.setAttribute('aria-expanded', 'false');
      }
    });
  }

  // PWA 설치 유도: 브라우저로 접속했을 때만
  var deferred = null;
  window.addEventListener('beforeinstallprompt', function (e) {
    e.preventDefault(); deferred = e; if (installBtn) installBtn.hidden = false;
  });
  if (installBtn) installBtn.addEventListener('click', async function () {
    if (deferred) {
      deferred.prompt();
      try { await deferred.userChoice; } catch (_) {}
      deferred = null; installBtn.hidden = true;
    } else if (certPanel) {
      // iOS 등 beforeinstallprompt 미지원 → 안내 노출
      certPanel.hidden = false;
      var h = document.getElementById('iosInstallHint'); if (h) h.hidden = false;
    }
  });
  window.addEventListener('appinstalled', function () {
    if (installBtn) installBtn.hidden = true;
    if (certBtn) certBtn.hidden = true;
    if (certPanel) certPanel.hidden = true;
  });
  // iOS Safari는 beforeinstallprompt가 없으므로 설치 버튼을 노출해 A2HS 안내로 유도
  if (isIOS && installBtn) installBtn.hidden = false;
})();
