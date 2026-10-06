// node make-site.js  -> arma en ./sitio la copia del jugador web para Cloudflare Pages (bingo-velada.pages.dev).
// Va despues de build.ps1: toma lo compilado en bin/Release/net9.0/wwwroot, marca el index.html como copia de
// internet, genera el service worker con la lista exacta de archivos, escribe _headers y el assetlinks.json que le
// dice a Android que los enlaces del sitio los puede abrir la app. No publica nada: eso es
//   npx wrangler pages deploy site --project-name bingo-velada
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const AQUI = __dirname;
const FUENTE = path.join(AQUI, 'bin', 'Release', 'net9.0', 'wwwroot');
const FRAMEWORK = path.join(FUENTE, '_framework');
const SITIO = path.resolve(process.argv[2] || path.join(AQUI, 'site'));

const ESTATICOS = ['index.html', 'bridge.js', 'bridge-shim.js', 'player.css', 'player.js', 'patterns.js', 'language.js',
  'texts.json', 'entry.js', 'entry.css', 'jsQR.js', 'jsQR.LICENSE.txt', 'manifest.webmanifest', 'favicon.png', 'icon-192.png', 'icon-512.png',
  'icon-maskable-512.png'];
const CARPETAS = ['assets', 'fonts'];

// Las huellas SHA-256 de las llaves que firman el apk: la de VeladaDocker (compilar en Docker) y la de depuracion de
// Visual Studio. Un apk firmado con otra llave no queda verificado y sus enlaces abren la pagina, no la app.
const APP = 'com.ncubo.bingo';
const HUELLAS = [
  '32:16:4D:27:50:5B:F8:CD:B9:EF:31:F6:1C:14:68:D4:AC:BB:9B:33:72:1E:01:34:E7:32:79:89:8B:93:4F:2C',
  '5D:53:6E:79:A8:55:79:65:DB:BE:0B:46:43:22:E5:7D:ED:D2:D6:83:84:DD:09:92:55:58:AF:4F:92:44:C3:04',
];

if (!fs.existsSync(path.join(FUENTE, 'index.html'))) throw new Error('no hay nada en ' + FUENTE + ': primero build.ps1');
const faltan = ESTATICOS.filter(f => !fs.existsSync(path.join(FUENTE, f)));
if (faltan.length) throw new Error('le faltan archivos a ' + FUENTE + ': ' + faltan.join(', ') + ' (primero build.ps1)');

fs.rmSync(SITIO, { recursive: true, force: true });
fs.mkdirSync(SITIO, { recursive: true });
const copiar = (de, a) => { fs.mkdirSync(path.dirname(a), { recursive: true }); fs.copyFileSync(de, a); };
for (const f of ESTATICOS) copiar(path.join(FUENTE, f), path.join(SITIO, f));
for (const c of CARPETAS) for (const f of fs.readdirSync(path.join(FUENTE, c))) copiar(path.join(FUENTE, c, f), path.join(SITIO, c, f));
// El bus iroh del navegador (iroh/iroh-browser.js y iroh/pkg), solo si build.ps1 lo encontro.
if (fs.existsSync(path.join(FUENTE, 'iroh'))) fs.cpSync(path.join(FUENTE, 'iroh'), path.join(SITIO, 'iroh'), { recursive: true, filter: f => !/\.d\.ts$/.test(f) });
// Los .pdb van: el arranque de este build los pide y sin ellos .NET no levanta ("download ... .pdb failed").
for (const f of fs.readdirSync(FRAMEWORK)) if (!/\.(gz|br)$/.test(f)) copiar(path.join(FRAMEWORK, f), path.join(SITIO, '_framework', f));

// .NET guarda su propia copia del runtime en una cache nombrada con la direccion entera de la pagina, # incluido:
// cada bingo sumaba otros 28 MB. Aca ya lo guarda el service worker, asi que se apaga en el arranque (el valor del
// blazor.boot.json le gana a lo que la pagina pase en withConfig).
const arranque = path.join(SITIO, '_framework', 'blazor.boot.json');
const boot = JSON.parse(fs.readFileSync(arranque, 'utf8'));
boot.cacheBootResources = false;
fs.writeFileSync(arranque, JSON.stringify(boot, null, 2));

const indice = path.join(SITIO, 'index.html');
const html = fs.readFileSync(indice, 'utf8');
if (!html.includes('<meta charset="utf-8">')) throw new Error('index.html sin <meta charset="utf-8">: no se donde marcarlo');
fs.writeFileSync(indice, html.replace('<meta charset="utf-8">', '<meta charset="utf-8">\n<meta name="bingo-site" content="internet">'));

const pedidos = [...html.matchAll(/(?:src|href)="([^"#:?]+)(?:\?[^"]*)?"/g)].map(m => m[1]).filter(r => !r.startsWith('/') && !r.startsWith('data'));
const rotos = [...new Set(pedidos)].filter(r => !fs.existsSync(path.join(SITIO, r)));
if (rotos.length) throw new Error('index.html pide archivos que la copia no lleva: ' + rotos.join(', ') + '. Van en ESTATICOS');

const recorrer = d => fs.readdirSync(d, { withFileTypes: true }).flatMap(e => e.isDirectory() ? recorrer(path.join(d, e.name)) : [path.join(d, e.name)]);
const relativos = recorrer(SITIO).map(f => path.relative(SITIO, f).split(path.sep).join('/')).filter(f => !f.endsWith('.txt')).sort();
const huella = crypto.createHash('sha256');
for (const f of relativos) huella.update(f).update(fs.readFileSync(path.join(SITIO, f)));
const VERSION = huella.digest('hex').slice(0, 12);
const ARCHIVOS = ['./', ...relativos.filter(f => f !== 'index.html').map(f => './' + f)];

const sw = `// Service worker de la copia de internet del jugador (version ${VERSION}). Guarda toda la pagina y el runtime .NET
// para que la app instalada abra sin internet, en la red local del bingo. Cada publicacion trae otra version: el
// service worker nuevo baja todo de nuevo, borra la cache vieja y la siguiente apertura ya usa lo nuevo.
const VERSION = '${VERSION}';
const CACHE = 'velada-jugador-' + VERSION;
const ARCHIVOS = ${JSON.stringify(ARCHIVOS)};
const CON_HUELLA = /\\.[a-z0-9]{10}\\.(wasm|dat|js)$/;

// Una respuesta que vino de una redireccion no sirve para contestar una navegacion: se rearma sin esa marca.
const limpia = async r => r.redirected ? new Response(await r.blob(), { status: r.status, headers: r.headers }) : r;

self.addEventListener('install', e => e.waitUntil((async () => {
  const cache = await caches.open(CACHE);
  await Promise.all(ARCHIVOS.map(async a => {
    const r = await fetch(a, { cache: CON_HUELLA.test(a) ? 'default' : 'reload' });
    if (!r.ok) throw new Error(a + ' respondio ' + r.status);
    await cache.put(a, await limpia(r));
  }));
  await self.skipWaiting();
})()));

self.addEventListener('activate', e => e.waitUntil((async () => {
  for (const k of await caches.keys()) if (k.startsWith('velada-jugador-') && k !== CACHE) await caches.delete(k);
  await self.clients.claim();
})()));

// Lo de este sitio sale de la cache y, si falta, de la red. El organizador y todo lo de afuera no pasa por aca.
self.addEventListener('fetch', e => {
  const u = new URL(e.request.url);
  if (e.request.method !== 'GET' || u.origin !== location.origin) return;
  e.respondWith((async () => {
    const cache = await caches.open(CACHE);
    const guardado = await cache.match(e.request.mode === 'navigate' ? './' : e.request, { ignoreSearch: true });
    if (guardado) return guardado;
    return fetch(e.request);
  })());
});
`;
fs.writeFileSync(path.join(SITIO, 'sw-player.js'), sw);

fs.mkdirSync(path.join(SITIO, '.well-known'), { recursive: true });
fs.writeFileSync(path.join(SITIO, '.well-known', 'assetlinks.json'), JSON.stringify([{
  relation: ['delegate_permission/common.handle_all_urls'],
  target: { namespace: 'android_app', package_name: APP, sha256_cert_fingerprints: HUELLAS },
}], null, 2));

fs.writeFileSync(path.join(SITIO, '_headers'), [
  '/sw-player.js',
  '  Cache-Control: no-cache',
  '/_framework/*.wasm',
  '  Cache-Control: public, max-age=31536000, immutable',
  '/_framework/*.dat',
  '  Cache-Control: public, max-age=31536000, immutable',
  '/.well-known/assetlinks.json',
  '  Content-Type: application/json',
  ''].join('\n'));

const todos = recorrer(SITIO);
const tam = todos.map(f => fs.statSync(f).size);
const mayor = todos[tam.indexOf(Math.max(...tam))];
console.log('sitio armado en ' + SITIO);
console.log('version ' + VERSION + ' | ' + todos.length + ' archivos | ' + (tam.reduce((a, b) => a + b, 0) / 1048576).toFixed(1) + ' MB');
console.log('el mas grande: ' + path.relative(SITIO, mayor) + ' (' + (Math.max(...tam) / 1048576).toFixed(2) + ' MB; Cloudflare acepta hasta 25 MiB)');
console.log('el service worker guarda ' + ARCHIVOS.length + ' archivos');
if (todos.length > 20000) throw new Error('Cloudflare Pages acepta hasta 20.000 archivos por sitio');
if (Math.max(...tam) > 25 * 1048576) throw new Error('hay un archivo de mas de 25 MiB: Cloudflare Pages no lo acepta');
