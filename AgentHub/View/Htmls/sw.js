const CACHE = 'agent-hub-v21';
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

self.addEventListener('notificationclick', function (e) {
  e.notification.close();
  e.waitUntil((async function () {
    const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const c of all) { if ('focus' in c) { try { return await c.focus(); } catch (_) {} } }
    if (self.clients.openWindow) return self.clients.openWindow('/');
  })());
});
