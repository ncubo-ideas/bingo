// Service worker minimo del jugador web: no guarda nada, solo existe para que Chrome evalue si la pagina se
// puede instalar. Chrome no lo registra si el certificado tiene error.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => { });
