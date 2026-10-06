// Genera los iconos del acceso directo sin ninguna libreria: un PNG RGBA a mano con zlib.
// Una bola de bingo: fondo del color del sello, circulo claro, y el numero 75 en bloques.
//   node make-icons.js <carpeta destino>
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const DESTINO = path.resolve(process.argv[2] || 'wwwroot');

function crc32(buf) {
  let c, crc = 0xffffffff;
  for (let n = 0; n < buf.length; n++) {
    c = (crc ^ buf[n]) & 0xff;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    crc = (crc >>> 8) ^ c;
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(tipo, datos) {
  const largo = Buffer.alloc(4); largo.writeUInt32BE(datos.length);
  const cuerpo = Buffer.concat([Buffer.from(tipo, 'ascii'), datos]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(cuerpo));
  return Buffer.concat([largo, cuerpo, crc]);
}

// Los digitos como matrices 3x5, para escribir "75" sin fuentes.
const DIGITOS = {
  '7': ['111', '001', '010', '010', '010'],
  '5': ['111', '100', '111', '001', '111'],
};

function png(tam) {
  const fondo = [0x7a, 0x2e, 0x3c];      // el sello (burdeos)
  const bola = [0xef, 0xec, 0xe4];       // el papel
  const tinta = [0x21, 0x1e, 0x1a];
  const filas = [];
  const cx = tam / 2, cy = tam / 2, r = tam * 0.36;

  // El "75" centrado en la bola: cada digito 3x5 bloques.
  const bloque = Math.max(1, Math.round(tam * 0.05));
  const anchoTexto = (3 + 1 + 3) * bloque;
  const x0 = Math.round(cx - anchoTexto / 2), y0 = Math.round(cy - (5 * bloque) / 2);

  for (let y = 0; y < tam; y++) {
    const fila = Buffer.alloc(1 + tam * 4);
    fila[0] = 0;
    for (let x = 0; x < tam; x++) {
      let c = fondo;
      const dx = x - cx, dy = y - cy;
      if (dx * dx + dy * dy <= r * r) {
        c = bola;
        const bx = Math.floor((x - x0) / bloque), by = Math.floor((y - y0) / bloque);
        if (by >= 0 && by < 5) {
          const digito = bx >= 0 && bx < 3 ? DIGITOS['7'] : (bx >= 4 && bx < 7 ? DIGITOS['5'] : null);
          const col = bx < 3 ? bx : bx - 4;
          if (digito && digito[by][col] === '1') c = tinta;
        }
      }
      const o = 1 + x * 4;
      fila[o] = c[0]; fila[o + 1] = c[1]; fila[o + 2] = c[2]; fila[o + 3] = 255;
    }
    filas.push(fila);
  }

  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(tam, 0); ihdr.writeUInt32BE(tam, 4);
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(Buffer.concat(filas), { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

for (const [nombre, tam] of [['favicon.png', 64], ['icon-192.png', 192], ['icon-512.png', 512]]) {
  const b = png(tam);
  fs.writeFileSync(path.join(DESTINO, nombre), b);
  console.log(nombre.padEnd(14) + tam + 'x' + tam + '  ' + b.length + ' bytes');
}
