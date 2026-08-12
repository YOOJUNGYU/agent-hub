// 호스트 콘솔 QR의 주소 조립을 검증한다.  실행: node tools/verify-qr.js
//
// 가장 위험한 실수는 QR에 콘솔 자신의 주소(https://127.0.0.1:포트)를 담는 것이다.
// 그러면 PC에서는 멀쩡히 열리지만 휴대폰에서는 절대 열리지 않는다. 그 회귀를 여기서 막는다.
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const root = path.resolve(__dirname, '..');
const htmls = path.join(root, 'AgentHub', 'View', 'Htmls');
const hostJsPath = path.join(htmls, 'js', 'host.js');
const hostHtmlPath = path.join(htmls, 'host.html');
const vendorPath = path.join(htmls, 'js', 'vendor', 'qrcode.js');

let failed = 0;
function check(name, ok, detail) {
  console.log((ok ? 'PASS  ' : 'FAIL  ') + name + (ok || !detail ? '' : '\n        ' + detail));
  if (!ok) failed++;
}

const hostJs = fs.readFileSync(hostJsPath, 'utf8');
const hostHtml = fs.readFileSync(hostHtmlPath, 'utf8');

// --- 배선 확인 ---
check('vendor/qrcode.js 파일 존재', fs.existsSync(vendorPath));
check('host.html이 vendor/qrcode.js를 로드', hostHtml.includes('/js/vendor/qrcode.js'));
check('host.html에 QR 버튼과 모달이 있음',
  hostHtml.includes('id="qrBtn"') && hostHtml.includes('id="qrModal"'));

// --- QR 주소가 loopback을 쓰지 않는지 ---
// host.js 전체에서 location.origin 을 QR 경로에 쓰면 안 된다(모바일 앱 app.js와 달리 콘솔은 loopback이다).
// 주석에 등장하는 것은 코드가 아니므로 먼저 걷어낸다.
const hostCode = hostJs.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
check('host.js가 location.origin으로 QR을 만들지 않음', !/location\.origin/.test(hostCode),
  'host.js에서 location.origin 사용이 발견됨 — QR이 127.0.0.1을 가리키게 된다');
check('QR 렌더링이 서버가 준 endpoint 주소를 사용', /drawQr\(\s*\$\('#qrConnect'\)\s*,\s*ep\.url\s*\)/.test(hostJs));

// --- 실제 host.js의 certUrlOf 를 꺼내 검증 ---
const m = hostJs.match(/function certUrlOf\(endpointUrl\) \{[\s\S]*?\n\}/);
check('host.js에서 certUrlOf를 찾음', !!m);
if (m) {
  const sandbox = {};
  vm.runInNewContext(m[0] + '\nthis.certUrlOf = certUrlOf;', sandbox);
  const certUrlOf = sandbox.certUrlOf;

  const cases = [
    ['https://192.168.246.201:47600', 'https://192.168.246.201:47600/api/cert'],
    ['https://192.168.246.201:47600/', 'https://192.168.246.201:47600/api/cert'],
    ['https://100.79.104.205:47600', 'https://100.79.104.205:47600/api/cert'],
  ];
  for (const [input, expected] of cases) {
    const actual = certUrlOf(input);
    check('certUrlOf(' + input + ')', actual === expected, '기대: ' + expected + ' / 실제: ' + actual);
  }
}

// --- 벤더 라이브러리가 이 주소들을 실제로 인코딩하는지 ---
const sandbox = { window: {} };
sandbox.self = sandbox;
vm.runInNewContext(fs.readFileSync(vendorPath, 'utf8'), sandbox);
const qrcode = sandbox.qrcode || sandbox.window.qrcode;
check('qrcode 라이브러리 로드', typeof qrcode === 'function');

if (typeof qrcode === 'function') {
  for (const url of ['https://192.168.246.201:47600', 'https://192.168.246.201:47600/api/cert']) {
    let ok = false, detail = '';
    try {
      const q = qrcode(0, 'M');
      q.addData(url);
      q.make();
      const n = q.getModuleCount();
      let dark = 0;
      for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) if (q.isDark(r, c)) dark++;
      const svg = q.createSvgTag({ cellSize: 4, margin: 4, scalable: true });
      // 모듈 수는 21 + 4*(버전-1). 어두운 모듈이 전체의 30~70%가 아니면 인코딩이 깨진 것이다.
      ok = n >= 21 && (n - 21) % 4 === 0 && dark > n * n * 0.3 && dark < n * n * 0.7
        && svg.startsWith('<svg') && svg.includes('viewBox');
      detail = 'modules=' + n + ' dark=' + dark;
    } catch (e) { detail = e.message; }
    check('QR 인코딩: ' + url, ok, detail);
  }
}

// --- host.html 태그 균형 ---
// 모달 마크업이 어긋나면 콘솔이 조용히 깨지므로(빈 화면/탭 먹통) 여기서 잡는다.
(function () {
  const VOID = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input',
    'link', 'meta', 'param', 'source', 'track', 'wbr', '!doctype']);
  const body = hostHtml.replace(/<!--[\s\S]*?-->/g, '');
  const stack = [];
  let mismatch = null;
  const re = /<(\/?)([a-zA-Z!][a-zA-Z0-9-]*)\b[^>]*?(\/?)>/g;
  let mt;
  while ((mt = re.exec(body)) !== null) {
    const closing = mt[1] === '/', tag = mt[2].toLowerCase(), selfClosed = mt[3] === '/';
    if (VOID.has(tag) || selfClosed) continue;
    if (!closing) stack.push(tag);
    else {
      const open = stack.pop();
      if (open !== tag && !mismatch) mismatch = '</' + tag + '> 를 닫으려는데 열려 있는 것은 <' + open + '>';
    }
  }
  check('host.html 태그 균형', !mismatch && stack.length === 0,
    mismatch || (stack.length ? '닫히지 않은 태그: ' + stack.join(', ') : ''));
})();

console.log(failed === 0 ? '\nALL PASS' : '\n' + failed + ' FAILED');
process.exit(failed === 0 ? 0 : 1);
