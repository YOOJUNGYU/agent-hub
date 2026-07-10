// 세션 터미널 화면: xterm.js ⇄ WebSocket(/ws/session?session=<id> → claude --resume <id>).
// 세션별로 동작 중인 쉘을 Agent Hub가 가져와 여러 클라이언트(PC·승인된 폰)가 실시간 공유한다.
// app.js의 showScreen/getToken 재사용.
(function () {
  let term, fit, tws, opened = false;

  function ensureTerm() {
    if (term) return;
    term = new Terminal({ cursorBlink: true, fontSize: 13, theme: { background: '#0b0f1a' } });
    fit = new FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open(document.getElementById('termView'));
    term.onData(d => send({ t: 'i', d }));
    window.addEventListener('resize', scheduleFit);
    // 가상 키보드/브라우저 UI로 뷰포트가 바뀌면 다시 맞춘다(하단 특수키 바가 가리지 않게 + 열 폭 재계산).
    if (window.visualViewport) {
      window.visualViewport.addEventListener('resize', scheduleFit);
      window.visualViewport.addEventListener('scroll', scheduleFit);
    }
    // 컨테이너 크기 변화(화면 전환/회전/키보드)에 반응해 xterm을 정확히 맞춘다 — 초기 렌더 깨짐 방지.
    if (window.ResizeObserver) {
      const wrap = document.getElementById('termViewWrap');
      if (wrap) new ResizeObserver(scheduleFit).observe(wrap);
    }
  }

  // 여러 이벤트가 몰릴 때 rAF로 한 번만 fit(서버 resize 메시지 폭주 방지).
  let fitScheduled = false;
  function scheduleFit() {
    if (fitScheduled) return;
    fitScheduled = true;
    requestAnimationFrame(() => { fitScheduled = false; doFit(); });
  }

  function doFit() {
    if (!term || !fit) return;
    // 컨테이너가 아직 0 크기(숨김/레이아웃 전)면 잘못된 1x1 등으로 맞춰 깨지므로 건너뛴다(다음 관측에서 재시도).
    const wrap = document.getElementById('termViewWrap');
    if (wrap && (wrap.clientWidth < 2 || wrap.clientHeight < 2)) return;
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
