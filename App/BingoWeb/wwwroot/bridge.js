// La "app" de la pagina servida: hace lo que en el APK hacen PlayerScreen y LivePlayer, con el actor
// de Puppeteer corriendo en .NET dentro del navegador. player.js no sabe la diferencia: recibe el mismo
// estado por el mismo evento.
import { dotnet } from './_framework/dotnet.js';

const Q = new URLSearchParams(location.search);
// En la copia de internet los datos del jugador van despues del #, asi nunca llegan al servidor que sirve la
// pagina. Servida por el organizador van en la query, como siempre. Se leen de los dos lados, primero el #.
const H = desdeElSello(new URLSearchParams(location.hash.slice(1)));
function desdeElSello(h) {
  if (h.get('f') || !h.get('d')) return h;
  try {
    const j = leerCodigo('d=' + h.get('d'));
    return new URLSearchParams({
      f: j.r || '', c: (j.c || []).join(','), m: j.m || '', n: j.n || '', yo: j.yo || '',
      o: (j.e || '').replace(/\/+$/, ''), i: j.i || '',
    });
  } catch (e) { return h; }
}
const dato = k => H.get(k) || Q.get(k) || '';
const YO = {
  funcion: dato('f'),
  yo: dato('yo'),
  marca: dato('m'),
  cartones: dato('c').split(',').map(n => parseInt(n, 10)).filter(n => n > 0),
  rol: 'juega',
  nombre: dato('n'),
  ticket: dato('i'),
};
const TENGO_CODIGO = !!(YO.funcion && YO.cartones.length);
const origenDe = u => { try { return new URL(u).origin; } catch (e) { return ''; } };
// La copia de internet se marca en su index.html. Ahi el organizador no es este origen: la direccion de su API en
// la red local llega en el codigo (o=http://192.168.0.8:18140/api) y se guarda con cada bingo.
const EN_INTERNET = (document.querySelector('meta[name="bingo-site"]') || {}).content === 'internet';
const API = (dato('o') || (EN_INTERNET ? '' : location.origin + '/api')).replace(/\/+$/, '');
// El https del mismo organizador: en esta prueba el proxy sirve http en 18140 y https en 18443.
const PUERTO_HTTPS = Q.get('hp') || (location.port === '18140' ? '18443' : '');

// Si el telefono ya confia en la autoridad del bingo, el https abre sin aviso: la pagina lo prueba en
// silencio y salta sola. Un error de certificado hace fallar el fetch enseguida; asi se distingue.
async function confiaEnElBingo() {
  if (!PUERTO_HTTPS) return false;
  try {
    await fetch('https://' + location.hostname + ':' + PUERTO_HTTPS + '/favicon.png?prueba=' + Date.now(),
      { mode: 'no-cors', cache: 'no-store', signal: AbortSignal.timeout(2500) });
    return true;
  } catch (e) { return false; }
}

function aHttps() {
  location.replace('https://' + location.hostname + ':' + PUERTO_HTTPS + location.pathname + location.search);
}
const BUZON = (origenDe(API) || location.origin) + '/tell/';
const IDB = { base: 'velada-jugador-journal', almacen: 'archivos' };
// La version de la pagina viaja en la URL de este modulo (bridge.js?v=N). El arranque de .NET no trae huella:
// pedirlo con la version hace que una pagina nueva baje el suyo y que sin red se use el de su version.
const VERSION = new URL(import.meta.url).searchParams.get('v') || '1';

// El idioma lo decide este puente, que hace de telefono: la eleccion guardada o, si no hay, el del navegador. Las
// frases salen del mismo texts.json de la app; se lo manda a la pagina en el 'ready', antes que nada, como PlayerScreen.
const IDIOMA_GUARDADO = 'velada-idioma';
const TODOS_LOS_TEXTOS = await fetch('texts.json?v=' + VERSION).then(r => r.json()).catch(() => ({}));
const aIdioma = tag => String(tag || '').trim().toLowerCase().startsWith('en') ? 'en' : 'es';
let idioma = aIdioma((() => { try { return localStorage.getItem(IDIOMA_GUARDADO); } catch (e) { return null; } })() || navigator.language);
let frases = {};
function armarFrases() {
  frases = {};
  for (const [clave, dicha] of Object.entries(TODOS_LOS_TEXTOS)) {
    if (!dicha || typeof dicha !== 'object') continue;
    frases[clave] = dicha[idioma] || dicha.es || '';
  }
}
armarFrases();
queueMicrotask(() => window.__empujar(mensajeDeTextos()));
function T(clave, huecos) {
  let dicho = frases[clave] || clave;
  for (const [hueco, valor] of Object.entries(huecos || {})) dicho = dicho.split('{' + hueco + '}').join(String(valor));
  return dicho;
}
const mensajeDeTextos = () => ({ kind: 'textos', idioma, textos: frases });
// Los avisos se guardan como clave y se traducen al mandarlos: si la persona cambia de idioma, el aviso cambia con ella.
const P = (clave, huecos) => [clave, huecos];
const dicho = p => (p ? T(p[0], p[1]) : '');
const dormir = ms => new Promise(r => setTimeout(r, ms));

// La entrada (entry.js): la tombola mientras se conecta y el carton repartido mientras se pone al dia. Solo se
// abre cuando se llega con un codigo; sin codigo la pagina abre en la puerta, como la app.
let entrando = false;
function abrirEntrada() {
  if (!TENGO_CODIGO || !window.Entrada) return;
  entrando = true;
  // El relay de iroh se levanta ya, mientras arranca .NET: cuando el actor pida el bus, el endpoint esta listo.
  if (YO.ticket) import(MODULO_IROH).then(m => m.bind()).catch(() => { });
  Entrada.abrir({
    titulo: T('jugador.web.entrada.titulo'),
    pie: YO.nombre ? T('jugador.web.entrada.pie', { bingo: YO.nombre }) : '',
    pasos: [
      { t: T('jugador.web.entrada.buscando'), s: T('jugador.web.entrada.buscando-sub') },
      { t: T('jugador.web.entrada.trayendo'), s: T('jugador.web.entrada.trayendo-sub') },
      { t: T('jugador.web.entrada.al-dia'), s: T('jugador.web.entrada.al-dia-sub') },
      { t: T('jugador.web.entrada.a-jugar'), s: '' },
    ],
  });
  // Si el organizador no aparece, que la persona no quede encerrada: el motivo a la vista y una salida.
  setTimeout(() => {
    if (!entrando) return;
    if (problema) Entrada.paso(0, 'ahora', null, dicho(problema));
    Entrada.boton(T('jugador.web.entrada.ver-igual'), () => { entrando = false; Entrada.cerrar(); });
  }, 15000);
}
function encontreAlOrganizador() {
  if (!entrando) return;
  Entrada.paso(0, 'hecho', T('jugador.web.entrada.encontre'), T('jugador.web.entrada.encontre-sub'));
  Entrada.paso(1, 'ahora');
}
// Con el bingo ya guardado en este navegador la mesa se abre sin esperar al organizador: el diario tiene los
// cartones y las bolitas que el organizador ya habia dado. Se conecta por detras; lo que haya salido mientras tanto
// llega con la respuesta del alta y se avisa con el "te pusimos al dia".
let guardadas = null;
async function terminarEntrada(desdeGuardado) {
  if (!entrando) return;
  if (desdeGuardado) {
    guardadas = pantalla ? pantalla.drawOrder.slice() : [];
    Entrada.paso(0, 'hecho', T('jugador.web.entrada.guardada'), T('jugador.web.entrada.guardada-sub'));
  } else encontreAlOrganizador();
  entrando = false;
  const cartones = pantalla ? pantalla.cards.map(c => c.number) : YO.cartones;
  Entrada.paso(1, 'hecho', T('jugador.web.entrada.llegaron'),
    T(cartones.length === 1 ? 'jugador.web.entrada.llego-uno' : 'jugador.web.entrada.llegaron-varios', { lista: cartones.join(', ') }));
  Entrada.paso(2, 'ahora');
  await dormir(800);
  const carton = pantalla && pantalla.cards[0];
  if (carton) {
    const suyos = new Set(carton.grid.flat());
    const salieron = pantalla.drawOrder || [];
    const sellar = salieron.filter(n => suyos.has(n));
    const cuenta = n => !salieron.length ? T('jugador.web.entrada.ninguna')
      : !n ? T('jugador.web.entrada.ninguna-tuya', { n: salieron.length })
      : n === 1 ? T('jugador.web.entrada.marque-una')
      : T('jugador.web.entrada.marque-varias', { n });
    await Entrada.reparto({
      titulo: T('jugador.web.entrada.lista'),
      cabecera: T('jugador.web.entrada.carton', { numero: carton.number }),
      mas: pantalla.cards.length > 1 ? T('jugador.web.entrada.y-mas', { n: pantalla.cards.length - 1 }) : '',
      grid: carton.grid, sellar, cuenta, orden: salieron,
    });
  }
  await Entrada.cerrar();
}
// "el 50 y el 53" / "50 and 53"
function listaDeBolitas(nums) {
  const dichas = nums.map(n => T('jugador.web.al-dia-numero', { n }));
  return dichas.length < 2 ? dichas.join('') : dichas.slice(0, -1).join(', ') + T('jugador.web.al-dia-y') + dichas[dichas.length - 1];
}

const registro = [];
function log(t) {
  registro.push(new Date().toTimeString().slice(0, 8) + '  ' + t);
  if (registro.length > 400) registro.shift();
  console.log('BINGO-WEB ' + t);
}

function abrirIDB() {
  return new Promise((ok, mal) => {
    const r = indexedDB.open(IDB.base, 2);
    r.onupgradeneeded = () => {
      const db = r.result;
      if (!db.objectStoreNames.contains(IDB.almacen)) db.createObjectStore(IDB.almacen);
      if (!db.objectStoreNames.contains('config')) db.createObjectStore('config');
    };
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
    for (const [ruta, b64] of Object.entries(arbol)) st.put(b64, ruta);
    tx.oncomplete = () => ok(Object.keys(arbol).length);
    tx.onerror = () => mal(tx.error);
  });
}

let ex = null;
let entre = false;
let ultima = null;
let conectado = false;
let problema = '';
let catalogo = null;
let figurasDelMotor = null;
let pantalla = null;
let gritado = false;
let mostrarPuerta = false;
const festejados = new Set();

function nombreFigura(f) { return (catalogo && catalogo.names && catalogo.names[f]) || f; }

function localizarFiguras() {
  if (!figurasDelMotor) return;
  const c = JSON.parse(JSON.stringify(figurasDelMotor));
  for (const f of c.figures || []) {
    const nombre = frases['figura.' + f + '.nombre'];
    const pista = frases['figura.' + f + '.pista'];
    if (nombre) c.names[f] = nombre;
    if (pista) c.hints[f] = pista;
  }
  catalogo = c;
}

function lineaDePremio(r) {
  return ((r && r.premios) || []).map(p => nombreFigura(p.figura) + ' ' + (p.esCash ? '$' : '') + p.premio).join('  ·  ');
}

function bonito(marca) {
  const s = (marca || '').toUpperCase();
  return s.length === 8 ? s.slice(0, 4) + '-' + s.slice(4) : s;
}

function ganados() {
  const lista = [];
  for (const c of (pantalla && pantalla.cards) || [])
    for (const f of c.figures || []) if (f.called) lista.push({ carton: c.number, figura: f.name });
  return lista;
}

// Los bingos en los que entro este telefono. El icono de la pantalla de inicio abre la raiz, sin codigo: con esta
// lista la pagina muestra MIS BINGOS como la app en vez de una entrada vacia. Si se pierde, se entra con el QR.
const MIS_BINGOS = 'velada-mis-bingos';
function misBingos() {
  try { const l = JSON.parse(localStorage.getItem(MIS_BINGOS) || '[]'); return Array.isArray(l) ? l : []; } catch (e) { return []; }
}

function recordarEsteBingo(cartones) {
  if (!TENGO_CODIGO) return;
  const q = new URLSearchParams({ f: YO.funcion, c: YO.cartones.join(','), m: YO.marca, n: YO.nombre, yo: YO.yo });
  if (Q.get('hp')) q.set('hp', Q.get('hp'));
  if (EN_INTERNET && API) q.set('o', API);
  const lista = misBingos().filter(b => b.id !== YO.funcion);
  lista.unshift({ id: YO.funcion, name: YO.nombre || YO.funcion, q: q.toString(), cards: cartones, last: Date.now() });
  try { localStorage.setItem(MIS_BINGOS, JSON.stringify(lista.slice(0, 12))); } catch (e) { log(T('jugador.web.log.no-recorde', { motivo: e.name })); }
}

// Abre un bingo en esta misma pagina. En la copia de internet va despues del #, y como cambiar el # no recarga, se
// recarga a mano. Servida por el organizador va en la query.
function irA(params) {
  if (EN_INTERNET) { location.hash = params.toString(); location.reload(); }
  else location.search = '?' + params.toString();
}

// Desde internet, Chrome pregunta la primera vez si la pagina puede hablarle a un aparato de la red local. Se avisa
// antes, para que la persona sepa que tocar, y si lo nego se le dice donde volver a activarlo.
let redLocal = 'granted';
async function mirarRedLocal() {
  for (const nombre of ['local-network', 'local-network-access']) {
    try {
      const s = await navigator.permissions.query({ name: nombre });
      redLocal = s.state;
      s.onchange = () => { redLocal = s.state; log(T('jugador.web.log.permiso-red-local', { estado: redLocal })); };
      return;
    } catch (e) { }
  }
}
function avisoDeRedLocal() {
  return redLocal === 'denied'
    ? P('jugador.web.red-local.negada')
    : P('jugador.web.red-local.pregunta');
}

function bingosParaLaPantalla(p) {
  const guardados = misBingos().map(b => ({ id: b.id, name: b.name, active: false, cards: b.cards }));
  if (!TENGO_CODIGO) { if (guardados.length) guardados[0].active = true; return guardados; }
  const aqui = { id: YO.funcion, name: YO.nombre, active: true, connected: conectado, cards: (p.cards || []).length || YO.cartones.length };
  return [aqui, ...guardados.filter(b => b.id !== YO.funcion)];
}

function estado() {
  const p = pantalla || { drawn: [], drawOrder: [], lastDraw: 0, playing: [], cards: [] };
  const g = ganados();
  return {
    kind: 'state',
    door: !TENGO_CODIGO || mostrarPuerta,
    canGoBack: TENGO_CODIGO && mostrarPuerta,
    askName: false,
    trouble: dicho(problema),
    who: YO.yo,
    festivalName: YO.nombre,
    badge: { number: YO.marca, pretty: bonito(YO.marca), name: YO.yo, qr: '' },
    festivals: bingosParaLaPantalla(p),
    round: { position: (ultima && ultima.ronda) || 0, playing: p.playing || [], prizeLine: lineaDePremio(ultima), between: false, over: false },
    drawn: p.drawn || [],
    drawOrder: p.drawOrder || [],
    lastDraw: p.lastDraw || 0,
    cards: p.cards || [],
    bingo: g.length ? T('jugador.bingo-con-carton', { figura: nombreFigura(g[0].figura), carton: g[0].carton }) : null,
    canShout: g.length > 0 && !gritado,
    shouted: gritado,
  };
}

function empujar() { window.__empujar(estado()); }

function leerPantalla(festejar) {
  const t = JSON.parse(ex.Pantalla());
  if (!t.ok) { log(T('jugador.web.log.sin-tablero', { error: t.error })); return; }
  pantalla = t;
  for (const c of t.cards)
    for (const f of c.figures) {
      const clave = c.number + '|' + f.name + '|' + ((ultima && ultima.ronda) || 0);
      if (!f.called || festejados.has(clave)) continue;
      festejados.add(clave);
      if (festejar) {
        log(T('jugador.web.log.bingo-completo', { carton: c.number, figura: nombreFigura(f.name) }));
        window.__empujar({ kind: 'celebrate', text: T('jugador.bingo-con-carton', { figura: nombreFigura(f.name), carton: c.number }) });
      }
    }
}

function explicarNo(status, texto) {
  try { const j = JSON.parse(texto); if (j && j.dice) return j.dice; } catch (e) { }
  return T('jugador.web.organizador-contesto', { estado: status });
}

async function ciclo() {
  let r;
  if (!API) { problema = P('jugador.web.codigo-sin-organizador'); empujar(); return; }
  try {
    // text/plain y no JSON: asi Chrome no manda la consulta previa de CORS, que el organizador no sabe contestar.
    const res = await fetch(API + '/alistarse/', {
      method: 'POST', headers: { 'Content-Type': 'text/plain;charset=UTF-8' }, signal: AbortSignal.timeout(4000),
      body: JSON.stringify({ funcion: YO.funcion, yo: YO.yo, escuchoEn: BUZON, cartones: YO.cartones, rol: YO.rol, marca: YO.marca }),
    });
    const texto = await res.text();
    if (!res.ok) throw new Error(explicarNo(res.status, texto));
    r = JSON.parse(texto);
  } catch (e) {
    if (conectado || !problema) log(T('jugador.web.log.no-contestaron', { motivo: e.message }));
    conectado = false;
    problema = EN_INTERNET && redLocal !== 'granted' ? avisoDeRedLocal()
      : entre ? P('jugador.web.sin-conexion')
      : P('jugador.web.todavia-no-alcanzo');
    empujar();
    return;
  }
  await usarRespuesta(r);
}

// Por iroh, si el codigo trae el ticket del organizador y el jugador web se armo con el bus del navegador: el
// alta va por el bus y las bolitas llegan empujadas, sin sondeo. Si no engancha, queda el HTTP de siempre.
let porIroh = false;
let reenganchando = false;
let fallosSeguidos = 0;
let reintento = null;
let sondeo = null;
const MODULO_IROH = new URL('iroh/iroh-browser.js', location.href).href;
const pedidoIroh = () => JSON.stringify({ funcion: YO.funcion, yo: YO.yo, escuchoEn: BUZON, cartones: YO.cartones, rol: YO.rol, marca: YO.marca });

function arrancarSondeo() { if (!sondeo) { sondeo = setInterval(ciclo, 3000); ciclo(); } }
function pararSondeo() { if (sondeo) { clearInterval(sondeo); sondeo = null; } }

async function entrarPorIroh() {
  if (!YO.ticket || typeof ex.EntrarPorIroh !== 'function') return false;
  window.__bingoNotice = que => {
    if (que === 'cerrado') { reengancharIroh('se cayo el enlace'); return; }
    const antes = pantalla ? pantalla.drawOrder.length : 0;
    leerPantalla(true);
    const ahora = pantalla ? pantalla.drawOrder.length : 0;
    // Una ronda nueva deja el tablero sin bolitas. La ronda del organizador viaja en la respuesta del alta y no en
    // el aviso: sin pedirla de nuevo, las bolitas de la ronda nueva se descartarian como tardias.
    if (ahora < antes) { log('iroh: arranco otra ronda'); empujar(); reengancharIroh('ronda nueva'); return; }
    if (ahora > antes) log('iroh: me llego la bolita ' + pantalla.lastDraw);
    empujar();
  };
  const t0 = performance.now();
  const vigia = setInterval(() => { try { if (ex.EnlacesIroh() > 0) { encontreAlOrganizador(); clearInterval(vigia); } } catch (e) { } }, 300);
  const r = JSON.parse(await ex.EntrarPorIroh(MODULO_IROH, YO.ticket, YO.funcion, pedidoIroh(), false));
  clearInterval(vigia);
  if (r.ok === false || r.no) { log('iroh: no entre (' + (r.error || r.dice) + '); sigo por HTTP'); return false; }
  log('iroh: entre por el bus en ' + ((performance.now() - t0) / 1000).toFixed(1) + ' s; las bolitas llegan solas');
  porIroh = true;
  await usarRespuesta(r);
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') reengancharIroh('volvi a la pestana'); });
  document.addEventListener('resume', () => reengancharIroh('la pestana se descongelo'));
  return true;
}

// Volver de una pestana congelada o de un enlace caido: el alta se pide de nuevo por el bus y su respuesta
// trae todas las bolitas de la ronda, asi que lo que se perdio mientras tanto se rellena. Si iroh no contesta,
// el HTTP de siempre mientras tanto, y otro intento por iroh a los 10 s.
async function reengancharIroh(por) {
  if (!porIroh || reenganchando) return;
  if (reintento) { clearTimeout(reintento); reintento = null; }
  reenganchando = true;
  const t0 = performance.now();
  const antes = pantalla ? pantalla.drawOrder.length : 0;
  const avisa = por !== 'ronda nueva' && !entrando;
  if (avisa) Entrada.avisar('volviendo', T('jugador.web.volviendo'), T('jugador.web.volviendo-sub'));
  try {
    // Cada vuelta (salvo una ronda nueva) arma un endpoint nuevo: el viejo pudo quedar sin relay mientras la pestana no corria.
    const r = JSON.parse(await ex.EntrarPorIroh(MODULO_IROH, YO.ticket, YO.funcion, pedidoIroh(), por !== 'ronda nueva'));
    if (r.ok === false || r.no) {
      fallosSeguidos++;
      const luego = fallosSeguidos <= 2 ? 2000 : 10000;
      log('iroh: no me pude volver a enganchar (' + (r.error || r.dice) + '); por HTTP mientras tanto, reintento en ' + (luego / 1000) + ' s');
      if (avisa || por === 'reintento') Entrada.avisar('otro', T('jugador.web.otro-camino'), T('jugador.web.otro-camino-sub'));
      arrancarSondeo();
      // Con la pagina escondida no se insiste: el aviso de que vuelve a verse dispara el proximo intento.
      if (!document.hidden) reintento = setTimeout(() => { reintento = null; reengancharIroh('reintento'); }, luego);
      return;
    }
    fallosSeguidos = 0;
    await usarRespuesta(r);
    pararSondeo();
    const ahora = pantalla ? pantalla.drawOrder.length : 0;
    if (avisa || por === 'reintento') {
      const nuevas = pantalla ? pantalla.drawOrder.slice(antes) : [];
      Entrada.avisar('listo', T('jugador.web.al-dia'), !nuevas.length ? T('jugador.web.al-dia-nada')
        : T(nuevas.length === 1 ? 'jugador.web.al-dia-salio' : 'jugador.web.al-dia-salieron', { lista: listaDeBolitas(nuevas) }));
    }
    log('iroh: al dia otra vez (' + por + ') en ' + ((performance.now() - t0) / 1000).toFixed(1) + ' s; recupere '
      + Math.max(0, ahora - antes) + ' bolitas; van ' + ahora + ' [' + (pantalla ? pantalla.drawOrder.join(',') : '') + ']');
  } finally {
    reenganchando = false;
  }
}

async function usarRespuesta(r) {
  conectado = true;
  problema = '';
  ultima = r;
  const antes = pantalla ? pantalla.drawOrder.length : 0;
  const cartones = JSON.stringify(r.cartones || YO.cartones);
  const bolitas = JSON.stringify(r.bolitas || []);
  const t = JSON.parse(entre
    ? ex.PonerAlDia(cartones, r.sejuega || 'AnyLine', r.ronda || 0, bolitas)
    : ex.Entrar(YO.funcion, cartones, r.sejuega || 'AnyLine', r.ronda || 0, bolitas));
  if (!t.ok) { problema = P('jugador.web.no-arme-carton', { error: t.error }); log(dicho(problema)); empujar(); return; }
  if (!entre) {
    log(T('jugador.web.log.entre', { bingo: YO.nombre, cartones: (r.cartones || YO.cartones).join(', ') }));
    recordarEsteBingo((r.cartones || YO.cartones).length);
  }
  entre = true;
  leerPantalla(true);
  if (pantalla && pantalla.drawOrder.length > antes && antes > 0) log(T('jugador.web.log.salio', { numero: pantalla.lastDraw }));
  empujar();
  const v = ex.Volcar();
  if (v) await guardarArbol(JSON.parse(v)).catch(e => log(T('jugador.web.log.no-guarde-diario', { motivo: e.name })));
  if (entrando) await terminarEntrada();
  else if (guardadas) {
    const nuevas = pantalla ? pantalla.drawOrder.filter(n => !guardadas.includes(n)) : [];
    guardadas = null;
    // Despues del reparto, no encima: el aviso espera a que la entrada se vaya.
    const avisar = () => Entrada.abierta() ? setTimeout(avisar, 300) : Entrada.avisar('listo', T('jugador.web.al-dia'),
      T(nuevas.length === 1 ? 'jugador.web.al-dia-salio' : 'jugador.web.al-dia-salieron', { lista: listaDeBolitas(nuevas) }));
    if (nuevas.length) avisar();
  }
}

async function cantarBingo() {
  const g = ganados();
  if (!g.length || gritado) { empujar(); return; }
  try {
    const res = await fetch(API + '/bingo/', {
      method: 'POST', headers: { 'Content-Type': 'text/plain;charset=UTF-8' }, signal: AbortSignal.timeout(6000),
      body: JSON.stringify({ funcion: YO.funcion, quien: YO.yo, marca: YO.marca, carton: g[0].carton, figura: g[0].figura }),
    });
    const texto = await res.text();
    if (!res.ok) throw new Error(explicarNo(res.status, texto));
    gritado = true;
    log(T('jugador.log.cante-bingo', { carton: g[0].carton }));
  } catch (e) {
    problema = P('jugador.web.no-avise-bingo', { motivo: e.message });
    log(dicho(problema));
  }
  empujar();
}

function leerCodigo(codigo) {
  const limpio = (codigo || '').trim();
  const corte = limpio.indexOf('d=');
  if (corte < 0) throw new Error(T('jugador.web.no-parece-codigo'));
  let crudo = limpio.slice(corte + 2);
  const cola = crudo.indexOf('&');
  if (cola >= 0) crudo = crudo.slice(0, cola);
  crudo = crudo.split('-').join('+').split('_').join('/');
  while (crudo.length % 4) crudo += '=';
  const bytes = Uint8Array.from(atob(crudo), ch => ch.charCodeAt(0));
  return JSON.parse(new TextDecoder().decode(bytes));
}

function entrarConCodigo(nombre, codigo) {
  try {
    const j = leerCodigo(codigo);
    if (!j.r || !j.c || !j.c.length) throw new Error(T('jugador.web.codigo-sin-cartones'));
    const q = new URLSearchParams({ f: j.r, c: j.c.join(','), m: j.m || '', n: j.n || '', yo: nombre || j.yo || 'Jugador' });
    if (EN_INTERNET) {
      const o = j.e || API;
      if (!o) throw new Error(T('jugador.web.codigo-sin-donde'));
      q.set('o', o.replace(/\/+$/, ''));
    }
    irA(q);
  } catch (e) {
    problema = P('jugador.web.codigo-no-sirve', { motivo: e.message });
    empujar();
  }
}

// La hoja que explica como hacer que el telefono confie en el bingo. No esta en el camino de nadie: solo
// sale con ?ca=1, para quien organiza y quiera la camara en vivo por https.
function enHtml(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function conNegritas(frase, huecos) {
  let html = enHtml(frase);
  for (const [hueco, valor] of Object.entries(huecos)) html = html.split('{' + hueco + '}').join('<b>' + enHtml(valor) + '</b>');
  return html;
}

function comoInstalar() {
  if (document.getElementById('como-certificado')) return;
  const capa = document.createElement('div');
  capa.id = 'como-certificado';
  capa.style.cssText = 'position:fixed;inset:0;z-index:9999;background:rgba(40,20,0,.72);display:flex;align-items:flex-end;justify-content:center;padding:16px';
  const hoja = document.createElement('div');
  hoja.style.cssText = 'width:100%;max-width:520px;background:#fff2d6;color:#3a1c00;border-radius:22px;padding:22px 20px 18px;font:15px/1.5 Inter,system-ui,sans-serif;display:grid;gap:12px;box-shadow:0 12px 40px rgba(0,0,0,.35)';
  hoja.innerHTML = `
    <div style="font:600 21px/1.2 Fredoka,system-ui,sans-serif">${enHtml(T('jugador.web.cert.titulo'))}</div>
    <div>${enHtml(T('jugador.web.cert.intro'))}</div>
    <ol style="margin:0;padding-left:22px;display:grid;gap:10px">
      <li>${enHtml(T('jugador.web.cert.paso1', { enlace: '\u0001' })).replace('\u0001', '<a href="/velada-ca.crt" download="velada-ca.crt" style="font-weight:700;color:#7a0000">' + enHtml(T('jugador.web.cert.baja')) + '</a>')}</li>
      <li>${conNegritas(T('jugador.web.cert.paso2'), { ajustes: T('jugador.web.cert.ajustes'), certificado: T('jugador.web.cert.certificado-ca'), archivo: 'velada-ca.crt' })}</li>
      <li>${conNegritas(T('jugador.web.cert.paso3'), { boton: T('jugador.web.cert.ya-lo-instale') })}</li>
    </ol>
    <div id="cert-estado" style="font-size:13px;color:#7a0000;min-height:18px"></div>
    <div style="display:flex;gap:10px;justify-content:flex-end">
      <button id="cert-cerrar" style="font:600 15px Fredoka,system-ui,sans-serif;padding:11px 18px;border-radius:999px;border:2px solid #3a1c00;background:transparent;color:#3a1c00">${enHtml(T('jugador.web.cert.ahora-no'))}</button>
      <button id="cert-listo" style="font:600 15px Fredoka,system-ui,sans-serif;padding:11px 20px;border-radius:999px;border:0;background:#f5a623;color:#3a1c00">${enHtml(T('jugador.web.cert.ya-lo-instale'))}</button>
    </div>`;
  capa.append(hoja);
  document.body.append(capa);
  hoja.querySelector('#cert-cerrar').onclick = () => capa.remove();
  hoja.querySelector('#cert-listo').onclick = async () => {
    const estado = hoja.querySelector('#cert-estado');
    estado.textContent = T('jugador.web.cert.probando');
    if (await confiaEnElBingo()) { estado.textContent = T('jugador.web.cert.listo'); log(T('jugador.web.log.salto-a-https')); aHttps(); return; }
    estado.textContent = T('jugador.web.cert.todavia-no');
  };
  log(T('jugador.web.log.mostre-certificado'));
}

// En http de la LAN el navegador no presta la camara en vivo, pero un campo de archivo con capture abre la
// aplicacion de camara del telefono y devuelve la foto. El en vivo queda para cuando haya https confiable.
function escanear() {
  if (window.isSecureContext && navigator.mediaDevices && navigator.mediaDevices.getUserMedia && 'BarcodeDetector' in window) escanearEnVivo();
  else pedirFoto(true);
}

// jsQR (Apache 2.0) lee en un worker: una foto sin codigo le puede costar varios segundos, y asi se corta a
// tiempo sin congelar la pantalla. El worker nace al tocar Escanear y baja jsQR mientras se saca la foto.
let lector = null;
function prepararLector() {
  if (lector) return lector;
  const fuente = 'importScripts(' + JSON.stringify(new URL('jsQR.js?v=' + VERSION, location.href).href) + ');postMessage({listo:true});'
    + 'onmessage=e=>{const m=e.data;const r=jsQR(new Uint8ClampedArray(m.px),m.w,m.h,{inversionAttempts:m.invertir?"attemptBoth":"dontInvert"});postMessage({leido:r?r.data:null});};';
  const url = URL.createObjectURL(new Blob([fuente], { type: 'text/javascript' }));
  const l = { url, worker: new Worker(url), arranco: false, roto: null, espera: null };
  l.worker.onmessage = e => {
    if (e.data.listo) { l.arranco = true; return; }
    const f = l.espera; l.espera = null; if (f) f.listo(e.data.leido);
  };
  l.worker.onerror = e => {
    e.preventDefault();
    l.roto = new Error(l.arranco ? T('jugador.web.lector-trabado') : T('jugador.web.lector-no-bajo'));
    const f = l.espera; l.espera = null; if (f) f.fallo(l.roto);
  };
  lector = l;
  return l;
}

function soltarLector() {
  if (!lector) return;
  lector.worker.terminate();
  URL.revokeObjectURL(lector.url);
  lector = null;
}

// Devuelve el texto del QR, null si no lo encontro, o undefined si se acabo el tiempo.
function preguntarAlLector(px, w, h, invertir, ms) {
  const l = prepararLector();
  if (l.roto) { soltarLector(); return Promise.reject(l.roto); }
  return new Promise((listo, fallo) => {
    const reloj = setTimeout(() => { l.espera = null; soltarLector(); listo(undefined); }, ms);
    l.espera = { listo: v => { clearTimeout(reloj); listo(v); }, fallo: e => { clearTimeout(reloj); soltarLector(); fallo(e); } };
    l.worker.postMessage({ px: px.buffer, w, h, invertir }, [px.buffer]);
  });
}

// Tiene que correr dentro del mismo toque: Chrome no abre la camara ni las fotos sin un gesto de la persona.
function pedirFoto(conCamara) {
  prepararLector();
  const campo = document.createElement('input');
  campo.type = 'file';
  campo.accept = 'image/*';
  if (conCamara) campo.setAttribute('capture', 'environment');
  campo.hidden = true;
  campo.addEventListener('change', () => { const foto = campo.files && campo.files[0]; campo.remove(); if (foto) leerFoto(foto); });
  campo.addEventListener('cancel', () => { campo.remove(); log(T('jugador.web.log.camara-sin-foto')); });
  document.body.append(campo);
  campo.click();
  log(conCamara ? T('jugador.web.log.abri-camara') : T('jugador.web.log.abri-fotos'));
}

async function leerFoto(foto) {
  const hoja = hojaDeLaFoto(foto);
  try {
    const imagen = await createImageBitmap(foto).catch(() => { throw new Error(T('jugador.web.imagen-no-abre')); });
    const inicio = performance.now();
    const leido = await buscarQR(imagen);
    log(T(leido ? 'jugador.web.log.qr-encontrado' : 'jugador.web.log.qr-no-encontrado', { ancho: imagen.width, alto: imagen.height, ms: Math.round(performance.now() - inicio) }));
    imagen.close();
    if (leido) { hoja.cerrar(); usarLeido(leido); return; }
    hoja.fallo(T('jugador.web.foto.no-encontre'), T('jugador.web.foto.no-encontre-consejo'));
  } catch (e) {
    log(T('jugador.web.log.no-lei-foto', { motivo: e.message }));
    hoja.fallo(T('jugador.web.foto.no-pude'), T('jugador.web.foto.no-pude-consejo', { motivo: e.message }));
  }
}

// De lo barato a lo caro: la foto entera achicada, el centro con mas detalle y la foto casi entera, por si
// el codigo quedo chico o es de los largos (el bingo:// lleva el ticket de iroh). El achicado va en alta
// calidad: el comun toma pocos pixeles por punto, deja el codigo dentado y jsQR no lo encuentra. Todo junto
// tiene 6 segundos: una foto sin codigo no deja a nadie esperando.
async function buscarQR(imagen) {
  const limite = performance.now() + 6000;
  const lienzo = document.createElement('canvas');
  const ctx = lienzo.getContext('2d', { willReadFrequently: true });
  const hechas = new Set();
  for (const [parte, lado, invertir] of [[1, 1100, true], [0.6, 1500, false], [1, 2200, false], [0.4, 1800, false]]) {
    const ancho = imagen.width * parte, alto = imagen.height * parte;
    const escala = Math.min(1, lado / Math.max(ancho, alto));
    const w = Math.round(ancho * escala), h = Math.round(alto * escala);
    if (hechas.has(parte + 'x' + w)) continue;
    hechas.add(parte + 'x' + w);
    lienzo.width = w; lienzo.height = h;
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(imagen, (imagen.width - ancho) / 2, (imagen.height - alto) / 2, ancho, alto, 0, 0, w, h);
    const quedan = limite - performance.now();
    if (quedan <= 0) return null;
    const leido = await preguntarAlLector(ctx.getImageData(0, 0, w, h).data, w, h, invertir, quedan);
    if (leido === undefined) { log(T('jugador.web.log.tiempo-qr')); return null; }
    if (leido) return leido;
  }
  return null;
}

// La misma hoja que "Pegar el codigo", con la foto a la vista: si no se lee, la persona ve por que.
function hojaDeLaFoto(foto) {
  const url = URL.createObjectURL(foto);
  const capa = document.createElement('div');
  capa.className = 'sheet';
  const tarjeta = document.createElement('div');
  tarjeta.className = 'sheet-card';
  const titulo = document.createElement('div');
  titulo.className = 'sheet-title';
  titulo.textContent = T('jugador.web.foto.leyendo');
  const vista = document.createElement('img');
  vista.src = url;
  vista.alt = T('jugador.web.foto.alt');
  vista.style.cssText = 'width:100%;max-height:34vh;object-fit:contain;border-radius:14px;background:rgba(14,5,26,.7)';
  const texto = document.createElement('div');
  texto.className = 'sheet-label';
  texto.style.cssText = 'text-align:center;font-size:13px;line-height:1.45';
  texto.textContent = T('jugador.web.foto.un-momento');
  tarjeta.append(titulo, vista, texto);
  capa.append(tarjeta);
  document.body.append(capa);
  const cerrar = () => { URL.revokeObjectURL(url); capa.remove(); };
  const boton = (letrero, clase, estilo, alTocar) => {
    const b = document.createElement('button');
    b.className = clase; b.textContent = letrero; b.onclick = alTocar;
    if (estilo) b.style.cssText = estilo;
    return b;
  };
  return {
    cerrar,
    fallo(porque, consejo) {
      titulo.textContent = porque;
      texto.textContent = consejo;
      tarjeta.append(
        boton(T('jugador.web.foto.otra'), 'sheet-go', '', () => { cerrar(); pedirFoto(true); }),
        boton(T('jugador.web.foto.guardada'), '', 'height:46px;border-radius:14px;border:1px solid rgba(255,220,150,.45);color:#fff2d6;font-family:Fredoka,sans-serif;font-weight:600;font-size:15px', () => { cerrar(); pedirFoto(false); }),
        boton(T('jugador.web.foto.mejor-no'), 'sheet-cancel', '', cerrar));
    },
  };
}

async function escanearEnVivo() {
  const capa = document.createElement('div');
  capa.style.cssText = 'position:fixed;inset:0;z-index:9999;background:#0b0b10;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:18px;padding:24px';
  const video = document.createElement('video');
  video.setAttribute('playsinline', ''); video.muted = true;
  video.style.cssText = 'width:100%;max-width:520px;aspect-ratio:1;object-fit:cover;border-radius:18px;border:3px solid #f5c542';
  const texto = document.createElement('div');
  texto.textContent = T('jugador.web.camara.apunta');
  texto.style.cssText = 'color:#fff;font:600 18px Fredoka,system-ui,sans-serif';
  const cancelar = document.createElement('button');
  cancelar.textContent = T('jugador.web.camara.cancelar');
  cancelar.style.cssText = 'font:600 16px Fredoka,system-ui,sans-serif;padding:12px 28px;border-radius:999px;border:0;background:#f5c542;color:#1b1b1b';
  capa.append(video, texto, cancelar);
  document.body.append(capa);
  let stream = null; let vivo = true;
  const terminar = () => { vivo = false; if (stream) stream.getTracks().forEach(t => t.stop()); capa.remove(); };
  cancelar.onclick = terminar;
  try {
    stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false });
  } catch (e) {
    terminar(); problema = P('jugador.web.camara-negada', { motivo: e.message }); empujar(); return;
  }
  video.srcObject = stream;
  await video.play().catch(() => { });
  const detector = new BarcodeDetector({ formats: ['qr_code'] });
  log(T('jugador.web.log.camara-abierta'));
  while (vivo) {
    try {
      const leidos = await detector.detect(video);
      if (leidos.length) { const leido = leidos[0].rawValue; terminar(); usarLeido(leido); return; }
    } catch (e) { }
    await new Promise(r => setTimeout(r, 250));
  }
}

function usarLeido(leido) {
  log(T('jugador.web.log.lei-qr', { inicio: leido.slice(0, 60) }));
  if (leido.startsWith('bingo://') || leido.startsWith('velada://')) { entrarConCodigo(YO.yo, leido); return; }
  try {
    const u = new URL(leido);
    const p = new URLSearchParams(u.hash.length > 1 ? u.hash.slice(1) : u.search);
    if (!p.get('f') && p.get('d')) { entrarConCodigo(YO.yo, u.hash); return; }
    if (p.get('f')) {
      // En la copia de internet el QR de un organizador (http://ip:puerto/?f=...) se abre aca mismo, con su API
      // anotada: el jugador no sale de la app instalada.
      if (EN_INTERNET) { if (!p.get('o')) p.set('o', u.origin + '/api'); p.delete('v'); irA(p); return; }
      location.href = leido;
      return;
    }
  } catch (e) { }
  problema = P('jugador.web.qr-no-es-bingo'); empujar();
}

function atender(m) {
  const partes = String(m).split('|');
  switch (partes[0]) {
    case 'ready':
      window.__empujar(mensajeDeTextos());
      if (catalogo) window.__empujar(catalogo);
      empujar();
      break;
    case 'idioma':
      idioma = aIdioma(partes[1]);
      try { localStorage.setItem(IDIOMA_GUARDADO, idioma); } catch (e) { }
      armarFrases();
      localizarFiguras();
      log(T('jugador.web.log.idioma', { idioma }));
      window.__empujar(mensajeDeTextos());
      if (catalogo) window.__empujar(catalogo);
      empujar();
      break;
    case 'wantlog':
      window.__empujar({ kind: 'log', text: registro.join(String.fromCharCode(10)) });
      break;
    case 'bingo':
      cantarBingo();
      break;
    case 'enter':
      entrarConCodigo(partes[1], partes.slice(2).join('|'));
      break;
    case 'scan':
      escanear();
      break;
    case 'switch': {
      if (TENGO_CODIGO && partes[1] === YO.funcion) { mostrarPuerta = false; problema = ''; empujar(); break; }
      const b = misBingos().find(x => x.id === partes[1]);
      if (!b) { problema = P('jugador.web.bingo-no-guardado'); empujar(); break; }
      log(T('jugador.web.log.vuelvo-al-bingo', { bingo: b.name }));
      const destino = new URLSearchParams(b.q);
      if (!EN_INTERNET && Q.get('v')) destino.set('v', Q.get('v'));
      irA(destino);
      break;
    }
    case 'another':
      mostrarPuerta = true; problema = ''; empujar();
      break;
    case 'back':
      mostrarPuerta = false; problema = ''; empujar();
      break;
    default:
      log(T('jugador.web.log.no-aplica', { pedido: partes[0] }));
      empujar();
  }
}

// El escaneo no espera en la cola a que arranque .NET: si espera, se pierde el toque y la camara no abre.
const mandarAlPuente = window.HybridWebView.SendRawMessage;
window.HybridWebView.SendRawMessage = m => String(m).split('|')[0] === 'scan' ? escanear() : mandarAlPuente(m);

if ('serviceWorker' in navigator) navigator.serviceWorker.register('./sw-player.js').then(() => log(T('jugador.web.log.sw-registrado'))).catch(e => log(T('jugador.web.log.sw-rechazado', { motivo: e.message })));
else log(window.isSecureContext ? T('jugador.web.log.sin-sw-navegador') : T('jugador.web.log.sin-sw-inseguro'));

(async () => {
  abrirEntrada();
  if (location.protocol === 'http:' && await confiaEnElBingo()) {
    log(T('jugador.web.log.salto-a-https'));
    aHttps();
    return;
  }
  try {
    let arbol = {};
    try { arbol = await leerArbol(); } catch (e) { log(T('jugador.web.log.no-lei-diario', { motivo: e.name })); }
    const { getAssemblyExports, getConfig } = await dotnet
      .withConfig({ disableNoCacheFetch: true })
      .withResourceLoader((tipo, nombre, url) => tipo === 'manifest' ? fetch(url + (url.includes('?') ? '&' : '?') + 'v=' + VERSION, { credentials: 'include' }) : undefined)
      .create();
    ex = (await getAssemblyExports(getConfig().mainAssemblyName)).BingoWeb.Player;
    for (const [ruta, b64] of Object.entries(arbol)) ex.Precargar(ruta, b64);
    const arranque = JSON.parse(ex.Arrancar());
    log(arranque.ok
      ? T('jugador.web.log.actor-arriba', { version: arranque.version, archivos: arranque.archivos, precargados: arranque.precargados })
      : T('jugador.web.log.actor-no-arranco', { error: arranque.error }));
    figurasDelMotor = JSON.parse(ex.FigurasConocidas());
    localizarFiguras();
    if (TENGO_CODIGO && Object.keys(arbol).length) {
      ex.Preparar(YO.funcion);
      const t = JSON.parse(ex.Estado());
      if (t.ok && t.cartones && t.cartones.length) {
        entre = true;
        leerPantalla(false);
        recordarEsteBingo(t.cartones.length);
        log(T('jugador.web.log.tablero-rearmado'));
      }
    }
  } catch (e) {
    problema = P('jugador.web.no-arranque', { motivo: e.message });
    log(dicho(problema));
  }
  window.__atender = atender;
  window.HybridWebView.__cola.splice(0).forEach(atender);
  empujar();
  if (entrando && entre && pantalla && pantalla.cards.length) terminarEntrada(true);
  if (Q.get('ca') === '1' && location.protocol === 'http:') comoInstalar();
  if (TENGO_CODIGO && ex) {
    if (EN_INTERNET) {
      await mirarRedLocal();
      if (redLocal !== 'granted') { problema = avisoDeRedLocal(); log(T('jugador.web.log.permiso-red-local', { estado: redLocal })); empujar(); }
    }
    const iroh = await entrarPorIroh().catch(e => { log('iroh: ' + e.message); return false; });
    if (!iroh) arrancarSondeo();
  }
})();
