const CACHE = 'agent-hub-{{VER}}'; // {{VER}}는 서버가 /sw.js 서빙 시 자산 해시로 치환(빌드마다 자동 무효화)
// index.html이 로드하는 렌더 블로킹 자산은 모두 포함해야 한다. 특히 /js/i18n.js 가 빠지면 오프라인에서
// 이 블로킹 스크립트를 네트워크에서 받으려다 멈춰 페이지가 페인트되지 못한다(아이콘 스플래시 영구 정지).
const ASSETS = ['/', '/index.html', '/css/app.css', '/js/i18n.js', '/js/app.js', '/icons/icon-192.png', '/icons/icon-512.png', '/manifest.webmanifest', '/js/xterm.js', '/js/addon-fit.js', '/css/xterm.css', '/js/term.js'];

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
  // API/WebSocket 및 콘솔 페이지는 항상 네트워크
  if (u.pathname.startsWith('/api/') || u.pathname.startsWith('/ws/') || u.pathname.startsWith('/host')) return;
  if (e.request.method !== 'GET') return;
  // 앱 실행(네비게이션): 캐시된 셸을 즉시 서빙(캐시 우선). 서버 미접속이어도 아이콘에서 멈추지 않고
  // 바로 실행되며, 최신 데이터는 app.js가 WebSocket으로 붙어 갱신한다. 캐시에 셸이 없으면(최초/캐시 증발)
  // 네트워크로 받아 즉시 캐시에 저장 → 다음 오프라인 실행을 보장한다. (index.html 갱신은 SW 버전업으로 반영)
  if (e.request.mode === 'navigate') {
    e.respondWith(
      caches.match('/index.html').then(r => r || caches.match('/')).then(cached =>
        cached || fetch(e.request).then(res => {
          if (res && res.ok) { const cp = res.clone(); caches.open(CACHE).then(c => c.put('/index.html', cp)); }
          return res;
        })
      )
    );
    return;
  }
  // 정적 자산: stale-while-revalidate — 캐시 우선으로 즉시 응답하고, 온라인이면 백그라운드로 네트워크 최신본을
  // 받아 캐시를 갱신한다. 이렇게 매 접속마다 캐시를 다시 채워, OS(안드로이드 등)가 캐시를 evict해도
  // 다음 온라인 접속에서 자동 복구된다(오프라인 실행이 계속 유지됨).
  e.respondWith(
    caches.match(e.request).then(cached => {
      const net = fetch(e.request).then(res => {
        if (res && res.ok) { const cp = res.clone(); caches.open(CACHE).then(c => c.put(e.request, cp)); }
        return res;
      }).catch(() => cached);
      return cached || net;
    })
  );
});

// Web Push: 앱이 꺼져 있어도 응답 대기 알림 표시. 암호화 payload로 질문 상세(title/body)가 오면 그대로 표시,
// 없으면 일반 문구로 폴백. 탭하면 앱이 열린다. 세션별 tag로 다른 세션 알림이 서로 덮어쓰지 않게 한다
// (동일 본문 반복 전송은 서버가 이미 막으므로, 도착한 푸시는 내용이 바뀐 것 → renotify로 알림).
self.addEventListener('push', function (e) {
  var title = 'Agent Hub';
  var body = '응답 대기 중인 세션이 있습니다';
  var sid = '';
  if (e.data) { try { var d = e.data.json(); if (d.title) title = d.title; if (d.body) body = d.body; if (d.sessionId) sid = d.sessionId; } catch (_) { var tx = e.data.text(); if (tx) body = tx; } }
  e.waitUntil(self.registration.showNotification(title, {
    body: body, tag: 'agenthub-' + (sid || 'ask'), renotify: true, icon: '/icons/icon-192.png', data: { url: '/' }
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
