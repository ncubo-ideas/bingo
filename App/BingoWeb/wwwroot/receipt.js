// Arma el comprobante que la persona se lleva: UN archivo .html con el journal del actor
// adentro (los archivos en base64) mas el resumen que el actor calculo. Al abrirse en file://
// deposita todo en SU libreta (localStorage del origen file://) y muestra las veladas
// acumuladas. IndexedDB es por origen: por eso el journal viaja ADENTRO del archivo.
export function armarComprobante(festivalId, resumen, journal, quien) {
  const esc = t => String(t == null ? '' : t).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const meses = ['enero','febrero','marzo','abril','mayo','junio','julio','agosto','septiembre','octubre','noviembre','diciembre'];
  const fecha = id => { const m = /^(\d{2})(\d{2})(\d{2})-(\d{2})(\d{2})/.exec(id || ''); return m ? parseInt(m[3],10) + ' de ' + (meses[parseInt(m[2],10)-1]||m[2]) + ' de 20' + m[1] + ', ' + m[4] + ':' + m[5] : (id || 'sin fecha'); };

  const DATOS = JSON.stringify({ festival: festivalId, quien, guardado: new Date().toISOString(), resumen, journal });

  const ESTILO = [
    ':root{--papel:#efece4;--hondo:#e5e1d6;--tinta:#211e1a;--suave:#6b665c;--sello:#7a2e3c;--linea:#cdc7b8;--marca:#c9a24a;--nueva:#2f7a4a}',
    '*{box-sizing:border-box}body{margin:0;padding:0 16px 40px;background:var(--papel);color:var(--tinta);font:16px/1.5 system-ui,-apple-system,Roboto,sans-serif}',
    '.hoja{max-width:560px;margin:0 auto}header{padding:24px 0 12px;border-bottom:2px solid var(--sello)}',
    '.cinta{font:600 11px/1 system-ui;letter-spacing:.14em;text-transform:uppercase;color:var(--sello);margin:0 0 8px}',
    'h1{margin:0 0 4px;font:400 26px/1.15 Georgia,serif}.sub{margin:0;color:var(--suave);font-size:14px}',
    'h2{margin:24px 0 6px;font:600 12px/1 system-ui;letter-spacing:.14em;text-transform:uppercase;color:var(--suave)}',
    '.bolitas{display:flex;flex-wrap:wrap;gap:6px}.bola{width:36px;height:36px;border-radius:50%;display:flex;align-items:center;justify-content:center;background:var(--hondo);border:1px solid var(--linea);font-weight:600}',
    '.carton{background:var(--hondo);border-radius:4px;padding:12px;margin-top:10px}.rotulo{display:flex;justify-content:space-between;font-size:13px;color:var(--suave);margin-bottom:8px}.rotulo b{color:var(--sello)}',
    '.si{color:var(--nueva);font-weight:600}table{width:100%;border-collapse:collapse;table-layout:fixed}th{font:600 13px/1 system-ui;letter-spacing:.1em;color:var(--sello);padding-bottom:6px}',
    'td{text-align:center;padding:8px 2px;font-size:17px;border:1px solid var(--linea);background:var(--papel)}td.marcada{background:var(--marca);font-weight:700}td.libre{color:var(--suave);font-size:10px}',
    '.lista{font-family:ui-monospace,Consolas,monospace;font-size:12px;line-height:1.6;margin:0;padding:10px 12px;background:var(--hondo);border-radius:3px;white-space:pre-wrap}',
    '.velada{padding:10px 12px;background:var(--hondo);border-radius:3px;margin-top:8px;font-size:14px}.velada b{color:var(--sello)}',
  ].join('');

  const DIBUJO = [
    "(function () {",
    "  'use strict';",
    "  var DATOS = " + DATOS + ";",
    "  var LETRAS = ['B','I','N','G','O'];",
    "  var $ = function (id) { return document.getElementById(id); };",
    "  function enFilas(c) { var filas = []; for (var f = 0; f < 5; f++) { var l = []; for (var b = 0; b < 5; b++) { if (b === 2 && f === 2) { l.push(null); continue; } l.push(c[b][(b === 2 && f > 2) ? f - 1 : f]); } filas.push(l); } return filas; }",
    "  function carton(c, salidas) { var set = {}; salidas.forEach(function (n) { set[n] = true; }); var figs = Object.keys(c.completa || {}).filter(function (f) { return c.completa[f]; });",
    "    return '<div class=\"carton\"><div class=\"rotulo\"><span>carton <b>#' + c.numero + '</b></span><span class=\"' + (figs.length ? 'si' : '') + '\">' + (figs.length ? figs.join(', ') + ' COMPLETA' : 'sin completar') + '</span></div><table><thead><tr>' + LETRAS.map(function (l) { return '<th>' + l + '</th>'; }).join('') + '</tr></thead><tbody>' + enFilas(c.columnas).map(function (fila) { return '<tr>' + fila.map(function (n) { return n === null ? '<td class=\"libre marcada\">LIBRE</td>' : '<td' + (set[n] ? ' class=\"marcada\"' : '') + '>' + n + '</td>'; }).join('') + '</tr>'; }).join('') + '</tbody></table></div>'; }",
    "  var r = DATOS.resumen;",
    "  $('bolitas').innerHTML = r.bolitas.map(function (n) { return '<div class=\"bola\">' + n + '</div>'; }).join('');",
    "  $('cuantas').textContent = '(' + r.bolitas.length + ')';",
    "  $('cartones').innerHTML = r.cartones.map(function (c) { return carton(c, r.bolitas); }).join('');",
    "  var archivos = Object.keys(DATOS.journal); var bytes = archivos.reduce(function (s, k) { return s + Math.floor(DATOS.journal[k].length * 3 / 4); }, 0);",
    "  $('journal').textContent = archivos.map(function (a) { return a + '  (' + Math.floor(DATOS.journal[a].length * 3 / 4) + ' bytes)'; }).join('\\n') + '\\n' + bytes + ' bytes en total, tal como los escribio el actor Puppeteer';",
    "  // La libreta de ESTE origen (file://): se deposita esta velada y se listan todas las guardadas.",
    "  var sondas = [['origen', location.protocol], ['isSecureContext', String(window.isSecureContext)]];",
    "  try {",
    "    var libreta = JSON.parse(localStorage.getItem('velada.libreta') || '{}');",
    "    var previa = libreta[DATOS.festival];",
    "    if (!previa || (previa.resumen.bolitas || []).length <= r.bolitas.length) libreta[DATOS.festival] = { quien: DATOS.quien, guardado: DATOS.guardado, resumen: r, journal: DATOS.journal };",
    "    localStorage.setItem('velada.libreta', JSON.stringify(libreta));",
    "    var ids = Object.keys(libreta).sort();",
    "    sondas.push(['libreta (localStorage de file://)', ids.length + ' velada(s) guardada(s): ' + ids.join(', ')]);",
    "    $('veladas').innerHTML = ids.map(function (id) { var v = libreta[id]; var ganadas = (v.resumen.cartones || []).filter(function (c) { return Object.keys(c.completa || {}).some(function (f) { return c.completa[f]; }); }).length;",
    "      return '<div class=\"velada\"><b>' + id + '</b> · ' + (v.resumen.cartones || []).length + ' carton(es) · ' + (v.resumen.bolitas || []).length + ' bolitas · ' + (ganadas ? ganadas + ' completo(s)' : 'sin completar') + ' · guardado ' + (v.guardado || '').slice(0, 16).replace('T', ' ') + '</div>'; }).join('');",
    "  } catch (e) { sondas.push(['libreta', 'TIRA ' + e.name]); }",
    "  // Y la prueba cruzada: la base de IndexedDB del origen http NO se ve desde aca.",
    "  new Promise(function (ok) { var t = setTimeout(function () { ok('sin respuesta en 3 s'); }, 3000); try { var q = indexedDB.open('velada-jugador-journal'); q.onsuccess = function () { clearTimeout(t); var db = q.result; var tiene = db.objectStoreNames.contains('archivos'); if (!tiene) { ok('base vacia: 0 almacenes (es OTRO origen)'); db.close(); return; } var c = db.transaction('archivos').objectStore('archivos').count(); c.onsuccess = function () { ok(c.result + ' registro(s)'); db.close(); }; }; q.onerror = function () { clearTimeout(t); ok('TIRA ' + (q.error && q.error.name)); }; q.onblocked = function () { clearTimeout(t); ok('BLOCKED'); }; } catch (e) { clearTimeout(t); ok('EXC ' + e.name); } })",
    "    .then(function (res) { sondas.push(['IndexedDB velada-jugador-journal desde file://', res]); pintaSondas(); });",
    "  function pintaSondas() { $('sonda').innerHTML = sondas.map(function (s) { return '<div><span>' + s[0] + '</span><b>' + s[1] + '</b></div>'; }).join(''); window.__comprobante = { festival: DATOS.festival, bolitas: r.bolitas, cartones: r.cartones.length, journalArchivos: archivos.length, sondas: sondas }; }",
    "  pintaSondas();",
    "  console.log('BINGO-RECEIPT ' + JSON.stringify({ festival: DATOS.festival, bolitas: r.bolitas.length, archivos: archivos.length }));",
    "})();",
  ].join('\n');

  return [
    '<!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">',
    '<title>' + esc(resumen.nombre || 'Bingo') + ' — mi comprobante</title><meta name="google" content="notranslate"><style>' + ESTILO + '</style></head><body><div class="hoja">',
    '<header><p class="cinta">Mi comprobante · con el journal adentro</p><h1>' + esc(resumen.nombre || 'Bingo.limpio') + '</h1><p class="sub">' + esc(fecha(festivalId)) + ' · ' + esc(quien) + '</p></header>',
    '<h2>Bolitas <span id="cuantas" style="color:var(--suave);font-weight:400"></span></h2><div class="bolitas" id="bolitas"></div>',
    '<h2>Mis cartones · el veredicto es del actor</h2><div id="cartones"></div>',
    '<h2>Mis veladas guardadas en este teléfono</h2><div id="veladas"></div>',
    '<h2>El journal que viaja adentro</h2><pre class="lista" id="journal"></pre>',
    '<h2>Sonda</h2><div class="lista" id="sonda" style="font-family:ui-monospace,Consolas,monospace"></div>',
    // Los datos tambien van en un bloque JSON aparte: es lo que lee 'Traer mi comprobante' para reimportar el journal.
    '<script type="application/json" id="velada-datos">' + DATOS.split('<').join(String.fromCharCode(92) + 'u003c') + '<' + '/script>',
    '</div><script>' + DIBUJO + '<' + '/script></body></html>',
  ].join('\n');
}
