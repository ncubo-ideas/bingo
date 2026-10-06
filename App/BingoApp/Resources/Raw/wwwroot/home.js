(function () {
  'use strict';

  const ui = { state: null, view: 'inicio', salesOf: null };

  /// El orden manda: es el de la barra de abajo y el que ubica la marquita.
  const TABS = ['inicio', 'funciones', 'ventas', 'mas'];

  const $ = (id) => document.getElementById(id);

  function send(msg) {
    if (window.HybridWebView && window.HybridWebView.SendRawMessage) {
      window.HybridWebView.SendRawMessage(msg);
    }
  }

  function show(el, visible) { el.classList.toggle('hidden', !visible); }

  function el(tag, cls, text) {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  // ── de que se ve cada pestaña ────────────────────────────────────────────

  /// Las cuatro pestañas son cuatro vistas de esta misma pagina: cambiar de
  /// pestaña no tiene que costar una pantalla nueva ni volver a preguntarle al
  /// diario. La cabecera tambien cambia -- Ventas y Mas se toman la pantalla
  /// completa, que es lo unico honesto cuando no hay nada que listar.
  function goView(which) {
    ui.view = which;

    const home = which === 'inicio';
    const list = which === 'funciones';
    const some = ui.state && ui.state.festivals.length > 0;

    show($('empty'), home && ui.state && !some);
    show($('full'), home && some);
    show($('all'), list);
    show($('ventas'), which === 'ventas');
    show($('mas'), which === 'mas');

    const sells = which === 'ventas';
    const more = which === 'mas';

    // Arriba hay saludo en las vistas que listan algo. Ventas tambien lo tiene, pero
    // dice de que va: es una pantalla de numeros y sin rotulo se lee como otra lista.
    // Y Mas igual: sin cabecera, su menu nacia pegado al reloj del telefono -- es la
    // cabecera la que paga esa franja, no el cuerpo.
    show(document.querySelector('.top'), home || list || sells || more);
    show($('helloSub'), list || sells || more);
    show($('pillNew'), list);

    $('hello').textContent = home ? saludo()
      : sells ? Idioma.texto('inicio.pestana.ventas')
      : more ? Idioma.texto('inicio.pestana.mas')
      : Idioma.texto('inicio.mis-bingos');

    $('helloSub').textContent = sells ? Idioma.texto('inicio.sub.ventas')
      : more ? Idioma.texto('inicio.sub.mas')
      : Idioma.texto('inicio.sub.bingos');

    document.querySelectorAll('.tab').forEach((tab) => {
      tab.classList.toggle('on', tab.dataset.go === which);
    });

    $('tabSlot').style.left = (TABS.indexOf(which) * 25) + '%';

    if (list) paintAll();
    if (sells) paintSales();
    if (which === 'mas') paintMas();

    $('body').scrollTop = 0;
  }

  function saludo() {
    const s = ui.state;
    return s && s.who ? Idioma.con('inicio.hola-nombre', { nombre: s.who }) : Idioma.texto('inicio.hola');
  }

  // ── un bingo, como tarjeta ─────────────────────────────────────────────

  /// Una sola tarjeta para las tres listas (los otros del inicio, los proximos y
  /// las anteriores): si el inicio y la lista dibujaran distinto el mismo bingo,
  /// habria que mantener dos.
  function cardOf(v) {
    const row = el('button', 'funcion');

    const top = el('div', 'funcion-top');
    const name = el('div', 'funcion-name' + (v.over ? ' old' : ''), v.name);
    top.appendChild(name);

    const chip = chipOf(v);
    if (chip) top.appendChild(chip);
    row.appendChild(top);

    row.appendChild(el('div', 'funcion-sub',
      Idioma.muchos(v.sold, 'inicio.vendidos.uno', 'inicio.vendidos.varios') + '  ·  '
      + Idioma.muchos(v.prizes, 'inicio.premios.uno', 'inicio.premios.varios')));

    const foot = el('div', 'funcion-foot');
    foot.appendChild(el('div', 'funcion-state' + (v.over ? ' old' : ''), stateOf(v)));
    foot.appendChild(el('div', 'funcion-go', Idioma.texto('inicio.ver-bingo') + ' →'));
    row.appendChild(foot);

    row.addEventListener('click', () => send('open|' + v.id));
    return row;
  }

  /// Hay juego cuando salio una bolita, no cuando hay una ronda en curso: un bingo
  /// recien creado ya tiene su ronda 1, y decirle "EN JUEGO" a algo donde nadie canto
  /// todavia hace que el organizador crea que llego tarde. Es la misma regla que usa
  /// el detalle.
  function chipOf(v) {
    // El ensayo se dice ANTES que el estado: que un bingo sea de practica cambia como
    // se lee todo lo demas, y confundirlo con uno real es el unico error que este
    // modo puede causar.
    if (v.practice) return el('span', 'chip chip-practice', '\u{1F3AD} ' + Idioma.texto('inicio.chip.ensayo'));
    if (v.over) return el('span', 'chip chip-over', Idioma.texto('inicio.chip.finalizado'));
    if (v.draws > 0) return el('span', 'chip chip-live', Idioma.texto('inicio.chip.en-juego'));
    if (v.rounds > 0) return el('span', 'chip chip-ready', Idioma.texto('inicio.chip.listo'));
    return null;
  }

  function fill(box, which) {
    box.textContent = '';
    which.slice().reverse().forEach((v) => box.appendChild(cardOf(v)));
  }

  // ── la lista completa ────────────────────────────────────────────────────

  function paintAll() {
    const s = ui.state;
    if (!s) return;

    const next = s.festivals.filter((v) => !v.over);
    const past = s.festivals.filter((v) => v.over);

    show($('allNone'), s.festivals.length === 0);
    show($('groupNext'), next.length > 0);
    show($('groupPast'), past.length > 0);

    fill($('listNext'), next);
    fill($('listPast'), past);
  }

  // ── el inicio ────────────────────────────────────────────────────────────

  /// La pestana "Mas": lo que no es de ninguna noche. La primera fila cambia de cara
  /// segun haya o no un ensayo armado -- crear otro cuando ya hay uno no ayuda a nadie,
  /// y sin un lugar para terminarlo el ensayo se quedaria para siempre en la lista.
  function paintMas() {
    const s = ui.state;
    if (!s) return;

    const ensayo = (s.festivals || []).find((v) => v.practice);

    ui.ensayo = ensayo ? ensayo.id : null;

    $('masEnsayoTitle').textContent = Idioma.texto(ensayo ? 'inicio.mas.ensayo.terminar' : 'inicio.mas.ensayo.crear');
    $('masEnsayoSub').textContent = Idioma.texto(ensayo ? 'inicio.mas.ensayo.sub.terminar' : 'inicio.mas.ensayo.sub.crear');

    $('masVersion').textContent = s.version
      ? Idioma.con('inicio.mas.version', { version: s.version + (s.build ? ' (' + s.build + ')' : '') })
      : Idioma.texto('inicio.mas.version.sin');
  }

  function render() {
    const s = ui.state;
    if (!s) return;

    // La pestana Mas se repinta con cada retrato: el ensayo puede nacer o morir
    // mientras esta abierta.
    if (ui.view === 'mas') paintMas();

    if (ui.view !== 'inicio') { goView(ui.view); return; }

    $('hello').textContent = saludo();

    // Por FECHA y no por orden de creacion: el de ahora es el que se juega antes.
    const alive = s.festivals.filter((v) => !v.over).slice().sort(soonestFirst);
    const now = alive.length > 0 ? alive[0] : null;

    // Los demas que tambien son de hoy suben a su propio grupo: con dos bingos la misma
    // noche, el segundo caia entre los "otros" como si fuera de otro dia.
    const alsoToday = now && isToday(now)
      ? alive.filter((v) => v.id !== now.id && isToday(v))
      : [];

    // Abajo van TODOS los demas, no solo los terminados: un bingo que no sea el
    // de ahora no puede quedar sin puerta desde el inicio.
    const upThere = new Set([now ? now.id : null, ...alsoToday.map((v) => v.id)]);
    const rest = s.festivals.filter((v) => !upThere.has(v.id));

    show($('empty'), s.festivals.length === 0);
    show($('full'), s.festivals.length > 0);

    if (now) {
      $('card').dataset.id = now.id;
      $('nextName').textContent = now.name;
      $('nextWhen').textContent = whenSaid(now) || Idioma.con('inicio.ahora.creado', { cuando: now.createdAt });

      // El numero grande dice en que va la noche: la ronda si esta jugando, y si
      // no, cuantas quedan anunciadas.
      if (now.rounds > 0 && now.currentRound > 0) {
        $('nextBigWhat').textContent = Idioma.texto('inicio.ahora.va-en-la');
        $('nextBigNum').textContent = Idioma.con('inicio.ahora.ronda-n', { n: now.currentRound });
        $('nextBigUnit').textContent = Idioma.con('inicio.ahora.de-n', { n: now.rounds })
          + '  ·  ' + (now.draws > 0 ? Idioma.muchos(now.draws, 'inicio.ahora.cantadas.uno', 'inicio.ahora.cantadas.varios') : Idioma.texto('inicio.ahora.sin-cantar'));
      } else if (now.rounds > 0) {
        $('nextBigWhat').textContent = Idioma.texto('inicio.ahora.entre-rondas');
        $('nextBigNum').textContent = now.rounds;
        $('nextBigUnit').textContent = Idioma.texto(now.rounds === 1 ? 'inicio.ahora.rondas-anunciadas.uno' : 'inicio.ahora.rondas-anunciadas.varios');
      } else {
        $('nextBigWhat').textContent = Idioma.texto('inicio.ahora.listo-para');
        $('nextBigNum').textContent = Idioma.texto('inicio.ahora.empezar');
        $('nextBigUnit').textContent = Idioma.texto('inicio.ahora.todavia-no-se-canta');
      }

      $('statSold').textContent = now.sold;
      $('statIn').textContent = now.people;
      $('statPrizes').textContent = now.prizes;
    } else if (s.festivals.length > 0) {
      // Todos terminados: la tarjeta de arriba no tiene que mentir.
      $('card').dataset.id = '';
      $('nextName').textContent = Idioma.texto('inicio.ahora.ninguno-en-curso');
      $('nextWhen').textContent = Idioma.texto('inicio.ahora.los-que-jugaste');
      $('nextBigWhat').textContent = '';
      $('nextBigNum').textContent = s.festivals.length;
      $('nextBigUnit').textContent = Idioma.texto(s.festivals.length === 1 ? 'inicio.ahora.bingos-jugados.uno' : 'inicio.ahora.bingos-jugados.varios');
      $('statSold').textContent = '0';
      $('statIn').textContent = '0';
      $('statPrizes').textContent = '0';
    }

    show($('see'), !!now);

    show($('todayWrap'), alsoToday.length > 0);
    $('todayTag').textContent = alsoToday.length === 1
      ? Idioma.texto('inicio.tambien-hoy')
      : Idioma.con('inicio.tambien-hoy-n', { n: alsoToday.length });

    // Sin invertir: ya vienen ordenados por hora, y el que abre antes va primero.
    const box = $('todayList');
    box.textContent = '';
    alsoToday.forEach((v) => box.appendChild(cardOf(v)));

    show($('pastWrap'), rest.length > 0);
    $('pastTag').textContent = Idioma.texto(rest.every((v) => v.over) ? 'inicio.bingos-anteriores' : 'inicio.tus-otros-bingos');

    fill($('past'), rest);
  }

  // ── cuando se juega cada uno ────────────────────────────────────────────

  function today() {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), now.getDate());
  }

  /// El dia que se juega, o null en los bingos de antes de que se preguntara la fecha.
  function playsOn(v) {
    const said = (v.playsOn || '').split('-');

    return said.length === 3
      ? new Date(Number(said[0]), Number(said[1]) - 1, Number(said[2]))
      : null;
  }

  function daysAway(v) {
    const day = playsOn(v);
    return day === null ? null : Math.round((day - today()) / 86400000);
  }

  function isToday(v) { return daysAway(v) === 0; }

  /// Los minutos del dia en que abre, para desempatar dos del mismo dia. Sin hora dicha
  /// va al final: el que si dijo a que hora es el que uno tiene mas presente.
  function opensAt(v) {
    const said = (v.doorsAt || '').split(':');

    return said.length === 2 ? Number(said[0]) * 60 + Number(said[1]) : 24 * 60;
  }

  /// Cual es "el de ahora". Antes era simplemente el ultimo creado, que no tiene nada
  /// que ver con cual se juega antes: quien preparo el de diciembre en enero lo veia
  /// arriba y el de esta noche escondido entre los otros.
  ///
  /// Orden: el de hoy primero --y entre varios de hoy, el que abre antes--, despues los
  /// que vienen, despues los que ya se les paso el dia sin cerrarse, y de ultimo los que
  /// no tienen fecha, que son los de antes de que se preguntara.
  ///
  /// Lo vencido va DESPUES de lo de hoy aunque este mas cerca en el calendario: un bingo
  /// al que se le paso el dia no es "el de ahora", es uno que quedo colgando. Ordenar
  /// solo por distancia lo ponia arriba y escondia el de esta noche.
  function whenRank(v) {
    const gap = daysAway(v);

    if (gap === null) return 3;
    if (gap === 0) return 0;

    return gap > 0 ? 1 : 2;
  }

  function soonestFirst(a, b) {
    const ra = whenRank(a);
    const rb = whenRank(b);

    if (ra !== rb) return ra - rb;
    if (ra === 3) return 0;

    const da = daysAway(a);
    const db = daysAway(b);

    // Entre los vencidos, el mas reciente primero: es el que uno todavia tiene en la
    // cabeza. Entre los que vienen, el mas proximo.
    if (da !== db) return ra === 2 ? db - da : da - db;

    return opensAt(a) - opensAt(b);
  }

  /// Cuando se juega, dicho en voz alta. Es lo que la tarjeta grande necesita para que
  /// se entienda cual es el de esta noche.
  function whenSaid(v) {
    const gap = daysAway(v);
    const hour = (v.doorsAt || '').split(':');
    const at = hour.length === 2
      ? ' ' + Idioma.con('inicio.cuando.a-las', { hora: Idioma.hora(Number(hour[0]), Number(hour[1])) })
      : '';

    if (gap === null) return v.playing ? Idioma.con('inicio.cuando.se-juega', { figuras: saidWell(v.playing) }) : '';
    if (gap === 0) return Idioma.texto('inicio.cuando.hoy') + at;
    if (gap === 1) return Idioma.texto('inicio.cuando.manana') + at;
    if (gap < 0) return Idioma.con('inicio.cuando.era-para', { fecha: Idioma.fecha(playsOn(v)) });

    return Idioma.con('inicio.cuando.el-dia', { fecha: Idioma.fechaLarga(playsOn(v)) });
  }

  /// La plata se escribe como la dice la gente: con su signo y de a miles.
  function money(n) {
    return '₡' + String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  }

  /// La letra de la carita. Va aca y no compartida con el detalle porque las dos
  /// paginas no comparten scripts: cada una carga los suyos.
  function initial(name) {
    return (name || '?').trim().charAt(0).toUpperCase() || '?';
  }

  /// Las figuras llegan con el nombre del motor y separadas por coma. Al organizador
  /// se le dicen por su nombre de la casa.
  function saidWell(figures) {
    return figures.split(',')
      .map((one) => window.Figuras.name(one.trim()))
      .join(', ');
  }

  function stateOf(v) {
    if (v.over) return Idioma.texto('inicio.estado.terminado');
    if (v.rounds > 0 && v.currentRound > 0) return Idioma.con('inicio.estado.ronda-de', { n: v.currentRound, total: v.rounds });
    if (v.rounds > 0) return Idioma.texto('inicio.estado.entre-rondas');
    return Idioma.texto('inicio.estado.sin-arrancar');
  }

  // -- ventas ---------------------------------------------------------------

  /// Ventas mira UN bingo a la vez: "cuanto vendi" es una pregunta de una noche. Cuenta
  /// CARTONES y no plata, porque el carton todavia no tiene precio en ningun lado -- ni
  /// en el pueblo, ni en el motor, ni en el aviso de venta.
  function paintSales() {
    const s = ui.state;
    if (!s) return;

    paintPicker();

    const v = salesNow();

    show($('vPickWrap'), s.festivals.length > 1);

    if (!v) {
      show($('vProgress'), false);
      show($('vWhoWrap'), false);
      show($('vNone'), true);
      $('vNone').textContent = Idioma.texto('inicio.ventas.ningun-bingo');

      $('vSold').textContent = '0';
      $('vSoldPct').textContent = '';
      $('vLeft').textContent = '0';
      $('vOf').textContent = '';
      return;
    }

    const total = v.cards || 0;
    const pct = total > 0 ? Math.round((v.sold / total) * 100) : 0;
    const price = v.price || 0;

    // La plata es vendidos x precio, y solo existe si el bingo tiene precio. La caja
    // se queda en su lugar vacia cuando no lo tiene: sacarla cambiaria la forma de la
    // pantalla el dia que se lo pongan.
    $('vMoney').textContent = price > 0 ? money(v.sold * price) : '—';
    $('vMoney').classList.toggle('tri-off', price <= 0);
    $('vMoneyNote').textContent = price > 0
      ? Idioma.con('inicio.ventas.a-precio-el-carton', { precio: money(price) })
      : Idioma.texto('inicio.ventas.falta-el-precio');
    $('vMoneyNote').classList.toggle('tri-bad', price <= 0);

    $('vMissing').textContent = Idioma.texto(price > 0 ? 'inicio.ventas.falta-la-hora' : 'inicio.ventas.falta-precio-y-hora');

    $('vSold').textContent = v.sold;
    $('vSoldPct').textContent = total > 0 ? Idioma.con('inicio.ventas.del-total', { pct: pct }) : Idioma.texto('inicio.ventas.sin-cartones-puestos');
    $('vLeft').textContent = Math.max(0, total - v.sold);
    $('vOf').textContent = Idioma.con('inicio.ventas.de-totales', { n: total });

    // La barra y el desglose solo cuando hay algo que mostrar: una barra en cero y una
    // lista vacia ocupan media pantalla para decir que no paso nada.
    show($('vProgress'), v.sold > 0);
    $('vProgressNum').textContent = Idioma.con('inicio.ventas.progreso', { vendidos: v.sold, total: total });
    $('vBar').style.width = pct + '%';
    $('vPct').textContent = Idioma.con('inicio.ventas.pct-vendido', { pct: pct });

    show($('vNone'), v.sold === 0);
    $('vNone').textContent = Idioma.texto(v.over ? 'inicio.ventas.ninguna.terminado' : 'inicio.ventas.ninguna.todavia');

    paintWhoSold(v);
  }

  /// El selector. Solo tiene sentido con mas de un bingo, y arranca en el que se esta
  /// jugando -- que es el que uno viene a mirar cuando abre Ventas en plena noche.
  function paintPicker() {
    const s = ui.state;
    const pick = $('vPick');

    const same = pick.children.length === s.festivals.length
      && [...pick.children].every((o, i) => o.value === s.festivals[i].id);

    if (!same) {
      pick.textContent = '';

      s.festivals.forEach((v) => {
        const option = el('option', null, v.name);
        option.value = v.id;
        pick.appendChild(option);
      });
    }

    if (!s.festivals.some((v) => v.id === ui.salesOf)) {
      const live = s.festivals.filter((v) => !v.over);
      const first = live.length > 0 ? live[live.length - 1] : s.festivals[s.festivals.length - 1];

      ui.salesOf = first ? first.id : null;
    }

    if (ui.salesOf) pick.value = ui.salesOf;
  }

  function salesNow() {
    const s = ui.state;

    return s.festivals.find((v) => v.id === ui.salesOf) || null;
  }

  /// Quien vendio cuanto, con su parte del total. El porcentaje es sobre lo VENDIDO y no
  /// sobre los cartones del bingo: la pregunta es como se repartio la venta entre ellas.
  function paintWhoSold(v) {
    const price = v.price || 0;

    const box = $('vWho');
    box.textContent = '';

    const selling = (v.sellers || []).filter((one) => one.sold > 0);

    show($('vWhoWrap'), selling.length > 0);
    if (selling.length === 0) return;

    selling.slice().sort((a, b) => b.sold - a.sold).forEach((one) => {
      const row = el('div', 'who');

      row.appendChild(el('div', 'who-face who-sell', initial(one.name)));

      const what = el('div', 'who-what');

      const line = el('div', 'who-line');
      line.appendChild(el('div', 'who-name', one.name));
      line.appendChild(el('div', 'sales-num',
        price > 0 ? money(one.sold * price) : String(one.sold)));
      what.appendChild(line);

      const share = v.sold > 0 ? Math.round((one.sold / v.sold) * 100) : 0;

      what.appendChild(el('div', 'who-sub',
        Idioma.muchos(one.sold, 'inicio.vendidos.uno', 'inicio.vendidos.varios') + '  ·  '
        + Idioma.con('inicio.ventas.de-lo-vendido', { pct: share })));

      const bar = el('div', 'bar-wrap');
      const fill = el('div', 'bar-fill');
      fill.style.width = share + '%';
      bar.appendChild(fill);
      what.appendChild(bar);

      row.appendChild(what);
      box.appendChild(row);
    });
  }

  // ── lo que el telefono le cuenta a la pagina ─────────────────────────────

  function onMessage(raw) {
    let msg;
    try { msg = JSON.parse(raw); } catch { return; }

    // El catalogo del idioma de la persona. Llega primero que todo lo demas, y otra vez
    // cada vez que cambia con el selector.
    if (msg.kind === 'textos') { window.Idioma.aprender(msg); if (ui.state) render(); return; }

    if (msg.kind === 'state') {
      ui.state = msg;
      render();
    } else if (msg.kind === 'figures') {
      window.Figuras.learn(msg.names, msg.hints);
      if (ui.state) render();
    } else if (msg.kind === 'log') {
      const box = $('logBox');
      box.textContent = msg.text;
      box.scrollTop = box.scrollHeight;
    }
  }

  window.addEventListener('HybridWebViewMessageReceived', (e) => onMessage(e.detail.message));

  // ── los gestos ───────────────────────────────────────────────────────────

  $('firstOne').addEventListener('click', () => send('new'));
  $('firstToo').addEventListener('click', () => send('new'));
  $('another').addEventListener('click', () => send('new'));
  $('pillNew').addEventListener('click', () => send('new'));

  $('see').addEventListener('click', () => {
    const id = $('card').dataset.id;
    if (id) send('open|' + id);
  });

  $('seeAll').addEventListener('click', () => goView('funciones'));

  // ── la pestana Mas ───────────────────────────────────────────────────────

  $('masEnsayo').addEventListener('click', () => {
    if (ui.ensayo) send('ensayofin|' + ui.ensayo);
    else send('ensayo');
  });

  $('masPapel').addEventListener('click', () => send('papel'));

  // El acerca-de no lleva a ninguna parte: lo que hay que saber ya esta escrito en su
  // propia bajada. Tocarlo no hace nada, y por eso no promete que si.
  $('masAcerca').addEventListener('click', () => {});

  $('vPick').addEventListener('change', () => {
    ui.salesOf = $('vPick').value;
    paintSales();
  });

  document.querySelectorAll('.tab').forEach((tab) => {
    tab.addEventListener('click', () => goView(tab.dataset.go));
  });

  $('logFold').addEventListener('click', () => {
    const box = $('logBox');
    const open = box.classList.contains('hidden');

    show(box, open);
    $('logChevron').classList.toggle('open', open);

    if (open) send('wantlog');
  });

  window.__test = (msg) => onMessage(JSON.stringify(msg));

  goView('inicio');
  send('ready');
})();
