const CACHE = 'agent-hub-{{VER}}'; // {{VER}}는 서버가 /sw.js 서빙 시 자산 해시로 치환(빌드마다 자동 무효화)
const ASSETS = ['/', '/index.html', '/css/app.css', '/js/app.js', '/icons/icon-192.png', '/icons/icon-512.png', '/manifest.webmanifest', '/js/xterm.js', '/js/addon-fit.js', '/css/xterm.css', '/js/term.js'];

self.addEventListener('install', e => {
  e.waitUntil(caches.open(CACHE).then(c => c.addAll(ASSETS)).then(() => self.skipWaiting()));
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
  // API/WebSocket 및 콘솔 페이지는 항상 네트워크
  if (u.pathname.startsWith('/api/') || u.pathname.startsWith('/ws/') || u.pathname.startsWith('/host')) return;
  e.respondWith(caches.match(e.request).then(r => r || fetch(e.request)));
});

// Web Push: 앱이 꺼져 있어도 응답 대기 알림 표시. payload 없이 오므로 일반 문구 사용
// (탭하면 앱이 열리고 어느 세션인지는 카드의 '응답 대기중'으로 확인). 같은 tag로 스팸 방지.
self.addEventListener('push', function (e) {
  var title = 'Agent Hub';
  var body = '응답 대기 중인 세션이 있습니다';
  if (e.data) { try { var d = e.data.json(); if (d.title) title = d.title; if (d.body) body = d.body; } catch (_) { var tx = e.data.text(); if (tx) body = tx; } }
  e.waitUntil(self.registration.showNotification(title, {
    body: body, tag: 'agenthub-ask', renotify: true, icon: '/icons/icon-192.png', data: { url: '/' }
  }));
});

self.addEventListener('notificationclick', function (e) {
  e.notification.close();
  e.waitUntil((async function () {
    const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const c of all) { if ('focus' in c) { try { return await c.focus(); } catch (_) {} } }
    if (self.clients.openWindow) return self.clients.openWindow('/');
  })());
});
