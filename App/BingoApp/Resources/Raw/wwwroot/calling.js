/* Cantar: la superficie que comparten las DOS pantallas que cantan -- la del organizador
   y la cara de cantar de quien ayuda.

   Es la misma para los dos a proposito, y los dos pueden cantar SIEMPRE: no hay turno ni
   permiso que pedir. Dos personas cantando la misma tombola no se estorban, porque la
   bolita la asienta el diario del organizador y una que ya salio no vuelve a salir.

   La bolita se TECLEA y despues se canta con un boton, no se busca en el cuadro de las
   75. Son dos cosas distintas las que eso arregla: la primera es que teclear no obliga a
   BUSCAR -- el que canta lee "I 27" de la bolita fisica y escribe 2 y 7, sin recorrer
   quince renglones con la vista; la segunda, y la que importa, es que hay un paso de mas
   antes de mandarla. Tocando el cuadro el toque ERA el canto, y un dedo torcido ya habia
   viajado al carton de todos. Ahora un dedo torcido se borra y nadie se enteró.

   Y son DOS SECCIONES, no una sola tirada larga: INGRESAR tiene lo que hace falta para
   meter la bolita y nada mas -- la ultima y el teclado --, y REVISAR tiene las cuentas,
   lo que se esta jugando y el cuadro de las 75. Separadas porque son dos momentos
   distintos: entre bolita y bolita el ojo no tiene que buscar el teclado entre setenta y
   cinco casillas, y cuando alguien pregunta "salio el 40?" se va a mirar sin miedo a
   tocar. Y hay una razon de medida ademas de una de sentido: con todo junto el boton de
   ingresar caia detras de la barra de abajo y habia que scrollear para CADA bolita.

   Lo que cambia entre una pantalla y otra no es esto: es lo que va ALREDEDOR -- el
   organizador cierra rondas y entrega premios, quien ayuda no. */

window.Cantando = (function () {
  'use strict';

  const $ = (id) => document.getElementById(id);

  const ui = {
    send: () => {},
    keys: {},        // las 75 celdas de la tombola
    padKeys: {},     // las 10 teclas del teclado
    typed: '',     // lo que se lleva marcado, como texto: '', '2', '27'
    fixing: 0,     // la bolita que se esta corrigiendo, 0 si ninguna
    lastBall: 0,
    drawn: [],
    sounding: false,
    tab: 'enter',  // 'enter' o 'check'
  };

  function show(el, visible) { if (el) el.classList.toggle('hidden', !visible); }

  function el(tag, cls, text) {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined) node.textContent = text;
    return node;
  }


  // ── el arranque ──────────────────────────────────────────────────────────

  /// `sender` es el puente de cada pantalla: las dos hablan los mismos verbos
  /// (`call|n` y `correct|malo|bueno`), cada una con su propio telefono del otro lado.
  function start(sender) {
    ui.send = sender;

    // Tocar una del cuadro no canta: senala la que esta mal, para corregirla.
    ui.keys = window.Tombola.build($('keys'), pickToFix);

    buildPad();

    if ($('oops')) $('oops').addEventListener('click', () => pickToFix(ui.lastBall));
    if ($('fixCancel')) $('fixCancel').addEventListener('click', stopFixing);

    $('tabEnter').addEventListener('click', () => goTab('enter'));
    $('tabCheck').addEventListener('click', () => goTab('check'));

    goTab('enter');
  }

  // ── las dos secciones ────────────────────────────────────────────────────

  /// Corrigiendo se vuelve solo a INGRESAR: el teclado esta ahi, y dejar la correccion
  /// abierta detras de la otra seccion es dejarla abierta a ciegas.
  function goTab(which) {
    ui.tab = which;

    const entering = which === 'enter';

    show($('paneEnter'), entering);
    show($('paneCheck'), !entering);

    $('tabEnter').classList.toggle('on', entering);
    $('tabCheck').classList.toggle('on', !entering);

    if (!entering && ui.fixing > 0) stopFixing();
  }

  // ── el teclado ───────────────────────────────────────────────────────────

  /// Tres columnas: 1..9, y el ultimo renglon con borrar, el cero y el que manda.
  /// El que manda va tercero porque es donde el pulgar ya esta, y porque el orden
  /// izquierda-a-derecha es el del gesto: me equivoque, cero, listo.
  function buildPad() {
    const box = $('padKeys');
    if (!box) return;

    box.textContent = '';

    // padKeys y no keys: ui.keys son las 75 celdas de la tombola, y pisarlas dejaba el
    // tablero de Revisar sin pintar sin que nada se quejara.
    ui.padKeys = {};

    for (let digit = 1; digit <= 9; digit++) {
      const key = el('button', 'pad-key', String(digit));
      key.addEventListener('click', () => typeDigit(String(digit)));
      ui.padKeys[digit] = key;
      box.appendChild(key);
    }

    const back = el('button', 'pad-key pad-back', '⌫');
    back.setAttribute('aria-label', 'borrar');
    back.addEventListener('click', erase);
    box.appendChild(back);

    const zero = el('button', 'pad-key', '0');
    zero.addEventListener('click', () => typeDigit('0'));
    ui.padKeys[0] = zero;
    box.appendChild(zero);

    const go = el('button', 'pad-go');
    go.id = 'padGo';
    go.addEventListener('click', commit);
    box.appendChild(go);
  }

  /// Dos digitos y ni uno mas: el 75 es el techo, asi que un tercero solo puede ser un
  /// dedo de mas. Y un cero adelante no se acepta porque no significa nada.
  function typeDigit(digit) {
    if (!ui.sounding) return;
    if (ui.typed.length >= 2) return;
    if (ui.typed.length === 0 && digit === '0') return;

    ui.typed += digit;
    paintPad();
    paintKeys();
  }

  function erase() {
    if (ui.typed.length === 0) return;

    ui.typed = ui.typed.slice(0, -1);
    paintPad();
    paintKeys();
  }

  function clearTyped() { ui.typed = ''; }

  function commit() {
    if (!ui.sounding) return;

    const number = Number(ui.typed);
    if (!fair(number)) return;

    if (ui.fixing > 0) {
      const wrong = ui.fixing;
      stopFixing();
      ui.send('correct|' + wrong + '|' + number);
      return;
    }

    clearTyped();
    paintPad();
    paintKeys();
    ui.send('call|' + number);
  }

  /// Si el numero que se lleva marcado se puede cantar. Lo que devuelve manda sobre el
  /// boton, y lo que dice manda sobre el aviso rojo.
  function fair(number) {
    if (!Number.isFinite(number) || number < 1 || number > 75) return false;

    // Cambiar la mala por ella misma no es un cambio, y la mala esta justamente entre
    // las que ya salieron: por eso se descarta ella primero y despues el resto.
    if (number === ui.fixing) return false;
    if (ui.drawn.indexOf(number) >= 0) return false;

    return true;
  }

  function whyNot(number) {
    if (!Number.isFinite(number) || number < 1 || number > 75) return Idioma.texto('cantando.va-del-1-al-75');
    if (number === ui.fixing) return Idioma.texto('cantando.la-que-estas-cambiando');
    if (ui.drawn.indexOf(number) >= 0) return Idioma.con('cantando.ya-salio', { numero: number });

    return '';
  }

  // ── lo que se pinta ──────────────────────────────────────────────────────

  /// El retrato que las dos pantallas arman igual:
  ///   { drawn: [n], last: n, sounding: bool, games: [{figure, prize, cash, winners}],
  ///     asleepTitle, asleepSub }
  function paint(s) {
    ui.sounding = !!s.sounding;
    ui.drawn = s.drawn || [];

    // Cuantas van y cuantas quedan: al que canta le importa el resto, que es lo que le
    // dice si la ronda va por la mitad o si ya casi.
    if ($('howLeft')) $('howLeft').textContent = ui.drawn.length === 0
      ? Idioma.texto('cantando.75-por-salir')
      : Idioma.con('cantando.cantadas-y-restantes', {
          cantadas: Idioma.muchos(ui.drawn.length, 'cantando.cantadas.uno', 'cantando.cantadas.varios'),
          restantes: 75 - ui.drawn.length });

    // La que se estaba corrigiendo puede haber dejado de estar: el otro que canta pudo
    // corregirla primero. Sin esto el CORREGIR de aca se manda contra una bolita que ya
    // no salio, y el motor lo rechaza en su idioma.
    if (ui.fixing > 0 && ui.drawn.indexOf(ui.fixing) < 0) stopFixing();

    paintGames(s.games || []);
    paintBall(s);
    paintCheck(s);
    paintRecent(ui.drawn);

    // Se pueden senalar mientras la ronda suena: con la ronda parada no hay nada que
    // corregir, y el motor lo rechazaria de todos modos.
    window.Tombola.paint(ui.keys, ui.drawn, s.last, ui.sounding);
    $('tombola').classList.toggle('off', !s.sounding);

    paintFixing();
    paintPlaying(s);
    paintPad();
    paintKeys();

    show($('asleep'), !s.sounding);
    if ($('asleepTitle')) $('asleepTitle').textContent = s.asleepTitle || '';
    if ($('asleepSub')) $('asleepSub').textContent = s.asleepSub || '';
  }

  /// La cabecera de REVISAR. Las cuentas se repiten a proposito con las de INGRESAR: el
  /// que viene a revisar no tiene que cambiar de seccion para saber cuantas van.
  function paintCheck(s) {
    if (!$('checkCount')) return;

    $('checkCount').textContent = Idioma.muchos(ui.drawn.length, 'cantando.bolitas-cantadas.uno', 'cantando.bolitas-cantadas.varios');
    $('checkLeft').textContent = Idioma.con('cantando.pendientes', { n: 75 - ui.drawn.length });

    show($('checkLast'), s.last > 0);
    if (s.last > 0) {
      $('checkLast').textContent = Idioma.con('cantando.ultima', { bolita: window.Tombola.letterOf(s.last) + ' ' + s.last });
      $('checkLast').style.color = window.Tombola.colorOf(s.last);
    }
  }

  /// Lo que se esta jugando AHORA, figura por figura. Nuestra ronda juega varias a la
  /// vez, asi que no hay "el juego actual" sino los que van.
  ///
  /// Vive en REVISAR y no en Ingresar: la cabecera ya dice que se juega, y repetirlo
  /// arriba del teclado empujaba el boton de ingresar fuera de la pantalla.
  function paintGames(games) {
    const box = $('checkGames');
    if (!box) return;

    box.textContent = '';

    games.forEach((game) => {
      const row = el('div', 'now-game');

      row.appendChild(window.Figuras.grid(game.figure, 'mini-grid'));

      const what = el('div', 'now-what');
      what.appendChild(el('div', 'now-figure', window.Figuras.name(game.figure)));

      // La linea del ganador solo existe si esta pantalla SABE de ganadores: quien ayuda
      // todavia no se entera de un bingo, y "sin ganador todavia" seria mentirle.
      if (Array.isArray(game.winners)) {
        const won = game.winners.length > 0;

        what.appendChild(el('div', 'now-who' + (won ? ' won' : ''),
          won ? Idioma.con('cantando.para', { quienes: game.winners.join(', ') }) : Idioma.texto('cantando.sin-ganador')));
      }

      row.appendChild(what);

      const prize = el('div', 'now-prize');
      const icon = el('img', 'now-icon');
      icon.src = game.cash ? 'assets/prize-cash.png' : 'assets/prize-gift.png';
      icon.alt = '';
      prize.appendChild(icon);
      prize.appendChild(el('div', 'now-what-prize',
        game.cash ? '$' + game.prize : game.prize));
      row.appendChild(prize);

      box.appendChild(row);
    });

    show(box.parentElement, games.length > 0);
  }

  function paintBall(s) {
    const ball = $('lastBall');
    if (!ball) return;

    ball.textContent = s.last > 0 ? String(s.last) : '—';
    ball.style.background = s.last > 0 ? window.Tombola.colorOf(s.last) : '';

    // El brinco solo cuando la bolita CAMBIA: si brincara en cada retrato, brincaria
    // tambien cuando lo unico que paso es que alguien compro un carton.
    if (s.last !== ui.lastBall) {
      ui.lastBall = s.last;
      ball.classList.remove('pop');
      void ball.offsetWidth;
      if (s.last > 0) ball.classList.add('pop');
    }

    $('howMany').textContent = s.last === 0
      ? Idioma.texto('cantando.ninguna-todavia')
      : Idioma.con('cantando.ultima-y-cuentas', {
        bolita: window.Tombola.letterOf(s.last) + ' ' + s.last,
        cantadas: Idioma.muchos(ui.drawn.length, 'cantando.cantadas.uno', 'cantando.cantadas.varios'),
        quedan: 75 - ui.drawn.length });
  }

  /// Las ultimas que salieron, en fila. El cuadro de las 75 dice CUALES salieron pero no
  /// en que orden, y esto es lo que sirve para cantar en voz alta "y antes habia
  /// salido...", o para el que llego tarde a mirar.
  function paintRecent(drawn) {
    const box = $('recent');
    if (!box) return;

    box.textContent = '';

    const last = drawn.slice(-8).reverse();

    show(box, last.length > 1);

    last.forEach((n, at) => {
      const one = el('div', 'recent-one' + (at === 0 ? ' first' : ''),
        window.Tombola.letterOf(n) + ' ' + n);
      one.style.borderColor = window.Tombola.colorOf(n);
      box.appendChild(one);
    });
  }

  /// El teclado entero: lo marcado, su letra, el aviso y el boton que manda.
  /// Lo que sigue abierto, con la figura dibujada. Las ya ganadas no se pintan: lo que
  /// el que canta necesita de reojo es que FALTA, no lo que ya se entrego.
  function paintPlaying(s) {
    // Guarda de superficie compartida: si una de las dos paginas todavia no tiene la
    // pieza, se sale en vez de tumbar el pintado entero. Reventar aca dejaba la pantalla
    // congelada sin una sola pista de por que.
    if (!$('nowPlaying') || !$('playingList')) return;

    const open = (s.games || []).filter((g) => (g.winners || []).length === 0);

    show($('nowPlaying'), ui.sounding && open.length > 0);

    if (!ui.sounding || open.length === 0) return;

    const box = $('playingList');
    box.textContent = '';

    open.forEach((game) => {
      const row = el('div', 'playing-one');

      const left = el('div', 'playing-side');
      left.appendChild(el('div', 'playing-what', Idioma.texto('cantando.figura')));
      left.appendChild(window.Figuras.grid(game.figure, 'mini-grid'));
      left.appendChild(el('div', 'playing-figure', window.Figuras.name(game.figure)));
      row.appendChild(left);

      row.appendChild(el('div', 'playing-cut'));

      const right = el('div', 'playing-side');
      right.appendChild(el('div', 'playing-what', Idioma.texto('cantando.premio-rotulo')));

      const ico = document.createElement('img');
      ico.className = 'playing-ico';
      ico.alt = '';
      ico.src = game.cash ? 'assets/prize-cash.png' : 'assets/prize-gift.png';
      right.appendChild(ico);

      right.appendChild(el('div', 'playing-figure', Idioma.texto(game.cash ? 'cantando.efectivo' : 'cantando.premio')));
      right.appendChild(el('div', 'playing-prize',
        game.cash ? '$' + game.prize : game.prize));
      row.appendChild(right);

      box.appendChild(row);
    });
  }

  /// Que teclas pueden llevar a una bolita que todavia no salio. Con nada tecleado,
  /// cualquier digito del 1 al 9 arranca una; con un 7 puesto, solo 0 a 5 completan algo
  /// que existe. Se apagan en vez de esconderse: mover las teclas de sitio a mitad de la
  /// noche seria peor que dejarlas quietas.
  function paintKeys() {
    if (!ui.padKeys) return;

    const left = [];
    for (let n = 1; n <= 75; n++) if (ui.drawn.indexOf(n) < 0) left.push(n);

    // Al corregir, la que se esta arreglando vuelve a estar disponible: es su propio
    // reemplazo valido si uno se arrepiente.
    if (ui.fixing > 0) left.push(ui.fixing);

    for (let digit = 0; digit <= 9; digit++) {
      const key = ui.padKeys[digit];
      if (!key) continue;

      const tried = ui.typed + digit;
      const can = left.some((n) => {
        const said = String(n);
        return said === tried || said.startsWith(tried);
      });

      key.disabled = !can;
    }
  }

  function paintPad() {
    const pad = $('pad');
    if (!pad) return;

    show(pad, ui.sounding);
    if (!ui.sounding) return;

    const fixing = ui.fixing > 0;
    const nothing = ui.typed.length === 0;
    const number = Number(ui.typed);
    const ok = !nothing && fair(number);

    pad.classList.toggle('fixing-now', fixing);

    $('padSays').textContent = Idioma.texto(fixing ? 'cantando.que-salio-de-verdad' : 'cantando.que-numero-salio');

    $('padShow').textContent = nothing ? '—' : ui.typed;
    $('padShow').classList.toggle('empty', nothing);

    // La letra es la comprobacion de verdad: viene impresa en la bolita que tiene en la
    // mano, asi que si la pantalla dice otra, el numero esta mal tecleado.
    const letter = $('padLetter');
    const showLetter = !nothing && number >= 1 && number <= 75;

    show(letter, showLetter);
    if (showLetter) {
      letter.textContent = window.Tombola.letterOf(number);
      letter.style.color = window.Tombola.colorOf(number);
    }

    // El aviso sale ANTES de mandar, no despues: el numero repetido se ve mientras se
    // teclea, y asi no hace falta que el organizador lo rechace.
    const bad = $('padBad');
    show(bad, !nothing && !ok);
    bad.textContent = nothing ? '' : whyNot(number);

    // El boton dice lo mismo que la seccion: INGRESAR mientras se ingresa, CORREGIR
    // mientras se corrige. Si dijera "cantar" adentro de una seccion que se llama
    // Ingresar, serian dos nombres para el mismo gesto.
    const go = $('padGo');
    go.textContent = Idioma.texto(fixing ? 'cantando.corregir' : 'cantando.ingresar');
    go.disabled = !ok;
  }

  // ── me equivoque ─────────────────────────────────────────────────────────

  /// Empezar a corregir la bolita `number`. Se llega por dos caminos: el enlace de abajo
  /// de la ultima -- que es el caso de siempre, el error se nota al toque -- y tocando
  /// cualquiera del cuadro en Revisar, porque a veces el error se descubre tres bolitas
  /// despues, o lo descubre alguien que estaba mirando su carton.
  function pickToFix(number) {
    if (!ui.sounding) return;
    if (!number || ui.drawn.indexOf(number) < 0) return;

    ui.fixing = number;
    clearTyped();
    goTab('enter');

    $('fixingSays').textContent = Idioma.con('cantando.no-era', { numero: number });

    paintFixing();
    paintPad();
    paintKeys();
  }

  function stopFixing() {
    ui.fixing = 0;
    clearTyped();

    paintFixing();
    paintPad();
    paintKeys();
  }

  function paintFixing() {
    const fixing = ui.fixing > 0;

    show($('fixing'), fixing);

    // El enlace solo existe si hay algo que corregir, y desaparece mientras corrige:
    // ahi lo que toca es marcar, no volver a abrir lo mismo.
    show($('oops'), !fixing && ui.lastBall > 0 && ui.sounding);
    if (ui.lastBall > 0 && $('oops')) {
      $('oops').textContent = Idioma.con('cantando.me-equivoque', { numero: ui.lastBall });
    }
  }

  return { start: start, paint: paint };
})();
