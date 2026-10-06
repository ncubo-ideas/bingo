(function () {
  'use strict';

  const BANDS = [
    { letter: 'B', hex: '#FF6B6B' },
    { letter: 'I', hex: '#F5A623' },
    { letter: 'N', hex: '#50C878' },
    { letter: 'G', hex: '#4DA3FF' },
    { letter: 'O', hex: '#9B7BFF' },
  ];
  const LETTER_COLORS = ['#4DA3FF', '#9B7BFF', '#50C878', '#F5A623', '#FF6B6B'];
  const CONFETTI_COLORS = ['#4DA3FF', '#9B7BFF', '#50C878', '#F5A623', '#FF6B6B', '#FFD166', '#FF8FB1'];

  const ui = {
    state: null,
    // Si pidio ver los botones de entrar a uno nuevo. Se olvida al entrar a un bingo: la
    // proxima vez que vuelva a la puerta manda otra vez su lista.
    wantsNew: false,
    active: 0,
    ballsShown: 0,
    prizeTimer: null,
    swipeX: 0,
    swipeY: 0,
    nameTouched: false,
  };

  const $ = (id) => document.getElementById(id);

  function bandOf(n) { return BANDS[Math.floor((n - 1) / 15)]; }

  function shade(hex, amt) {
    const num = parseInt(hex.slice(1), 16);
    const clamp = (v) => Math.max(0, Math.min(255, v));
    const r = clamp((num >> 16) + amt);
    const g = clamp(((num >> 8) & 0xff) + amt);
    const b = clamp((num & 0xff) + amt);
    return '#' + ((r << 16) | (g << 8) | b).toString(16).padStart(6, '0');
  }

  function send(msg) {
    if (window.HybridWebView && window.HybridWebView.SendRawMessage) {
      window.HybridWebView.SendRawMessage(msg);
    }
  }

  function show(el, visible) { el.classList.toggle('hidden', !visible); }

  function render() {
    const s = ui.state;
    if (!s) return;

    show($('door'), s.door);
    show($('play'), !s.door);
    show($('doorBack'), s.door && s.canGoBack);
    show($('nameAsk'), !!s.askName);

    const trouble = $('doorTrouble');
    trouble.textContent = s.trouble || '';
    show(trouble, !!s.trouble);

    // Antes del corte de la puerta a proposito: el carnet se ensena en los dos lados. La
    // primera compra pasa en la puerta, y la segunda con las bolitas ya cayendo.
    renderBadge(s);

    if (!s.door) ui.wantsNew = false;

    // La lista de bingos, tambien antes del corte: vive en la puerta, y la puerta es
    // justo el lado que se salta con el return de abajo.
    renderMine(s);

    if (s.door) {
      // De vuelta en la puerta la mesa arranca de cero, como al girar: sin lupa,
      // sin foco, sin contador de bolitas del bingo anterior y en la primera
      // pagina — si no, al entrar a la siguiente bingo reaparecerian.
      land.lupa = null;
      land.spot = null;
      clearTimeout(land.spotTimer);
      land.shown = -1;
      land.entered = false;
      land.page = 0;
      show($('landLupa'), false);
      return;
    }

    if (ui.active >= s.cards.length) ui.active = Math.max(0, s.cards.length - 1);

    renderBanner(s);
    renderChips(s);
    renderBalls(s);
    renderCard(s);
    renderMinis(s);
    renderRoundStrip(s);
    renderTrophy(s);
    renderMega(s);
    renderLand(s);
  }

  function renderBadge(s) {
    const badge = s.badge || {};
    const pretty = badge.pretty || '····-····';

    $('doorBadgeNumber').textContent = pretty;
    $('badgeNumber').textContent = pretty;
    $('badgeWho').textContent = badge.name || '';

    const qr = $('badgeQr');
    if (badge.qr) { qr.src = badge.qr; show(qr, true); } else { show(qr, false); }

    // El telefono ya sabe como se llama de la vez pasada; solo se lo pone si la persona
    // todavia no escribio nada, para no borrarle lo que esta tecleando.
    if (!ui.nameTouched && badge.name) $('nameField').value = badge.name;
  }

  function openBadge() {
    show($('badgeModal'), true);
    send('badge');
  }

  function renderBanner(s) {
    const card = s.cards[ui.active];
    $('cardNo').textContent = card ? String(card.number) : '—';

    const grid = $('bannerPattern');
    grid.innerHTML = '';
    const lit = window.Figuras.cells(s.round.playing[0]);

    for (let i = 0; i < 25; i++) {
      const dot = document.createElement('span');
      if (lit.includes(i)) dot.className = 'lit';
      grid.appendChild(dot);
    }

    let round = '';
    if (s.round.playing.length > 0 && !s.round.over && !s.round.between) {
      round = (s.round.position > 0 ? Idioma.texto('jugador.ronda') + ' ' + s.round.position + ' · ' : '')
        + (s.round.prizeLine && s.round.prizeLine.length > 0
          ? s.round.prizeLine.toUpperCase()
          : Idioma.texto('jugador.se-juega') + ' '
            + s.round.playing.map((one) => window.Figuras.name(one)).join(' ' + Idioma.texto('jugador.y') + ' ').toUpperCase());
    }
    $('roundLine').textContent = round;
  }

  function renderMine(s) {
    const wrap = $('mineSlot');
    const box = $('mineList');
    const hay = s.festivals.length > 0;
    const lista = hay && !ui.wantsNew;

    // El hueco del cartel es UNO SOLO y se turna ENTERO: o la lista de bingos o los dos
    // botones de entrar. Mostrarlos juntos empujaba los botones fuera del cartel,
    // flotando sobre el mostrador -- este layout esta medido contra el dibujo.
    show(wrap, s.door && lista);
    show($('doorButtons'), !lista);

    // La frase se queda SIEMPRE (esconderla subia el carnet 56px fuera de su hueco) y
    // acompaña el turno: guia la lista, guia al que llega de cero, y cuando los botones
    // tapan la lista se vuelve EL CAMINO DE VUELTA a ella.
    const sub = $('doorSub');
    sub.classList.toggle('door-sub-link', hay && ui.wantsNew);
    sub.textContent = hay && ui.wantsNew ? '‹ ' + Idioma.texto('jugador.puerta.volver-a-mis-bingos')
      : hay ? Idioma.texto('jugador.puerta.toca-tu-bingo')
      : Idioma.texto('jugador.puerta.explicacion');

    // Se vacia SIEMPRE, incluso al salir sin bingos: escondido con filas viejas adentro es
    // un DOM que miente, y el dia que alguien muestre la lista por otro camino aparecen.
    box.innerHTML = '';

    if (!s.door || !lista) return;

    for (const v of s.festivals) {
      const row = document.createElement('button');
      row.className = 'mine-one' + (v.active ? ' mine-one-here' : '');

      const left = document.createElement('div');
      left.className = 'mine-side';

      const name = document.createElement('div');
      name.className = 'mine-name';
      name.textContent = v.name;
      left.appendChild(name);

      const much = document.createElement('div');
      much.className = 'mine-much';
      // Un carton no es "1 cartones". La cuenta en cero pasa cuando el codigo entro pero
      // el diario todavia no le reconocio nada.
      const cuantos = v.cards > 0 ? Idioma.muchos(v.cards, 'jugador.cartones.uno', 'jugador.cartones.varios')
        : Idioma.texto('jugador.cartones.ninguno');
      // Sin contestar no es "no existe": el bingo esta guardado y los cartones son suyos.
      // Se dice, y tocarlo vuelve a intentar -- callarlo dejaba una fila que no reaccionaba.
      much.textContent = v.connected === false ? cuantos + ' · ' + Idioma.texto('jugador.tocar-para-reconectar') : cuantos;
      if (v.connected === false) much.classList.add('mine-off');
      left.appendChild(much);

      row.appendChild(left);

      // En la puerta nadie "esta" en un bingo: la marca dice cual fue el ULTIMO, que
      // es la pregunta que se trae al abrir la app. Con o sin red -- lo ultimo es un
      // hecho del telefono, no del organizador.
      if (v.active) {
        const here = document.createElement('span');
        here.className = 'mine-here';
        here.textContent = Idioma.texto('jugador.ultima-vez');
        row.appendChild(here);
      }

      const chev = document.createElement('span');
      chev.className = 'mine-chev';
      chev.textContent = '›';
      row.appendChild(chev);

      row.addEventListener('click', () => send('switch|' + v.id));
      box.appendChild(row);
    }
  }

  function renderChips(s) {
    const box = $('chips');

    // Con UNO tambien se muestran. Antes hacia falta estar en dos para ver la fila, y la
    // unica salida era la carpa de la barra de abajo: un icono sin etiqueta entre otros
    // cuatro. Para tener el segundo bingo habia que adivinar ese icono, asi que quien
    // entraba a uno se quedaba encerrado ahi. El chip de agregar rompe el circulo.
    show(box, s.festivals.length > 0);
    box.innerHTML = '';

    for (const v of s.festivals) {
      const chip = document.createElement('button');
      chip.className = 'chip' + (v.active ? ' chip-active' : '');
      chip.textContent = v.name;
      chip.addEventListener('click', () => send('switch|' + v.id));
      box.appendChild(chip);
    }

    const mas = document.createElement('button');
    mas.className = 'chip chip-add';
    mas.textContent = '+ ' + Idioma.texto('jugador.puerta.otro-bingo');
    mas.addEventListener('click', () => send('another'));
    box.appendChild(mas);
  }

  function renderBalls(s) {
    const row = $('recentRow');
    row.innerHTML = '';

    const order = s.drawOrder;

    if (order.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'tube-empty';
      empty.textContent = Idioma.texto('jugador.esperando-bolita');
      row.appendChild(empty);
      ui.ballsShown = 0;
      return;
    }

    const last5 = order.slice(-5);
    const isNew = order.length > ui.ballsShown;
    ui.ballsShown = order.length;

    last5.forEach((n, i) => {
      const band = bandOf(n);
      const hot = i === last5.length - 1;

      const ball = document.createElement('div');
      ball.className = 'ball' + (hot && isNew ? ' ball-new' : '');
      ball.style.background = 'radial-gradient(circle at 36% 26%, ' + shade(band.hex, 60)
        + ' 0%, ' + band.hex + ' 52%, ' + shade(band.hex, -45) + ' 100%)';
      ball.style.boxShadow = hot
        ? '0 0 16px ' + band.hex + ', 0 0 28px ' + band.hex + 'aa'
        : '0 3px 8px rgba(0,0,0,.4)';

      const core = document.createElement('span');
      core.className = 'ball-core';
      core.style.color = shade(band.hex, -62);
      core.innerHTML = '<span class="ball-letter">' + band.letter
        + '</span><span class="ball-num">' + n + '</span>';

      ball.appendChild(core);
      row.appendChild(ball);
    });
  }

  function fitCard() {
    const hanger = $('hanger');
    if (hanger.classList.contains('hidden')) return;

    const stage = $('stage');
    const rail = stage.querySelector('.rail');
    const manyCards = ui.state && ui.state.cards.length > 1;

    const room = stage.clientHeight - rail.offsetHeight - (manyCards ? 70 : 10) - 34;
    if (room < 60) return;

    const width = Math.min(stage.clientWidth * 0.88, room * (1003 / 1214));
    $('cardBox').style.width = Math.max(120, width) + 'px';
  }

  function renderCard(s) {
    const card = s.cards[ui.active];
    const has = !!card;

    show($('hanger'), has);
    show($('waiting'), !has);
    show($('peekL'), has && s.cards.length > 1);
    show($('peekR'), has && s.cards.length > 1);
    if (!has) return;

    fitCard();

    const won = card.figures.some((f) => f.called);

    show($('winFrame'), won);

    fillCells($('cells'), card, s);
  }

  /// Las 25 celdas de un carton, marcadas. Lo usan el carton colgado (vertical y
  /// horizontal con 1-2) y el carton plano de la mesa horizontal: el mismo codigo
  /// para que los tres marquen identico.
  function fillCells(box, card, s) {
    const drawn = new Set(s.drawn);

    box.innerHTML = '';

    for (let r = 0; r < 5; r++) {
      for (let c = 0; c < 5; c++) {
        const n = card.grid[c][r];
        const free = n === 0;
        const marked = !free && drawn.has(n);
        const hit = marked && n === s.lastDraw;

        const colhi = !free && !marked && s.lastDraw > 0
          && c === Math.floor((s.lastDraw - 1) / 15);

        const cell = document.createElement('div');
        cell.className = 'cell'
          + (free ? ' cell-free' : marked ? ' cell-marked' : colhi ? ' cell-colhi' : '');

        if (!free) {
          const chip = document.createElement('span');
          chip.className = 'cell-chip';
          chip.textContent = String(n);

          if (marked) {
            const hex = bandOf(n).hex;
            chip.style.background = 'radial-gradient(circle at 38% 28%, #ffffff 0%, '
              + hex + ' 52%, ' + shade(hex, -38) + ' 100%)';
            chip.style.boxShadow = '0 3px 8px rgba(0,0,0,.35), 0 0 12px ' + hex + '99';
          } else if (colhi) {
            const hex = bandOf(s.lastDraw).hex;
            chip.style.boxShadow = 'inset 0 0 0 2px ' + hex + ', 0 0 9px ' + hex + '80';
            chip.style.textShadow = '0 0 9px ' + hex + 'bf';
          }

          cell.appendChild(chip);
        }

        if (hit) {
          const ring = document.createElement('span');
          ring.className = 'cell-hit-ring';
          cell.appendChild(ring);
        }

        box.appendChild(cell);
      }
    }
  }

  function renderMinis(s) {
    const box = $('minis');
    show(box, s.cards.length > 1);
    box.innerHTML = '';

    s.cards.forEach((card, i) => {
      const mini = document.createElement('button');
      mini.className = 'mini' + (i === ui.active ? ' mini-active' : '');
      mini.innerHTML = '<span class="mini-thumb"><img src="assets/card-only.png" alt=""></span>'
        + '<span class="mini-label">' + card.number + '</span>';
      mini.addEventListener('click', () => goCard(i));
      box.appendChild(mini);
    });
  }

  function renderRoundStrip(s) {
    const strip = $('roundStrip');

    if (s.round.over) {
      strip.textContent = Idioma.texto('jugador.termino');
      show(strip, true);
    } else if (s.round.between) {
      strip.textContent = Idioma.texto('jugador.entre-rondas');
      show(strip, true);
    } else {
      show(strip, false);
    }
  }

  function renderTrophy(s) {
    const hasPrize = s.round.prizeLine && s.round.prizeLine.length > 0
      && !s.round.over && !s.round.between;
    show($('trophy'), !!hasPrize);
    if (!hasPrize) show($('prizeRibbon'), false);
  }

  function renderMega(s) {
    const won = !!s.bingo;
    const mega = $('mega');

    // Tres estados y no dos: apagado, encendido esperando que lo toquen, y ya
    // cantado. El tercero hacia falta -- antes el megafono se veia igual antes y
    // despues de tocarlo, asi que la persona no sabia si el organizador se entero.
    mega.className = 'mega ' + (won ? (s.shouted ? 'mega-sent' : 'mega-on') : 'mega-off');

    const badge = $('megaBadge');
    badge.textContent = s.shouted ? Idioma.texto('jugador.avisado') : Idioma.texto('jugador.bingo');
    show(badge, won);
  }

  // ── la vista horizontal ────────────────────────────────────────────────
  // Acostado, el juego deja de ser "mirar MI carton" y pasa a ser "vigilar MI
  // MESA": todos los cartones a la vez, marcandose solos. Nada de aca corre en
  // vertical: renderLand se va apenas el media query no aplica, y el resto del
  // pipeline queda como estaba.

  const LANDQ = '(orientation: landscape) and (max-height: 560px)';

  const land = {
    shown: -1,       // cuantas bolitas ya animamos; -1 = recien llegamos (no animar el
                     // ponerse al dia), distinto de 0 = "sin bolitas, la primera SI anima"
    lupa: null,      // el NUMERO del carton agrandado (no el indice: la lista cambia)
    lupaFresh: false, // la lupa recien se abrio: el pop va solo esta vez
    spot: null,      // el numero del carton con el foco del bingo
    spotTimer: null,
    entered: false,  // ya corrio la entrada escalonada de los cartones
    page: 0,         // con mas de 3 cartones, que pagina de a 3 esta en la mesa
    slide: 0,        // el proximo dibujo de la mesa entra barriendo: +1 o -1
    ghostTimer: null,
    swiped: false,   // el ultimo gesto fue deslizar: que no abra la lupa
    swipeX: 0,
    swipeY: 0,
  };

  const PER_PAGE = 3;

  function isLand() { return window.matchMedia(LANDQ).matches; }

  function landBall(n, hot) {
    const band = bandOf(n);

    const ball = document.createElement('div');
    ball.className = 'ball';
    ball.style.background = 'radial-gradient(circle at 36% 26%, ' + shade(band.hex, 60)
      + ' 0%, ' + band.hex + ' 52%, ' + shade(band.hex, -45) + ' 100%)';
    ball.style.boxShadow = hot
      ? '0 0 16px ' + band.hex + ', 0 0 28px ' + band.hex + 'aa'
      : '0 3px 8px rgba(0,0,0,.4)';

    const core = document.createElement('span');
    core.className = 'ball-core';
    core.style.color = shade(band.hex, -62);
    core.innerHTML = '<span class="ball-letter">' + band.letter
      + '</span><span class="ball-num">' + n + '</span>';

    ball.appendChild(core);
    return ball;
  }

  /// Que tan cerca va un carton: cuantas bolitas le faltan a su figura mas
  /// cercana (0 si ya gano) y, cuando falta UNA, cual es — el numero viene del
  /// motor (waiting, la variante mas cercana de verdad), no del dibujito.
  function tension(card) {
    let left = Infinity;
    let listen = null;

    for (const f of card.figures) {
      if (f.called) return { left: 0, listen: null };

      const missing = f.waiting ? f.waiting.length : f.need - f.have;
      if (missing < left) {
        left = missing;
        listen = missing === 1 && f.waiting && f.waiting.length === 1 ? f.waiting[0] : null;
      }
    }

    return { left: left, listen: listen };
  }

  function missingOn(card) { return tension(card).left; }

  /// Un carton de la mesa horizontal. Es EL MISMO carton del vertical: el tablero
  /// de madera con sus fichas B-I-N-G-O y su estrella (card-only.png) y las celdas
  /// encima, con las mismas medidas de siempre. Dibujarlo a mano en CSS ahorraba
  /// alto pero cambiaba el juego de aparato; girar el telefono tiene que verse
  /// como la misma velada de otra forma, no como otra cosa.
  ///
  /// Lo unico que NO viaja del vertical son las cuerdas y las perillas: alla el
  /// carton cuelga de un riel de madera que aca no existe, y una cuerda que no
  /// esta atada a nada es un dibujo mintiendo. Aca el carton esta apoyado.
  ///
  /// El numero y la tension van en un cartelito de madera prendido al marco, que
  /// es lo unico que el vertical resuelve en otro lado (alla, en el banderin).
  function landCard(s, i, cardW, isNew, hottest) {
    const card = s.cards[i];
    const now = tension(card);
    const won = now.left === 0;

    const outer = document.createElement('div');
    outer.className = 'land-card'
      + (now.left === 1 && i === hottest ? ' land-falta-hot' : '');
    outer.dataset.card = i;

    const box = document.createElement('div');
    box.className = 'card-box';
    box.style.width = Math.round(cardW) + 'px';
    box.innerHTML = '<div class="card-glow"></div>'
      + '<img class="card-img" src="assets/card-only.png" alt="">'
      + '<div class="win-frame' + (won ? '' : ' hidden') + '"></div>';

    const cells = document.createElement('div');
    cells.className = 'cells';
    fillCells(cells, card, s);
    box.appendChild(cells);

    const sign = document.createElement('div');
    sign.className = 'land-sign'
      + (won ? ' land-sign-bingo' : now.left === 1 ? ' land-sign-falta' : '');
    // "FALTA EL 20" y no "¡FALTA 1! → 20": en una ficha corta, decir que falta
    // una y cual es lo mismo dicho dos veces.
    sign.textContent = '#' + card.number + (won ? ' · ' + Idioma.texto('jugador.bingo')
      : now.left !== 1 ? ''
      : now.listen ? ' · ' + Idioma.texto('jugador.falta-el') + ' ' + now.listen
      : ' · ' + Idioma.texto('jugador.falta-una'));
    box.appendChild(sign);

    outer.appendChild(box);

    if (isNew) {
      // La ola: la columna de la letra que acaba de salir se enciende carton por
      // carton, de izquierda a derecha, guiando el barrido del ojo.
      cells.querySelectorAll('.cell-colhi .cell-chip').forEach((chip) => {
        chip.classList.add('land-wave');
        chip.style.animationDelay = (i * 60) + 'ms';
      });

      // Y el carton que acaba de marcar se menea: "aca cayo una".
      const drawn = new Set(s.drawn);
      const struck = s.lastDraw > 0
        && card.grid.some((column) => column.includes(s.lastDraw)) && drawn.has(s.lastDraw);
      if (struck) outer.classList.add('land-rock');
    }

    return outer;
  }

  /// La marquesina: el tambor redobla, la bolita nueva sale disparada en arco al
  /// trono, la anterior se corre a la estela en domino, y las bombillas del borde
  /// hacen su cadena. Todo one-shot, disparado solo cuando la bolita ES nueva.
  function renderMarquee(s, isNew) {
    const order = s.drawOrder;
    const drum = $('lmDrum');
    const throne = $('lmThrone');
    const trail = $('lmTrail');

    // Los nodos del tambor y las bombillas persisten entre renders: para que el
    // one-shot se repita en bolitas consecutivas hay que quitar la clase y forzar
    // el reflow antes de reponerla, como el columpio del carton.
    drum.classList.remove('lm-shaking');
    if (isNew) { void drum.offsetWidth; drum.classList.add('lm-shaking'); }

    throne.innerHTML = '';
    trail.innerHTML = '';

    if (order.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'lm-empty';
      empty.textContent = Idioma.texto('jugador.esperando-bolita');
      trail.appendChild(empty);
    } else {
      const reigning = landBall(order[order.length - 1], true);
      if (isNew) reigning.classList.add('lm-arc');
      throne.appendChild(reigning);

      trail.className = 'lm-trail' + (isNew ? ' land-stepped' : '');
      order.slice(-8, -1).reverse().forEach((n, i) => {
        const ball = landBall(n, false);
        ball.style.setProperty('--nth', i);
        trail.appendChild(ball);
      });
    }

    const bulbs = document.querySelector('.lm-bulbs');
    if (bulbs.children.length === 0) {
      for (let i = 0; i < 11; i++) {
        const bulb = document.createElement('i');
        bulb.style.setProperty('--nth', i);
        bulbs.appendChild(bulb);
      }
    }
    bulbs.classList.remove('lm-chasing');
    if (isNew) { void bulbs.offsetWidth; bulbs.classList.add('lm-chasing'); }

    // La placa de la figura y el premio.
    const grid = $('landPattern');
    grid.innerHTML = '';
    const lit = window.Figuras.cells(s.round.playing[0]);

    for (let i = 0; i < 25; i++) {
      const dot = document.createElement('span');
      if (lit.includes(i)) dot.className = 'lit';
      grid.appendChild(dot);
    }

    const paused = s.round.over || s.round.between;

    let line = '';
    if (s.round.playing.length > 0 && !paused) {
      line = (s.round.position > 0 ? Idioma.texto('jugador.ronda') + ' ' + s.round.position + ' · ' : '')
        + s.round.playing.map((one) => window.Figuras.name(one)).join(' ' + Idioma.texto('jugador.y') + ' ').toUpperCase();
    }
    $('landFigure').textContent = line;

    const prize = $('landPrize');
    const hasPrize = s.round.prizeLine && s.round.prizeLine.length > 0 && !paused;
    show(prize, !!hasPrize);
    if (hasPrize) $('landPrizeText').textContent = s.round.prizeLine.toUpperCase();

    // Entre rondas o terminada, la marquesina lo anuncia en el centro.
    const plate = $('lmState');
    show(plate, paused);
    $('land').classList.toggle('land-dim', paused);
    if (paused) {
      plate.innerHTML = '';
      if (s.round.over) plate.innerHTML = '<img src="assets/trophy.png" alt="">';
      plate.appendChild(document.createTextNode(Idioma.texto(s.round.over ? 'jugador.termino' : 'jugador.entre-rondas')));
    }
  }

  /// El termometro es solo el contador de calma: "TE FALTAN 2", que no lo dice
  /// nadie mas. A una bolita se CALLA en todos los casos — la pestana del carton
  /// lo canta si esta a la vista, y si esta en otra pagina su miniatura se dora y
  /// brilla; la escena entera ademas se tine de oro. Repetirlo arriba era mostrar
  /// lo mismo dos veces.
  function renderThermo(s, paused) {
    const thermo = $('lmThermo');

    let best = Infinity;
    for (const card of s.cards) {
      const now = tension(card);
      if (now.left > 0 && now.left < best) best = now.left;
    }

    $('land').classList.toggle('land-hot', best === 1 && !paused);

    if (paused || best < 2 || best > 5) {
      show(thermo, false);
      return;
    }

    thermo.className = 'lm-thermo';
    thermo.textContent = Idioma.texto('jugador.te-faltan') + ' ' + best;
    show(thermo, true);
  }

  /// Con un solo carton, el espacio sobrante contesta la pregunta de verdad:
  /// QUE numeros espero. Bolitas de a de veras, por figura, con el conteo.
  function renderLandPanel(s, idx) {
    const panel = $('landPanel');
    const lone = idx >= 0 ? s.cards[idx] : null;

    show(panel, !!lone && lone.figures.length > 0);
    if (!lone || lone.figures.length === 0) return;

    panel.innerHTML = '<div class="land-panel-title">' + Idioma.texto('jugador.numeros-que-esperas') + '</div>';

    for (const f of lone.figures) {
      const fig = document.createElement('div');
      fig.className = 'land-fig';

      const name = document.createElement('div');
      name.className = 'land-fig-name';
      name.textContent = window.Figuras.name(f.name);
      fig.appendChild(name);

      const waiting = f.waiting || [];

      if (!f.called && waiting.length > 0 && waiting.length <= 10) {
        const balls = document.createElement('div');
        balls.className = 'land-waiting';
        waiting.forEach((n) => balls.appendChild(landBall(n, false)));
        fig.appendChild(balls);
      }

      const count = document.createElement('div');
      count.className = 'land-fig-count';
      count.textContent = f.called ? Idioma.texto('jugador.bingo') : f.have + ' / ' + f.need;
      fig.appendChild(count);

      panel.appendChild(fig);
    }

    if (tension(lone).left === 1) {
      const close = document.createElement('div');
      close.className = 'land-close';
      close.textContent = Idioma.texto('jugador.te-falta-una');
      panel.appendChild(close);
    }
  }

  function renderLandShelf(s, isNew) {
    const shelf = $('landShelf');
    shelf.innerHTML = '';

    const n = s.cards.length;

    if (n === 0) {
      renderLandPanel(s, -1);
      const empty = document.createElement('div');
      empty.className = 'waiting';
      empty.innerHTML = '<div class="waiting-emoji">🎫</div><div class="waiting-title">'
        + Idioma.texto('jugador.sin-cartones.titulo') + '</div><div class="waiting-sub">' + Idioma.texto('jugador.sin-cartones.explicacion') + '</div>';
      shelf.appendChild(empty);
      return;
    }

    // La pagina se decide ANTES de medir: si la mesa queda con un solo carton
    // (unico total, o el ultimo de una pagina impar), el panel de "los numeros
    // que esperas" acompana y el ancho del estante cambia.
    const paged = n > PER_PAGE;
    const pages = paged ? Math.ceil(n / PER_PAGE) : 1;
    if (land.page >= pages || land.page < 0) land.page = 0;

    const start = paged ? land.page * PER_PAGE : 0;
    const visible = Math.min(PER_PAGE, n - start);

    renderLandPanel(s, visible === 1 && n !== 2 ? start : -1);

    const W = shelf.clientWidth;
    const H = shelf.clientHeight;
    if (W < 60 || H < 60) return;

    // El carton mas caliente: entre los que estan a una, el de la figura mas corta.
    let hottest = -1, closest = Infinity;
    s.cards.forEach((card, i) => {
      const missing = missingOn(card);
      if (missing !== 1) return;
      const size = Math.min(...card.figures.filter((f) => !f.called).map((f) => f.need));
      if (size < closest) { closest = size; hottest = i; }
    });

    // La franja de las miniaturas se descuenta del centrado, no solo del alto.
    shelf.style.paddingBottom = paged ? '44px' : '0px';
    shelf.style.setProperty('--cols', visible);

    // El carton manda: su arte es 1003x1214 y de ahi sale todo. Se toma el mayor
    // que entre a lo ancho de su lugar y a lo alto de la mesa, y el lugar es
    // siempre el de la pagina LLENA sobre la mesa ENTERA — asi un carton solo
    // mide lo mismo que sus hermanos, en vez de estirarse al triple o encogerse
    // porque el panel de al lado le tomo la mitad del ancho al medirlo.
    const aside = $('landPanel');
    const withPanel = getComputedStyle(aside).display !== 'none';
    const tableW = W + (withPanel ? aside.getBoundingClientRect().width + 14 : 0);

    const slots = n < PER_PAGE ? n : PER_PAGE;
    const cardW = Math.min(
      (tableW - (slots - 1) * 12) / slots,
      (H - (paged ? 44 : 0) - 4) * (1003 / 1214));

    // La fuente sale de la celda real del arte: 86.12% del ancho para cinco.
    const font = Math.round(cardW * 0.8612 / 5 * 0.5);
    shelf.style.setProperty('--lfs', font + 'px');

    for (let i = start; i < start + visible; i++) {
      const card = landCard(s, i, cardW, isNew, hottest);

      if (land.slide) {
        card.classList.add('land-sweep-in');
        card.style.setProperty('--dir', land.slide);
        card.style.animationDelay = (land.slide > 0
          ? i - start : visible - 1 - (i - start)) * 55 + 'ms';
      } else if (!land.entered) {
        card.classList.add('land-in');
        card.style.animationDelay = ((i - start) * 60) + 'ms';
      }

      shelf.appendChild(card);
    }

    land.entered = true;
    renderLandMinis(s, paged, hottest);
    applySpot(s, shelf);

    // El barrido se consume al final: las miniaturas tambien lo miran para dar
    // su saltito.
    land.slide = 0;
  }

  /// Las miniaturas, el mismo idioma del vertical: todas las cartas a la vista
  /// chiquitas, las que estan en la mesa encendidas, y el estado de cada una en
  /// su etiqueta — dorada si le falta una, roja si ya canto. Tocarla trae su
  /// pagina a la mesa.
  function renderLandMinis(s, paged, hottest) {
    const box = $('landMinis');
    show(box, paged);
    if (!paged) return;

    box.innerHTML = '';

    s.cards.forEach((card, i) => {
      const now = tension(card);
      const onTable = Math.floor(i / PER_PAGE) === land.page;

      const mini = document.createElement('button');
      mini.className = 'mini' + (onTable ? ' mini-active' : '')
        + (onTable && land.slide ? ' land-mini-jump' : '')
        + (now.left === 0 ? ' land-mini-bingo' : '')
        + (now.left === 1 ? ' land-mini-falta' + (i === hottest && !onTable ? ' land-mini-hot' : '') : '');
      mini.innerHTML = '<span class="mini-thumb"><img src="assets/card-only.png" alt=""></span>'
        + '<span class="mini-label">' + card.number + '</span>';
      mini.dataset.page = Math.floor(i / PER_PAGE);
      box.appendChild(mini);
    });
  }

  /// Cambia la pagina de la mesa con un barrido de cartas: los que estaban se
  /// van de canto hacia un lado y los que llegan entran del otro, escalonados y
  /// asentandose con un rebote. El sentido lo manda el viaje, asi que coincide
  /// con el dedo cuando se desliza.
  function goPage(s, page) {
    const pages = Math.ceil(s.cards.length / PER_PAGE);
    const bounded = Math.max(0, Math.min(pages - 1, page));

    if (bounded === land.page) return;

    const dir = bounded > land.page ? 1 : -1;
    const shelf = $('landShelf');
    const ghost = $('landGhost');
    const stage = ghost.getBoundingClientRect();

    // Los que se van salen del estante ANTES de reconstruirlo, cada uno clavado
    // en el lugar que ocupaba: si se quedaran adentro, el innerHTML del estante
    // se los llevaria a mitad de la despedida.
    const leaving = Array.from(shelf.querySelectorAll('.land-card'));

    leaving.forEach((card, k) => {
      const spot = card.getBoundingClientRect();
      card.style.left = Math.round(spot.left - stage.left) + 'px';
      card.style.top = Math.round(spot.top - stage.top) + 'px';
      card.style.setProperty('--dir', dir);
      card.style.animationDelay = (dir > 0 ? k : leaving.length - 1 - k) * 45 + 'ms';
      card.className = 'land-card land-sweep-out';
      ghost.appendChild(card);
    });

    clearTimeout(land.ghostTimer);
    land.ghostTimer = setTimeout(() => { ghost.innerHTML = ''; }, 700);

    land.page = bounded;
    land.slide = dir;
    renderLandShelf(s, false);
  }

  /// El foco vive en el estado, no en el DOM: cada bolita reconstruye la mesa
  /// entera y unas clases sueltas no sobreviven al innerHTML.
  function applySpot(s, shelf) {
    shelf.classList.remove('land-spotting');
    if (land.spot === null) return;

    const idx = s.cards.findIndex((c) => c.number === land.spot);
    const node = idx >= 0 ? shelf.querySelector('[data-card="' + idx + '"]') : null;
    if (!node) return;

    shelf.classList.add('land-spotting');
    node.classList.add('land-spot');
  }

  function renderLandLupa(s) {
    const lupa = $('landLupa');
    const idx = land.lupa === null ? -1 : s.cards.findIndex((c) => c.number === land.lupa);

    if (!isLand() || idx < 0 || s.cards.length <= 2) {
      land.lupa = null;
      show(lupa, false);
      return;
    }

    lupa.innerHTML = '';
    const cardW = Math.min(window.innerWidth * 0.44, window.innerHeight * 0.94 * (1003 / 1214));
    lupa.style.setProperty('--lfs', Math.round(cardW * 0.8612 / 5 * 0.5) + 'px');
    const card = landCard(s, idx, cardW, false, -1);
    if (land.lupaFresh) { card.classList.add('land-pop'); land.lupaFresh = false; }
    lupa.appendChild(card);
    show(lupa, true);
  }

  function renderLand(s) {
    const box = $('land');

    if (!isLand()) { show(box, false); show($('landLupa'), false); return; }
    show(box, true);

    // -1 es "recien llegamos": ponerse al dia no anima, pero la primera bolita
    // de la noche (0 -> 1) si.
    const isNew = land.shown !== -1 && s.drawOrder.length > land.shown;
    land.shown = s.drawOrder.length;

    box.classList.toggle('land-chipsup', s.festivals.length > 1);

    renderMarquee(s, isNew);
    renderLandShelf(s, isNew);
    renderThermo(s, s.round.over || s.round.between);
    renderLandLupa(s);
  }

  /// El foco: antes de que el confeti tape todo, 700ms de "ESE carton gano". Vive
  /// en land.spot y lo pinta applySpot en cada render, porque el estado que llega
  /// detras del celebrate reconstruye la mesa y borraria unas clases sueltas. Si
  /// el ganador estaba en otra pagina, la mesa salta sola a mostrarlo.
  function landSpot(text) {
    const s = ui.state;
    if (!s) return false;

    // El ULTIMO numero del texto: el formato es '{figura} con el carton {N}' y
    // una figura podria llamarse con digitos ('4 esquinas').
    const nums = (text || '').match(/\d+/g);
    if (!nums) return false;

    const idx = s.cards.findIndex((c) => String(c.number) === nums[nums.length - 1]);
    if (idx < 0) return false;

    land.spot = s.cards[idx].number;
    goPage(s, Math.floor(idx / PER_PAGE));
    applySpot(s, $('landShelf'));

    clearTimeout(land.spotTimer);
    land.spotTimer = setTimeout(() => {
      land.spot = null;
      if (ui.state && isLand()) renderLand(ui.state);
    }, 1600);

    return true;
  }

  function goCard(i) {
    const s = ui.state;
    if (!s || s.cards.length === 0) return;

    const dir = i > ui.active ? -1 : 1;
    ui.active = ((i % s.cards.length) + s.cards.length) % s.cards.length;

    const hanger = $('hanger');
    hanger.classList.remove('hanger-swing');
    void hanger.offsetWidth;
    hanger.style.setProperty('--sd', (dir * 9) + 'deg');
    hanger.classList.add('hanger-swing');

    const peek = dir < 0 ? $('peekR') : $('peekL');
    peek.classList.remove('peek-nudge');
    void peek.offsetWidth;
    peek.classList.add('peek-nudge');

    const num = $('cardNo');
    num.classList.remove('num-pulse');
    void num.offsetWidth;
    num.classList.add('num-pulse');

    const wrap = $('bannerWrap');
    wrap.style.setProperty('--bntx', (dir * -46) + 'px');
    wrap.classList.remove('banner-slide');
    void wrap.offsetWidth;
    wrap.classList.add('banner-slide');

    const rail = document.querySelector('.rail');
    rail.style.setProperty('--rtx', (dir * -36) + 'px');
    rail.classList.remove('rail-slide');
    void rail.offsetWidth;
    rail.classList.add('rail-slide');

    const banner = $('banner');
    let sparks = banner.querySelector('.banner-sparks');
    if (sparks) sparks.remove();
    sparks = document.createElement('div');
    sparks.className = 'banner-sparks';
    for (let k = 0; k < 12; k++) {
      const sp = document.createElement('span');
      const ang = (k / 12) * Math.PI * 2;
      sp.style.setProperty('--dx', Math.round(Math.cos(ang) * 34) + 'px');
      sp.style.setProperty('--dy', Math.round(Math.sin(ang) * 16 - 6) + 'px');
      sp.style.animationDelay = (Math.random() * 0.12) + 's';
      sparks.appendChild(sp);
    }
    banner.appendChild(sparks);

    renderBanner(s);
    renderCard(s);
    renderMinis(s);
  }

  function showPrize() {
    const s = ui.state;
    if (!s || !s.round.prizeLine) return;

    $('prizeText').textContent = s.round.prizeLine.toUpperCase();

    const ribbon = $('prizeRibbon');
    ribbon.classList.add('hidden');
    void ribbon.offsetWidth;
    ribbon.classList.remove('hidden');

    clearTimeout(ui.prizeTimer);
    ui.prizeTimer = setTimeout(() => show(ribbon, false), 2500);
  }

  function celebrate(text) {
    const box = $('confetti');
    box.innerHTML = '';

    for (let i = 0; i < 90; i++) {
      const piece = document.createElement('span');
      const size = 6 + Math.random() * 8;
      piece.style.left = (Math.random() * 100) + '%';
      piece.style.width = size + 'px';
      piece.style.height = size + 'px';
      piece.style.background = CONFETTI_COLORS[i % CONFETTI_COLORS.length];
      piece.style.borderRadius = Math.random() > 0.5 ? '50%' : '2px';
      piece.style.animationDuration = (2.2 + Math.random() * 1.6) + 's';
      piece.style.animationDelay = (Math.random() * 0.9) + 's';
      box.appendChild(piece);
    }

    const letters = $('bingoLetters');
    letters.innerHTML = '';
    'BINGO'.split('').forEach((ch, i) => {
      const span = document.createElement('span');
      span.textContent = ch;
      span.style.color = LETTER_COLORS[i];
      span.style.animationDelay = (i * 0.1) + 's';
      letters.appendChild(span);
    });

    $('celebrateWhat').textContent = text || '';
    show($('celebrate'), true);
  }

  function onMessage(raw) {
    let msg;
    try { msg = JSON.parse(raw); } catch { return; }

    if (msg.kind === 'textos') {
      window.Idioma.aprender(msg);
      if (ui.state) render();
    } else if (msg.kind === 'state') {
      ui.state = msg;
      render();
    } else if (msg.kind === 'figures') {
      window.Figuras.learn(msg.names, msg.hints);
      if (ui.state) render();
    } else if (msg.kind === 'celebrate') {
      if (isLand() && landSpot(msg.text)) setTimeout(() => celebrate(msg.text), 700);
      else celebrate(msg.text);
      if (ui.state) { ui.state.bingo = msg.text; renderMega(ui.state); }
    } else if (msg.kind === 'log') {
      const box = $('logBox');
      box.textContent = msg.text;
      box.scrollTop = box.scrollHeight;
    }
  }

  window.addEventListener('HybridWebViewMessageReceived', (e) => onMessage(e.detail.message));

  $('doorScan').addEventListener('click', () => send('scan|' + $('nameField').value.trim()));
  $('doorCode').addEventListener('click', () => show($('codeSheet'), true));
  $('closeSheet').addEventListener('click', () => show($('codeSheet'), false));
  $('doorBack').addEventListener('click', () => send('back'));

  $('enterByHand').addEventListener('click', () => {
    const name = $('nameField').value.trim();
    const code = $('codeField').value.trim();
    if (!code) return;
    show($('codeSheet'), false);
    send('enter|' + name + '|' + code);
  });

  $('trophy').addEventListener('click', showPrize);

  /// El megafono CANTA: le avisa al organizador que esta persona esta reclamando.
  /// Antes solo repetia la animacion y nada salia del telefono -- quien completaba
  /// tenia que gritar y esperar que alguien lo mirara.
  $('mega').addEventListener('click', () => {
    const s = ui.state;
    if (!s || !s.bingo) return;

    celebrate(s.bingo);

    if (s.shouted) { toast(Idioma.texto('jugador.ya-avisaste')); return; }
    if (!s.canShout) return;

    toast(Idioma.texto('jugador.cantando-bingo'));
    send('bingo');
  });

  $('celebrate').addEventListener('click', () => show($('celebrate'), false));

  $('navLog').addEventListener('click', () => {
    show($('logPanel'), true);
    send('wantlog');
  });
  $('closeLog').addEventListener('click', () => show($('logPanel'), false));
  // Destapa los dos botones grandes en el mismo sitio, en vez de mandar a otra pantalla:
  // volver es lo frecuente, entrar a uno nuevo es lo raro, y lo raro puede costar un toque.
  $('doorMore').addEventListener('click', () => { ui.wantsNew = true; render(); });

  // La frase-guia, cuando los botones tapan la lista, es el camino de vuelta.
  $('doorSub').addEventListener('click', () => {
    if (ui.wantsNew) { ui.wantsNew = false; render(); }
  });

  $('navExit').addEventListener('click', () => send('another'));

  let toastTimer = null;

  function toast(text) {
    const box = $('toast');
    box.textContent = text;
    box.classList.add('hidden');
    void box.offsetWidth;
    box.classList.remove('hidden');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => show(box, false), 1700);
  }

  $('navLang').addEventListener('click', () => show($('langModal'), true));
  $('navChange').addEventListener('click', () => toast(Idioma.texto('jugador.nav.cambiar-carton') + ' · ' + Idioma.texto('general.pronto')));
  $('navBadge').addEventListener('click', openBadge);
  $('navSync').addEventListener('click', () => toast(Idioma.texto('jugador.nav.sincronizar') + ' · ' + Idioma.texto('general.pronto')));

  $('doorBadge').addEventListener('click', openBadge);
  $('closeBadge').addEventListener('click', () => show($('badgeModal'), false));
  $('badgeModal').addEventListener('click', (e) => {
    if (e.target === $('badgeModal')) show($('badgeModal'), false);
  });

  // El nombre se guarda apenas lo escribe: viaja en el carnet para que quien vende no lo
  // tenga que teclear, y el carnet se puede abrir sin haber entrado a ningun bingo.
  $('nameField').addEventListener('input', () => { ui.nameTouched = true; });
  $('nameField').addEventListener('change', () => {
    const name = $('nameField').value.trim();
    if (name) send('myname|' + name);
  });

  $('closeLang').addEventListener('click', () => show($('langModal'), false));
  $('langModal').addEventListener('click', (e) => {
    if (e.target === $('langModal')) show($('langModal'), false);
  });

  function chooseLang(lang, flag) {
    // El dueno del idioma es el telefono: se le avisa y el vuelve con el catalogo
    // nuevo por el puente (mensaje 'textos'), que es lo que repinta la pagina.
    send('idioma|' + lang);

    const old = flag.querySelector('.lang-sparks');
    if (old) old.remove();
    const sparks = document.createElement('span');
    sparks.className = 'lang-sparks';
    for (let k = 0; k < 10; k++) {
      const sp = document.createElement('i');
      sp.style.setProperty('--rot', Math.round((k / 10) * 360) + 'deg');
      sp.style.animationDelay = (Math.random() * 0.1) + 's';
      sparks.appendChild(sp);
    }
    flag.appendChild(sparks);

    setTimeout(() => show($('langModal'), false), 650);
  }

  $('pickEn').addEventListener('click', () => chooseLang('en', $('pickEn')));
  $('pickEs').addEventListener('click', () => chooseLang('es', $('pickEs')));

  const stage = $('stage');
  stage.addEventListener('pointerdown', (e) => { ui.swipeX = e.clientX; ui.swipeY = e.clientY; });
  stage.addEventListener('pointerup', (e) => {
    const s = ui.state;
    if (!s || s.cards.length < 2) return;

    const dx = e.clientX - ui.swipeX;
    const dy = e.clientY - ui.swipeY;
    if (Math.abs(dx) < 40 || Math.abs(dx) <= Math.abs(dy)) return;

    goCard(dx < 0 ? ui.active + 1 : ui.active - 1);
  });

  // El tubo entero es el blanco tactil del registro: en la mesa, un dedo impreciso
  // le pega a 78x300 sin mirar.
  $('landTube').addEventListener('click', () => {
    show($('logPanel'), true);
    send('wantlog');
  });

  // Tocar un carton lo agranda; tocar en cualquier lado lo devuelve.
  $('landShelf').addEventListener('click', (e) => {
    const s = ui.state;
    if (!s || land.swiped) return;

    const card = e.target.closest('.land-card');
    if (!card) return;

    const idx = Number(card.dataset.card);
    if (!s.cards[idx]) return;

    land.lupa = s.cards[idx].number;
    land.lupaFresh = true;
    renderLandLupa(s);
  });

  // Deslizar la mesa pasa de pagina, como el swipe del carton en vertical.
  $('landShelf').addEventListener('pointerdown', (e) => {
    land.swipeX = e.clientX;
    land.swipeY = e.clientY;
  });
  $('landShelf').addEventListener('pointerup', (e) => {
    const s = ui.state;
    land.swiped = false;
    if (!s || s.cards.length <= PER_PAGE) return;

    const dx = e.clientX - land.swipeX;
    const dy = e.clientY - land.swipeY;
    if (Math.abs(dx) < 40 || Math.abs(dx) <= Math.abs(dy)) return;

    land.swiped = true;
    goPage(s, land.page + (dx < 0 ? 1 : -1));
    setTimeout(() => { land.swiped = false; }, 250);
  });

  // Tocar una miniatura trae su pagina a la mesa.
  $('landMinis').addEventListener('click', (e) => {
    const s = ui.state;
    const mini = e.target.closest('.mini');
    if (!s || !mini) return;
    goPage(s, Number(mini.dataset.page));
  });


  $('landLupa').addEventListener('click', () => {
    land.lupa = null;
    show($('landLupa'), false);
  });

  // Al girar, la otra vista arranca de cero: sin animaciones acumuladas, sin lupa,
  // en la primera pagina y con la entrada escalonada de nuevo.
  window.matchMedia(LANDQ).addEventListener('change', () => {
    land.shown = -1;
    land.entered = false;
    land.lupa = null;
    land.spot = null;
    land.page = 0;
    if (ui.state) render();
  });

  window.addEventListener('resize', () => {
    fitCard();
    if (isLand() && ui.state && !ui.state.door) renderLand(ui.state);
  });

  window.__test = (state) => onMessage(JSON.stringify(state));

  send('ready');
})();
