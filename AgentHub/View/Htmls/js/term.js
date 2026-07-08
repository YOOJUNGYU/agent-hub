// 웹 터미널 화면: xterm.js ⇄ /ws/term (ConPTY). app.js의 showScreen/getToken/$ 재사용.
(function () {
  let term, fit, tws, opened = false;

  async function terminalEnabled() {
    try { const s = await (await fetch('/api/terminal/status')).json(); return !!s.enabled; }
    catch (_) { return false; }
  }

  // 모니터 진입 시 버튼 노출 여부 갱신 (app.js에서 호출)
  window.refreshTermButton = async function () {
    const btn = document.getElementById('termBtn');
    const newBtn = document.getElementById('newSessionBtn');
    const enabled = await terminalEnabled();
    if (btn) btn.hidden = !enabled;
    if (newBtn) newBtn.hidden = !enabled;
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

  window.openTerminal = function () {
    ensureTerm();
    if (tws) { try { tws.close(); } catch (_) {} tws = null; }
    term.reset();
    showScreen('terminal');
    history.pushState({ screen: 'terminal' }, '');
    setTimeout(doFit, 60);
    const url = (location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host
      + '/ws/term?token=' + encodeURIComponent(getToken());
    tws = new WebSocket(url);
    tws.binaryType = 'arraybuffer';
    opened = true;
    tws.onmessage = ev => {
      if (typeof ev.data === 'string') {
        let m; try { m = JSON.parse(ev.data); } catch (_) { return; }
        if (m.type === 'ready') doFit();
        else if (m.type === 'denied') { term.write('\r\n[' + (m.reason === 'disabled' ? '터미널이 비활성화됨' : '권한 없음') + ']\r\n'); }
        else if (m.type === 'exit') { term.write('\r\n[세션 종료]\r\n'); }
      } else {
        term.write(new Uint8Array(ev.data));
      }
    };
    tws.onclose = () => { /* 유지: 사용자가 뒤로가기로 정리 */ };
  };

  function closeTerminal() {
    try { tws && tws.close(); } catch (_) {}
    tws = null; opened = false;
  }

  document.getElementById('termBtn') && document.getElementById('termBtn').addEventListener('click', () => window.openTerminal());
  document.getElementById('termBack') && document.getElementById('termBack').addEventListener('click', () => { if (opened) history.back(); });

  // 뒤로가기(popstate)로 터미널을 벗어나면 정리 후 목록으로
  window.addEventListener('popstate', () => {
    if (opened) { closeTerminal(); showScreen('monitor'); }
  });
})();
