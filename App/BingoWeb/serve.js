const http = require('http');
const fs = require('fs');
const path = require('path');

const RAIZ = path.resolve(process.argv[2]);
const PUERTO = parseInt(process.argv[3] || '18160', 10);
const ORGANIZADOR = { host: process.env.ORGANIZER_HOST || '127.0.0.1', port: parseInt(process.env.ORGANIZER_PORT || '18080', 10) };
const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript',
  '.json': 'application/json', '.wasm': 'application/wasm', '.dat': 'application/octet-stream',
  '.dll': 'application/octet-stream', '.pdb': 'application/octet-stream', '.css': 'text/css',
  '.blat': 'application/octet-stream', '.webcil': 'application/octet-stream', '.ico': 'image/x-icon',
  '.png': 'image/png', '.webp': 'image/webp', '.woff2': 'font/woff2', '.webmanifest': 'application/manifest+json',
};
const hora = () => new Date().toTimeString().slice(0, 8);

function leer(req) {
  return new Promise(ok => { const t = []; req.on('data', c => t.push(c)); req.on('end', () => ok(Buffer.concat(t))); });
}

const manejador = async (req, res) => {
  const limpio = decodeURIComponent(req.url.split('?')[0]);

  if (limpio.startsWith('/tell/')) {
    await leer(req);
    res.writeHead(204, { 'Access-Control-Allow-Origin': '*' });
    return res.end();
  }

  if (limpio.startsWith('/api/')) {
    const cuerpo = await leer(req);
    const ruta = limpio.slice(4);
    const salida = http.request({ ...ORGANIZADOR, path: ruta, method: req.method,
      headers: { 'Content-Type': req.headers['content-type'] || 'application/json', 'Content-Length': cuerpo.length } },
      r => {
        const t = [];
        r.on('data', c => t.push(c));
        r.on('end', () => {
          const b = Buffer.concat(t);
          console.log(`${hora()} ${req.method} ${ruta} -> ${r.statusCode} ${b.length}B`);
          res.writeHead(r.statusCode, { 'Content-Type': r.headers['content-type'] || 'text/plain', 'Cache-Control': 'no-store' });
          res.end(b);
        });
      });
    salida.on('error', e => { console.log(`${hora()} ${ruta} -> ERROR ${e.message}`); res.writeHead(502); res.end(e.message); });
    salida.end(cuerpo);
    return;
  }

  const f = path.join(RAIZ, limpio === '/' ? 'index.html' : limpio);
  if (!f.startsWith(RAIZ)) { res.writeHead(403); return res.end(); }
  const gz = (req.headers['accept-encoding'] || '').includes('gzip') && fs.existsSync(f + '.gz');
  fs.readFile(gz ? f + '.gz' : f, (err, cuerpo) => {
    if (err) { res.writeHead(404); return res.end(); }
    const cab = {
      'Content-Type': MIME[path.extname(f).toLowerCase()] || 'application/octet-stream', 'Vary': 'Accept-Encoding',
      'Content-Length': cuerpo.length,
      'Cache-Control': limpio.startsWith('/_framework/') ? 'public, max-age=31536000, immutable' : 'public, max-age=604800',
    };
    if (gz) cab['Content-Encoding'] = 'gzip';
    res.writeHead(200, cab);
    res.end(cuerpo);
  });
};

http.createServer(manejador).listen(PUERTO, '0.0.0.0',
  () => console.log(`jugador web: ${RAIZ} en http://localhost:${PUERTO}; /api -> ${ORGANIZADOR.host}:${ORGANIZADOR.port}`));
