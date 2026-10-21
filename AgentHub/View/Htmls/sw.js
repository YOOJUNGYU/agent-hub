const CACHE = 'agent-hub-{{VER}}'; // {{VER}}는 서버가 /sw.js 서빙 시 자산 해시로 치환(빌드마다 자동 무효화)
// index.html이 로드하는 렌더 블로킹 자산은 모두 포함해야 한다. 특히 /js/i18n.js 가 빠지면 오프라인에서
// 이 블로킹 스크립트를 네트워크에서 받으려다 멈춰 페이지가 페인트되지 못한다(아이콘 스플래시 영구 정지).
const ASSETS = ['/', '/index.html', '/css/app.css', '/js/i18n.js', '/js/app.js', '/icons/icon-192.png', '/icons/icon-512.png', '/manifest.webmanifest'];

self.addEventListener('install', e => {
  // addAll은 원자적이라 자산 하나만 실패해도(모바일 WiFi 순간 끊김 등) 전체 캐시가 비어 오프라인이 깨진다.
  // 개별 add로 담아 일부 실패에도 나머지는 캐시되게 한다(부족분은 아래 fetch 핸들러가 온라인에서 다시 채움).
  e.waitUntil(
    caches.open(CACHE)
      .then(c => Promise.all(ASSETS.map(a => c.add(a).catch(() => {}))))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', e => {
  e.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', e => {
  const u = new URL(e.request.url);
  // API/WebSocket, 콘솔 페이지, 픽셀 오피스(자체 SPA)는 항상 네트워크
  if (u.pathname.startsWith('/api/') || u.pathname.startsWith('/ws/') || u.pathname.startsWith('/host')
      || u.pathname.startsWith('/office')) return;
  if (e.request.method !== 'GET') return;
  // 온라인이면 항상 서버의 최신본(업데이트가 바로 보이게), 서버에 못 붙으면 캐시로(오프라인 실행 유지).
  // 예전엔 캐시 우선이라 휴대폰 PWA가 업데이트 뒤에도 옛 index.html·app.js를 계속 띄웠다.
  // 네비게이션(/, /?open=…)은 셸 하나(/index.html)로 저장한다.
  e.respondWith(fresh(e.request.url, e.request.mode === 'navigate' ? '/index.html' : e.request));
});

const OFFLINE_AFTER_MS = 3000; // 이 시간 안에 서버 응답이 없으면 캐시로(서버가 꺼졌거나 VPN 끊김)

function fresh(url, key) {
  const net = fetch(url, { cache: 'no-cache' }).then(res => {
    if (res && res.ok) { const cp = res.clone(); caches.open(CACHE).then(c => c.put(key, cp)); }
    return res;
  });
  const cached = () => caches.match(key);
  const slow = new Promise(r => setTimeout(r, OFFLINE_AFTER_MS)).then(cached).then(c => c || net);
  return Promise.race([net.catch(cached), slow]);
}

// Web Push: 앱이 꺼져 있어도 응답 대기 알림 표시. 암호화 payload로 질문 상세(title/body)가 오면 그대로 표시,
// 없으면 일반 문구로 폴백. 탭하면 앱이 열린다. 세션별 tag로 다른 세션 알림이 서로 덮어쓰지 않게 한다
// (동일 본문 반복 전송은 서버가 이미 막으므로, 도착한 푸시는 내용이 바뀐 것 → renotify로 알림).
self.addEventListener('push', function (e) {
  var title = 'Agent Hub';
  var body = '응답 대기 중인 세션이 있습니다';
  var sid = '';
  if (e.data) { try { var d = e.data.json(); if (d.title) title = d.title; if (d.body) body = d.body; if (d.sessionId) sid = d.sessionId; } catch (_) { var tx = e.data.text(); if (tx) body = tx; } }
  e.waitUntil(self.registration.showNotification(title, {
    body: body, tag: 'agenthub-' + (sid || 'ask'), renotify: true, icon: '/icons/icon-192.png',
    data: { url: sid ? '/?open=' + encodeURIComponent(sid) : '/' } // 오피스가 그 세션 대화창을 연다
  }));
});

self.addEventListener('notificationclick', function (e) {
  e.notification.close();
  e.waitUntil((async function () {
    const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const c of all) { if ('focus' in c) { try { return await c.focus(); } catch (_) {} } }
    if (self.clients.openWindow) return self.clients.openWindow((e.notification.data && e.notification.data.url) || '/');
  })());
});
