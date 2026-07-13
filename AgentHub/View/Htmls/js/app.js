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

// 인증서 평문 HTTP 부트스트랩 URL. 자체서명 인증서를 삭제/만료하면 HTTPS로 .crt를 못 받으므로(신뢰 깨짐),
// HTTP로 받도록 안내한다. 포트는 온라인일 때 캐시한 서버 값 우선, 없으면 HTTPS 포트+1로 폴백(서버 기본 규칙).
function certHttpUrl() {
  var p = null;
  try { p = localStorage.getItem('agenthub.certHttpPort'); } catch (e) {}
  if (!p) { var n = Number(location.port); p = n ? String(n + 1) : ''; }
  return 'http://' + location.hostname + (p ? ':' + p : '') + '/cert';
}
// 인증서 URL을 패널·오프라인 화면 요소에 일괄 적용. 포트 캐시(/api/server/status)가 비동기로 갱신되므로 그 후 재호출한다.
function applyCertUrls() {
  var cu = certHttpUrl();
  var pu = document.getElementById('certPanelUrl'); if (pu) pu.textContent = cu;
  var pcu = document.getElementById('pendingCertUrl'); if (pcu) pcu.textContent = cu;
  var dl = document.querySelector('#certPanel .cert-dl'); if (dl) dl.href = cu;
  var pl = document.getElementById('pendingCertLink'); if (pl) pl.href = cu;
}

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
let authReceived = false;        // 서버 auth 상태를 한 번이라도 받았는지(연결 문제와 실제 승인 대기 구분용)
let authApproved = false;        // 현재 승인 상태(푸시 구독은 승인된 기기만)
let pendingMode = 'connecting';  // authPending 화면 모드: 'connecting' | 'offline' | 'pending'
function setPendingState(mode) {
  pendingMode = mode;
  const keys = { connecting: ['pending.connecting', 'pending.connectingDesc'],
                 offline:    ['pending.offline',    'pending.offlineDesc'],
                 pending:    ['pending.title',      'pending.desc'] };
  const [titleKey, descKey] = keys[mode] || keys.pending;
  $('#pendingTitle').textContent = t(titleKey);
  $('#pendingDesc').textContent = t(descKey);
  $('#pendingConn').hidden = (mode !== 'offline');    // 연결 실패 시에만 인증서 재설치 안내 노출
  $('#pendingSpinner').hidden = (mode === 'offline'); // 실패 상태에선 스피너 숨김
  if (mode === 'offline') applyCertUrls(); // 인증서가 깨진 상태 → HTTPS 대신 HTTP(/cert) 주소로 링크·텍스트 갱신(경고 없이 받힘)
}
function applyAuth(status) {
  authReceived = true;
  authApproved = (status === 'approved');
  if (status === 'approved') { showScreen('monitor'); ensurePushSubscribed(); }
  else if (status === 'pending') { setPendingState('pending'); showScreen('authPending'); }
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
let ws, connectTimer = null;
function connect() {
  const url = (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host
    + '/ws/agents?token=' + encodeURIComponent(token);
  ws = new WebSocket(url);
  // 인증서 삭제/서버 다운 시 WSS가 열리지도 닫히지도 않고 CONNECTING에 멈춰 무한 '연결 중'이 될 수 있다
  // (이때 auth 메시지를 못 받아 기기인증·오프라인 안내 어느 화면으로도 못 감). 일정 시간 응답이 없으면
  // 연결을 강제 종료해 오프라인(인증서 재설치 안내) 흐름으로 넘긴다. onclose가 재접속을 예약한다.
  if (connectTimer) clearTimeout(connectTimer);
  connectTimer = setTimeout(() => {
    if (!authReceived) { setBadge(false); setPendingState('offline'); }
    try { ws.close(); } catch (e) { /* noop */ }
  }, 7000);
  ws.onopen = () => { if (connectTimer) { clearTimeout(connectTimer); connectTimer = null; } setBadge(true); if (!authReceived) setPendingState('connecting'); };
  ws.onclose = () => { if (connectTimer) { clearTimeout(connectTimer); connectTimer = null; } setBadge(false); if (!authReceived) setPendingState('offline'); setTimeout(connect, 3000); };
  ws.onerror = () => { try { ws.close(); } catch (e) { /* noop */ } };
  ws.onmessage = ev => {
    try {
      const m = JSON.parse(ev.data);
      if (m.type === 'auth') applyAuth(m.status);
      else if (m.type === 'sessions') {
        renderSessions(m.sessions);
        if (currentSessionId === null && document.getElementById('terminal').hidden) { showScreen('monitor'); refreshNotifyBtn(); }
      }
      else if (m.type === 'activity') { renderActivity(m.sessionId, m.events); }
      else if (m.type === 'ask') { handleAsk(m); }
      else if (m.type === 'done') { handleDone(m); }
      else if (m.type === 'elicit') { handleElicit(m); }
      else if (m.type === 'permission') { handlePermission(m); }
      else if (m.type === 'reply') { handleReply(m); }
      else if (m.type === 'replyClose') { handleReplyClose(m); }
      else if (m.type === 'answerBlocked') { handleAnswerBlocked(m); }
    } catch (e) { /* ignore malformed */ }
  };
}

// ---- 세션 리스트 / 상세 활동 피드 ----
let currentSessionId = null;
let sessionsById = {};
let lastSessions = [];          // 마지막 세션 스냅샷(대기 표시만 바뀔 때 재렌더용)
const awaitingSet = new Set();  // '응답 대기중' 트랜지언트 신호(권한/일반 알림). 질문(AskUserQuestion)은 pendingAsk가 담당.
let firstActivityRender = false; // 상세 진입 직후 첫 렌더는 무조건 최하단

function renderSessions(sessions) {
  lastSessions = sessions || [];
  const list = $('#sessionList');
  const sum = $('#summary');
  if (lastSessions.length === 0) {
    list.innerHTML = '<div class="empty" data-i18n="monitor.empty">최근 활동한 세션이 없습니다.</div>';
    sum.textContent = '';
    if (window.I18n) I18n.apply();
    return;
  }
  sessionsById = {}; lastSessions.forEach(s => { sessionsById[s.id] = s; });
  const active = lastSessions.filter(s => s.status === 'active').length;
  sum.textContent = t('summary.count') + ': ' + lastSessions.length + ' · active ' + active;
  list.innerHTML = lastSessions.map(cardHtml).join('');
  list.querySelectorAll('.session-card').forEach(el =>
    el.addEventListener('click', () => openDetail(el.getAttribute('data-id'))));
  updateDetailRun(); // 스냅샷 갱신 시 상세 헤더의 실행지표(토큰·작업중)도 최신화
}

// 대기 표시만 바뀐 경우(권한/알림 수신) 목록 화면이면 다시 그린다.
function rerenderSessions() {
  if (!document.getElementById('monitor').hidden) renderSessions(lastSessions);
}

// '응답 대기중' 트랜지언트 표시 토글. 질문은 서버 스냅샷의 pendingAsk가 지속 신호로 담당한다.
function setWaiting(id, on) {
  if (!id) return;
  if (on) awaitingSet.add(id); else awaitingSet.delete(id);
  rerenderSessions();
}

function isWaiting(s) { return !!(s && (s.pendingAsk || awaitingSet.has(s.id))); }

// 알림 본문 접두사: 어느 세션인지 (세션 제목)으로 표시. 앱 이름([agent-hub])은 생략(어차피 이 앱 알림).
function titlePrefix(id) {
  const s = sessionsById[id];
  let tt = s && s.title ? String(s.title) : '';
  if (!tt) return '';
  if (tt.length > 40) tt = tt.slice(0, 40) + '…';
  return '(' + tt + ') ';
}

function cardHtml(s) {
  const waiting = isWaiting(s);
  const badge = '<span class="badge-status ' + esc(s.status) + '">' + esc(s.status) + '</span>';
  const waitPill = waiting ? '<span class="card-wait">' + esc(t('card.waiting')) + '</span>' : '';
  return '<div class="session-card' + (waiting ? ' waiting' : '') + '" data-id="' + esc(s.id) + '">'
    + '<div class="card-top">' + badge + '<span class="card-title">' + esc(s.title) + '</span>' + waitPill + '</div>'
    + '<div class="card-meta">' + esc(s.project || '') + (s.gitBranch ? ' · ' + esc(s.gitBranch) : '') + '</div>'
    + '<div class="card-task">' + esc(s.currentTask || '') + '</div>'
    + runHtml(s)
    + '<div class="card-time">' + rel(s.lastActivityAt) + '</div>'
    + '</div>';
}

// ---- 세션 실행 지표: 작업 중 애니메이션 + 회전 상태어 + 세션 경과시간·누적 토큰 ----
const RUN_VERBS = ['Channeling','Whirlpooling','Percolating','Simmering','Conjuring','Noodling','Marinating',
  'Ruminating','Tinkering','Brewing','Cogitating','Composing','Wrangling','Manifesting','Synthesizing',
  'Pondering','Spelunking','Crunching','Finagling','Vibing'];
function pickVerb() { return RUN_VERBS[Math.floor(Math.random() * RUN_VERBS.length)]; }
function fmtDur(ms) {
  if (!(ms > 0)) ms = 0;
  const s = Math.floor(ms / 1000), h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), ss = s % 60;
  if (h) return h + 'h ' + m + 'm';
  if (m) return m + 'm ' + ss + 's';
  return ss + 's';
}
function fmtTok(n) {
  n = n || 0;
  if (n >= 1e6) return (n / 1e6).toFixed(1) + 'M';
  if (n >= 1e3) return (n / 1e3).toFixed(1) + 'k';
  return String(n);
}
// 세션의 실행 지표 HTML. 현재 턴·누적 경과시간과 토큰은 항상, 회전 상태어+펄스는 작업 중일 때만.
function runHtml(s) {
  if (!s) return '';
  const first = s.firstActivityAt ? Date.parse(s.firstActivityAt) : 0;
  if (!first) return '';
  const last = s.lastActivityAt ? Date.parse(s.lastActivityAt) : first;
  const turn = s.turnStartAt ? Date.parse(s.turnStartAt) : first;
  const working = !!s.working;
  const end = working ? Date.now() : last;
  // 라벨 있는 경과시간 span. data-start를 두면 타이머가 작업 중일 때 라이브로 갱신.
  const tSpan = (start, label) => '<span class="run-t" data-start="' + start + '" data-label="' + esc(label) + '">'
    + esc(label) + ' ' + fmtDur(end - start) + '</span>';
  const tok = s.totalTokens ? '<span class="run-tok"> · ' + fmtTok(s.totalTokens) + ' tok</span>' : '';
  const head = working ? '<span class="run-dot"></span><span class="run-verb">' + esc(pickVerb()) + '…</span> ' : '';
  return '<div class="run' + (working ? ' working' : '') + '">'
    + head + tSpan(turn, t('run.turn')) + '<span class="run-sep"> · </span>' + tSpan(first, t('run.total')) + tok
    + '</div>';
}
// 상세 헤더의 실행 지표 갱신(현재 세션 기준).
function updateDetailRun() {
  const el = document.getElementById('detailRun');
  if (!el) return;
  const html = runHtml(sessionsById[currentSessionId]);
  el.innerHTML = html;
  el.hidden = !html;
}

function openDetail(id) {
  awaitingSet.delete(id); // 상세를 여는 순간 트랜지언트 대기 표시는 해제(사용자가 확인함)
  currentSessionId = id;
  firstActivityRender = true;
  document.getElementById('detailTitle').textContent = (sessionsById[id] && sessionsById[id].title) || '';
  $('#activityFeed').innerHTML =
    '<div class="loading"><span class="spinner"></span></div>';
  showScreen('detail');
  updateDetailRun(); // 상세 헤더에 실행지표 표시
  // 히스토리 항목 추가 → 기기 뒤로가기가 앱 종료 대신 popstate로 목록 복귀
  history.pushState({ screen: 'detail', id }, '');
  send({ type: 'watch', sessionId: id });
  scheduleAskExpiredGuidance(id); // 라이브 elicit이 재전송되면 취소, 아니면(창 경과) 안내 표시
}

// 상세 → 목록 복귀 (기기 back / 화면 버튼 공통 경로)
function backToList() {
  send({ type: 'unwatch' });
  closeAskExpired(); // 목록으로 나가면 만료 안내도 닫는다
  if (replyState) { send({ type: 'replyDismiss', id: replyState.id }); closeReplyOverlay(); }
  currentSessionId = null;
  showScreen('monitor');
  rerenderSessions(); // openDetail에서 해제한 대기표시를 목록에 반영(다음 스냅샷 전 최신화)
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
  ensurePushSubscribed();
}
document.getElementById('notifyBtn') && document.getElementById('notifyBtn').addEventListener('click', async () => {
  if (!('Notification' in window)) return;
  try { await Notification.requestPermission(); } catch (_) {}
  refreshNotifyBtn();
});

// ---- Web Push 구독(앱 종료/백그라운드 알림) ----
// 승인 + 알림 권한이 있을 때 1회 구독하고 서버에 등록. 실패는 무시(연결 시 인앱 알림으로 폴백).
let _pushSynced = false;
function urlBase64ToUint8Array(b64) {
  const pad = '='.repeat((4 - b64.length % 4) % 4);
  const s = (b64 + pad).replace(/-/g, '+').replace(/_/g, '/');
  const raw = atob(s);
  const arr = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) arr[i] = raw.charCodeAt(i);
  return arr;
}
function ensurePushSubscribed() {
  if (_pushSynced || !authApproved) return;
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return;
  if (!('Notification' in window) || Notification.permission !== 'granted') return;
  _pushSynced = true;
  (async () => {
    try {
      const reg = await navigator.serviceWorker.ready;
      let sub = await reg.pushManager.getSubscription();
      if (!sub) {
        const r = await (await fetch('/api/push/vapid-key')).json();
        if (!r || !r.key) { _pushSynced = false; return; }
        sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(r.key) });
      }
      const res = await fetch('/api/push/subscribe', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Device-Token': token },
        body: JSON.stringify(sub)
      });
      if (!res || !res.ok) _pushSynced = false; // 서버 저장 실패(401/500 등)는 fetch가 throw 안 하므로 직접 확인 → 재시도 여지
    } catch (e) { _pushSynced = false; /* 다음 기회에 재시도 */ }
  })();
}

// ---- 입력 필요 알림 → 세션 카드 '응답 대기중' 표시(상단 배너 폐지) + 시스템 푸시 ----
function handleAsk(m) {
  if (('Notification' in window) && Notification.permission === 'granted') {
    var title = t('ask.title');
    var opts = { body: titlePrefix(m.sessionId) + (m.message || ''), tag: m.sessionId || 'ask' };
    if (navigator.serviceWorker && navigator.serviceWorker.ready) {
      navigator.serviceWorker.ready
        .then(function (reg) { return reg.showNotification(title, opts); })
        .catch(function () { try { new Notification(title, opts); } catch (e) {} });
    } else {
      try { new Notification(title, opts); } catch (e) {}
    }
  }
  setWaiting(m.sessionId, true); // 카드 색상으로 '응답 대기중' 표시
}

// ---- 작업 완료 알림(매 턴 종료) → 시스템 알림만. 대기 카드로 표시하지 않음(정보성). ----
function handleDone(m) {
  if (!('Notification' in window) || Notification.permission !== 'granted') return;
  var title = t('done.title');
  // 고정 문구라 서버 메시지 대신 클라이언트 로컬라이즈 텍스트 사용(인앱). 세션당 tag로 알림 누적 방지.
  var opts = { body: titlePrefix(m.sessionId) + t('done.body'), tag: 'done-' + (m.sessionId || '') };
  if (navigator.serviceWorker && navigator.serviceWorker.ready) {
    navigator.serviceWorker.ready
      .then(function (reg) { return reg.showNotification(title, opts); })
      .catch(function () { try { new Notification(title, opts); } catch (e) {} });
  } else {
    try { new Notification(title, opts); } catch (e) {}
  }
}

// ---- elicit 오버레이(AskUserQuestion 질문+답변 선택) ----
// 서버 PermissionRequest 훅이 질문을 push하면 옵션을 골라 바로 답변한다(터미널 불필요).
let elicit = null; // { id, questions:[{header,question,multiSelect,options:[{label,description}]}], step, answers:{} }
const ELICIT_OTHER = '__other__';

function handleElicit(m) {
  const qs = Array.isArray(m.questions) ? m.questions.filter(q => q && q.question) : [];
  if (qs.length === 0) return;
  clearAskExpiredTimer(); closeAskExpired(); // 라이브 답변 화면이 도착 → '만료 안내'를 대체
  elicit = { id: m.id, sessionId: m.sessionId, questions: qs, step: 0, answers: {} };
  // resent=서버가 세션 재오픈(watch) 시 다시 내려준 것 → 시스템 알림은 생략(중복 방지), 화면만 다시 띄운다.
  if (!m.resent && ('Notification' in window) && Notification.permission === 'granted') {
    const opts = { body: titlePrefix(m.sessionId) + qs[0].question, tag: 'elicit-' + m.id, requireInteraction: true };
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
  if (elicit) setWaiting(elicit.sessionId, false); // 취소 시 트랜지언트 해제(미답 상태면 pendingAsk가 계속 표시)
  document.getElementById('elicit').hidden = true;
  elicit = null;
}

// 서버가 답변 전달을 차단(clawd-on-desk 동시 실행 등)했을 때: 안내 후, 답 오버레이가 있으면 다시 열어 재시도 유도.
function handleAnswerBlocked(m) {
  alert(t('answer.blockedClawd'));
  if (elicit) { document.getElementById('elicit').hidden = false; renderElicitStep(); }
  else if (replyState) { document.getElementById('reply').hidden = false; }
}

// ---- 원격 답변 창이 지난 질문 안내 ----
// 대기중 세션에 들어갔을 때 라이브 elicit(재전송)이 오면 답변 화면이 뜨고, 안 오면(창 경과·미등록)
// 트랜스크립트의 질문을 읽기 전용으로 보여주며 "세션 터미널로 답하거나 PC에서 답하라"고 안내한다.
let askExpiredTimer = null, askExpiredSession = null;
function clearAskExpiredTimer() { if (askExpiredTimer) { clearTimeout(askExpiredTimer); askExpiredTimer = null; } }
function scheduleAskExpiredGuidance(id) {
  clearAskExpiredTimer();
  const s = sessionsById[id];
  if (!s || !s.pendingAsk) return; // 미답 질문(AskUserQuestion)이 있을 때만
  askExpiredTimer = setTimeout(() => { askExpiredTimer = null; showAskExpired(id); }, 1200);
}
function showAskExpired(id) {
  const s = sessionsById[id];
  if (!s || !s.pendingAsk || currentSessionId !== id || elicit) return; // 화면 이동/라이브 답변중이면 취소
  const pa = s.pendingAsk;
  document.getElementById('askExpiredHeader').textContent = pa.header || '';
  document.getElementById('askExpiredQuestion').textContent = pa.question || '';
  const opts = Array.isArray(pa.options) ? pa.options : [];
  document.getElementById('askExpiredOptions').innerHTML = opts.map(o =>
    '<div class="elicit-opt" style="cursor:default"><span class="elicit-opt-body"><span class="elicit-opt-label">'
    + esc(o) + '</span></span></div>').join('');
  askExpiredSession = id;
  document.getElementById('askExpired').hidden = false;
  if (window.I18n) I18n.apply();
}
function closeAskExpired() {
  clearAskExpiredTimer();
  const el = document.getElementById('askExpired'); if (el) el.hidden = true;
  askExpiredSession = null;
}
document.getElementById('askExpiredClose') && document.getElementById('askExpiredClose').addEventListener('click', closeAskExpired);
document.getElementById('askExpiredOpenTerm') && document.getElementById('askExpiredOpenTerm').addEventListener('click', () => {
  const id = askExpiredSession;
  if (!id || !window.openSessionTerminal) return;
  if (!confirm(t('term.confirmOpen'))) return;
  closeAskExpired();
  window.openSessionTerminal(id, sessionsById[id] && sessionsById[id].title);
});

document.getElementById('elicitNext') && document.getElementById('elicitNext').addEventListener('click', () => {
  if (!elicit) return;
  const ans = collectElicitAnswer();
  if (ans == null) return; // 최소 하나는 선택
  const q = elicit.questions[elicit.step];
  elicit.answers[q.question] = ans;
  if (elicit.step < elicit.questions.length - 1) { elicit.step++; renderElicitStep(); return; }
  send({ type: 'elicitAnswer', id: elicit.id, answers: elicit.answers });
  setWaiting(elicit.sessionId, false); // 답변 전송 → 트랜지언트 대기표시 해제(미처리분은 서버 pendingAsk가 유지)
  // 오버레이만 감추고 elicit은 유지 — clawd 실행 등으로 차단 회신(answerBlocked)이 오면 다시 열어 재시도.
  document.getElementById('elicit').hidden = true;
});
document.getElementById('elicitBack') && document.getElementById('elicitBack').addEventListener('click', () => {
  if (!elicit) return;
  if (elicit.step === 0) { closeElicit(); return; } // 취소(무응답 → 서버 타임아웃 후 PC 프롬프트로 폴백)
  elicit.step--; renderElicitStep();
});

// ---- 권한 요청(PreToolUse) 원격 승인 ----
let currentPermId = null;
let currentPermSession = null;
function handlePermission(m) {
  currentPermId = m.id;
  currentPermSession = m.sessionId || null;
  setWaiting(m.sessionId, true);
  if (('Notification' in window) && Notification.permission === 'granted') {
    var title = t('perm.title');
    var opts = { body: titlePrefix(m.sessionId) + (m.detail || m.tool || ''), tag: 'perm-' + m.id, requireInteraction: true };
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
  setWaiting(currentPermSession, false);
  currentPermId = null; currentPermSession = null;
  document.getElementById('permBanner').hidden = true;
}
document.getElementById('permAllow') && document.getElementById('permAllow').addEventListener('click', () => sendPermission('allow'));
document.getElementById('permDeny') && document.getElementById('permDeny').addEventListener('click', () => sendPermission('deny'));

// ---- 답장(턴 종료 후 자유 텍스트로 세션 이어가기) ----
let replyState = null; // { id, sessionId }
function handleReply(m) {
  if (m.sessionId !== currentSessionId) return; // 지금 보고 있는 세션만(교차-세션 팝업 방지)
  replyState = { id: m.id, sessionId: m.sessionId };
  document.getElementById('replyMessage').textContent = m.message || '';
  const ta = document.getElementById('replyText'); if (ta) ta.value = '';
  // resent(재접속 재전송)면 시스템 알림 생략(중복 방지).
  if (!m.resent && ('Notification' in window) && Notification.permission === 'granted') {
    const opts = { body: titlePrefix(m.sessionId) + (m.message || ''), tag: 'reply-' + m.id, requireInteraction: true };
    if (navigator.serviceWorker && navigator.serviceWorker.ready)
      navigator.serviceWorker.ready.then(r => r.showNotification(t('reply.title'), opts)).catch(() => { try { new Notification(t('reply.title'), opts); } catch (e) {} });
    else try { new Notification(t('reply.title'), opts); } catch (e) {}
  }
  document.getElementById('reply').hidden = false;
  if (window.I18n) I18n.apply();
}
function closeReplyOverlay() {
  document.getElementById('reply').hidden = true;
  replyState = null;
}
function handleReplyClose(m) {
  if (replyState && m.sessionId === replyState.sessionId) closeReplyOverlay();
}
document.getElementById('replySend') && document.getElementById('replySend').addEventListener('click', () => {
  if (!replyState) return;
  const ta = document.getElementById('replyText');
  const text = ta ? ta.value.trim() : '';
  if (!text) return; // 빈 답장은 전송하지 않음
  send({ type: 'reply', id: replyState.id, text });
  // 오버레이만 감추고 replyState는 유지 — clawd 실행 등으로 answerBlocked가 오면 다시 열어 재시도(elicit와 동일).
  document.getElementById('reply').hidden = true;
});
document.getElementById('replyDismiss') && document.getElementById('replyDismiss').addEventListener('click', () => {
  if (!replyState) return;
  send({ type: 'replyDismiss', id: replyState.id });
  closeReplyOverlay();
});

// ---- 언어 변경 시 동적 콘텐츠 재렌더 ----
document.addEventListener('i18n:changed', () => {
  setBadge(wsConnected);
  setPendingState(pendingMode); // 언어 변경 시 I18n.apply가 기본 문구로 되돌리므로 현재 모드로 재적용
});

// 화면 "← 목록" 버튼: 히스토리를 되돌려(popstate) 기기 back과 동일 경로로 처리
document.getElementById('backBtn').addEventListener('click', () => {
  if (currentSessionId !== null) history.back();
});

// 기기 뒤로가기(popstate): 상세 화면이면 목록으로 복귀(앱 종료 방지)
window.addEventListener('popstate', () => {
  if (currentSessionId !== null) backToList();
});

setPendingState('connecting'); // 최초: WS 응답 전까지 '연결 확인 중'(승인 대기로 오인 방지)
showScreen('authPending');
connect();
refreshNotifyBtn();

// 온라인일 때(인증서 정상) 인증서 HTTP 부트스트랩 포트를 캐시 — 이후 인증서가 깨져도 복구 주소를 정확히 안내.
try {
  fetch('/api/server/status').then(function (r) { return r.json(); }).then(function (s) {
    if (s && s.certHttpPort) {
      try { localStorage.setItem('agenthub.certHttpPort', String(s.certHttpPort)); } catch (e) {}
      applyCertUrls(); // 실제 포트 확보 후 인증서 링크/주소 재적용(폴백 +1이 틀렸던 경우 보정)
    }
  }).catch(function () {});
} catch (e) {}

// 작업 중 세션의 실행지표 라이브 갱신: 턴·누적 경과시간 1초 틱 + 상태어 회전(전체 재렌더 없이 요소만 갱신).
setInterval(function () {
  var now = Date.now();
  document.querySelectorAll('.run.working .run-t').forEach(function (el) {
    var start = Number(el.getAttribute('data-start')) || 0;
    var label = el.getAttribute('data-label') || '';
    if (start) el.textContent = label + ' ' + fmtDur(now - start);
  });
}, 1000);
setInterval(function () {
  document.querySelectorAll('.run.working .run-verb').forEach(function (v) { v.textContent = pickVerb() + '…'; });
}, 2500);

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

  // 인증서 메뉴 토글(헤더 드롭다운) — PWA로 실행 중이어도 항상 유지한다.
  // (인증서는 삭제·만료될 수 있고, 그때 재설치 경로가 없으면 앱이 영영 연결 불가. 링크는 시스템 브라우저로 넘겨 경고 통과·설치.)
  if (certBtn && certPanel) {
    applyCertUrls(); // 패널 다운로드 링크·주소를 HTTP(/cert)로 설정(인증서 삭제/미설치 상태에서도 경고 없이 동작)
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

  // 설치 유도는 브라우저에서만(이미 설치된 앱에서는 설치 버튼 숨김).
  if (isStandalone) { if (installBtn) installBtn.hidden = true; return; }

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
    if (installBtn) installBtn.hidden = true; // 인증서 버튼은 유지(추후 재설치 대비)
  });
  // iOS Safari는 beforeinstallprompt가 없으므로 설치 버튼을 노출해 A2HS 안내로 유도
  if (isIOS && installBtn) installBtn.hidden = false;
})();
