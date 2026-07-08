// 공용 다국어(i18n) — 콘솔(host.html)·모바일(index.html) 공통.
// 저장: localStorage['agenthub.lang'] = 'ko' | 'en'. 미설정 시 navigator.language로 초기 감지.
// 정적 텍스트: data-i18n / data-i18n-ph(placeholder) / data-i18n-title 속성.
// 동적 텍스트: I18n.t(key[, vars]). 언어 변경 시 document 'i18n:changed' 이벤트 발생.
(function (global) {
  const LANG_KEY = 'agenthub.lang';

  const DICT = {
    ko: {
      // 공통 서버 상태
      'server.active': '🟢 서버 활성',
      'server.stopped': '🔴 중지',
      'server.checking': '확인 중…',
      // 콘솔 탭
      'tab.devices': '기기 관리',
      'tab.clients': '연결된 기기',
      'tab.logs': '로그',
      'tab.settings': '설정',
      // 기기 관리
      'devices.pending': '인증 대기',
      'devices.approved': '승인된 기기',
      'devices.loading': '기기 정보를 불러오는 중…',
      'devices.empty': '아직 인증을 요청한 기기가 없습니다.',
      'devices.col.name': '이름',
      'devices.col.status': '상태',
      'devices.col.ip': 'IP',
      'devices.col.requested': '요청 시각',
      'devices.col.actions': '동작',
      'devices.noname': '(이름 없음)',
      'act.approve': '승인',
      'act.revoke': '해제',
      'act.delete': '삭제',
      'status.pending': '대기',
      'status.approved': '승인됨',
      'status.revoked': '해제됨',
      // 연결된 기기
      'clients.count': '접속한 모바일 기기',
      'clients.loading': '접속 기기 정보를 불러오는 중…',
      'clients.empty': '아직 접속한 모바일 기기가 없습니다. 접속 URL을 모바일에서 열어보세요.',
      'clients.col.ip': 'IP',
      'clients.col.device': '기기 (User-Agent)',
      'clients.col.connected': '접속 시각',
      // 설정
      'settings.general': '일반', 'settings.terminalGroup': '터미널 · 알림', 'settings.lang': '언어 (Language)',
      'settings.portLabel': '서버 포트 (1024–65535)',
      'settings.save': '저장 후 재시작',
      'settings.hint': '포트를 바꾸면 서버가 재시작되고 접속 주소가 변경됩니다.',
      'settings.saving': '저장 중…',
      'settings.saved': '저장됨. 서버가 새 주소({url})로 재시작됩니다…',
      'settings.error': '오류: ',
      'settings.reqFail': '요청 실패: ',
      'settings.termEnable': '웹 터미널 허용(모바일에서 PC 명령 실행)',
      'settings.termWarn': '승인된 기기가 이 PC에서 명령을 실행할 수 있게 됩니다. 필요할 때만 켜세요.',
      'settings.termShell': '기본 셸',
      'settings.termShellHint': '기본은 cmd.exe 입니다. PowerShell을 쓰려면 powershell.exe(Windows PowerShell) 또는 pwsh.exe(PowerShell 7)를 입력하세요. 저장 후 새로 여는 터미널부터 적용됩니다.',
      'settings.termCwd': '시작 폴더 (비우면 사용자 홈)',
      'settings.termSave': '터미널 설정 저장',
      // 모바일 — WebSocket 상태
      'ws.connecting': '연결 중…',
      'ws.connected': '🟢 실시간 연결됨',
      'ws.disconnected': '🔴 연결 끊김',
      // 모바일 — 인증/대기/모니터
      'auth.title': '기기 인증이 필요합니다',
      'auth.desc': '이 기기의 접속을 허용하려면 PC(Agent Hub)에서 승인해야 합니다. 아래에 기기 이름을 입력하고 인증을 요청하세요.',
      'auth.namePh': '기기 이름 (예: 내 아이폰)',
      'auth.requestBtn': '인증 요청 보내기',
      'auth.sending': '요청 전송 중…',
      'auth.reqFail': '요청 실패: ',
      'auth.reqErr': '오류',
      'pending.title': '승인 대기 중…',
      'pending.desc': 'PC(Agent Hub)에서 이 기기를 승인하면 자동으로 모니터 화면이 표시됩니다.',
      'monitor.loading': '세션 정보를 불러오는 중…',
      'monitor.empty': '최근 활동한 세션이 없습니다.',
      'detail.back': '← 목록',
      'term.open': '⌨ 터미널',
      'term.openSession': '⌨ 터미널 열기',
      'term.title': '터미널',
      'term.warn': '이 터미널은 PC에서 실제 명령을 실행합니다. 신뢰하는 경우에만 사용하세요.',
      'notify.on': '🔔 알림 켜기',
      'ask.title': 'Claude가 입력을 기다립니다',
      'ask.answer': '답변하기',
      'ask.dismiss': '닫기',
      'guide.link': '📖 사용법',
      'cert.hdr': '🔒 인증서',
      'pwa.install': '⬇ 앱 설치',
      'pwa.iosHint': 'iOS: 공유 버튼 → “홈 화면에 추가”를 눌러 앱으로 설치하세요.',
      'cert.summary': '📱 인증서 설치 (PWA 설치 · 보안경고 제거)',
      'cert.download': '인증서 다운로드',
      'cert.android': 'Android — 반드시 설정에서 “CA 인증서”로 설치하세요 (다운로드한 파일을 그냥 탭하면 “신뢰할 수 있는 기관…” 오류가 납니다):\n1) 위 “인증서 다운로드” → 다운로드 폴더에 AgentHub.crt 저장\n2) 설정 앱 → 보안(및 개인정보) → 기타 보안 설정 → “기기 저장공간에서 설치”(또는 “인증서 설치”)\n3) “CA 인증서” 선택 (VPN·앱 인증서 아님)\n4) “개인정보가 보호되지 않을 수 있음” 경고 → 계속/설치\n5) 다운로드 폴더의 AgentHub.crt 선택\n(삼성: 설정 → 보안 및 개인정보 → 기타 보안 설정 → 기기 저장공간에서 설치 → CA 인증서)',
      'cert.ios': 'iOS — 반드시 Safari로 진행:\n1) 위 “인증서 다운로드” → “프로파일이 다운로드됨” 안내\n2) 설정 앱 상단 “프로파일 다운로드됨” → 설치 (기기 암호 입력)\n3) 설정 → 일반 → 정보 → 인증서 신뢰 설정에서 AgentHub 신뢰 켜기',
      'cert.note': '설치 후 브라우저를 새로고침하면 보안경고 없이 접속되고, 홈 화면에 추가(PWA 설치)할 수 있습니다.\niOS에서 설치 후에도 접속이 안 되면 인증서 유효기간(10년) 때문일 수 있어요 — 알려주시면 짧게 재발급해 드립니다.',
      'summary.count': '세션',
      'summary.total': '전체 에이전트',
      'summary.working': '작업 중',
      'summary.error': '오류',
      'agent.working': '작업 중',
      'agent.idle': '대기',
      'agent.error': '오류'
    },
    en: {
      'server.active': '🟢 Server active',
      'server.stopped': '🔴 Stopped',
      'server.checking': 'Checking…',
      'tab.devices': 'Devices',
      'tab.clients': 'Connected',
      'tab.logs': 'Logs',
      'tab.settings': 'Settings',
      'devices.pending': 'Pending',
      'devices.approved': 'Approved',
      'devices.loading': 'Loading devices…',
      'devices.empty': 'No device has requested access yet.',
      'devices.col.name': 'Name',
      'devices.col.status': 'Status',
      'devices.col.ip': 'IP',
      'devices.col.requested': 'Requested',
      'devices.col.actions': 'Actions',
      'devices.noname': '(no name)',
      'act.approve': 'Approve',
      'act.revoke': 'Revoke',
      'act.delete': 'Delete',
      'status.pending': 'Pending',
      'status.approved': 'Approved',
      'status.revoked': 'Revoked',
      'clients.count': 'Connected mobile devices',
      'clients.loading': 'Loading connected devices…',
      'clients.empty': 'No mobile device connected yet. Open the access URL on your phone.',
      'clients.col.ip': 'IP',
      'clients.col.device': 'Device (User-Agent)',
      'clients.col.connected': 'Connected',
      'settings.lang': '언어 (Language)',
      'settings.general': 'General', 'settings.terminalGroup': 'Terminal · Alerts', 'settings.portLabel': 'Server port (1024–65535)',
      'settings.save': 'Save & restart',
      'settings.hint': 'Changing the port restarts the server and changes the access URL.',
      'settings.saving': 'Saving…',
      'settings.saved': 'Saved. Server is restarting at {url}…',
      'settings.error': 'Error: ',
      'settings.reqFail': 'Request failed: ',
      'settings.termEnable': 'Allow web terminal (run PC commands from mobile)',
      'settings.termWarn': 'Approved devices will be able to run commands on this PC. Enable only when needed.',
      'settings.termShell': 'Default shell',
      'settings.termShellHint': 'Default is cmd.exe. To use PowerShell, enter powershell.exe (Windows PowerShell) or pwsh.exe (PowerShell 7). Applies to terminals opened after saving.',
      'settings.termCwd': 'Start folder (leave empty to use user home)',
      'settings.termSave': 'Save terminal settings',
      'ws.connecting': 'Connecting…',
      'ws.connected': '🟢 Live',
      'ws.disconnected': '🔴 Disconnected',
      'auth.title': 'Device authorization required',
      'auth.desc': 'To allow this device, approve it on the PC (Agent Hub). Enter a device name below and request authorization.',
      'auth.namePh': 'Device name (e.g. My iPhone)',
      'auth.requestBtn': 'Request authorization',
      'auth.sending': 'Sending request…',
      'auth.reqFail': 'Request failed: ',
      'auth.reqErr': 'error',
      'pending.title': 'Waiting for approval…',
      'pending.desc': 'Once the PC (Agent Hub) approves this device, the monitor will appear automatically.',
      'monitor.loading': 'Loading sessions…',
      'monitor.empty': 'No recently active sessions.',
      'detail.back': '← List',
      'term.open': '⌨ Terminal',
      'term.openSession': '⌨ Open terminal',
      'term.title': 'Terminal',
      'term.warn': 'This terminal runs real commands on the PC. Use only if you trust it.',
      'notify.on': '🔔 Enable alerts',
      'ask.title': 'Claude is waiting for your input',
      'ask.answer': 'Answer',
      'ask.dismiss': 'Dismiss',
      'guide.link': '📖 Guide',
      'cert.hdr': '🔒 Cert',
      'pwa.install': '⬇ Install app',
      'pwa.iosHint': 'iOS: tap the Share button → “Add to Home Screen” to install the app.',
      'cert.summary': '📱 Install certificate (for PWA install · removes security warning)',
      'cert.download': 'Download certificate',
      'cert.android': 'Android — you MUST install via Settings as a “CA certificate” (tapping the downloaded file shows a “trusted authority only” error):\n1) Tap “Download certificate” above → saved to Downloads as AgentHub.crt\n2) Settings → Security (& privacy) → More security settings → “Install from device storage” (or “Install a certificate”)\n3) Choose “CA certificate” (NOT VPN & app)\n4) On the “your data won’t be private” warning → Install anyway\n5) Pick AgentHub.crt from Downloads\n(Samsung: Settings → Security and privacy → More security settings → Install from device storage → CA certificate)',
      'cert.ios': 'iOS — use Safari:\n1) Tap “Download certificate” above → “Profile Downloaded” notice\n2) Settings app → “Profile Downloaded” at top → Install (enter device passcode)\n3) Settings → General → About → Certificate Trust Settings → enable trust for AgentHub',
      'cert.note': 'After installing, refresh the browser to connect without warnings and Add to Home Screen (PWA install).\nOn iOS, if it still won’t connect after install, the 10-year certificate validity may be the cause — tell me and I’ll re-issue it shorter.',
      'summary.count': 'Sessions',
      'summary.total': 'Total agents',
      'summary.working': 'Working',
      'summary.error': 'Errors',
      'agent.working': 'Working',
      'agent.idle': 'Idle',
      'agent.error': 'Error'
    }
  };

  function detect() {
    const s = localStorage.getItem(LANG_KEY);
    if (s === 'ko' || s === 'en') return s;
    return (navigator.language || '').toLowerCase().indexOf('en') === 0 ? 'en' : 'ko';
  }

  let lang = detect();

  function t(key, vars) {
    let s = (DICT[lang] && DICT[lang][key]);
    if (s == null) s = (DICT.ko[key] != null ? DICT.ko[key] : key);
    if (vars) for (const k in vars) s = s.split('{' + k + '}').join(vars[k]);
    return s;
  }

  function apply(root) {
    root = root || document;
    root.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.getAttribute('data-i18n')); });
    root.querySelectorAll('[data-i18n-ph]').forEach(el => { el.setAttribute('placeholder', t(el.getAttribute('data-i18n-ph'))); });
    root.querySelectorAll('[data-i18n-title]').forEach(el => { el.setAttribute('title', t(el.getAttribute('data-i18n-title'))); });
    root.querySelectorAll('select[data-lang-select]').forEach(sel => { sel.value = lang; });
  }

  function setLang(l) {
    if (l !== 'ko' && l !== 'en' || l === lang) { apply(); return; }
    lang = l;
    localStorage.setItem(LANG_KEY, l);
    document.documentElement.lang = l;
    apply();
    document.dispatchEvent(new CustomEvent('i18n:changed', { detail: { lang: l } }));
  }

  function getLang() { return lang; }

  global.I18n = { t, apply, setLang, getLang, LANG_KEY };

  document.documentElement.lang = lang;

  // 언어 셀렉터 자동 연결(select[data-lang-select]) + 최초 정적 텍스트 치환.
  function init() {
    document.querySelectorAll('select[data-lang-select]').forEach(sel => {
      sel.addEventListener('change', () => setLang(sel.value));
    });
    apply();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})(window);
