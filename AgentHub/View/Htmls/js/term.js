// 터미널 화면: xterm.js ⇄ WebSocket(ConPTY). 두 종류를 다룬다.
//  - 세션 터미널: /ws/session?session=<id> → claude --resume <id> (승인 기기면 항상 허용)
//  - 범용 셸:    /ws/term            → cmd.exe (웹 터미널 토글 ON일 때만)
// app.js의 showScreen/getToken 재사용.
(function () {
  let term, fit, tws, opened = false;

  async function terminalEnabled() {
    try { const s = await (await fetch('/api/terminal/status')).json(); return !!s.enabled; }
    catch (_) { return false; }
  }

  // 모니터 진입 시 범용 셸 버튼 노출 여부 갱신 (app.js에서 호출). 세션 터미널 버튼은 상세에서 상시 노출.
  window.refreshTermButton = async function () {
    const btn = document.getElementById('termBtn');
    if (btn) btn.hidden = !(await terminalEnabled());
  };

  function ensureTerm() {
    if (term) return;
    term = new Terminal({ cursorBlink: true, fontSize: 13, theme: { background: '#0b0f1a' } });
    fit = new FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open(document.getElementById('termView'));
    term.onData(d => send({ t: 'i', d }));
    window.addEventListener('resize', doFit);
  }

  function doFit() {
    if (!term || !fit) return;
    try { fit.fit(); send({ t: 'r', cols: term.cols, rows: term.rows }); } catch (_) {}
  }

  function send(o) { try { tws && tws.readyState === 1 && tws.send(JSON.stringify(o)); } catch (_) {} }

  // 로딩 오버레이: resume는 ready 직후 이력을 PTY 출력으로 재생하므로, 첫 출력 바이트가 올 때까지 표시.
  // 단, resume가 출력을 전혀 안 내보내는 경우(충돌·빈 세션 등) 무한 로딩이 되므로 타임아웃 폴백을 둔다.
  let loadTimer = null;
  function showLoading(on) {
    const el = document.getElementById('termLoading');
    if (el) el.hidden = !on;
    if (loadTimer) { clearTimeout(loadTimer); loadTimer = null; }
    if (on) loadTimer = setTimeout(() => {
      const e2 = document.getElementById('termLoading'); if (e2) e2.hidden = true; loadTimer = null;
    }, 8000);
  }

  const DENY = { disabled: '터미널이 비활성화됨', unauthorized: '권한 없음', nosession: '세션 정보 없음', nocwd: '세션 폴더를 찾을 수 없음' };

  function connect(url, title) {
    ensureTerm();
    if (tws) { try { tws.close(); } catch (_) {} tws = null; }
    term.reset();
    const tt = document.getElementById('termTitle');
    if (tt) tt.textContent = title || '터미널';
    showScreen('terminal');
    history.pushState({ screen: 'terminal' }, '');
    showLoading(true);
    setTimeout(doFit, 60);
    tws = new WebSocket(url);
    tws.binaryType = 'arraybuffer';
    opened = true;
    tws.onmessage = ev => {
      if (typeof ev.data === 'string') {
        let m; try { m = JSON.parse(ev.data); } catch (_) { return; }
        if (m.type === 'ready') doFit();
        else if (m.type === 'denied') { showLoading(false); term.write('\r\n[' + (DENY[m.reason] || '연결 거부') + ']\r\n'); }
        else if (m.type === 'exit') { showLoading(false); term.write('\r\n[세션 종료]\r\n'); }
      } else {
        showLoading(false); // 첫 출력 바이트 = 이력 재생 시작
        term.write(new Uint8Array(ev.data));
      }
    };
    tws.onclose = () => { showLoading(false); /* 유지: 사용자가 뒤로가기로 정리 */ };
  }

  const wsBase = () => (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host;

  // 범용 셸 (웹 터미널 토글)
  window.openTerminal = function () {
    connect(wsBase() + '/ws/term?token=' + encodeURIComponent(getToken()), '터미널');
  };

  // 세션 터미널 (claude --resume attach)
  window.openSessionTerminal = function (sessionId, title) {
    if (!sessionId) return;
    connect(wsBase() + '/ws/session?token=' + encodeURIComponent(getToken())
      + '&session=' + encodeURIComponent(sessionId), title || 'claude');
  };

  function closeTerminal() {
    try { tws && tws.close(); } catch (_) {}
    tws = null; opened = false;
  }

  document.getElementById('termBtn') && document.getElementById('termBtn').addEventListener('click', () => window.openTerminal());
  document.getElementById('termBack') && document.getElementById('termBack').addEventListener('click', () => { if (opened) history.back(); });

  // 특수키 버튼 → PTY로 해당 시퀀스 전송 (모바일 키보드로 못 넣는 Esc/Tab/방향키/Ctrl 등)
  const keys = document.getElementById('termKeys');
  keys && keys.addEventListener('click', e => {
    const btn = e.target.closest('.key-btn');
    if (!btn) return;
    send({ t: 'i', d: btn.getAttribute('data-seq') });
  });

  // 뒤로가기(popstate)로 터미널을 벗어나면 정리 후 목록으로
  window.addEventListener('popstate', () => {
    if (opened) { closeTerminal(); showScreen('monitor'); }
  });
})();
