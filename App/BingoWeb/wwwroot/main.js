import { dotnet } from './_framework/dotnet.js';
import { armarComprobante } from './receipt.js?v=2';
import { instalarGuardado } from './saving.js?v=1';

// El codigo REAL de Chela en Bingo.limpio (de las preferencias del 5558), salvo que la URL
// diga otro bingo: ?f=<id>&c=<cartones>&m=<marca>&n=<nombre> (el segundo codigo real de Chela
// es f=260820-145426&c=15&m=2BZ0Q6KT&n=Prueba.del.cambio).
const Q = new URLSearchParams(location.search);
const YO = {
  funcion: Q.get('f') || '260824-081538',
  yo: Q.get('yo') || 'Chela',
  marca: Q.get('m') || '1KS4GFA9',
  cartones: (Q.get('c') || '1').split(',').map(n => parseInt(n, 10)).filter(n => n > 0),
  rol: 'juega',
  nombre: Q.get('n') || 'Bingo.limpio',
};
const BUZON = location.origin + '/tell/';
const LETRAS = ['B', 'I', 'N', 'G', 'O'];
// ?solo=1 apaga el sondeo: lo que se vea salio del journal guardado y de ningun otro lado.
const SOLO = new URLSearchParams(location.search).get('solo') === '1';

const $ = id => document.getElementById(id);
let vistas = [];
let sondeos = 0;
let journaleadas = 0;
let entre = false;
let ultimaRespuesta = { ronda: null, anuncio: '?', premios: [] };
window.__estado = { entre: false };

function log(t) {
  const hora = new Date().toTimeString().slice(0, 8);
  $('registro').textContent += hora + '  ' + t + '\n';
  $('registro').scrollTop = $('registro').scrollHeight;
  console.log('BINGO-WASM ' + t);
}

// ── el destino duradero: IndexedDB, un registro por archivo del journal ─────
const IDB = { base: 'velada-jugador-journal', almacen: 'archivos' };

function abrirIDB() {
  return new Promise((ok, mal) => {
    const r = indexedDB.open(IDB.base, 2);
    r.onupgradeneeded = () => { const db = r.result; if (!db.objectStoreNames.contains(IDB.almacen)) db.createObjectStore(IDB.almacen); if (!db.objectStoreNames.contains('config')) db.createObjectStore('config'); };
    r.onsuccess = () => ok(r.result);
    r.onerror = () => mal(r.error);
  });
}

async function leerArbol() {
  const db = await abrirIDB();
  return new Promise((ok, mal) => {
    const arbol = {};
    const cur = db.transaction(IDB.almacen).objectStore(IDB.almacen).openCursor();
    cur.onsuccess = () => { const c = cur.result; if (!c) return ok(arbol); arbol[c.key] = c.value; c.continue(); };
    cur.onerror = () => mal(cur.error);
  });
}

async function guardarArbol(arbol) {
  const db = await abrirIDB();
  return new Promise((ok, mal) => {
    const tx = db.transaction(IDB.almacen, 'readwrite');
    const st = tx.objectStore(IDB.almacen);
    st.clear();
    let bytes = 0;
    for (const [ruta, b64] of Object.entries(arbol)) { st.put(b64, ruta); bytes += Math.floor(b64.length * 3 / 4); }
    tx.oncomplete = () => ok({ archivos: Object.keys(arbol).length, bytes });
    tx.onerror = () => mal(tx.error);
  });
}

// Cosas sueltas que la pagina recuerda (la carpeta elegida, por ejemplo), en el almacen config.
async function leerConfig(k) { const db = await abrirIDB(); return new Promise((ok, mal) => { const q = db.transaction('config').objectStore('config').get(k); q.onsuccess = () => ok(q.result); q.onerror = () => mal(q.error); }); }
async function guardarConfig(k, v) { const db = await abrirIDB(); return new Promise((ok, mal) => { const tx = db.transaction('config', 'readwrite'); tx.objectStore('config').put(v, k); tx.oncomplete = ok; tx.onerror = () => mal(tx.error); }); }

// La cara viene por columnas del C#; la grilla la quiere por filas, centro libre en (2,2).
function enFilas(columnas) {
  const filas = [];
  for (let f = 0; f < 5; f++) {
    const linea = [];
    for (let b = 0; b < 5; b++) {
      if (b === 2 && f === 2) { linea.push(null); continue; }
      linea.push(columnas[b][(b === 2 && f > 2) ? f - 1 : f]);
    }
    filas.push(linea);
  }
  return filas;
}

function pintarCarton(c, salidas) {
  const set = new Set(salidas);
  const figs = Object.keys(c.completa || {});
  const completas = figs.filter(f => c.completa[f]);
  const celdas = enFilas(c.columnas).map(linea => '<tr>' + linea.map(n =>
    n === null ? '<td class="libre marcada">LIBRE</td>' : '<td' + (set.has(n) ? ' class="marcada"' : '') + '>' + n + '</td>'
  ).join('') + '</tr>').join('');
  return '<div class="carton" data-carton="' + c.numero + '" data-completa="' + (completas.length > 0) + '">'
    + '<div class="rotulo"><span>carton <b>#' + c.numero + '</b></span>'
    + '<span class="veredicto ' + (completas.length ? 'si' : '') + '">'
    + (completas.length ? completas.join(', ') + ' COMPLETA (HasCompleted del actor)' : 'todavia no') + '</span></div>'
    + '<table><thead><tr>' + LETRAS.map(l => '<th>' + l + '</th>').join('') + '</tr></thead><tbody>' + celdas + '</tbody></table></div>';
}

function pintar(t, r, origen) {
  if (!t.ok) {
    $('sub').innerHTML = '<span class="aviso">el actor tiro en ' + t.donde + ': ' + t.error + '</span>';
    log('ACTOR TIRO en ' + t.donde + ': ' + t.error);
    $('voz').textContent = (t.voz || []).join('\n') || '(nada)';
    window.__estado = { entre, error: t.error };
    return;
  }
  const premios = (r.premios || []).map(p => p.figura + ': ' + p.premio).join(' · ');
  $('estado').innerHTML =
    '<span>ronda <b>' + (r.ronda ?? t.rondaGuardada) + '</b></span><span>se juega <b>' + t.figuras.join(', ') + '</b></span>'
    + '<span style="color:var(--suave)">ronda local del actor <b>' + t.ronda + '</b></span>'
    + '<span>anuncio <b>' + (r.anuncio || '?') + '</b></span>' + (premios ? '<span>' + premios + '</span>' : '')
    + '<span>pulso <b style="font-family:ui-monospace,monospace;font-size:12px">' + (t.pulso || '').slice(0, 24) + '</b></span>';

  const nuevas = t.bolitas.filter(n => !vistas.includes(n));
  if (origen === 'journal') log('TABLERO RECONSTRUIDO DESDE EL JOURNAL GUARDADO: ' + t.bolitas.length + ' bolitas [' + t.bolitas.join(', ') + '], sin hablar con el organizador');
  else if (entre) nuevas.forEach(n => log('BOLITA NUEVA ' + n + ' -> RegisterDraw(@numero) en el diario'));
  else log('entre: ' + t.bolitas.length + ' bolitas ya salidas [' + t.bolitas.join(', ') + '], todas journaleadas');
  journaleadas += t.bolitasNuevas || 0;
  if (t.rehizoLaRonda && entre) log('la ronda cambio: MoveOnToRound en el diario, tablero limpio');
  vistas = t.bolitas.slice();

  $('cuantas').textContent = '(' + t.bolitas.length + ')';
  $('bolitas').innerHTML = t.bolitas.map(n => '<div class="bola' + (entre && origen !== 'journal' && nuevas.includes(n) ? ' nueva' : '') + '">' + n + '</div>').join('');
  $('cartones').innerHTML = t.cartones.map(c => pintarCarton(c, t.bolitas)).join('');
  t.cartones.forEach(c => Object.keys(c.completa).forEach(f => {
    if (c.completa[f] && (nuevas.length || !entre || origen === 'journal')) log('carton ' + c.numero + ': ' + f + ' COMPLETA segun HasCompleted del actor');
  }));
  $('asientos').textContent = t.asientos;
  $('journaleadas').textContent = journaleadas;
  $('voz').textContent = (t.voz || []).join('\n') || '(el diario no dijo nada)';
  window.__estado = { entre: true, origen, ronda: t.ronda, bolitas: t.bolitas, asientos: t.asientos, journaleadas, precargados: t.precargados,
    cartones: t.cartones.map(c => ({ numero: c.numero, completa: c.completa })), voz: t.voz };
}

async function alistarme() {
  const res = await fetch('/api/alistarse/', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: AbortSignal.timeout(4000),
    body: JSON.stringify({ funcion: YO.funcion, yo: YO.yo, escuchoEn: BUZON, cartones: YO.cartones, rol: YO.rol, marca: YO.marca }) });
  const texto = await res.text();
  if (!res.ok) throw new Error('HTTP ' + res.status + ' ' + texto.slice(0, 200));
  return JSON.parse(texto);
}

async function volcar() {
  const v = ex.Volcar();
  if (!v) return;
  const r = await guardarArbol(JSON.parse(v));
  $('idb').textContent = r.archivos + ' archivo(s), ' + r.bytes + ' bytes';
  window.__idb = r;
  if (window.__guardado && window.__guardado.escribir) await window.__guardado.escribir('ciclo');
  if (!volcar.yaDicho) { volcar.yaDicho = true; log('journal volcado a IndexedDB: ' + r.archivos + ' archivo(s), ' + r.bytes + ' bytes (y despues de cada ciclo)'); }
}

// El loader pide blazor.boot.json con cache:"no-cache" a la fuerza y con el organizador apagado esa
// revalidacion se queda colgada; los demas archivos si obedecen a disableNoCacheFetch. Para el
// manifiesto le damos nosotros un fetch comun, que acepta lo que Chrome guardo en su cache de disco.
function arrancarDotnet() {
  return dotnet.withConfig({ disableNoCacheFetch: true })
    .withResourceLoader((tipo, nombre, url) => tipo === 'manifest' ? fetch(url, { credentials: 'include' }) : undefined)
    .create();
}

const t0 = performance.now();
let ex;
try {
  // 1. Lo guardado, de vuelta a la memoria del runtime ANTES de arrancar el actor.
  let precargados = 0;
  try {
    const arbol = await leerArbol();
    for (const [ruta, b64] of Object.entries(arbol)) precargados++;
    window.__arbolGuardado = Object.keys(arbol);
    const { getAssemblyExports, getConfig } = await arrancarDotnet();
    ex = (await getAssemblyExports(getConfig().mainAssemblyName)).BingoWeb.Player;
    for (const [ruta, b64] of Object.entries(arbol)) { const r = ex.Precargar(ruta, b64); if (r !== 'ok') log('precarga fallo en ' + ruta + ': ' + r); }
  } catch (e) {
    log('IndexedDB no se pudo leer (' + e.name + '): arranco sin journal previo');
    if (!ex) { const { getAssemblyExports, getConfig } = await arrancarDotnet(); ex = (await getAssemblyExports(getConfig().mainAssemblyName)).BingoWeb.Player; }
  }
  // 2. Arrancar = ConfigureStorage(FileSystem, /journal) + Start(): si habia archivos, rehidrata.
  const arranque = ex.Arrancar();
  $('motor').textContent = arranque + '  (' + Math.round(performance.now() - t0) + ' ms)' + (SOLO ? '  ·  MODO SOLO: sin sondeo' : '');
  log(arranque);
  if (arranque.startsWith('ERROR')) { window.__estado = { error: arranque }; throw new Error(arranque); }
  $('origen').textContent = precargados > 0 ? 'journal guardado en este telefono' : 'sin journal previo';
  // 3. Si el journal traia el bingo, se pinta YA, sin haberle hablado a nadie.
  if (precargados > 0) {
    ex.Preparar(YO.funcion);
    const t = JSON.parse(ex.Estado());
    if (t.ok && t.cartones && t.cartones.length) { $('sub').textContent = YO.yo + ' · numero ' + YO.marca; pintar(t, ultimaRespuesta, 'journal'); entre = true; }
    else log('el journal guardado no traia este bingo (' + (t.error || 'sin cartones') + '); espero al organizador');
  }
} catch (e) {
  $('sub').innerHTML = '<span class="aviso">no arranco: ' + e.message + '</span>';
  console.error('BINGO-WASM-ERROR', e);
  throw e;
}

async function ciclo() {
  let r;
  try { r = await alistarme(); }
  catch (e) { log('no me contestaron: ' + e.message); return; }
  sondeos++; $('sondeos').textContent = sondeos;
  ultimaRespuesta = r;
  const cartones = JSON.stringify(r.cartones || YO.cartones);
  const bolitas = JSON.stringify(r.bolitas || []);
  const t = JSON.parse(entre
    ? ex.PonerAlDia(cartones, r.sejuega || 'AnyLine', r.ronda || 0, bolitas)
    : ex.Entrar(YO.funcion, cartones, r.sejuega || 'AnyLine', r.ronda || 0, bolitas));
  $('sub').textContent = YO.yo + ' · numero ' + YO.marca;
  pintar(t, r, 'organizador');
  entre = entre || t.ok;
  await volcar().catch(e => log('no pude volcar el journal a IndexedDB: ' + e.name));
}

// ── el icono en la pantalla de inicio: un boton propio, y Chrome decide cuanto nos deja ──
// Si Chrome dispara beforeinstallprompt, el boton abre SU dialogo nativo (un toque). Si no lo
// dispara, el boton ensena el camino a mano. Una pagina no puede crear el icono sola.
window.__instalacion = { evento: false, resultado: null, standalone: matchMedia('(display-mode: standalone)').matches, dispararonAppinstalled: false };
let bip = null;
window.addEventListener('beforeinstallprompt', e => { e.preventDefault(); bip = e; window.__instalacion.evento = true; log('Chrome ofrecio instalar (beforeinstallprompt): el boton puede abrir su dialogo'); });
window.addEventListener('appinstalled', () => { window.__instalacion.dispararonAppinstalled = true; log('appinstalled: el icono quedo en la pantalla'); });
$('instalar').onclick = async () => {
  const caja = $('instrucciones');
  if (bip) {
    try { bip.prompt(); const r = await bip.userChoice; window.__instalacion.resultado = r.outcome; log('dialogo de instalacion: ' + r.outcome); bip = null; return; }
    catch (e) { log('el dialogo de instalacion fallo: ' + e.message); }
  }
  const enCustomTab = !('serviceWorker' in navigator) && /wv|Version\/[\d.]+.*Chrome/i.test(navigator.userAgent);
  caja.style.display = 'block';
  caja.innerHTML = '<b>Chrome no me deja poner el icono por vos</b> (esta pagina llega por http, sin certificado). Hacelo asi, son tres toques:<br>'
    + '1. Toca los tres puntos <b>⋮</b> arriba a la derecha.<br>2. Toca <b>Agregar a pantalla de inicio</b> (o "Instalar aplicacion" si aparece).<br>3. Toca <b>Agregar</b>.<br>'
    + '<span style="color:var(--suave)">Si en vez de la barra de Chrome ves una <b>×</b> arriba, primero toca <b>Abrir en Chrome</b> en ese menu: estas en una pestana de otra app.'
    + (enCustomTab ? ' (parece que es tu caso)' : '') + '</span>';
  window.__instalacion.resultado = 'instrucciones';
  log('sin beforeinstallprompt en este origen: mostre las instrucciones a mano');
};
if (window.__instalacion.standalone) { $('instalar').style.display = 'none'; log('abierto desde el icono instalado (display-mode: standalone)'); }

// ── el comprobante que la persona se lleva: el journal del actor viaja ADENTRO del archivo ──
// El comprobante de este momento: el mismo HTML sirve para bajarlo o para escribirlo en una carpeta.
function armarComprobanteActual() {
  const resumen = JSON.parse(ex.Estado());
  if (!resumen.ok) { log('no puedo armar el comprobante: ' + resumen.error); return null; }
  resumen.nombre = YO.nombre;
  const journal = JSON.parse(ex.ExportarJournal());
  return { html: armarComprobante(YO.funcion, resumen, journal, YO.yo + ' · ' + YO.marca), archivos: Object.keys(journal).length, bolitas: resumen.bolitas.length };
}
window.descargarComprobante = async function () {
  try {
    const c = armarComprobanteActual(); if (!c) return null;
    const nombre = 'velada-' + YO.funcion + '-' + new Date().toTimeString().slice(0, 8).replace(/:/g, '') + '.html';
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([c.html], { type: 'text/html' }));
    a.download = nombre;
    document.body.appendChild(a); a.click(); a.remove();
    $('guardado').textContent = 'bajado: ' + nombre + ' (' + Math.round(c.html.length / 1024) + ' KB, ' + c.archivos + ' archivo(s) del journal adentro)';
    log('COMPROBANTE BAJADO: ' + nombre + ' con ' + c.archivos + ' archivo(s) del journal y ' + c.bolitas + ' bolitas');
    window.__ultimoComprobante = nombre;
    return nombre;
  } catch (e) { log('no pude armar el comprobante: ' + e.message); return null; }
};
$('guardar').onclick = () => window.descargarComprobante();
instalarGuardado({ $, log, YO, leerConfig, guardarConfig, guardarArbol, armarComprobanteActual });

// ── lo que solo existe en contexto seguro: service worker, persist(), OPFS, carpeta ──
window.__seguro = { isSecureContext: window.isSecureContext, serviceWorker: 'serviceWorker' in navigator, storage: !!navigator.storage,
  persist: !!(navigator.storage && navigator.storage.persist), opfs: !!(navigator.storage && navigator.storage.getDirectory),
  showDirectoryPicker: typeof window.showDirectoryPicker, cryptoSubtle: !!(window.crypto && crypto.subtle), sw: 'no registrado', persistido: null, precache: null };
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('./sw.js').then(async reg => {
    window.__seguro.sw = 'registrado (' + (reg.active ? 'activo' : reg.installing ? 'instalando' : reg.waiting ? 'esperando' : '?') + ')';
    log('service worker registrado: precachando la pagina y el runtime .NET');
    if (navigator.storage && navigator.storage.persist) { window.__seguro.persistido = await navigator.storage.persist(); log('navigator.storage.persist() -> ' + window.__seguro.persistido); }
  }).catch(e => { window.__seguro.sw = 'fallo: ' + e.message; log('service worker: ' + e.message); });
} else {
  log('sin service worker: este origen no es contexto seguro (' + location.origin + ')');
}

if (SOLO) {
  log('modo solo: no le pregunto nada al organizador');
} else {
  await ciclo();
  setInterval(ciclo, 3000);
}
