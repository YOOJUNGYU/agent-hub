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
      'settings.general': '일반', 'settings.lang': '언어 (Language)',
      'settings.portLabel': '서버 포트 (1024–65535)',
      'settings.save': '저장 후 재시작',
      'settings.hint': '포트를 바꾸면 서버가 재시작되고 접속 주소가 변경됩니다.',
      'settings.autoStart': 'Windows 시작 시 자동 실행',
      'settings.autoStartHint': '로그인하면 Agent Hub가 자동으로 실행됩니다(기본 켜짐). 설치본에서만 적용됩니다.',
      'settings.saving': '저장 중…',
      'settings.saved': '저장됨. 서버가 새 주소({url})로 재시작됩니다…',
      'settings.error': '오류: ',
      'settings.reqFail': '요청 실패: ',
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
      'pending.connecting': '서버 연결 확인 중…',
      'pending.connectingDesc': 'Agent Hub 서버에 연결하고 있습니다. 잠시만 기다려 주세요.',
      'pending.offline': '연결할 수 없습니다 — 인증서 확인 필요',
      'pending.offlineDesc': 'PC(Agent Hub)가 켜져 있는지 확인하세요. 인증서를 삭제했거나 새로 발급된 경우, 아래 주소를 휴대폰 브라우저(Chrome/Safari)로 열어 인증서를 다시 설치하세요(보안 경고가 나오면 계속 진행하세요).',
      'pending.certBtn': '새 인증서 설치',
      'monitor.loading': '세션 정보를 불러오는 중…',
      'monitor.empty': '최근 활동한 세션이 없습니다.',
      'monitor.emptyInjectable': '입력 가능한 세션이 없습니다.',
      'monitor.injectableOnly': '입력 가능만',
      'card.waiting': '응답 대기중',
      'done.title': '작업 완료',
      'done.body': '작업을 완료했습니다',
      'run.turn': '턴',
      'run.total': '누적',
      'answer.blockedClawd': 'Clawd on Desk가 실행 중이라 답변을 PC로 전달할 수 없습니다.\nClawd on Desk를 종료한 뒤 다시 시도하세요.',
      'detail.back': '← 목록',
      'notify.on': '🔔 알림 켜기',
      'ask.title': 'Claude가 입력을 기다립니다',
      'ask.answer': '답변하기',
      'ask.dismiss': '닫기',
      'elicit.title': 'Claude가 질문합니다',
      'elicit.question': '질문',
      'elicit.chooseOne': '하나를 선택하세요',
      'elicit.chooseMulti': '하나 이상 선택하세요',
      'elicit.other': '기타(직접 입력)',
      'elicit.otherPh': '답변을 입력하세요',
      'elicit.cancel': '취소',
      'elicit.back': '이전',
      'elicit.next': '다음',
      'elicit.submit': '답변 보내기',
      'perm.title': 'Claude가 권한을 요청합니다',
      'perm.q': '실행을 허용할까요?',
      'perm.allow': '허용',
      'perm.deny': '거부',
      'guide.link': '📖 사용법',
      'cert.hdr': '🔒 인증서',
      'pwa.install': '⬇ 앱 설치',
      'pwa.iosHint': 'iOS: 공유 버튼 → “홈 화면에 추가”를 눌러 앱으로 설치하세요.',
      'cert.summary': '📱 인증서 설치 (PWA 설치 · 보안경고 제거)',
      'cert.download': '인증서 다운로드',
      'cert.openInBrowser': '인증서를 삭제했거나 앱에서 안 열리면, 아래 주소를 휴대폰 브라우저로 열고 보안 경고가 나오면 계속 진행하세요:',
      'cert.android': 'Android — 반드시 설정에서 “CA 인증서”로 설치하세요 (다운로드한 파일을 그냥 탭하면 “신뢰할 수 있는 기관…” 오류가 납니다):\n1) 위 “인증서 다운로드” → 다운로드 폴더에 AgentHub.crt 저장\n2) 설정 앱 → 보안(및 개인정보) → 기타 보안 설정 → “기기 저장공간에서 설치”(또는 “인증서 설치”)\n3) “CA 인증서” 선택 (VPN·앱 인증서 아님)\n4) “개인정보가 보호되지 않을 수 있음” 경고 → 계속/설치\n5) 다운로드 폴더의 AgentHub.crt 선택\n(삼성: 설정 → 보안 및 개인정보 → 기타 보안 설정 → 기기 저장공간에서 설치 → CA 인증서)',
      'cert.ios': 'iOS — 반드시 Safari로 진행:\n1) 위 “인증서 다운로드” → “프로파일이 다운로드됨” 안내\n2) 설정 앱 상단 “프로파일 다운로드됨” → 설치 (기기 암호 입력)\n3) 설정 → 일반 → 정보 → 인증서 신뢰 설정에서 AgentHub 신뢰 켜기',
      'cert.note': '설치 후 브라우저를 새로고침하면 보안경고 없이 접속되고, 홈 화면에 추가(PWA 설치)할 수 있습니다.\niOS에서 설치 후에도 접속이 안 되면 인증서 유효기간(10년) 때문일 수 있어요 — 알려주시면 짧게 재발급해 드립니다.',
      'installPromo.installTitle': '📲 앱으로 설치하면 더 편해요',
      'installPromo.installDesc': '홈 화면에서 바로 열고, 앱을 닫아도 알림을 받을 수 있어요.',
      'installPromo.installBtn': '앱 설치',
      'installPromo.certTitle': '🔒 먼저 인증서를 설치하세요',
      'installPromo.certDesc': '앱 설치와 보안경고 제거를 위해 인증서가 필요합니다.',
      'installPromo.certBtn': '인증서 설치',
      'installPromo.iosTitle': '📲 홈 화면에 앱 추가',
      'installPromo.iosDesc': 'Safari 공유 버튼 → “홈 화면에 추가”로 설치하세요. (처음이면 인증서부터)',
      'installPromo.iosBtn': '설치 방법',
      'summary.count': '세션',
      'summary.total': '전체 에이전트',
      'summary.working': '작업 중',
      'summary.error': '오류',
      'agent.working': '작업 중',
      'agent.idle': '대기',
      'agent.error': '오류',
      'inject.placeholder': '답변 입력…',
      'inject.send': '전송',
      'inject.hintCodex': 'Codex(데스크톱 앱) 세션은 모바일 직접 입력을 지원하지 않습니다. PC의 Codex 앱에서 답해 주세요.',
      'inject.hintNoConsole': '이 세션은 직접 입력이 안 되는 터미널(Windows Terminal 등)에서 실행 중입니다. cmd.exe 또는 PowerShell 창에서 claude를 실행하면 모바일에서 바로 답할 수 있어요.',
      'inject.hintNoPid': '실행 중인 세션 프로세스를 찾을 수 없습니다(종료됐거나 훅 미보고). PC에서 직접 답하거나 세션을 다시 시작하세요.',
      'inject.hintFailed': '전송에 실패했습니다. 잠시 후 다시 시도해 주세요.',
      'inject.hintNotShell': 'PC에서 CLI(cmd·PowerShell 등)로 실행한 claude 세션에서만 모바일 답변을 입력할 수 있어요. 이 세션은 직접 입력을 받지 못합니다.',
      'qna.multiOnPc': '여러 문항으로 된 질문입니다. 답변 창이 지나 폰에서는 보낼 수 없으니 PC에서 답해 주세요.',
      'qna.sendFailed': '전송에 실패했습니다. PC에서 답해 주세요.'
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
      'settings.general': 'General', 'settings.portLabel': 'Server port (1024–65535)',
      'settings.save': 'Save & restart',
      'settings.hint': 'Changing the port restarts the server and changes the access URL.',
      'settings.autoStart': 'Launch on Windows startup',
      'settings.autoStartHint': 'Agent Hub starts automatically when you log in (on by default). Applies to the installed build only.',
      'settings.saving': 'Saving…',
      'settings.saved': 'Saved. Server is restarting at {url}…',
      'settings.error': 'Error: ',
      'settings.reqFail': 'Request failed: ',
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
      'pending.connecting': 'Connecting to server…',
      'pending.connectingDesc': 'Connecting to the Agent Hub server. Please wait a moment.',
      'pending.offline': "Can't connect — check the certificate",
      'pending.offlineDesc': 'Make sure the PC (Agent Hub) is running. If you deleted or reissued the certificate, open the address below in your phone browser (Chrome/Safari) and install the certificate again (continue past any security warning).',
      'pending.certBtn': 'Install new certificate',
      'monitor.loading': 'Loading sessions…',
      'monitor.empty': 'No recently active sessions.',
      'monitor.emptyInjectable': 'No input-ready sessions.',
      'monitor.injectableOnly': 'Input-ready only',
      'card.waiting': 'Waiting for you',
      'done.title': 'Done',
      'done.body': 'Task completed',
      'run.turn': 'turn',
      'run.total': 'total',
      'answer.blockedClawd': "Clawd on Desk is running, so your answer can't be delivered to the PC.\nClose Clawd on Desk and try again.",
      'detail.back': '← List',
      'notify.on': '🔔 Enable alerts',
      'ask.title': 'Claude is waiting for your input',
      'ask.answer': 'Answer',
      'ask.dismiss': 'Dismiss',
      'elicit.title': 'Claude has a question',
      'elicit.question': 'Question',
      'elicit.chooseOne': 'Choose one',
      'elicit.chooseMulti': 'Choose one or more',
      'elicit.other': 'Other (type your own)',
      'elicit.otherPh': 'Type your answer',
      'elicit.cancel': 'Cancel',
      'elicit.back': 'Back',
      'elicit.next': 'Next',
      'elicit.submit': 'Send answer',
      'perm.title': 'Claude is requesting permission',
      'perm.q': 'Allow this action?',
      'perm.allow': 'Allow',
      'perm.deny': 'Deny',
      'guide.link': '📖 Guide',
      'cert.hdr': '🔒 Cert',
      'pwa.install': '⬇ Install app',
      'pwa.iosHint': 'iOS: tap the Share button → “Add to Home Screen” to install the app.',
      'cert.summary': '📱 Install certificate (for PWA install · removes security warning)',
      'cert.download': 'Download certificate',
      'cert.openInBrowser': "If you deleted the certificate or it won't open in the app, open this address in your phone browser and continue past any security warning:",
      'cert.android': 'Android — you MUST install via Settings as a “CA certificate” (tapping the downloaded file shows a “trusted authority only” error):\n1) Tap “Download certificate” above → saved to Downloads as AgentHub.crt\n2) Settings → Security (& privacy) → More security settings → “Install from device storage” (or “Install a certificate”)\n3) Choose “CA certificate” (NOT VPN & app)\n4) On the “your data won’t be private” warning → Install anyway\n5) Pick AgentHub.crt from Downloads\n(Samsung: Settings → Security and privacy → More security settings → Install from device storage → CA certificate)',
      'cert.ios': 'iOS — use Safari:\n1) Tap “Download certificate” above → “Profile Downloaded” notice\n2) Settings app → “Profile Downloaded” at top → Install (enter device passcode)\n3) Settings → General → About → Certificate Trust Settings → enable trust for AgentHub',
      'cert.note': 'After installing, refresh the browser to connect without warnings and Add to Home Screen (PWA install).\nOn iOS, if it still won’t connect after install, the 10-year certificate validity may be the cause — tell me and I’ll re-issue it shorter.',
      'installPromo.installTitle': '📲 Install as an app',
      'installPromo.installDesc': 'Open straight from your home screen and get alerts even when the app is closed.',
      'installPromo.installBtn': 'Install app',
      'installPromo.certTitle': '🔒 Install the certificate first',
      'installPromo.certDesc': 'The certificate is required to install the app and remove the security warning.',
      'installPromo.certBtn': 'Install certificate',
      'installPromo.iosTitle': '📲 Add the app to your Home Screen',
      'installPromo.iosDesc': 'Use Safari’s Share button → “Add to Home Screen”. (First time? Install the certificate first.)',
      'installPromo.iosBtn': 'How to install',
      'summary.count': 'Sessions',
      'summary.total': 'Total agents',
      'summary.working': 'Working',
      'summary.error': 'Errors',
      'agent.working': 'Working',
      'agent.idle': 'Idle',
      'agent.error': 'Error',
      'inject.placeholder': 'Type a reply…',
      'inject.send': 'Send',
      'inject.hintCodex': 'Codex (desktop app) sessions do not support direct input from mobile. Answer in the Codex app on your PC.',
      'inject.hintNoConsole': 'This session runs in a terminal that does not accept direct input (e.g. Windows Terminal). Run claude in a cmd.exe or PowerShell window and you can answer from mobile.',
      'inject.hintNoPid': 'Could not find the running session process (ended or hook not reported). Answer on your PC or restart the session.',
      'inject.hintFailed': 'Send failed. Please try again in a moment.',
      'inject.hintNotShell': 'Mobile replies work only for claude sessions started from a CLI (cmd, PowerShell, etc.) on the PC. This session does not accept direct input.',
      'qna.multiOnPc': 'This question has multiple parts. The answer window has passed, so it can not be sent from the phone — please answer on the PC.',
      'qna.sendFailed': 'Send failed — please answer on the PC.'
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
