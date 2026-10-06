// Service worker del jugador: precacha la pagina y el runtime .NET entero (lo que lista
// _framework/blazor.boot.json) para que la pagina CARGUE aunque nadie la sirva. Solo existe en
// contexto seguro (https o localhost): sobre http://<ip> el navegador ni lo registra.
const CACHE = 'velada-jugador-v1';
const PAGINA = ['./', './index.html', './main.js', './receipt.js', './manifest.webmanifest', './icon-192.png', './icon-512.png', './favicon.png'];

async function recursosDelRuntime() {
  const boot = await (await fetch('./_framework/blazor.boot.json', { cache: 'no-store' })).json();
  const nombres = new Set(['dotnet.js']);
  const grupos = boot.resources || {};
  for (const grupo of Object.values(grupos)) {
    if (grupo && typeof grupo === 'object' && !Array.isArray(grupo)) {
      for (const nombre of Object.keys(grupo)) if (!nombre.endsWith('.pdb') && !nombre.endsWith('.map')) nombres.add(nombre);
    }
  }
  return ['./_framework/blazor.boot.json', ...[...nombres].map(n => './_framework/' + n)];
}

self.addEventListener('install', event => {
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE);
    const runtime = await recursosDelRuntime();
    const todo = [...PAGINA, ...runtime];
    let ok = 0, mal = 0;
    for (const url of todo) {
      try { const r = await fetch(url, { cache: 'no-store' }); if (r.ok) { await cache.put(url, r.clone()); ok++; } else mal++; }
      catch (e) { mal++; }
    }
    await cache.put('./__precache', new Response(JSON.stringify({ ok, mal, total: todo.length, cuando: new Date().toISOString() })));
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', event => { event.waitUntil(self.clients.claim()); });

self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== self.location.origin) return;
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/buzon') || url.pathname.startsWith('/tell/')) return;
  event.respondWith((async () => {
    const cache = await caches.open(CACHE);
    const clave = url.pathname === '/' ? './' : '.' + url.pathname;
    const guardado = await cache.match(clave);
    if (guardado) return guardado;
    try { const r = await fetch(event.request); if (r.ok) cache.put(clave, r.clone()); return r; }
    catch (e) { return new Response('sin red y sin cache para ' + url.pathname, { status: 504 }); }
  })());
});
