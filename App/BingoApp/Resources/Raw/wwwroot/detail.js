(function () {
  'use strict';

  const VIEWS = ['hub', 'rounds', 'cards', 'team', 'players', 'person', 'handout'];

  const ui = {
    state: null,
    view: 'hub',
    tab: 'cards',   // la pestana del carrusel; cartones primero, que es el pulso de la venta
    adding: null,   // el oficio de la persona que se esta anotando, null si ninguna
    person: null,   // la marca de la persona abierta, null si ninguna
    many: null,     // 'give', 'back' o 'move' mientras se pregunta cuantos
    manyTo: null,   // a quien se le mueven, cuando se mueven
    announcing: null, // el modo marcado en la hoja de como se anuncia
    nextAct: null,    // que hace el boton de la accion recomendada, segun lo que falte
    hour: -1,         // la hora marcada en el reloj, sin confirmar
    minute: 0,
    doing: null,      // 'give' o 'back' mientras se reparte desde el hub
    toWhom: null,     // la marca de a quien, sin confirmar
    howMany: 0,       // cuantos cartones, sin confirmar
    cash: true,
    newGames: [],
    figures: [],
  };

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

  function prizeText(game) { return game.cash ? '$' + game.prize : game.prize; }

  /// Quien lo TOMO manda sobre para quien se corto. Un papelito cortado para Sonia lo
  /// puede terminar usando Marta, y lo que el organizador necesita ver es quien tiene los
  /// cartones en la mano, no para quien eran.
  function whoHasIt(one) {
    return one.takenBy || one.meantFor || '';
  }

  function initial(name) {
    return (name || '?').trim().charAt(0).toUpperCase() || '?';
  }

  // ── las vistas ───────────────────────────────────────────────────────────

  function goView(which) {
    ui.view = which;

    VIEWS.forEach((v) => show($(v), v === which));

    // El pie con "empezar a cantar" solo vive en el hub: adentro de una vista lo
    // que uno quiere es volver, no arrancar la noche por accidente.
    show($('foot'), which === 'hub' && !!ui.state && !ui.state.over);

    // El carrusel vive FUERA del body -- si viviera adentro, el overflow del scroll
    // le recortaria el circulo que muerde la corona. Por eso se esconde a mano al
    // entrar a una vista: ahi manda la vista, no las pestanas.
    show($('ringRow'), which === 'hub');

    $('body').scrollTop = 0;
  }

  function goBack() {
    // Desde una persona se vuelve a la LISTA y no al hub: se entro desde ahi, y mandar
    // dos pasos atras de un toque obliga a rehacer el camino para mirar a la siguiente.
    if (ui.view === 'person') { ui.person = null; goView('team'); return; }

    // De repartir se sale al hub, que es de donde se entro.
    if (ui.view === 'handout') { ui.doing = null; goView('hub'); return; }

    if (ui.view !== 'hub') goView('hub');
    else send('leave');
  }

  // ── lo que se pinta ──────────────────────────────────────────────────────

  function render() {
    const s = ui.state;
    if (!s) return;

    $('title').textContent = s.name;
    $('where').textContent = s.where;
    paintWhen();

    const chip = $('chip');
    chip.className = 'chip chip-big '
      + (s.over ? 'chip-over' : s.draws > 0 ? 'chip-live' : 'chip-ready');
    // Antes de la primera bolita lo que importa es cuanto falta, no en que ronda va:
    // la ronda 1 existe desde que se creo el bingo y decirla es prometer que ya empezo.
    chip.textContent = s.over
      ? Idioma.texto('detalle.termino')
      : s.draws > 0
        ? (s.currentRound > 0 ? Idioma.con('crear.ronda-de', { n: s.currentRound, total: s.rounds })
                              : Idioma.texto('inicio.ahora.entre-rondas'))
        : howLongLeft(s);

    // Los dos primeros numeros de la tarjeta. El tercero lo pone paintSettings, que es
    // quien sabe si hay precio y por lo tanto si la cuenta es plata o bolitas.
    $('statSold').textContent = s.sold;
    $('statIn').textContent = s.players;

    paintDoors();

    $('boxAll').textContent = s.forSale;
    $('boxLeft').textContent = s.left;

    paintSettings();
    paintNextStep();
    paintRounds();
    paintTeam();
    paintPlayers();

    // Al final y solo si esta abierta: pisa el titulo de la cabecera con el nombre de la
    // persona, asi que tiene que correr DESPUES de que render lo puso.
    if (ui.view === 'person') paintPerson();
    if (ui.view === 'handout') paintHandout();

    show($('foot'), ui.view === 'hub' && !s.over);
    // El ▶ es del render, no de la frase: solo acompana al arranque.
    $('call').querySelector('.play').style.display = s.draws > 0 ? 'none' : '';
    $('callSays').textContent = Idioma.texto(s.draws > 0 ? 'detalle.seguir-cantando' : 'detalle.empezar-a-cantar');
  }

  /// El carrusel del hub: cuatro circulos y UNA tarjeta a la vez. Cada tarjeta trae
  /// lo esencial de su tema -- el reparto de cartones, quien canta, que se juega -- y
  /// un "Ver..." que lleva adentro. La pestana elegida vive en ui.tab.
  function paintDoors() {
    const s = ui.state;

    // El circulo activo se levanta; abajo, SOLO su tarjeta.
    document.querySelectorAll('.ring').forEach((b) => {
      b.classList.toggle('ring-on', b.dataset.pane === ui.tab);
    });
    show($('paneRounds'), ui.tab === 'rounds');
    show($('paneCards'), ui.tab === 'cards');
    show($('paneTeam'), ui.tab === 'team');
    show($('panePlayers'), ui.tab === 'players');

    // ── rondas y premios: una tarjetita por figura, como la maqueta ──
    $('doorRounds').textContent = Idioma.muchos(s.rounds, 'crear.rondas-n.una', 'crear.rondas-n.varias')
      + '  ·  ' + Idioma.muchos(s.prizes, 'inicio.premios.uno', 'inicio.premios.varios');

    const flat = [];
    s.roundList.forEach((round, i) => round.games.forEach((g) => flat.push({ n: i + 1, g: g })));

    const box = $('roundCards');
    box.textContent = '';

    flat.slice(0, 3).forEach((one) => {
      const card = el('div', 'round-mini');
      // El dibujito va sobre su azulejo morado suave: pelado contra el blanco se
      // perdia, y el azulejo es lo que hace leerse "esto es una ronda" de un vistazo.
      const tile = el('div', 'round-tile');
      tile.appendChild(window.Figuras.grid(one.g.figure, 'mini-grid'));
      card.appendChild(tile);
      const what = el('div', 'round-mini-what');
      what.appendChild(el('div', 'round-mini-tag', Idioma.con('inicio.ahora.ronda-n', { n: one.n })));
      what.appendChild(el('div', 'round-mini-name', window.Figuras.name(one.g.figure)));
      what.appendChild(el('div', 'round-mini-prize', Idioma.con('detalle.rondas.premio', { premio: prizeText(one.g) })));
      card.appendChild(what);
      box.appendChild(card);
    });
    if (flat.length > 3) box.appendChild(el('div', 'more-figs', Idioma.con('detalle.rondas.mas-n', { n: flat.length - 3 })));
    if (flat.length === 0) box.appendChild(el('div', 'pane-note', Idioma.texto('detalle.rondas.ninguna')));

    // ── cartones: vendido, asignado y libre NO se pisan ──
    // Asignado = en manos de colaboradores y todavia sin vender. La cuenta de antes
    // (total - libres) metia lo vendido adentro y la barra mentia por gorda.
    const given = Math.max(0, s.forSale - s.left - s.sold);

    $('doorCardNum').textContent = s.forSale;
    $('legGiven').textContent = given;
    $('legFree').textContent = s.left;

    const pct = (n) => (s.forSale > 0 ? (n / s.forSale) * 100 : 0);
    $('barSold').style.width = pct(s.sold) + '%';
    $('barGiven').style.width = pct(given) + '%';

    // Corto, porque ahora es subtitulo de cabecera. El "que hacer" del precio que
    // falta lo dice la Proxima accion, que para eso esta.
    $('paneCardPrice').textContent = (s.price || 0) > 0
      ? Idioma.con('detalle.cartones.por-carton', { precio: money(s.price) })
      : Idioma.texto('detalle.cartones.sin-precio-todavia');

    // ── colaboradores, con quien canta al frente ──
    const inside = s.team.filter((t) => t.standing === 'Taken');

    $('doorTeam').textContent = s.team.length === 0
      ? Idioma.texto('detalle.equipo.ninguno-dado')
      : Idioma.con('detalle.equipo.adentro-y-codigos', { adentro: inside.length,
        codigos: Idioma.muchos(s.team.length, 'detalle.equipo.codigos.uno', 'detalle.equipo.codigos.varios') });

    // La lista COMPLETA, aca mismo. Las caritas se fueron: con doce personas eran cinco
    // iniciales y un '+7' que obligaban a entrar a otra pantalla para saber quien es
    // quien. Cada fila es la misma tarjeta de la vista completa, asi que tocarla sigue
    // llevando a lo suyo.
    const teamBox = $('doorTeamList');
    teamBox.textContent = '';

    const alive = s.team.slice().sort(byWhoFirst).filter((t) => t.standing !== 'SentHome');

    if (alive.length === 0) {
      teamBox.appendChild(el('div', 'none', Idioma.texto('detalle.equipo.anota')));
    } else {
      alive.forEach((one) => teamBox.appendChild(cardOfPerson(one)));
    }

    // La voz de la noche. El que canta puede no haber entrado aun: se dice eso y no
    // un nombre vacio.
    const singer = s.team.find((t) => t.duty === 'canta' && t.standing !== 'SentHome');
    $('whoSings').textContent = !singer
      ? Idioma.texto('detalle.equipo.nadie-canta')
      : singer.standing === 'Taken'
        ? singer.takenBy
        : Idioma.texto('detalle.equipo.codigo-dado-falta-entrar');

    // ── quienes juegan ──
    // "jugadores" ya cuenta tambien a los que compraron sin entrar todavia: la venta
    // los mete, como la pantalla siempre prometio.
    $('doorPlayers').textContent = Idioma.texto(s.players === 0 ? 'detalle.jugadores.ninguno-todavia' : 'detalle.jugadores.con-su-carton');

    faces($('doorPlayerFaces'), s.playerList.map((p) => p.name), 0);
  }

  /// Cuanto falta para la noche. Sin fecha guardada -- los bingos de antes -- se dice
  /// que esta listo, que es lo unico cierto.
  function howLongLeft(s) {
    const said = (s.playsOn || '').split('-');

    if (said.length !== 3) return Idioma.texto(s.rounds > 0 ? 'detalle.listo-para-empezar' : 'detalle.sin-arrancar');

    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const day = new Date(Number(said[0]), Number(said[1]) - 1, Number(said[2]));
    const gap = Math.round((day - today) / 86400000);

    if (gap <= 0) return Idioma.texto('detalle.se-juega-hoy');
    if (gap === 1) return Idioma.texto('detalle.falta-un-dia');

    return Idioma.con('detalle.faltan-dias', { n: gap });
  }

  function faces(box, names, waiting) {
    box.textContent = '';

    names.slice(0, 5).forEach((name) => box.appendChild(el('div', 'face', initial(name))));

    for (let i = 0; i < Math.min(waiting, 3); i++) {
      box.appendChild(el('div', 'face waiting', '·'));
    }

    if (names.length > 5) {
      box.appendChild(el('div', 'face waiting', '+' + (names.length - 5)));
    }

    show(box, names.length > 0 || waiting > 0);
  }

  /// Cuando se juega, en la corona. Los bingos de antes de que se preguntara la fecha
  /// no la traen, y entonces la pastilla no se dibuja: mejor sin ella que con un dia
  /// inventado.
  function paintWhen() {
    const s = ui.state;
    const said = (s.playsOn || '').split('-');

    if (said.length !== 3) { show(document.querySelector('.crown-when'), false); return; }

    show(document.querySelector('.crown-when'), true);

    const day = new Date(Number(said[0]), Number(said[1]) - 1, Number(said[2]));
    $('crownDay').textContent = Idioma.etiqueta === 'en'
      ? Idioma.mesCorto(day.getMonth()) + ' ' + day.getDate() + ', ' + day.getFullYear()
      : day.getDate() + ' ' + Idioma.mesCorto(day.getMonth()) + '. ' + day.getFullYear();

    const hour = (s.doorsAt || '').split(':');
    const told = hour.length === 2;

    show($('crownHourWrap'), told);
    show($('crownNoHour'), !told);

    if (told) {
      const h = Number(hour[0]);
      $('crownHour').textContent = Idioma.hora(h, Number(hour[1]));
    }
  }

  // -- a que hora abre --------------------------------------------------------

  /// El reloj arranca en la hora que ya tiene, y sin ninguna, en una hora de bingo:
  /// nadie abre las puertas a las 3:17.
  function openHour() {
    const said = (ui.state.doorsAt || '').split(':');

    if (said.length === 2) {
      ui.hour = Number(said[0]);
      ui.minute = Number(said[1]);
    } else {
      ui.hour = 19;
      ui.minute = 0;
    }

    paintClock();
    show($('hourSheet'), true);
  }

  function paintClock() {
    $('clockHour').textContent = ui.hour % 12 === 0 ? 12 : ui.hour % 12;
    $('clockMinute').textContent = String(ui.minute).padStart(2, '0');

    $('isAm').classList.toggle('on', ui.hour < 12);
    $('isPm').classList.toggle('on', ui.hour >= 12);
  }

  /// Se mueve la hora del reloj de doce y se le devuelve la mitad del dia que tenia:
  /// subir de 11 a 12 no puede saltar de la manana a la tarde sola.
  function bumpClock(what, by) {
    if (what === 'hour') {
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
    ui.hour = pm ? (ui.hour % 12) + 12 : ui.hour % 12;
    paintClock();
  }

  function saveHour() {
    send('doorsat|' + String(ui.hour).padStart(2, '0') + ':'
      + String(ui.minute).padStart(2, '0'));

    show($('hourSheet'), false);
  }

  /// Las dos decisiones, cada una donde se usa. El modo NO dice "sin configurar": el
  /// motor cae en OnClaim si nadie eligio, asi que la respuesta ya existe y la pantalla
  /// la asume. El precio si puede faltar de verdad -- no hay precio por omision.
  function paintSettings() {
    const s = ui.state;

    const priced = (s.price || 0) > 0;

    // El tercer numero es la plata cuando el carton tiene precio, y las bolitas cuando
    // no: sin precio, un "0 en ventas" seria mentira.
    $('priceNow').textContent = priced ? Idioma.con('detalle.cartones.por-carton', { precio: money(s.price) }) : Idioma.texto('detalle.cartones.sin-precio');
    $('priceNow').classList.toggle('slot-set', priced);
    $('priceEdit').textContent = Idioma.texto(priced ? 'detalle.cartones.cambiar' : 'detalle.cartones.ponerle') + ' →';

    $('announceNow').textContent = Idioma.texto(s.announce === 'Automatic' ? 'detalle.anuncio.automatico' : 'detalle.anuncio.el-jugador-avisa');
  }

  // -- lo que sigue ----------------------------------------------------------

  /// Que le conviene hacer AHORA al organizador. El orden no es caprichoso: primero lo
  /// que traba la venta --sin rondas no hay que jugar, sin colaboradores no hay quien
  /// venda, sin cartones en mano no se vende, sin precio nadie sabe que cobrar--, y de
  /// ultimo lo que solo mejora la noche.
  ///
  /// El modo de anuncio va al final y dicho como invitacion, no como falta: le dimos un
  /// valor por omision a proposito, asi que no le debe nada a nadie. Esta ahi porque los
  /// dos modos cambian mucho la noche y alguien podria no saber que el otro existe.
  function whatIsNext(s) {
    if (s.rounds === 0) {
      return {
        says: Idioma.texto('detalle.proximo.rondas'),
        go: Idioma.texto('detalle.proximo.rondas.ir'),
        act: () => goView('rounds'),
      };
    }

    if (s.team.length === 0) {
      return {
        says: Idioma.texto('detalle.proximo.equipo'),
        go: Idioma.texto('detalle.proximo.equipo.ir'),
        act: () => goView('team'),
      };
    }

    // Repartido es lo que salio del libro. Con cero, nadie tiene con que vender.
    if (s.forSale - s.left === 0) {
      return {
        says: Idioma.texto('detalle.proximo.repartir'),
        go: Idioma.texto('detalle.proximo.repartir.ir'),
        act: () => startHandout('give'),
      };
    }

    if ((s.price || 0) === 0) {
      return {
        says: Idioma.texto('detalle.proximo.precio'),
        go: Idioma.texto('detalle.proximo.precio.ir'),
        act: () => { goView('cards'); openPrice(); },
      };
    }

    if (!(s.doorsAt || '').includes(':')) {
      return {
        says: Idioma.texto('detalle.proximo.hora'),
        go: Idioma.texto('detalle.proximo.hora.ir'),
        act: openHour,
      };
    }

    if (s.announce !== 'Automatic' && s.announce !== 'OnClaim') {
      return {
        says: Idioma.texto('detalle.proximo.anuncio'),
        go: Idioma.texto('detalle.proximo.anuncio.ir'),
        act: openAnnounce,
      };
    }

    return null;
  }

  /// La recomendacion ya no es un recuadro al fondo del scroll: es una ⓘ al lado del
  /// titulo de la pestana en la que uno esta, y el consejo sale al tocarla.
  ///
  /// El recuadro grande ocupaba el pie de TODAS las pestanas diciendo siempre lo mismo,
  /// y competia con el contenido que uno vino a mirar. Un consejo se pide, no se grita:
  /// la ⓘ solo aparece cuando de verdad hay algo que recomendar, y ahi queda esperando.
  ///
  /// Se esconde cuando no queda nada por preparar, cuando el bingo termino, y tambien
  /// cuando ya se esta cantando: a esa altura decirle que le falta algo es estorbar.
  function paintNextStep() {
    const s = ui.state;
    const next = s.over || s.draws > 0 ? null : whatIsNext(s);
    const box = $('nextStep');

    document.querySelectorAll('.pane-tip').forEach((tip) => {
      show(tip, !!next);
      tip.classList.toggle('on', !!next && ui.tipOpen);
    });

    if (!next) { show(box, false); ui.tipOpen = false; return; }

    $('nextStepSays').textContent = next.says;
    $('nextStepGo').textContent = next.go;
    ui.nextAct = next.act;

    // El consejo vive DENTRO de la pestana abierta: asi sale pegado al titulo que uno
    // acaba de tocar y no al fondo de la pagina. Solo hay uno, y se muda.
    const pane = document.querySelector('#hub .pane:not(.hidden)');

    if (pane && box.parentElement !== pane) pane.appendChild(box);

    show(box, !!ui.tipOpen);
  }

  function toggleTip() {
    ui.tipOpen = !ui.tipOpen;
    paintNextStep();
  }

  /// La plata se escribe como la dice la gente: con su signo y de a miles.
  function money(n) {
    return '₡' + String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  }

  function paintRounds() {
    const box = $('roundList');
    box.textContent = '';

    const s = ui.state;

    if (s.roundList.length === 0) {
      box.appendChild(el('div', 'none', Idioma.texto('detalle.rondas.de-antes')));
      return;
    }

    s.roundList.forEach((round) => {
      const card = el('div', 'round');

      const head = el('div', 'round-head');
      const what = el('div');
      what.appendChild(el('div', 'round-name', Idioma.con('inicio.ahora.ronda-n', { n: round.position })));
      what.appendChild(el('div', 'round-sub', roundSays(round)));
      head.appendChild(what);
      card.appendChild(head);

      const body = el('div', 'round-body');

      round.games.forEach((game, k) => {
        if (k > 0) {
          const link = el('div', 'linkline');
          link.appendChild(el('div', null, '↓'));
          link.appendChild(el('div', null, Idioma.texto('detalle.rondas.mismos-numeros')));
          body.appendChild(link);
        }

        const line = el('div', 'game');
        line.appendChild(el('div', 'game-no', String(k + 1)));

        const text = el('div', 'game-what');
        text.appendChild(el('div', 'game-figure', window.Figuras.name(game.figure)));
        text.appendChild(el('div', 'game-prize', prizeText(game)
          + (game.winners.length > 0
            ? '  ·  ' + Idioma.con('detalle.rondas.para', { quienes: game.winners.join(', ') })
            : '')));
        line.appendChild(text);

        if (game.winners.length > 0) {
          const done = el('div', 'who-act', Idioma.texto('detalle.rondas.entregado'));
          line.appendChild(done);
        }

        body.appendChild(line);
      });

      card.appendChild(body);
      box.appendChild(card);
    });
  }

  function roundSays(round) {
    if (round.standing === 'Playing') return Idioma.texto('detalle.rondas.suena-ahora');
    if (round.standing === 'Played') return Idioma.texto('detalle.rondas.jugada');
    if (round.standing === 'CalledOff') return Idioma.texto('detalle.rondas.cancelada');
    return Idioma.texto('detalle.rondas.en-la-fila');
  }

  /// Una lista de numeros dicha como la dice la gente: por tramos. El libro se reparte
  /// desde el principio, asi que a cada persona le toca un tramo seguido y decirle
  /// "del 31 al 38" es mas corto y mas util que los ocho numeros. Pero no siempre es uno
  /// solo -- un carton devuelto o anulado vuelve al libro y parte el tramo --, asi que se
  /// dicen los tramos que haya: "31-38, 45".
  function inRanges(numbers) {
    const sorted = (numbers || []).slice().sort((a, b) => a - b);
    const runs = [];

    sorted.forEach((n) => {
      const last = runs[runs.length - 1];

      if (last && n === last.to + 1) last.to = n;
      else runs.push({ from: n, to: n });
    });

    return runs.map((r) => (r.from === r.to ? String(r.from) : r.from + '-' + r.to)).join(', ');
  }

  function paintTeam() {
    paintSpread();
    paintEverybody();
  }

  /// Como estan repartidos los cartones del bingo. Es lo primero que se ve porque es
  /// lo que le pone techo a todo lo demas: no se le puede apartar a nadie mas de lo que
  /// queda libre.
  function paintSpread() {
    const s = ui.state;

    // forSale ya ES el total que hay en el bingo, y left lo que de eso sigue en el
    // libro: repartido es la resta. Es la misma cuenta que hace la puerta del hub.
    const total = s.forSale;
    const given = total - s.left;

    $('spreadTotal').textContent = total;
    $('spreadGiven').textContent = given;
    $('spreadFree').textContent = s.left;
    $('spreadBar').style.width = total > 0 ? Math.round((given / total) * 100) + '%' : '0%';

    // El tramo y no solo la cuenta: el libro se reparte desde el principio, asi que lo
    // libre es casi siempre un tramo seguido y es como el organizador lo piensa.
    $('spreadRange').textContent = s.free.length > 0
      ? Idioma.con('detalle.repartir.libres', { tramos: inRanges(s.free) })
      : '';
  }

  /// TODOS en una lista. Quien canta y quien vende no son dos secciones: es la misma
  /// persona anotada para dos oficios distintos, y el oficio es una propiedad del
  /// papelito. Van primero los que cantan nada mas porque sin ellos la noche no
  /// arranca, no porque sean otra cosa.
  function paintEverybody() {
    const box = $('teamList');
    box.textContent = '';

    const s = ui.state;
    const everybody = s.team.slice().sort(byWhoFirst);
    const alive = everybody.filter((one) => one.standing !== 'SentHome');

    $('teamCount').textContent = alive.length === 0
      ? Idioma.texto('detalle.equipo.nadie-anotado')
      : Idioma.muchos(alive.length, 'detalle.equipo.colaboradores.uno', 'detalle.equipo.colaboradores.varios');

    if (everybody.length === 0) {
      box.appendChild(el('div', 'none', Idioma.texto('detalle.equipo.anota-larga')));
      return;
    }

    everybody.forEach((one) => box.appendChild(cardOfPerson(one)));
  }

  /// Los que cantan arriba, los que se fueron abajo, y el resto como vinieron.
  function byWhoFirst(a, b) {
    const rank = (one) => (one.standing === 'SentHome' ? 2 : one.duty === 'canta' ? 0 : 1);

    return rank(a) - rank(b);
  }

  /// La tarjeta de una persona en la lista. Se toca y se entra a lo suyo: las acciones no
  /// caben en un renglon, y apretadas ahi la mas peligrosa -- sacarla -- queda al lado de
  /// la mas comun.
  function cardOfPerson(one) {
    const card = el('button', 'person');

    const gone = one.standing === 'SentHome';
    const free = one.standing === 'Offered';

    const top = el('div', 'person-top');

    top.appendChild(el('div', 'who-face '
      + (gone ? 'who-gone' : one.duty === 'canta' ? 'who-call' : 'who-sell'),
      initial(whoHasIt(one)) || '·'));

    const what = el('div', 'who-what');

    // El nombre y su oficio en el mismo renglon: en una lista sola, el oficio es lo
    // unico que distingue a una persona de la otra, y leerlo en la linea de abajo
    // obliga a bajar la vista en cada una.
    const line = el('div', 'who-line');
    line.appendChild(el('div', 'who-name' + (gone ? ' gone' : ''),
      whoHasIt(one) || Idioma.texto('detalle.persona.codigo-sin-usar')));
    line.appendChild(el('span', 'chip ' + (one.duty === 'canta' ? 'chip-live' : 'chip-sell'),
      Idioma.texto(one.duty === 'canta' ? 'detalle.persona.canta' : 'detalle.persona.vende')));
    what.appendChild(line);

    what.appendChild(el('div', 'who-sub', whoSays(one, gone, free)));
    top.appendChild(what);

    top.appendChild(el('div', 'person-go', '\u203a'));
    card.appendChild(top);

    // La barra solo cuando de verdad tiene cartones: una barra en cero al lado de cada
    // nombre no dice nada y llena la lista de rayas grises.
    const hand = (one.hand || []).length;
    const sold = one.sold || 0;

    if (hand + sold > 0) {
      const bar = el('div', 'bar-wrap');
      const fill = el('div', 'bar-fill');
      fill.style.width = Math.round((sold / (hand + sold)) * 100) + '%';
      bar.appendChild(fill);
      card.appendChild(bar);

      const feet = el('div', 'person-feet');
      feet.appendChild(el('div', null, Idioma.muchos(sold, 'inicio.vendidos.uno', 'inicio.vendidos.varios')));
      feet.appendChild(el('div', null, Idioma.con('detalle.persona.sin-vender', { n: hand })));
      card.appendChild(feet);
    }

    card.addEventListener('click', () => goPerson(one.mark));

    return card;
  }

  /// Lo que pasa con esa persona. El OFICIO no se dice aca -- ya lo dice la pastilla
  /// de al lado del nombre --, asi que este renglon es para el estado: si le falta
  /// escanear, si esta conectada, y que tiene en la mano.
  function whoSays(one, gone, free) {
    const y = '  ·  ';

    if (gone) return Idioma.texto('detalle.persona.la-sacaste');

    // Sin usar todavia: lo que importa es que le falta escanear, y cuantos cartones la
    // esperan. Esos cartones NO estan en su mano -- se le entregan cuando entra --, asi
    // que se dicen apartados y no en mano.
    if (free) {
      const waiting = Idioma.texto('detalle.persona.falta-escanear');

      return one.cardsMeant > 0
        ? waiting + y + Idioma.con('detalle.persona.apartados', { n: one.cardsMeant })
        : waiting;
    }

    // Cortado para una y usado por otra: no es un error --pasarse el codigo entre
    // vecinas es lo normal-- pero el organizador le va a entregar cartones a quien tiene
    // el telefono, no a quien el pensaba, y tiene que notarlo.
    const otra = one.elseTook ? Idioma.con('detalle.persona.entro-con-codigo-de', { quien: one.meantFor }) + y : '';

    if (one.duty === 'canta') return otra + Idioma.texto(one.here ? 'detalle.persona.adentro' : 'detalle.persona.no-conectada');
    if (!one.here) return otra + Idioma.texto('detalle.persona.no-conectada');

    // Los vendidos NO se dicen aca: el pie de la tarjeta ya los lleva, con su barra.
    return otra + ((one.hand || []).length > 0
      ? Idioma.con('detalle.persona.tiene', { cuales: inRanges(one.hand) })
      : Idioma.texto('detalle.persona.sin-cartones-en-mano'));
  }

  // -- lo de una persona ----------------------------------------------------

  function goPerson(mark) {
    ui.person = mark;
    goView('person');
    paintPerson();
  }

  function personNow() {
    const s = ui.state;
    if (!s || !ui.person) return null;

    return s.team.find((one) => one.mark === ui.person) || null;
  }

  function paintPerson() {
    const one = personNow();

    // La sacaron, o el diario ya no la tiene: mejor volver a la lista que quedarse en una
    // pantalla que habla de alguien que no esta.
    if (!one) { goView('team'); return; }

    const hand = (one.hand || []).length;
    const sold = one.sold || 0;
    const gone = one.standing === 'SentHome';
    const free = one.standing === 'Offered';
    const sells = one.duty !== 'canta';

    // La cabecera habla de ELLA mientras esta abierta: render la deja diciendo el nombre
    // del bingo y en donde esta el organizador, que aca no viene al caso.
    $('title').textContent = whoHasIt(one) || Idioma.texto('detalle.persona.codigo-sin-usar');
    $('where').textContent = whoSays(one, gone, free);

    $('personFace').textContent = initial(whoHasIt(one)) || '·';
    $('personFace').className = 'who-face big-face '
      + (gone ? 'who-gone' : sells ? 'who-sell' : 'who-call');

    $('personCards').textContent = free ? one.cardsMeant : hand;
    document.querySelector('#person .spread-what').textContent = Idioma.texto(free
      ? 'detalle.persona.apartados-para-cuando-entre'
      : 'detalle.persona.en-la-mano');

    $('personSold').textContent = sold;
    $('personLeft').textContent = hand;
    $('personBar').style.width = hand + sold > 0
      ? Math.round((sold / (hand + sold)) * 100) + '%'
      : '0%';

    $('personRange').textContent = hand > 0 ? Idioma.con('detalle.persona.tiene', { cuales: inRanges(one.hand) }) : '';

    paintManageDoors(one, sells, hand);

    show($('personOut'), !gone);
    $('personOut').textContent = Idioma.texto(free ? 'detalle.persona.anular-codigo' : 'detalle.persona.sacar');
  }

  /// Las tres gestiones de UNA persona. Se llama distinto que las puertas del hub a
  /// proposito: son dos listas de renglones parecidos, pero la de arriba lleva a otra
  /// vista y esta hace algo, y con el mismo nombre una pisaba a la otra en silencio.
  ///
  /// Apagada NO es invisible: ver que "mover cartones" existe y esta
  /// apagado le dice al organizador que se puede, y la nota de abajo le dice por que hoy
  /// no. Escondidas, tendria que adivinar que el bingo existe.
  function paintManageDoors(one, sells, hand) {
    const box = $('personDoors');
    box.textContent = '';

    if (!sells) {
      $('personNote').textContent = Idioma.texto('detalle.persona.quien-canta-no-toca');
      return;
    }

    const s = ui.state;
    const others = s.team.filter((x) => x.duty !== 'canta' && x.here
      && x.standing === 'Taken' && x.mark !== one.mark);

    const doors = [
      { key: 'move', ico: '\u21c4', name: Idioma.texto('detalle.persona.mover.nombre'), sub: Idioma.texto('detalle.persona.mover.sub'),
        can: one.here && hand > 0 && others.length > 0 },
      { key: 'give', ico: '+', name: Idioma.texto('detalle.persona.dar.nombre'), sub: Idioma.texto('detalle.persona.dar.sub'),
        can: one.here && s.left > 0 },
      { key: 'back', ico: '\u21a9', name: Idioma.texto('detalle.persona.devolver.nombre'), sub: Idioma.texto('detalle.persona.devolver.sub'),
        can: one.here && hand > 0 },
    ];

    doors.forEach((door) => {
      const row = el('button', 'row-door' + (door.can ? '' : ' off'));
      row.disabled = !door.can;

      row.appendChild(el('div', 'row-door-ico', door.ico));

      const what = el('div', 'row-door-what');
      what.appendChild(el('div', 'row-door-name', door.name));
      what.appendChild(el('div', 'row-door-sub', door.sub));
      row.appendChild(what);

      row.appendChild(el('div', 'row-door-go', '\u203a'));
      row.addEventListener('click', () => openMany(door.key));

      box.appendChild(row);
    });

    $('personNote').textContent = whyNotSays(one, hand, others.length);
  }

  function whyNotSays(one, hand, others) {
    if (one.standing === 'Offered') return Idioma.texto('detalle.persona.porque.sin-escanear');
    if (one.standing === 'SentHome') return Idioma.texto('detalle.persona.porque.sacada');
    if (!one.here) return Idioma.texto('detalle.persona.porque.no-conectada');
    if (hand === 0) return Idioma.texto('detalle.persona.porque.sin-cartones');
    if (others === 0) return Idioma.texto('detalle.persona.porque.nadie-mas');

    return '';
  }

  // -- cuantos cartones -----------------------------------------------------

  function openMany(what) {
    const one = personNow();
    if (!one) return;

    ui.many = what;
    ui.manyTo = null;

    const hand = (one.hand || []).length;
    const name = whoHasIt(one);

    const said = {
      give: { title: Idioma.con('detalle.cuantos.dar.titulo', { nombre: name }), sub: Idioma.texto('detalle.cuantos.dar.sub'),
              go: Idioma.texto('detalle.cuantos.dar.ir'), top: ui.state.left },
      back: { title: Idioma.con('detalle.cuantos.recoger.titulo', { nombre: name }), sub: Idioma.texto('detalle.cuantos.recoger.sub'),
              go: Idioma.texto('detalle.cuantos.recoger.ir'), top: hand },
      move: { title: Idioma.con('detalle.cuantos.mover.titulo', { nombre: name }), sub: Idioma.texto('detalle.cuantos.mover.sub'),
              go: Idioma.texto('detalle.cuantos.mover.ir'), top: hand },
    }[what];

    $('manyTitle').textContent = said.title;
    $('manySub').textContent = said.sub;
    $('manyGo').textContent = said.go;
    $('manyHow').value = '';
    $('manyHow').max = said.top;
    $('manyHint').textContent = Idioma.con('detalle.cuantos.hasta', { n: said.top })
      + (what === 'give' ? '' : ' ' + Idioma.texto('detalle.cuantos.se-sacan-los-ultimos'));

    show($('manyWhoWrap'), what === 'move');
    if (what === 'move') paintManyWho(one);

    show($('manySheet'), true);
    setTimeout(() => $('manyHow').focus(), 120);
  }

  function paintManyWho(one) {
    const box = $('manyWho');
    box.textContent = '';

    ui.state.team
      .filter((x) => x.duty !== 'canta' && x.here && x.standing === 'Taken'
        && x.mark !== one.mark)
      .forEach((other) => {
        const pick = el('button', 'pick-one');
        pick.appendChild(el('div', 'pick-name', other.meantFor || other.takenBy));
        pick.appendChild(el('div', 'pick-says',
          Idioma.con('detalle.cuantos.en-la-mano', { n: (other.hand || []).length })));

        pick.addEventListener('click', () => {
          ui.manyTo = other.mark;
          [...box.children].forEach((c) => c.classList.remove('on'));
          pick.classList.add('on');
        });

        box.appendChild(pick);
      });
  }

  function closeMany() { ui.many = null; show($('manySheet'), false); }

  function saveMany() {
    const one = personNow();
    if (!one) return;

    const how = parseInt($('manyHow').value, 10) || 0;

    if (how <= 0) { toast(Idioma.texto('detalle.log.dime-cuantos')); return; }
    if (ui.many === 'move' && !ui.manyTo) { toast(Idioma.texto('detalle.log.dime-a-quien')); return; }

    if (ui.many === 'give') send('handmore|' + one.mark + '|' + how);
    if (ui.many === 'back') send('takeback|' + one.mark + '|' + how);
    if (ui.many === 'move') send('movecards|' + one.mark + '|' + ui.manyTo + '|' + how);

    closeMany();
  }

  // -- dar y devolver cartones, sin entrar a cada persona ---------------------

  /// MOVER no esta en esta hoja a proposito: pasar cartones de una vendedora a otra lo
  /// inicia quien los tiene. El organizador solo pone en circulacion lo suyo y lo
  /// recoge; reasignar entre dos por detras no es un gesto que el pueblo acepte.
  function openHandout() {
    show($('handoutSheet'), true);
  }

  function startHandout(doing) {
    ui.doing = doing;
    ui.toWhom = null;
    ui.moveTo = null;
    ui.howMany = 0;

    show($('handoutSheet'), false);
    goView('handout');
    paintHandout();
  }

  /// Quien puede recibir o devolver. Los que cantan no tocan cartones, y a quien no
  /// esta conectada el organizador no le puede entregar: el pueblo se niega, asi que
  /// mejor decirlo antes que dejar tocar y contestar que no.
  function whoCanHandle() {
    return ui.state.team.filter((one) =>
      one.duty !== 'canta' && one.standing === 'Taken');
  }

  function mostFor(one) {
    return ui.doing === 'give'
      ? ui.state.left
      : (one ? (one.hand || []).length : 0);
  }

  /// Al elegir de quien salen, el destino elegido se suelta: pudo quedar apuntando a
  /// la misma persona que ahora los da.
  function pickFrom(mark) {
    ui.toWhom = mark;
    ui.howMany = 0;
    if (ui.moveTo === mark) ui.moveTo = null;
    paintHandout();
  }

  function paintHandout() {
    const giving = ui.doing === 'give';
    const moving = ui.doing === 'move';

    $('handoutTitle').textContent = Idioma.texto(giving ? 'detalle.repartir.dar' : moving ? 'detalle.repartir.mover' : 'detalle.repartir.devolver');

    $('handoutHowTag').textContent = Idioma.texto(giving ? 'detalle.repartir.cantidad-a-asignar'
      : moving ? 'detalle.repartir.cuantos-mover'
      : 'detalle.repartir.cantidad-a-devolver');

    $('handoutWhoTag').textContent = Idioma.texto(moving ? 'detalle.repartir.desde-quien' : 'detalle.repartir.selecciona-colaborador');

    paintHandoutWho();

    const one = whoCanHandle().find((x) => x.mark === ui.toWhom);
    show($('handoutHow'), !!one);

    // El destino solo existe al mover, y solo despues de saber de quien salen.
    show($('handoutToWrap'), moving && !!one);
    if (moving && one) paintHandoutTo(one);

    const go = $('handoutGo');
    go.textContent = Idioma.texto(giving ? 'detalle.repartir.asignar' : moving ? 'detalle.repartir.mover' : 'detalle.repartir.devolver');

    go.classList.toggle('go-danger', ui.doing === 'back');
    go.disabled = !one || ui.howMany <= 0 || (moving && !ui.moveTo);

    if (!one) return;

    $('handoutMany').textContent = ui.howMany;
    $('handoutLess').disabled = ui.howMany <= 0;
    $('handoutMore').disabled = ui.howMany >= mostFor(one);

    paintHandoutQuick(one);
    paintHandoutSays(one);
  }

  /// A quien le pasan los cartones. No aparece quien los da -- pasarselos a si misma
  /// no es nada -- ni quien no esta conectada: su telefono es el que tiene que
  /// enterarse de que ahora son suyos.
  function paintHandoutTo(from) {
    const box = $('handoutTo');
    box.textContent = '';

    const others = whoCanHandle().filter((one) => one.mark !== from.mark);

    if (others.length === 0) {
      box.appendChild(el('div', 'none', Idioma.texto('detalle.repartir.nadie-mas-vendiendo')));
      return;
    }

    others.forEach((one) => {
      const name = whoHasIt(one) || Idioma.texto('general.sin-nombre');
      const row = el('button', 'pick-one who-pick' + (ui.moveTo === one.mark ? ' on' : ''));
      row.disabled = !one.here;

      row.appendChild(el('div', 'who-face who-sell', initial(name)));

      const what = el('div', 'who-what');
      what.appendChild(el('div', 'who-name', name));
      what.appendChild(el('div', 'who-sub', !one.here
        ? Idioma.texto('detalle.persona.no-conectada')
        : Idioma.muchos((one.hand || []).length, 'detalle.repartir.asignados.uno', 'detalle.repartir.asignados.varios')));
      row.appendChild(what);

      row.appendChild(el('div', 'radio' + (ui.moveTo === one.mark ? ' radio-on' : '')));

      row.addEventListener('click', () => { ui.moveTo = one.mark; paintHandout(); });

      box.appendChild(row);
    });
  }

  function paintHandoutWho() {
    const box = $('handoutWho');
    box.textContent = '';

    const all = whoCanHandle();

    if (all.length === 0) {
      box.appendChild(el('div', 'none', Idioma.texto('detalle.repartir.nadie-vendiendo')));
      return;
    }

    all.forEach((one) => {
      const hand = (one.hand || []).length;
      const name = whoHasIt(one) || Idioma.texto('general.sin-nombre');

      // Sin conexion no se le puede entregar ni recoger: el telefono de la persona es
      // el que anota, y si no escucha el cambio se pierde. Se muestra igual, apagado,
      // para que se entienda por que no aparece.
      const can = one.here && (ui.doing === 'give' ? ui.state.left > 0 : hand > 0);

      const row = el('button', 'pick-one who-pick' + (ui.toWhom === one.mark ? ' on' : ''));
      row.disabled = !can;

      row.appendChild(el('div', 'who-face who-sell', initial(name)));

      const what = el('div', 'who-what');
      what.appendChild(el('div', 'who-name', name));
      what.appendChild(el('div', 'who-sub', !one.here
        ? Idioma.texto('detalle.persona.no-conectada')
        : Idioma.muchos(hand, 'detalle.repartir.asignados.uno', 'detalle.repartir.asignados.varios')));
      row.appendChild(what);

      row.appendChild(el('div', 'radio' + (ui.toWhom === one.mark ? ' radio-on' : '')));

      row.addEventListener('click', () => pickFrom(one.mark));

      box.appendChild(row);
    });
  }

  /// Los atajos, que es lo que evita apretar el mas veinte veces. El "Max" es el tope
  /// de verdad: lo libre si se esta dando, lo que tiene en mano si se esta recogiendo.
  function paintHandoutQuick(one) {
    const box = $('handoutQuick');
    box.textContent = '';

    const most = mostFor(one);
    const signo = ui.doing === 'give' ? '+' : ui.doing === 'move' ? '' : '-';

    [10, 20].forEach((by) => {
      const chip = el('button', 'many-one', signo + by);
      chip.disabled = ui.howMany + by > most;
      chip.addEventListener('click', () => {
        ui.howMany = Math.min(most, ui.howMany + by);
        paintHandout();
      });
      box.appendChild(chip);
    });

    const max = el('button', 'many-one', 'Max.');
    max.disabled = most <= 0;
    max.addEventListener('click', () => { ui.howMany = most; paintHandout(); });
    box.appendChild(max);
  }

  /// Como queda la cosa despues. Es la unica linea que contesta "y entonces cuantos me
  /// quedan", que es lo que uno esta haciendo la cuenta mentalmente mientras aprieta.
  function paintHandoutSays(one) {
    const box = $('handoutSays');
    box.textContent = '';
    box.classList.toggle('after-bad', ui.doing !== 'give');

    const name = whoHasIt(one) || Idioma.texto('detalle.repartir.esa-persona');
    const hand = (one.hand || []).length;

    if (ui.doing === 'move') {
      const to = whoCanHandle().find((x) => x.mark === ui.moveTo);

      box.classList.remove('after-bad');
      const cuantos = Idioma.muchos(ui.howMany, 'jugador.cartones.uno', 'jugador.cartones.varios');
      box.appendChild(el('div', 'after-line strong', to
        ? Idioma.con('detalle.repartir.le-pasa-a', { nombre: name, cuantos: cuantos, quien: whoHasIt(to) || Idioma.texto('detalle.repartir.la-otra-persona') })
        : Idioma.con('detalle.repartir.le-pasa', { nombre: name, cuantos: cuantos })));
      box.appendChild(el('div', 'after-line',
        Idioma.con('detalle.repartir.le-quedaran', { cuantos: Idioma.muchos(hand - ui.howMany, 'jugador.cartones.uno', 'jugador.cartones.varios') })));
      box.appendChild(el('div', 'after-line', Idioma.texto('detalle.repartir.disponibles-no-cambian')));
      return;
    }

    if (ui.doing === 'give') {
      box.appendChild(el('div', 'after-line', Idioma.con('detalle.repartir.disponibles', { n: ui.state.left })));
      box.appendChild(el('div', 'after-line strong',
        Idioma.con('detalle.repartir.recibira', { nombre: name, cuantos: Idioma.muchos(ui.howMany, 'jugador.cartones.uno', 'jugador.cartones.varios') })));
    } else {
      box.appendChild(el('div', 'after-line strong',
        Idioma.con('detalle.repartir.devolvera', { nombre: name, cuantos: Idioma.muchos(ui.howMany, 'jugador.cartones.uno', 'jugador.cartones.varios') })));
      box.appendChild(el('div', 'after-line', Idioma.con('detalle.repartir.disponibles-seran', { n: ui.state.left + ui.howMany })));
      box.appendChild(el('div', 'after-line',
        Idioma.con('detalle.repartir.le-quedaran', { cuantos: Idioma.muchos(hand - ui.howMany, 'jugador.cartones.uno', 'jugador.cartones.varios') })));
    }
  }

  function sendHandout() {
    if (!ui.toWhom || ui.howMany <= 0) return;

    if (ui.doing === 'move') {
      if (!ui.moveTo) { toast(Idioma.texto('detalle.log.dime-a-quien-se-los-pasa')); return; }
      send('movecards|' + ui.toWhom + '|' + ui.moveTo + '|' + ui.howMany);
    } else {
      send((ui.doing === 'give' ? 'handmore|' : 'takeback|') + ui.toWhom + '|' + ui.howMany);
    }

    ui.doing = null;
    goView('hub');
  }

  // -- el precio y el anuncio -------------------------------------------------

  function openPrice() {
    const s = ui.state;

    $('priceHow').value = s.price > 0 ? s.price : '';

    // Cambiarlo con ventas hechas cambia la cuenta de lo ya vendido: el precio es UN
    // numero del bingo, no una historia por venta. Se advierte antes, no despues.
    $('priceHint').textContent = s.sold > 0
      ? Idioma.con('detalle.precio.ojo-ya-vendidos', { n: s.sold })
      : Idioma.texto('detalle.precio.hint');

    show($('priceSheet'), true);
    setTimeout(() => $('priceHow').focus(), 120);
  }

  function savePrice() {
    const said = parseInt($('priceHow').value, 10);

    if (!Number.isFinite(said) || said <= 0) { toast(Idioma.texto('detalle.log.dime-cuanto-vale')); return; }

    send('price|' + said);
    show($('priceSheet'), false);
  }

  function openAnnounce() {
    const s = ui.state;

    // Marcado lo que rige hoy, que sin eleccion previa es OnClaim: la hoja no puede
    // abrirse en blanco si el bingo ya se esta jugando de una manera.
    ui.announcing = s.announce === 'Automatic' ? 'Automatic' : 'OnClaim';

    paintAnnouncePick();
    show($('announceSheet'), true);
  }

  function paintAnnouncePick() {
    $('announceVoice').classList.toggle('on', ui.announcing === 'OnClaim');
    $('announceAuto').classList.toggle('on', ui.announcing === 'Automatic');
    $('announceGo').disabled = !ui.announcing;
  }

  function saveAnnounce() {
    if (!ui.announcing) return;

    send('announce|' + ui.announcing);
    show($('announceSheet'), false);
  }

  // -- anotar a alguien -----------------------------------------------------

  /// El oficio lo trae quien abrio la hoja, no se elige adentro: se llega desde "elegir
  /// persona" (canta) o desde "agregar colaborador" (vende), y en los dos casos ya se
  /// sabe a que.
  function openAdd() {
    ui.adding = 'vende';

    $('addTitle').textContent = Idioma.texto('detalle.anotar.titulo');
    $('addSub').textContent = Idioma.texto('detalle.anotar.sub');

    $('addName').value = '';
    $('addCards').value = '';

    pickDuty('vende');

    show($('addSheet'), true);
    setTimeout(() => $('addName').focus(), 120);
  }

  /// Elegir el oficio adentro de la hoja. Los cartones solo tienen sentido para quien
  /// vende, asi que el campo aparece y desaparece con el oficio en vez de quedarse ahi
  /// apagado sin explicar por que.
  function pickDuty(duty) {
    ui.adding = duty;

    const sells = duty !== 'canta';

    $('addSells').classList.toggle('on', sells);
    $('addCalls').classList.toggle('on', !sells);

    show($('addCardsWrap'), sells);
    show($('addCardsHint'), sells);

    if (!sells) return;

    // Cuantos hay y de donde saldrian. El tramo no se promete -- los cartones se toman
    // del libro cuando la persona ENTRA, y para entonces puede haber cambiado --, pero
    // decirle de donde van a salir es lo que le deja repartir de corrido.
    const free = ui.state ? ui.state.free : [];

    $('addCardsHint').textContent = free.length === 0
      ? Idioma.texto('detalle.anotar.sin-libres')
      : Idioma.con('detalle.anotar.se-le-entregan', { n: free.length, cuales: inRanges(free) });
  }

  function closeAdd() { ui.adding = null; show($('addSheet'), false); }

  function saveAdd() {
    const name = $('addName').value.trim();

    if (name.length === 0) { toast(Idioma.texto('detalle.log.dime-como-se-llama')); return; }

    const cards = ui.adding === 'canta' ? 0 : parseInt($('addCards').value, 10) || 0;

    send('entrust|' + ui.adding + '|' + name + '|' + cards);
    closeAdd();
  }

  function paintPlayers() {
    const box = $('playerList');
    box.textContent = '';

    const s = ui.state;

    if (s.playerList.length === 0) {
      box.appendChild(el('div', 'none', Idioma.texto('detalle.jugadores.ninguno-largo')));
      return;
    }

    s.playerList.forEach((one) => {
      const row = el('div', 'who');

      row.appendChild(el('div', 'who-face who-play', initial(one.name)));

      const what = el('div', 'who-what');
      what.appendChild(el('div', 'who-name', one.name));
      what.appendChild(el('div', 'who-sub', one.cards.length === 0
        ? Idioma.texto('detalle.jugadores.sin-cartones')
        : Idioma.con('detalle.jugadores.cartones-cuales', { cuantos: Idioma.muchos(one.cards.length, 'jugador.cartones.uno', 'jugador.cartones.varios'), cuales: one.cards.join(', ') })));

      // El que compro pero todavia no escaneo su codigo tambien juega esta noche; la
      // fila lo dice en vez de esconderlo, que era el hueco de antes.
      if (one.entered === false) {
        what.appendChild(el('div', 'who-sub', Idioma.texto('detalle.jugadores.aun-no-entra')));
      }

      row.appendChild(what);

      box.appendChild(row);
    });
  }

  // ── anunciar una ronda ───────────────────────────────────────────────────

  function openSheet() {
    ui.newGames = [];
    $('prize').value = '';
    pickForm(true);
    paintNewGames();
    showFigureHint();
    show($('sheet'), true);
  }

  function closeSheet() { show($('sheet'), false); }

  function paintNewGames() {
    const box = $('newGames');
    box.textContent = '';

    ui.newGames.forEach((game, i) => {
      const line = el('div', 'game');
      line.appendChild(el('div', 'game-no', String(i + 1)));

      const text = el('div', 'game-what');
      text.appendChild(el('div', 'game-figure', window.Figuras.name(game.figure)));
      text.appendChild(el('div', 'game-prize', prizeText(game)));
      line.appendChild(text);

      const drop = el('button', 'game-drop', Idioma.texto('general.quitar'));
      drop.addEventListener('click', () => {
        ui.newGames.splice(i, 1);
        paintNewGames();
      });
      line.appendChild(drop);

      box.appendChild(line);
    });

    $('saveRound').disabled = ui.newGames.length === 0;
  }

  function addGame() {
    const figure = $('figure').value;
    const prize = $('prize').value.trim();

    if (!prize) {
      toast(Idioma.texto(ui.cash ? 'crear.log.cuanto-da' : 'detalle.log.que-cosa-da'));
      return false;
    }

    if (ui.cash && !(parseInt(prize, 10) > 0)) {
      toast(Idioma.texto('detalle.log.efectivo-en-numeros'));
      return false;
    }

    if (ui.newGames.some((g) => g.figure.toLowerCase() === figure.toLowerCase())) {
      toast(Idioma.con('detalle.log.figura-repetida', { figura: window.Figuras.name(figure) }));
      return false;
    }

    ui.newGames.push({ figure: figure, cash: ui.cash, prize: prize });
    paintNewGames();
    return true;
  }

  function saveRound() {
    if ($('prize').value.trim() && !addGame()) return;
    if (ui.newGames.length === 0) return;

    send('addround|' + JSON.stringify({ games: ui.newGames }));
    closeSheet();
  }

  function pickForm(cash) {
    ui.cash = cash;

    $('cash').classList.toggle('on', cash);
    $('goods').classList.toggle('on', !cash);

    $('prizeLabel').textContent = Idioma.texto(cash ? 'detalle.ronda.monto' : 'detalle.ronda.que-se-gana');

    const prize = $('prize');
    prize.type = cash ? 'number' : 'text';
    prize.inputMode = cash ? 'numeric' : 'text';
    prize.placeholder = cash ? '0' : Idioma.texto('crear.premio.ejemplo');
  }

  function showFigureHint() {
    $('figureHint').textContent = window.Figuras.says($('figure').value);
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
      ui.figures = msg.figures || [];
      window.Figuras.learn(msg.names, msg.hints);

      const box = $('figure');
      box.textContent = '';

      ui.figures.forEach((name) => {
        const option = document.createElement('option');
        option.value = name;
        option.textContent = window.Figuras.name(name);
        box.appendChild(option);
      });

      showFigureHint();
    } else if (msg.kind === 'log') {
      const box = $('logBox');
      box.textContent = msg.text;
      box.scrollTop = box.scrollHeight;
    } else if (msg.kind === 'toast') {
      toast(msg.text);
    } else if (msg.kind === 'code') {
      // El QR de UNA persona. Llega cuando el organizador lo pide, y no adentro del
      // retrato: son PNG en base64 y mandar el de cada colaborador en cada latido es
      // mandar la pantalla entera cien veces por nada.
      $('codeFor').textContent = msg.title || Idioma.con('detalle.codigo.de', { quien: (msg.forWhom || '').toUpperCase() });
      $('codeCopy').textContent = msg.title ? Idioma.texto('colaborador.sello.copiar-enlace') : Idioma.texto('detalle.codigo.copiar');
      $('codeSays').textContent = msg.says || '';
      $('codeQr').src = msg.png ? 'data:image/png;base64,' + msg.png : '';
      $('codeText').textContent = msg.text || '';
      show($('codeLayer'), true);
    }
  }

  window.addEventListener('HybridWebViewMessageReceived', (e) => onMessage(e.detail.message));

  // ── los gestos ───────────────────────────────────────────────────────────

  let toastTimer = null;

  function toast(text) {
    const box = $('toast');
    box.textContent = text;
    box.classList.add('hidden');
    void box.offsetWidth;
    box.classList.remove('hidden');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => show(box, false), 1900);
  }

  $('back').addEventListener('click', goBack);

  document.querySelectorAll('.ring').forEach((tab) => {
    tab.addEventListener('click', () => {
      ui.tab = tab.dataset.pane;
      // Cambiar de pestana cierra el consejo: se pidio para ESA pestana, y quedaria
      // abierto en otra como si hablara de lo que uno acaba de abrir.
      ui.tipOpen = false;
      paintDoors();
      paintNextStep();
    });
  });

  // La pestana por defecto se marca sin esperar el estado: el primer cuadro ya
  // se ve con su disco prendido, no una fila apagada que despues despierta.
  document.querySelector('.ring[data-pane="' + ui.tab + '"]').classList.add('ring-on');

  $('seeRounds').addEventListener('click', () => goView('rounds'));
  $('seeCards').addEventListener('click', () => goView('cards'));
  // El boton de la cabecera del equipo AGREGA, no lleva a mirar: la lista ya se ve
  // entera ahi mismo.
  $('addTeam').addEventListener('click', openAdd);

  $('seePlayers').addEventListener('click', () => goView('players'));

  $('call').addEventListener('click', () => send('call'));
  $('more').addEventListener('click', () => send('more'));

  // Cada tecla entra DIRECTO a lo suyo: la hoja que preguntaba "cual de las tres"
  // era un toque de mas en algo que se hace toda la noche. La hoja se queda para el
  // camino viejo desde la ficha de una persona.
  $('actGive').addEventListener('click', () => startHandout('give'));
  $('actMove').addEventListener('click', () => startHandout('move'));
  $('actBack').addEventListener('click', () => startHandout('back'));

  $('pickGive').addEventListener('click', () => startHandout('give'));
  $('pickBack').addEventListener('click', () => startHandout('back'));
  $('handoutNo').addEventListener('click', () => show($('handoutSheet'), false));
  $('handoutSheet').addEventListener('click', (e) => {
    if (e.target === $('handoutSheet')) show($('handoutSheet'), false);
  });

  $('handoutLess').addEventListener('click', () => {
    ui.howMany = Math.max(0, ui.howMany - 1);
    paintHandout();
  });
  $('handoutMore').addEventListener('click', () => {
    const one = whoCanHandle().find((x) => x.mark === ui.toWhom);
    ui.howMany = Math.min(mostFor(one), ui.howMany + 1);
    paintHandout();
  });
  $('handoutGo').addEventListener('click', sendHandout);

  $('nextStepGo').addEventListener('click', () => {
    ui.tipOpen = false;
    if (ui.nextAct) ui.nextAct();
  });

  document.querySelectorAll('.pane-tip').forEach((tip) => {
    tip.addEventListener('click', toggleTip);
  });

  $('crownWhen').addEventListener('click', openHour);
  document.querySelectorAll('[data-bump]').forEach((key) => {
    key.addEventListener('click', () => bumpClock(key.dataset.bump, Number(key.dataset.by)));
  });
  $('isAm').addEventListener('click', () => setHalf(false));
  $('isPm').addEventListener('click', () => setHalf(true));
  $('hourGo').addEventListener('click', saveHour);
  $('hourNo').addEventListener('click', () => show($('hourSheet'), false));
  $('hourSheet').addEventListener('click', (e) => {
    if (e.target === $('hourSheet')) show($('hourSheet'), false);
  });

  $('priceEdit').addEventListener('click', openPrice);
  $('priceGo').addEventListener('click', savePrice);
  $('priceNo').addEventListener('click', () => show($('priceSheet'), false));
  $('priceSheet').addEventListener('click', (e) => {
    if (e.target === $('priceSheet')) show($('priceSheet'), false);
  });

  $('announceEdit').addEventListener('click', openAnnounce);
  $('announceVoice').addEventListener('click', () => { ui.announcing = 'OnClaim'; paintAnnouncePick(); });
  $('announceAuto').addEventListener('click', () => { ui.announcing = 'Automatic'; paintAnnouncePick(); });
  $('announceGo').addEventListener('click', saveAnnounce);
  $('announceNo').addEventListener('click', () => show($('announceSheet'), false));
  $('announceSheet').addEventListener('click', (e) => {
    if (e.target === $('announceSheet')) show($('announceSheet'), false);
  });

  $('addSomebody').addEventListener('click', openAdd);
  $('addSells').addEventListener('click', () => pickDuty('vende'));
  $('addCalls').addEventListener('click', () => pickDuty('canta'));

  $('personQr').addEventListener('click', () => { if (ui.person) send('share|' + ui.person); });
  $('entrancePoster').addEventListener('click', () => send('poster'));
  $('personOut').addEventListener('click', () => {
    if (ui.person) send('sendhome|' + ui.person);
  });

  $('manyGo').addEventListener('click', saveMany);
  $('manyNo').addEventListener('click', closeMany);
  $('manySheet').addEventListener('click', (e) => { if (e.target === $('manySheet')) closeMany(); });
  $('addGo').addEventListener('click', saveAdd);
  $('addNo').addEventListener('click', closeAdd);
  $('addSheet').addEventListener('click', (e) => { if (e.target === $('addSheet')) closeAdd(); });

  $('addName').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    e.preventDefault();
    saveAdd();
  });

  $('codeCopy').addEventListener('click', () => send('copy|' + $('codeText').textContent));
  $('codeClose').addEventListener('click', () => show($('codeLayer'), false));

  $('addRound').addEventListener('click', openSheet);
  $('addGame').addEventListener('click', () => { if (addGame()) $('prize').value = ''; });
  $('saveRound').addEventListener('click', saveRound);
  $('closeSheet').addEventListener('click', closeSheet);

  $('sheet').addEventListener('click', (e) => { if (e.target === $('sheet')) closeSheet(); });

  $('cash').addEventListener('click', () => pickForm(true));
  $('goods').addEventListener('click', () => pickForm(false));
  $('figure').addEventListener('change', showFigureHint);

  $('prize').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    e.preventDefault();
    if (addGame()) $('prize').value = '';
  });

  $('logFold').addEventListener('click', () => {
    const box = $('logBox');
    const open = box.classList.contains('hidden');

    show(box, open);
    $('logChevron').classList.toggle('open', open);

    if (open) send('wantlog');
  });

  document.querySelectorAll('input, select').forEach((field) => {
    field.addEventListener('focus', () => {
      setTimeout(() => field.scrollIntoView({ block: 'center', behavior: 'smooth' }), 260);
    });
  });

  window.__test = (msg) => onMessage(JSON.stringify(msg));

  goView('hub');
  send('ready');
})();
