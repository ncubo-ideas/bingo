// La entrada al bingo del jugador web. Mientras se conecta muestra la tombola y los pasos de verdad (encontrar al
// organizador, traer los cartones, ponerse al dia); cuando el alta llega, reparte el carton en la mesa y sella las
// bolitas que ya salieron, y recien ahi deja ver el juego. Tambien el aviso de "volviendo a la mesa" cuando la
// pestaña vuelve de estar congelada. No sabe de idiomas: bridge.js le pasa cada frase ya traducida.
window.Entrada = (function () {
  const LETRAS = 'BINGO';
  const COLORES = ['#4DA3FF', '#9B7BFF', '#50C878', '#F5A623', '#FF6B6B'];
  const espera = ms => new Promise(r => setTimeout(r, ms));
  let raiz = null;
  let aviso = null;
  let avisoReloj = null;

  const letraDe = n => Math.min(4, Math.floor((n - 1) / 15));

  function bola(n, clase, demora) {
    const i = letraDe(n);
    const c = COLORES[i];
    return '<div class="ent-bola ' + (clase || '') + '" style="' + (demora ? 'animation-delay:' + demora + ';' : '') + 'background: radial-gradient(circle at 35% 30%, '
      + c + 'cc, ' + c + ' 55%, #2a0f3d 120%)"><span>' + LETRAS[i] + n + '</span></div>';
  }

  const ESTRELLA = '<svg viewBox="0 0 24 24"><path d="M12 1 L14 10 L23 12 L14 14 L12 23 L10 14 L1 12 L10 10 Z" fill="#fff2d0"/></svg>';
  const VISTO = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#3a1c00" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>';

  function tombola() {
    const meridianos = [0, -0.6, -1.2, -1.8].map(d =>
      '<ellipse class="tmb-meridiano" style="animation-delay:' + d + 's" cx="140" cy="104" rx="78" ry="78" fill="none" stroke="url(#tmbAlambre)" stroke-width="2.4"/>').join('');
    return '<div class="tmb" aria-hidden="true">'
      + '<div class="tmb-rayos"></div>'
      + '<svg width="280" height="250" viewBox="0 0 280 250">'
      + '<defs>'
      + '<linearGradient id="tmbOro" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff0b8"/><stop offset=".45" stop-color="#f5a623"/><stop offset="1" stop-color="#9c5a08"/></linearGradient>'
      + '<linearGradient id="tmbOroLado" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#9c5a08"/><stop offset=".5" stop-color="#ffe08a"/><stop offset="1" stop-color="#9c5a08"/></linearGradient>'
      + '<linearGradient id="tmbRojo" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#d42a1e"/><stop offset="1" stop-color="#6e0606"/></linearGradient>'
      + '<linearGradient id="tmbAlambre" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ffe9a8"/><stop offset="1" stop-color="#d48a12"/></linearGradient>'
      + '<radialGradient id="tmbVidrio" cx=".38" cy=".32" r=".75"><stop offset="0" stop-color="#7a3fa0" stop-opacity=".55"/><stop offset=".7" stop-color="#2a0f3d" stop-opacity=".55"/><stop offset="1" stop-color="#ffd98a" stop-opacity=".25"/></radialGradient>'
      + '<radialGradient id="tmbHalo" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#ffc35a" stop-opacity=".4"/><stop offset="1" stop-color="#ffc35a" stop-opacity="0"/></radialGradient>'
      + '</defs>'
      + '<circle cx="140" cy="104" r="116" fill="url(#tmbHalo)"/>'
      + '<ellipse cx="140" cy="244" rx="92" ry="6" fill="rgba(0,0,0,.35)"/>'
      + '<path d="M52 104 C40 150 70 196 112 214" fill="none" stroke="#3a1c00" stroke-width="11" stroke-linecap="round"/>'
      + '<path d="M52 104 C40 150 70 196 112 214" fill="none" stroke="url(#tmbOroLado)" stroke-width="6" stroke-linecap="round"/>'
      + '<path d="M228 104 C240 150 210 196 168 214" fill="none" stroke="#3a1c00" stroke-width="11" stroke-linecap="round"/>'
      + '<path d="M228 104 C240 150 210 196 168 214" fill="none" stroke="url(#tmbOroLado)" stroke-width="6" stroke-linecap="round"/>'
      + '<path d="M84 222 L196 222 L212 242 L68 242 Z" fill="url(#tmbRojo)" stroke="#3a1c00" stroke-width="2.5" stroke-linejoin="round"/>'
      + '<rect x="78" y="216" width="124" height="9" rx="4.5" fill="url(#tmbOro)" stroke="#3a1c00" stroke-width="2"/>'
      + '<circle cx="104" cy="233" r="2.6" fill="#ffd98a"/><circle cx="140" cy="233" r="2.6" fill="#ffd98a"/><circle cx="176" cy="233" r="2.6" fill="#ffd98a"/>'
      + '<path d="M118 206 Q140 228 162 206" fill="url(#tmbOro)" stroke="#3a1c00" stroke-width="2.5"/>'
      + '<circle cx="140" cy="104" r="78" fill="url(#tmbVidrio)"/>'
      + '</svg>'
      + '<div class="tmb-dentro">'
      + bola(7, 'tmb-a') + bola(22, 'tmb-b') + bola(53, 'tmb-c tmb-lejos') + bola(66, 'tmb-d tmb-lejos')
      + bola(40, 'tmb-a', '-1.1s') + bola(12, 'tmb-c tmb-lejos', '-.9s') + bola(29, 'tmb-d', '-1.7s') + bola(47, 'tmb-b tmb-lejos', '-1.3s')
      + '</div>'
      + '<svg width="280" height="250" viewBox="0 0 280 250">'
      + '<ellipse cx="140" cy="104" rx="78" ry="20" fill="none" stroke="url(#tmbAlambre)" stroke-width="2.2"/>'
      + '<ellipse cx="140" cy="66" rx="64" ry="14" fill="none" stroke="url(#tmbAlambre)" stroke-width="1.8" opacity=".85"/>'
      + '<ellipse cx="140" cy="142" rx="64" ry="14" fill="none" stroke="url(#tmbAlambre)" stroke-width="1.8" opacity=".85"/>'
      + meridianos
      + '<circle cx="140" cy="104" r="78" fill="none" stroke="#3a1c00" stroke-width="10"/>'
      + '<circle cx="140" cy="104" r="78" fill="none" stroke="url(#tmbOro)" stroke-width="6"/>'
      + '<path d="M86 52 A74 74 0 0 1 150 31" fill="none" stroke="rgba(255,255,255,.55)" stroke-width="4" stroke-linecap="round"/>'
      + '<rect x="131" y="178" width="18" height="16" rx="3" fill="url(#tmbOroLado)" stroke="#3a1c00" stroke-width="2"/>'
      + '<circle cx="52" cy="104" r="9" fill="url(#tmbOro)" stroke="#3a1c00" stroke-width="2.5"/>'
      + '<circle cx="228" cy="104" r="9" fill="url(#tmbOro)" stroke="#3a1c00" stroke-width="2.5"/>'
      + '<g class="tmb-manivela"><path d="M228 104 L228 72" stroke="#3a1c00" stroke-width="8" stroke-linecap="round"/><path d="M228 104 L228 72" stroke="url(#tmbOro)" stroke-width="4.5" stroke-linecap="round"/><circle cx="228" cy="70" r="7" fill="#d61e1e" stroke="#3a1c00" stroke-width="2.5"/></g>'
      + '</svg>'
      + '<div class="tmb-destello"></div>'
      + bola(17, 'tmb-sale')
      + '<div class="tmb-chispa" style="left:22px;top:24px">' + ESTRELLA + '</div>'
      + '<div class="tmb-chispa" style="left:246px;top:16px;animation-delay:.6s">' + ESTRELLA + '</div>'
      + '<div class="tmb-chispa" style="left:14px;top:160px;animation-delay:1.2s">' + ESTRELLA + '</div>'
      + '<div class="tmb-chispa" style="left:258px;top:150px;animation-delay:.3s">' + ESTRELLA + '</div>'
      + '</div>';
  }

  function texto(clase, dicho) {
    const e = document.createElement('div');
    e.className = clase;
    e.textContent = dicho || '';
    return e;
  }

  /// Abre la entrada con los cuatro pasos: [{t, s}], el primero en curso.
  function abrir(t) {
    cerrarYa();
    raiz = document.createElement('div');
    raiz.className = 'ent';
    raiz.setAttribute('role', 'status');
    raiz.setAttribute('aria-live', 'polite');
    const logo = document.createElement('img');
    logo.className = 'ent-logo';
    logo.src = 'assets/bingo-logo-trim.webp';
    logo.alt = '';
    raiz.appendChild(logo);
    raiz.appendChild(texto('ent-titulo', t.titulo));
    const escena = document.createElement('div');
    escena.className = 'ent-escena';
    escena.style.cssText = 'display:flex;justify-content:center;width:100%';
    escena.innerHTML = tombola();
    raiz.appendChild(escena);
    const pasos = document.createElement('div');
    pasos.className = 'ent-pasos';
    t.pasos.forEach((p, i) => {
      const fila = document.createElement('div');
      fila.className = 'ent-paso ' + (i === 0 ? 'ahora' : 'falta');
      const marca = document.createElement('div');
      marca.className = 'ent-marca';
      marca.innerHTML = VISTO;
      const col = document.createElement('div');
      col.className = 'ent-paso-texto';
      col.appendChild(texto('ent-paso-t', p.t));
      col.appendChild(texto('ent-paso-s', p.s));
      fila.appendChild(marca);
      fila.appendChild(col);
      pasos.appendChild(fila);
    });
    raiz.appendChild(pasos);
    raiz.appendChild(texto('ent-pie', t.pie));
    document.body.appendChild(raiz);
  }

  const abierta = () => !!raiz;

  /// Cambia un paso: estado 'hecho' | 'ahora' | 'falta', y si vienen, sus textos.
  function paso(i, estado, t, s) {
    if (!raiz) return;
    const fila = raiz.querySelectorAll('.ent-paso')[i];
    if (!fila) return;
    fila.className = 'ent-paso ' + estado;
    if (t != null) fila.querySelector('.ent-paso-t').textContent = t;
    if (s != null) fila.querySelector('.ent-paso-s').textContent = s;
  }

  /// Un boton de salida, para cuando el organizador no aparece: deja ver lo que el telefono ya tiene.
  function boton(dicho, alTocar) {
    if (!raiz || raiz.querySelector('.ent-boton')) return;
    const b = document.createElement('button');
    b.className = 'ent-boton';
    b.textContent = dicho;
    b.addEventListener('click', alTocar);
    raiz.querySelector('.ent-pasos').after(b);
  }

  /// El reparto: el carton entra a la mesa y se sellan, en el orden en que salieron, los numeros suyos que ya
  /// salieron. Termina cuando el ultimo sello cayo y se leyo la cuenta.
  async function reparto(r) {
    if (!raiz) return;
    raiz.querySelector('.ent-titulo').textContent = r.titulo;
    const escena = raiz.querySelector('.ent-escena');
    const pasos = raiz.querySelector('.ent-pasos');
    const boton = raiz.querySelector('.ent-boton');
    if (boton) boton.remove();
    pasos.style.display = 'none';
    escena.className = 'ent-mesa';
    escena.style.cssText = '';
    escena.innerHTML = '';
    const real = document.getElementById('hanger');
    if (real && real.querySelector('.cell')) { await repartoDelJuego(r, escena, real); return; }
    const carton = document.createElement('div');
    carton.className = 'ent-carton';
    const cab = document.createElement('div');
    cab.className = 'ent-carton-cab';
    cab.appendChild(texto('ent-carton-n', r.cabecera));
    cab.appendChild(texto('ent-carton-mas', r.mas));
    carton.appendChild(cab);
    const grilla = document.createElement('div');
    grilla.className = 'ent-grilla';
    for (const l of LETRAS) grilla.appendChild(texto('ent-letra', l));
    const celdas = {};
    for (let fila = 0; fila < 5; fila++)
      for (let col = 0; col < 5; col++) {
        const n = r.grid[col][fila];
        const celda = texto('ent-celda', n === 0 ? '★' : String(n));
        if (n === 0) celda.style.color = '#c47a12';
        celdas[n] = celda;
        grilla.appendChild(celda);
      }
    carton.appendChild(grilla);
    escena.appendChild(carton);
    await espera(1050);
    const sellar = r.sellar || [];
    const paso = sellar.length > 8 ? Math.max(120, 2400 / sellar.length) : 300;
    for (const n of sellar) {
      const c = celdas[n];
      if (!c) continue;
      const s = document.createElement('div');
      s.className = 'ent-sello';
      c.appendChild(s);
      await espera(paso);
    }
    escena.appendChild(texto('ent-cuenta', typeof r.cuenta === 'function' ? r.cuenta(sellar.length) : r.cuenta));
    await espera(sellar.length ? 1100 : 900);
  }

  /// El reparto con el MISMO carton del juego: el que player.js ya dibujo debajo de la entrada, copiado. Entra
  /// colgando con su propio vaiven (hanger-swing) y sus bolitas aparecen una por una, en el orden en que salieron.
  async function repartoDelJuego(r, escena, real) {
    const colgado = real.cloneNode(true);
    colgado.removeAttribute('id');
    colgado.querySelectorAll('[id]').forEach(e => e.removeAttribute('id'));
    colgado.classList.remove('hidden', 'hanger-swing');
    colgado.style.marginTop = '0';
    const caja = colgado.querySelector('.card-box');
    const alto = escena.clientHeight - 70;
    caja.style.width = Math.max(180, Math.min(320, escena.clientWidth * 0.86, alto * (1003 / 1214))) + 'px';
    colgado.querySelectorAll('.cell-hit-ring').forEach(e => e.remove());
    const gano = colgado.querySelector('.win-frame');
    if (gano) gano.classList.add('hidden');
    colgado.querySelectorAll('.cell-colhi').forEach(c => {
      c.classList.remove('cell-colhi');
      const ficha = c.querySelector('.cell-chip');
      if (ficha) { ficha.style.boxShadow = ''; ficha.style.textShadow = ''; }
    });
    const orden = r.orden || [];
    const lugar = n => { const i = orden.indexOf(n); return i < 0 ? 999 : i; };
    const marcadas = [...colgado.querySelectorAll('.cell-marked')].map(c => {
      const ficha = c.querySelector('.cell-chip');
      const antes = { fondo: ficha.style.background, sombra: ficha.style.boxShadow };
      c.classList.remove('cell-marked');
      ficha.style.background = '';
      ficha.style.boxShadow = '';
      return { c, ficha, antes, n: Number(ficha.textContent) };
    }).sort((a, b) => lugar(a.n) - lugar(b.n));
    escena.appendChild(colgado);
    void colgado.offsetWidth;
    colgado.classList.add('hanger-swing');
    await espera(1300);
    const paso = marcadas.length > 8 ? Math.max(120, 2400 / marcadas.length) : 320;
    for (const m of marcadas) {
      m.c.classList.add('cell-marked');
      m.ficha.style.background = m.antes.fondo;
      m.ficha.style.boxShadow = m.antes.sombra;
      m.ficha.classList.add('ent-ficha-sale');
      await espera(paso);
    }
    escena.appendChild(texto('ent-cuenta', typeof r.cuenta === 'function' ? r.cuenta(marcadas.length) : r.cuenta));
    await espera(marcadas.length ? 1200 : 1000);
  }

  function cerrarYa() {
    if (raiz) raiz.remove();
    raiz = null;
  }

  /// Se desvanece hacia el juego.
  async function cerrar() {
    if (!raiz) return;
    const r = raiz;
    raiz = null;
    r.classList.add('ent-fuera');
    await espera(600);
    r.remove();
  }

  /// El aviso de arriba. tipo 'volviendo' (ruleta girando), 'listo' (dorado, se va solo) u 'otro' (sigue).
  function avisar(tipo, titulo, sub) {
    // Con la entrada en pantalla el aviso espera: aparece despues del reparto, nunca encima.
    if (raiz) { clearTimeout(avisoReloj); avisoReloj = setTimeout(() => avisar(tipo, titulo, sub), 300); return; }
    clearTimeout(avisoReloj);
    if (aviso) aviso.remove();
    aviso = document.createElement('div');
    aviso.className = 'ent-aviso' + (tipo === 'listo' ? ' listo' : '');
    aviso.setAttribute('role', 'status');
    const icono = document.createElement('div');
    icono.className = 'ent-aviso-icono ' + (tipo === 'listo' ? 'ok' : 'ruleta');
    if (tipo === 'listo') icono.innerHTML = VISTO.replace('#3a1c00', '#ffd98a').replace('width="16" height="16"', 'width="20" height="20"');
    const col = document.createElement('div');
    col.appendChild(texto('ent-aviso-t', titulo));
    if (sub) col.appendChild(texto('ent-aviso-s', sub));
    aviso.appendChild(icono);
    aviso.appendChild(col);
    document.body.appendChild(aviso);
    if (tipo === 'listo') avisoReloj = setTimeout(quitarAviso, 3800);
  }

  function quitarAviso() {
    clearTimeout(avisoReloj);
    if (!aviso) return;
    const a = aviso;
    aviso = null;
    a.classList.add('fuera');
    setTimeout(() => a.remove(), 450);
  }

  return { abrir, abierta, paso, boton, reparto, cerrar, avisar, quitarAviso };
})();
