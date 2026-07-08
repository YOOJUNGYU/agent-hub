// 호스트 콘솔의 '세션 터미널' 탭: /ws/session?session=<id> 에 붙어 폰과 같은 세션 PTY를 공유(실시간 동기화).
// loopback(PC)이라 토큰 없이 접속 허용된다. xterm.js 재사용.
(function () {
  let term, fit, tws, opened = false;
  const $ = s => document.querySelector(s);

  function ensureTerm() {
    if (term) return;
    term = new Terminal({ cursorBlink: true, fontSize: 13, theme: { background: '#0b0f1a' } });
    fit = new FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open($('#stView'));
    term.onData(d => send({ t: 'i', d }));
    window.addEventListener('resize', doFit);
  }

  function doFit() {
    if (!term || !fit) return;
    try { fit.fit(); send({ t: 'r', cols: term.cols, rows: term.rows }); } catch (_) {}
  }

  function send(o) { try { tws && tws.readyState === 1 && tws.send(JSON.stringify(o)); } catch (_) {} }

  async function loadSessions() {
    const sel = $('#stSession');
    if (!sel) return;
    try {
      const data = await (await fetch('/api/sessions')).json();
      const list = (data && data.sessions) || [];
      const cur = sel.value;
      sel.innerHTML = list.map(s =>
        `<option value="${s.id}">${esc(s.title || s.project || s.id)}</option>`).join('');
      if (cur) sel.value = cur;
    } catch (_) {}
  }

  function esc(s) { return (s == null ? '' : String(s)).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

  const DENY = { unauthorized: '권한 없음', nosession: '세션 정보 없음', nocwd: '세션 폴더를 찾을 수 없음' };

  function openTerm() {
    const sel = $('#stSession');
    if (!sel || !sel.value) return;
    ensureTerm();
    if (tws) { try { tws.close(); } catch (_) {} tws = null; }
    term.reset();
    opened = true;
    setTimeout(doFit, 60);
    const url = (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host
      + '/ws/session?session=' + encodeURIComponent(sel.value);
    tws = new WebSocket(url);
    tws.binaryType = 'arraybuffer';
    tws.onmessage = ev => {
      if (typeof ev.data === 'string') {
        let m; try { m = JSON.parse(ev.data); } catch (_) { return; }
        if (m.type === 'ready') doFit();
        else if (m.type === 'denied') term.write('\r\n[' + (DENY[m.reason] || '연결 거부') + ']\r\n');
        else if (m.type === 'exit') term.write('\r\n[세션 종료]\r\n');
      } else {
        term.write(new Uint8Array(ev.data));
      }
    };
  }

  // 탭 진입 시 세션 목록 갱신 + 레이아웃 맞춤
  const tabBtn = document.querySelector('[data-view="sessionterm"]');
  tabBtn && tabBtn.addEventListener('click', () => { loadSessions(); if (opened) setTimeout(doFit, 80); });
  $('#stOpen') && $('#stOpen').addEventListener('click', openTerm);

  // 특수키 버튼 → PTY로 시퀀스 전송
  const keys = $('#stKeys');
  keys && keys.addEventListener('click', e => {
    const b = e.target.closest('.key-btn');
    if (b) send({ t: 'i', d: b.getAttribute('data-seq') });
  });
})();
