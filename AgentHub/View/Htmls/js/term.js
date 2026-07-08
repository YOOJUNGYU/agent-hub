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

  const DENY = { disabled: '터미널이 비활성화됨', unauthorized: '권한 없음', nosession: '세션 정보 없음', nocwd: '세션 폴더를 찾을 수 없음' };

  function connect(url, title) {
    ensureTerm();
    if (tws) { try { tws.close(); } catch (_) {} tws = null; }
    term.reset();
    const tt = document.getElementById('termTitle');
    if (tt) tt.textContent = title || '터미널';
    showScreen('terminal');
    history.pushState({ screen: 'terminal' }, '');
    setTimeout(doFit, 60);
    tws = new WebSocket(url);
    tws.binaryType = 'arraybuffer';
    opened = true;
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
    tws.onclose = () => { /* 유지: 사용자가 뒤로가기로 정리 */ };
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

  // 하단 입력창 → PTY에 텍스트 + Enter 전송 (모바일에서 xterm 직접 타이핑 대신 편하게 프롬프트/슬래시 명령 입력)
  function sendPrompt() {
    const inp = document.getElementById('termPromptInput');
    if (!inp) return;
    send({ t: 'i', d: inp.value + '\r' });
    inp.value = '';
    if (term) term.focus();
  }
  document.getElementById('termPromptSend') && document.getElementById('termPromptSend').addEventListener('click', sendPrompt);
  document.getElementById('termPromptInput') && document.getElementById('termPromptInput').addEventListener('keydown', e => {
    if (e.key === 'Enter') { e.preventDefault(); sendPrompt(); }
  });

  // 뒤로가기(popstate)로 터미널을 벗어나면 정리 후 목록으로
  window.addEventListener('popstate', () => {
    if (opened) { closeTerminal(); showScreen('monitor'); }
  });
})();
