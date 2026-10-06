(function () {
  'use strict';

  // Lo que se arma vive ACA hasta el ultimo paso: el telefono no se entera de nada
  // hasta que se toca crear, y ahi recibe el plan entero de una sola vez.
  // Cinco secciones en la pagina, TRES circulos en el indicador: armar los premios y
  // repasar el resumen son la continuacion del tercer paso, no pasos aparte -- es como
  // lo cuenta la maqueta. Con un punto por seccion la barra no terminaba nunca y el
  // wizard se sentia el doble de largo de lo que es.
  const SECTIONS = 5;

  const DOTS = [
    {
      clave: 'crear.paso.informacion',
      icon: '<rect x="5" y="3" width="14" height="18" rx="2" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M8 8h8M8 12h8M8 16h5" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>',
    },
    {
      clave: 'crear.paso.cartones',
      icon: '<path d="M3 9.5a2 2 0 0 0 0 5V18a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-3.5a2 2 0 0 1 0-5V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2z" fill="none" stroke="currentColor" stroke-width="1.7"/><path d="M14 5v14" stroke="currentColor" stroke-width="1.7" stroke-dasharray="2 2.6"/>',
    },
    {
      clave: 'crear.paso.rondas',
      icon: '<circle cx="12" cy="12" r="8.5" fill="none" stroke="currentColor" stroke-width="1.7"/><circle cx="12" cy="12" r="4.5" fill="none" stroke="currentColor" stroke-width="1.7"/><circle cx="12" cy="12" r="1.4" fill="currentColor"/>',
    },
  ];

  const CHECK = '<path d="M5 12.5l4.5 4.5L19 7" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>';

  // Los saltos de la rueda, de su archivo. Una cantidad escrita a mano se mete en la
  // lista ordenada en vez de reemplazarla: asi la rueda la puede centrar y se ve donde
  // cae respecto de los saltos redondos.
  const QUANTITIES = [100, 200, 300, 400, 500, 600, 700, 800, 900, 1000];

  // Como se ve cada renglon segun lo lejos que este del centro. Es su tabla, tal cual.
  const WHEEL_ROWS = [
    { size: '20px', color: '#D3D5DE', opacity: 0.5, scale: 'scale(0.94)' },
    { size: '26px', color: '#9A9DAE', opacity: 0.8, scale: 'scale(0.94)' },
    { size: '40px', color: '#171827', opacity: 1, scale: 'scale(1)' },
  ];

  const ui = {
    step: 0,
    stage: 'pattern',   // pattern | prize | more
    cash: true,
    figure: null,
    plays: today(),     // el dia que se juega
    hour: -1,           // -1 mientras no digan hora
    minute: 0,
    draft: null,        // el mes que se esta mirando en el calendario
    picked: null,       // el dia marcado en el calendario, sin confirmar
    cards: 500,         // lo que marca la rueda
    ownCards: false,    // true cuando la cantidad la escribieron a mano
    roundCount: 1,
    rounds: [{ games: [] }],
    at: 0,              // que ronda se esta armando
    figures: [],
    creating: false,
  };

  const $ = (id) => document.getElementById(id);

  function send(msg) {
    if (window.HybridWebView && window.HybridWebView.SendRawMessage) {
      window.HybridWebView.SendRawMessage(msg);
    }
  }

  function show(el, visible) { el.classList.toggle('hidden', !visible); }

  function say(line) { send('say|' + line); }

  function el(tag, cls, text) {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function svg(inside) {
    return '<svg viewBox="0 0 24 24" width="15" height="15">' + inside + '</svg>';
  }

  function prizeText(game) { return game.cash ? '$' + game.prize : game.prize; }

  // ── el dia ───────────────────────────────────────────────────────────────

  /// Hoy a las cero horas. Sin cortarle la hora, dos fechas del mismo dia se comparan
  /// distinto segun el momento en que se abrio la pantalla.
  function today() {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), now.getDate());
  }

  function sameDay(a, b) {
    return a.getFullYear() === b.getFullYear()
      && a.getMonth() === b.getMonth()
      && a.getDate() === b.getDate();
  }

  /// El dia como lo entiende el telefono: "yyyy-MM-dd" armado a mano y no con toISOString,
  /// que pasa por UTC y en nuestro huso devuelve el dia anterior.
  function asWired(day) {
    const two = (n) => String(n).padStart(2, '0');
    return day.getFullYear() + '-' + two(day.getMonth() + 1) + '-' + two(day.getDate());
  }

  function daysBetween(from, to) {
    return Math.round((to - from) / 86400000);
  }

  /// Como se dice un dia en voz alta. Hoy y manana tienen nombre propio; de ahi en
  /// adelante lo que ubica es el dia de la semana, no el numero.
  function daySaid(day) {
    const gap = daysBetween(today(), day);

    if (gap === 0) return Idioma.texto('general.hoy');
    if (gap === 1) return Idioma.texto('general.manana');

    return Idioma.fechaLarga(day);
  }

  function hourSaid() {
    if (ui.hour < 0) return Idioma.texto('crear.sin-hora');

    return Idioma.hora(ui.hour, ui.minute);
  }

  function paintWhen() {
    $('dateSaid').textContent = daySaid(ui.plays);
    $('timeSaid').textContent = ui.hour < 0 ? Idioma.texto('crear.elegir-hora') : hourSaid();
    $('timeCard').classList.toggle('when-empty', ui.hour < 0);
  }

  // ── el calendario ────────────────────────────────────────────────────────

  function openDate() {
    ui.picked = new Date(ui.plays);
    ui.draft = new Date(ui.plays.getFullYear(), ui.plays.getMonth(), 1);

    paintCalendar();
    show($('dateSheet'), true);
  }

  function paintCalendar() {
    $('calMonth').textContent = Idioma.mes(ui.draft.getMonth()) + ' ' + ui.draft.getFullYear();

    // Se rearma cada vez: los nombres de los dias cambian con el idioma.
    const week = $('calDays');
    week.textContent = '';
    for (let d = 0; d < 7; d++) week.appendChild(el('div', 'cal-day-name', Idioma.diaSemana(d)));

    const grid = $('calGrid');
    grid.textContent = '';

    const first = new Date(ui.draft.getFullYear(), ui.draft.getMonth(), 1);
    const last = new Date(ui.draft.getFullYear(), ui.draft.getMonth() + 1, 0).getDate();

    // Los huecos antes del primero: sin ellos el 1 no cae bajo su dia de la semana y
    // el mes entero se lee corrido.
    for (let i = 0; i < first.getDay(); i++) grid.appendChild(el('div', 'cal-hole'));

    for (let number = 1; number <= last; number++) {
      const day = new Date(ui.draft.getFullYear(), ui.draft.getMonth(), number);
      const cell = el('button', 'cal-cell', String(number));

      // El pueblo se niega a anunciar para un dia que ya paso, asi que aca no se
      // ofrece: mejor que no se pueda tocar a que conteste que no.
      if (day < today()) {
        cell.disabled = true;
        cell.classList.add('cal-gone');
      } else {
        if (sameDay(day, ui.picked)) cell.classList.add('on');
        if (sameDay(day, today())) cell.classList.add('cal-today');

        cell.addEventListener('click', () => {
          ui.picked = day;
          paintCalendar();
        });
      }

      grid.appendChild(cell);
    }

    // No se puede retroceder mas alla del mes de hoy: no habria un solo dia elegible.
    const now = today();
    $('calBack').disabled = ui.draft.getFullYear() === now.getFullYear()
      && ui.draft.getMonth() === now.getMonth();
  }

  function slideMonth(by) {
    ui.draft = new Date(ui.draft.getFullYear(), ui.draft.getMonth() + by, 1);
    paintCalendar();
  }

  function saveDate() {
    ui.plays = ui.picked;
    paintWhen();
    show($('dateSheet'), false);
  }

  // ── la hora ──────────────────────────────────────────────────────────────

  function openTime() {
    // Sin hora dicha, el reloj arranca en una hora de bingo y no en la de ahora:
    // nadie abre las puertas a las 3:17.
    if (ui.hour < 0) { ui.hour = 19; ui.minute = 0; }

    paintClock();
    show($('timeSheet'), true);
  }

  function paintClock() {
    const twelve = ui.hour % 12 === 0 ? 12 : ui.hour % 12;

    $('clockHour').textContent = twelve;
    $('clockMinute').textContent = String(ui.minute).padStart(2, '0');

    $('isAm').classList.toggle('on', ui.hour < 12);
    $('isPm').classList.toggle('on', ui.hour >= 12);
  }

  function bumpClock(what, by) {
    if (what === 'hour') {
      // Se mueve la hora del reloj de doce y se le devuelve la mitad del dia que ya
      // tenia: subir de 11 a 12 no puede saltar de la manana a la tarde sola.
      const pm = ui.hour >= 12;
      let twelve = (ui.hour % 12) + by;

      if (twelve < 0) twelve = 11;
      if (twelve > 11) twelve = 0;

      ui.hour = pm ? twelve + 12 : twelve;
    } else {
      let minute = ui.minute + by;

      if (minute < 0) minute = 55;
      if (minute > 59) minute = 0;

      ui.minute = minute;
    }

    paintClock();
  }

  function setHalf(pm) {
    const twelve = ui.hour % 12;
    ui.hour = pm ? twelve + 12 : twelve;
    paintClock();
  }

  // ── los pasos ────────────────────────────────────────────────────────────

  function goStep(which) {
    ui.step = which;

    for (let i = 0; i < SECTIONS; i++) show($('s' + (i + 1)), i === which);

    $('stepName').textContent = Idioma.texto(DOTS[Math.min(which, DOTS.length - 1)].clave);

    // El paso de los premios avanza con sus propias respuestas, no con "Siguiente":
    // dejar el boton ahi seria darle dos caminos a la misma decision.
    show($('foot'), which !== 3);
    // Cada paso nombra lo que hace su boton, en vez de un "Siguiente" que no dice a
    // donde lleva. El de las rondas ademas cuenta cuantas se van a configurar.
    $('next').textContent =
      which === 2 ? nextRoundsSays()
      : which === SECTIONS - 1 ? Idioma.texto('crear.titulo')
      : Idioma.texto('crear.continuar');

    if (which === 1) paintWheel();
    if (which === 2) paintRoundCount();
    if (which === 3) openRound(firstUnfinished());
    if (which === 4) paintReview();

    paintSteps();
    $('body').scrollTop = 0;
  }

  /// De los premios en adelante los tres van completados: ya no queda nada que el
  /// indicador pueda prometer, y dejar el tercero a medias mientras se arman cinco
  /// premios hace parecer que el wizard no avanza.
  function paintSteps() {
    const box = $('steps');
    box.textContent = '';

    const at = ui.step >= DOTS.length ? DOTS.length : ui.step;

    DOTS.forEach((step, i) => {
      if (i > 0) {
        const bar = el('div', 'bar' + (i <= at ? ' done' : ''));
        box.appendChild(bar);
      }

      const dot = el('div', 'dot' + (i < at ? ' done' : i === at ? ' on' : ''));
      dot.innerHTML = svg(i < at ? CHECK : step.icon);
      box.appendChild(dot);
    });
  }

  function onNext() {
    if (ui.step === 0) { goStep(1); return; }

    if (ui.step === 1) {
      if (howManyCards() <= 0) {
        say(Idioma.texto('crear.log.cuantos-cartones'));
        return;
      }
      goStep(2);
      return;
    }

    if (ui.step === 2) { goStep(3); return; }

    create();
  }

  /// Atras SIEMPRE devuelve un paso de verdad y nunca borra lo escrito. Adentro de los
  /// premios retrocede de etapa, y desde la primera etapa de la primera ronda sale al
  /// paso anterior en vez de quedarse trabado.
  function onBack() {
    if (ui.step === 0) { send('leave'); return; }

    if (ui.step === 3) {
      if (ui.stage === 'prize') { goStage('pattern'); return; }

      if (ui.stage === 'pattern') {
        // Con juegos ya guardados, atras vuelve a la lista de la ronda; sin ninguno,
        // se va a la ronda anterior o, si es la primera, al paso de cuantas rondas.
        if (here().games.length > 0) { goStage('more'); return; }
        if (ui.at > 0) { openRound(ui.at - 1); return; }

        goStep(2);
        return;
      }

      if (ui.stage === 'more') {
        if (ui.at > 0) { openRound(ui.at - 1); return; }
        goStep(2);
        return;
      }
    }

    goStep(ui.step - 1);
  }

  /// El precio del carton, o cero si no lo dijo: es opcional y se puede poner
  /// despues desde el detalle.
  function cardPrice() {
    const said = parseInt($('price').value, 10);

    return Number.isFinite(said) && said > 0 ? said : 0;
  }

  function howManyCards() { return ui.cards; }

  /// La lista con la cantidad escrita a mano metida en su lugar, si no era uno de los
  /// saltos redondos.
  function quantities() {
    const all = QUANTITIES.slice();

    if (!all.includes(ui.cards)) all.push(ui.cards);

    return all.sort((a, b) => a - b);
  }

  /// Cinco renglones: el elegido en el medio y dos a cada lado. En las puntas de la
  /// lista se repite el extremo en vez de dejar el hueco, que es lo que hace su rueda.
  function paintWheel() {
    const box = $('wheel');

    box.querySelectorAll('.wheel-row').forEach((row) => row.remove());

    const all = quantities();
    const middle = all.indexOf(ui.cards);

    [-2, -1, 0, 1, 2].forEach((off) => {
      const at = Math.max(0, Math.min(all.length - 1, middle + off));
      const value = all[at];
      const look = WHEEL_ROWS[2 - Math.abs(off)];

      const row = el('button', 'wheel-row', String(value));

      // Con la cantidad escrita a mano la rueda entera se apaga: lo que manda es el
      // campo, y dejar un numero negro en el medio diria lo contrario.
      row.style.fontSize = look.size;
      row.style.color = ui.ownCards ? '#9A9DAE' : look.color;
      row.style.opacity = ui.ownCards ? 0.5 : look.opacity;
      row.style.transform = look.scale;

      row.addEventListener('click', () => pickQuantity(value));

      box.insertBefore(row, $('wheel').querySelector('.wheel-bottom'));
    });
  }

  function pickQuantity(value) {
    ui.cards = value;
    ui.ownCards = false;
    $('cards').value = '';
    paintWheel();
  }

  /// Girarla con el dedo. Cada 34px de arrastre corre un salto: menos y se pasa de
  /// largo con el pulgar, mas y no se siente que responda.
  function spinWheel(box) {
    let from = null;
    let atStart = 0;

    const all = () => quantities();

    box.addEventListener('pointerdown', (e) => {
      from = e.clientY;
      atStart = all().indexOf(ui.cards);
      box.setPointerCapture(e.pointerId);
    });

    box.addEventListener('pointermove', (e) => {
      if (from === null) return;

      const list = all();
      const moved = Math.round((from - e.clientY) / 34);
      const at = Math.max(0, Math.min(list.length - 1, atStart + moved));

      if (list[at] === ui.cards) return;

      ui.cards = list[at];
      ui.ownCards = false;
      $('cards').value = '';
      paintWheel();
    });

    const soltar = (e) => {
      from = null;
      try { box.releasePointerCapture(e.pointerId); } catch (ignored) { /* ya lo solto */ }
    };

    box.addEventListener('pointerup', soltar);
    box.addEventListener('pointercancel', soltar);
  }

  function festivalName() {
    const name = $('name').value.trim();

    // Sin nombre, el del mes: un bingo tiene que poder crearse sin pensar como se llama.
    return name.length > 0 ? name : Idioma.con('crear.bingo-de-mes', { mes: Idioma.mes(ui.plays.getMonth()) });
  }

  // ── 3 · cuantas rondas ───────────────────────────────────────────────────

  function paintRoundCount() {
    $('roundCount').textContent = ui.roundCount;
    $('roundUnit').textContent = Idioma.texto(ui.roundCount === 1 ? 'crear.rondas.una' : 'crear.rondas.varias');
    $('lessRounds').disabled = ui.roundCount <= 1;

    // El boton del pie dice cuantas se van a configurar, asi que cambia con el numero.
    $('next').textContent = nextRoundsSays();

    document.querySelectorAll('.quick-round').forEach((chip) => {
      chip.classList.toggle('on', Number(chip.dataset.rounds) === ui.roundCount);
    });
  }

  /// Cambiar la cantidad no tira lo ya armado: se agregan rondas vacias al final, o se
  /// quitan desde el final. Rehacer una ronda por tocar un + es lo que hace que la gente
  /// no se atreva a corregir.
  function setRoundCount(howMany) {
    ui.roundCount = Math.max(1, Math.min(20, howMany));

    while (ui.rounds.length < ui.roundCount) ui.rounds.push({ games: [] });
    while (ui.rounds.length > ui.roundCount) ui.rounds.pop();

    if (ui.at >= ui.rounds.length) ui.at = ui.rounds.length - 1;

    paintRoundCount();
  }

  // ── 4 · los premios, una pregunta a la vez ───────────────────────────────

  function here() { return ui.rounds[ui.at]; }

  function firstUnfinished() {
    const at = ui.rounds.findIndex((round) => round.games.length === 0);
    return at < 0 ? 0 : at;
  }

  function openRound(at) {
    ui.at = at;
    goStage(here().games.length > 0 ? 'more' : 'pattern');
  }

  function goStage(stage) {
    ui.stage = stage;

    show($('stagePattern'), stage === 'pattern');
    show($('stagePrize'), stage === 'prize');
    show($('stageMore'), stage === 'more');

    if (stage === 'pattern') {
      ui.figure = null;
      $('patternWho').textContent = whichGame();
      paintFigures();
    }

    if (stage === 'prize') {
      $('prizeWho').textContent = whichGame() + '  ·  ' + window.Figuras.name(ui.figure);
      $('prize').value = '';
      pickForm(true);
    }

    if (stage === 'more') paintMore();

    paintChips();
    $('body').scrollTop = 0;
  }

  function whichGame() {
    return Idioma.con('crear.ronda-de', { n: ui.at + 1, total: ui.roundCount })
      + '  ·  ' + Idioma.con('crear.juego-n', { n: here().games.length + 1 });
  }

  /// El boton del pie en el paso de las rondas: cuenta cuantas se van a configurar.
  function nextRoundsSays() {
    return ui.roundCount === 1
      ? Idioma.texto('crear.configurar-mi-ronda')
      : Idioma.con('crear.configurar-mis-rondas', { n: ui.roundCount });
  }

  /// Lo que la ronda lleva hasta ahora, y la pregunta de si va otro. Cada juego trae su
  /// Quitar en su renglon: equivocarse en un premio no obliga a rehacer la ronda.
  function paintMore() {
    const round = here();

    const box = $('moreList');
    box.textContent = '';

    round.games.forEach((game, k) => {
      const row = el('div', 'round-game');
      row.appendChild(el('span', 'round-game-tick', '✓'));

      const what = el('div', 'round-game-what');
      what.appendChild(el('div', 'round-game-figure', window.Figuras.name(game.figure)));
      what.appendChild(el('div', 'round-game-prize', prizeText(game)));
      row.appendChild(what);

      // El icono pegado a lo que describe, y la accion al extremo derecho: es donde
      // esta el Editar de las rondas ya cerradas, asi que la vista la busca ahi.
      const ico = document.createElement('img');
      ico.className = 'round-game-ico';
      ico.alt = '';
      ico.src = game.cash ? 'assets/prize-cash.png' : 'assets/prize-gift.png';
      row.appendChild(ico);

      const drop = el('button', 'game-out', Idioma.texto('general.quitar'));
      drop.addEventListener('click', () => {
        round.games.splice(k, 1);
        goStage(round.games.length > 0 ? 'more' : 'pattern');
      });
      row.appendChild(drop);

      box.appendChild(row);
    });
  }

  /// Donde va uno: el rotulo, los puntitos, y las rondas ya cerradas como filas con su
  /// Editar. Los puntos no crecen con el numero de rondas, que era lo que hacia que la
  /// fila de pastillas se saliera de la pantalla y quedara debajo de "Cambiar cantidad".
  function paintChips() {
    $('roundWhere').textContent = Idioma.con('crear.ronda-de', { n: ui.at + 1, total: ui.roundCount });

    const dots = $('roundDots');
    dots.textContent = '';

    ui.rounds.forEach((round, i) => {
      dots.appendChild(el('div', 'round-dot'
        + (i === ui.at ? ' now' : round.games.length > 0 ? ' done' : '')));
    });

    const box = $('roundsDone');
    box.textContent = '';

    ui.rounds.forEach((round, i) => {
      if (i >= ui.at || round.games.length === 0) return;

      const row = el('div', 'round-done');
      row.appendChild(el('span', 'round-done-tick', '✓'));
      row.appendChild(el('div', 'round-done-what', Idioma.con('inicio.ahora.ronda-n', { n: i + 1 }) + '  ·  '
        + Idioma.muchos(round.games.length, 'inicio.premios.uno', 'inicio.premios.varios')));

      const edit = el('button', 'link', Idioma.texto('general.editar'));
      edit.addEventListener('click', () => openRound(i));
      row.appendChild(edit);

      box.appendChild(row);
    });
  }

  function paintFigures() {
    const box = $('figures');
    box.textContent = '';

    // Las que ya se prometieron en ESTA ronda no se vuelven a ofrecer: dos veces la
    // misma figura en la misma tombola es un premio que nadie puede ganar dos veces.
    const taken = here().games.map((g) => g.figure.toLowerCase());

    ui.figures.forEach((name) => {
      if (taken.includes(name.toLowerCase())) return;

      const one = el('button', 'pick-one');

      one.appendChild(window.Figuras.grid(name));

      const what = el('div');
      what.appendChild(el('span', 'pick-name', window.Figuras.name(name)));
      what.appendChild(el('span', 'pick-says', window.Figuras.says(name)));
      one.appendChild(what);

      one.addEventListener('click', () => {
        ui.figure = name;
        goStage('prize');
      });

      box.appendChild(one);
    });
  }

  function pickForm(cash) {
    ui.cash = cash;

    $('cash').classList.toggle('on', cash);
    $('goods').classList.toggle('on', !cash);

    $('prizeLabel').textContent = Idioma.texto(cash ? 'crear.premio.cuanto' : 'crear.premio.que-se-lleva');

    const prize = $('prize');
    prize.type = cash ? 'number' : 'text';
    prize.inputMode = cash ? 'numeric' : 'text';
    prize.placeholder = cash ? '0' : Idioma.texto('crear.premio.ejemplo');
  }

  function saveGame() {
    const prize = $('prize').value.trim();

    if (!prize) {
      say(Idioma.texto(ui.cash ? 'crear.log.cuanto-da' : 'crear.log.que-se-lleva'));
      return;
    }

    if (ui.cash && !(parseInt(prize, 10) > 0)) {
      say(Idioma.texto('crear.log.efectivo-en-numeros'));
      return;
    }

    here().games.push({ figure: ui.figure, cash: ui.cash, prize: prize });
    say(Idioma.con('crear.log.ronda-promete', { n: ui.at + 1, figura: window.Figuras.name(ui.figure),
      premio: prizeText(here().games[here().games.length - 1]) }));

    goStage('more');
  }

  /// Cerrar la ronda lleva a la que sigue, y si era la ultima, al resumen. Si quedo
  /// alguna sin premios mas atras, esa es la que sigue: el resumen no puede recibir
  /// una ronda vacia.
  function closeRound() {
    say(Idioma.con('organizador.log.ronda-lista', { n: ui.at + 1 }));

    const pending = ui.rounds.findIndex((round) => round.games.length === 0);

    if (pending >= 0) { openRound(pending); return; }

    goStep(4);
  }

  // ── 5 · listo ────────────────────────────────────────────────────────────

  function paintReview() {
    $('revName').textContent = festivalName();
    $('revWhen').textContent = daySaid(ui.plays)
      + (ui.hour >= 0 ? '  ·  ' + hourSaid() : '');

    const prizes = ui.rounds.reduce((sum, r) => sum + r.games.length, 0);

    $('revStats').textContent = Idioma.muchos(howManyCards(), 'jugador.cartones.uno', 'jugador.cartones.varios')
      + (cardPrice() > 0 ? ' ' + Idioma.con('crear.a-precio', { precio: cardPrice() }) : '') + '  ·  '
      + Idioma.muchos(ui.rounds.length, 'crear.rondas-n.una', 'crear.rondas-n.varias') + '  ·  '
      + Idioma.muchos(prizes, 'inicio.premios.uno', 'inicio.premios.varios');

    const list = $('revList');
    list.textContent = '';

    ui.rounds.forEach((round, i) => {
      const card = el('div', 'round');

      const head = el('div', 'round-head');
      const what = el('div');
      what.appendChild(el('div', 'round-name', Idioma.con('inicio.ahora.ronda-n', { n: i + 1 })));
      what.appendChild(el('div', 'round-sub', Idioma.muchos(round.games.length, 'crear.juegos.uno', 'crear.juegos.varios')));
      head.appendChild(what);

      // Editar desde el resumen: es el ultimo lugar donde uno se da cuenta, y sin esto
      // habria que salir y rehacer.
      const edit = el('button', 'link', Idioma.texto('general.editar'));
      edit.addEventListener('click', () => { goStep(3); openRound(i); });
      head.appendChild(edit);

      card.appendChild(head);

      const body = el('div', 'round-body');

      round.games.forEach((game, k) => {
        if (k > 0) {
          const link = el('div', 'linkline');
          link.appendChild(el('div', null, '↓'));
          link.appendChild(el('div', null, Idioma.texto('crear.mismos-numeros')));
          body.appendChild(link);
        }

        const line = el('div', 'game');
        line.appendChild(el('div', 'game-no', String(k + 1)));

        const text = el('div', 'game-what');
        text.appendChild(el('div', 'game-figure', window.Figuras.name(game.figure)));
        text.appendChild(el('div', 'game-prize', prizeText(game)));
        line.appendChild(text);

        body.appendChild(line);
      });

      card.appendChild(body);
      list.appendChild(card);
    });
  }

  function create() {
    if (ui.creating) return;

    ui.creating = true;
    $('next').disabled = true;
    show($('busy'), true);

    send('create|' + JSON.stringify({
      name: festivalName(),
      date: asWired(ui.plays),
      time: ui.hour < 0
        ? ''
        : String(ui.hour).padStart(2, '0') + ':' + String(ui.minute).padStart(2, '0'),
      cards: howManyCards(),
      price: cardPrice(),
      rounds: ui.rounds.map((r) => ({ games: r.games })),
    }));
  }

  // ── lo que el telefono le cuenta a la pagina ─────────────────────────────

  function onMessage(raw) {
    let msg;
    try { msg = JSON.parse(raw); } catch { return; }

    // El catalogo del idioma de la persona. Llega primero que todo lo demas, y otra vez
    // cada vez que cambia con el selector.
    if (msg.kind === 'textos') { window.Idioma.aprender(msg); paintWhen(); goStep(ui.step); return; }

    if (msg.kind === 'figures') {
      ui.figures = msg.figures || [];
      window.Figuras.learn(msg.names, msg.hints);

      if (ui.step === 3 && ui.stage === 'pattern') paintFigures();
    } else if (msg.kind === 'log') {
      const box = $('logBox');
      box.textContent = msg.text;
      box.scrollTop = box.scrollHeight;
    } else if (msg.kind === 'made') {
      // El telefono ya lo creo: la pantalla de exito reemplaza al wizard entero, y las
      // dos salidas las decide el organizador.
      show($('busy'), false);
      $('doneName').textContent = festivalName();
      show($('done'), true);
    } else if (msg.kind === 'failed') {
      ui.creating = false;
      $('next').disabled = false;
      show($('busy'), false);
    }
  }

  window.addEventListener('HybridWebViewMessageReceived', (e) => onMessage(e.detail.message));

  // ── los gestos ───────────────────────────────────────────────────────────

  $('next').addEventListener('click', onNext);
  $('back').addEventListener('click', onBack);

  $('dateCard').addEventListener('click', openDate);
  $('calBack').addEventListener('click', () => slideMonth(-1));
  $('calNext').addEventListener('click', () => slideMonth(1));
  $('dateGo').addEventListener('click', saveDate);
  $('dateSheet').addEventListener('click', (e) => {
    if (e.target === $('dateSheet')) show($('dateSheet'), false);
  });

  $('timeCard').addEventListener('click', openTime);
  document.querySelectorAll('[data-bump]').forEach((key) => {
    key.addEventListener('click', () => bumpClock(key.dataset.bump, Number(key.dataset.by)));
  });
  $('isAm').addEventListener('click', () => setHalf(false));
  $('isPm').addEventListener('click', () => setHalf(true));
  $('timeGo').addEventListener('click', () => { paintWhen(); show($('timeSheet'), false); });
  $('timeNone').addEventListener('click', () => {
    ui.hour = -1;
    paintWhen();
    show($('timeSheet'), false);
  });
  $('timeSheet').addEventListener('click', (e) => {
    if (e.target === $('timeSheet')) { paintWhen(); show($('timeSheet'), false); }
  });

  spinWheel($('wheel'));

  $('cards').addEventListener('input', () => {
    const said = parseInt($('cards').value, 10);

    ui.ownCards = $('cards').value.trim().length > 0;

    if (Number.isFinite(said) && said > 0) ui.cards = said;

    paintWheel();
  });

  $('lessRounds').addEventListener('click', () => setRoundCount(ui.roundCount - 1));
  $('moreRounds').addEventListener('click', () => setRoundCount(ui.roundCount + 1));
  document.querySelectorAll('.quick-round').forEach((chip) => {
    chip.addEventListener('click', () => setRoundCount(Number(chip.dataset.rounds)));
  });

  $('changeCount').addEventListener('click', () => goStep(2));

  $('cash').addEventListener('click', () => pickForm(true));
  $('goods').addEventListener('click', () => pickForm(false));

  $('saveGame').addEventListener('click', saveGame);

  $('prize').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    e.preventDefault();
    saveGame();
  });

  $('another').addEventListener('click', () => goStage('pattern'));
  $('roundDone').addEventListener('click', closeRound);

  $('doneGo').addEventListener('click', () => send('open'));
  $('doneHome').addEventListener('click', () => send('leave'));

  $('logFold').addEventListener('click', () => {
    const box = $('logBox');
    const open = box.classList.contains('hidden');

    show(box, open);
    $('logChevron').classList.toggle('open', open);

    if (open) send('wantlog');
  });

  document.querySelectorAll('input').forEach((field) => {
    field.addEventListener('focus', () => {
      setTimeout(() => field.scrollIntoView({ block: 'center', behavior: 'smooth' }), 260);
    });
  });

  window.__test = (msg) => onMessage(JSON.stringify(msg));

  paintWheel();
  paintWhen();
  goStep(0);
  send('ready');
})();
