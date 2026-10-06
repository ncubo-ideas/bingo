(function () {
  'use strict';

  const ui = {
    state: null,
    asked: null,
    verdict: false,
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

  // ── lo que se pinta ──────────────────────────────────────────────────────

  /// Cantar -- la tombola, la bolita grande, corregir y lo que se esta jugando -- es de
  /// `calling.js`, el mismo modulo que usa quien ayuda: los dos cantan igual y con los
  /// mismos verbos. Lo de aca abajo es lo que SOLO hace el organizador.
  function render() {
    const s = ui.state;
    if (!s) return;

    $('title').textContent = s.name;
    // "Ronda 1 de 3 · 2 juegos abiertos". NO dice "juego X de Y" como la maqueta: en
    // este bingo los juegos de una ronda corren A LA VEZ sobre la misma tombola, asi
    // que numerarlos diria algo que no pasa. Cuantos siguen abiertos si es cierto.
    const open = (s.games || []).filter((g) => (g.winners || []).length === 0).length;

    const ronda = Idioma.con('crear.ronda-de', { n: s.roundAt, total: s.roundCount });
    $('roundLine').textContent = s.roundAt > 0
      ? (open > 0
        ? Idioma.con('cantar.ronda-y-juegos', { ronda: ronda, juegos: Idioma.muchos(open, 'cantar.juegos-abiertos.uno', 'cantar.juegos-abiertos.varios') })
        : ronda)
      : s.roundLine;

    window.Cantando.paint({
      drawn: s.drawn,
      last: s.last,
      sounding: s.playing,
      games: s.games,
      asleepTitle: Idioma.texto(s.over ? 'cantar.dormido.termino' : 'cantar.dormido.sin-ronda'),
      asleepSub: Idioma.texto(s.over
        ? 'cantar.dormido.nada-mas'
        : s.anyPlanned ? 'cantar.dormido.arranca-la-que-sigue' : 'cantar.dormido.anuncia-otra'),
    });

    paintBoard();
    paintRound();
    paintPleas();
    paintShouts();
    paintBingo();
  }

  function paintBoard() {
    const box = $('board');
    box.textContent = '';

    const s = ui.state;

    show($('boardNone'), s.board.length === 0);

    s.board.forEach((row) => {
      const line = el('div', 'row-board');

      const who = el('div', 'row-who');
      const waiting = row.done && !row.dismissed && !row.closed;

      who.appendChild(el('div', 'row-name' + (waiting ? ' hot' : ''),
        Idioma.con('cantar.fila.quien-carton', { quien: row.who, carton: row.card })));
      who.appendChild(el('div', 'row-fig', window.Figuras.name(row.figure)));
      line.appendChild(who);

      const far = el('div', 'row-far'
        + (row.dismissed ? ' no' : row.done && row.closed ? ' done' : waiting ? ' hot' : ''),
        row.dismissed ? Idioma.texto('cantar.fila.no-valia')
          : row.done && row.closed ? Idioma.texto('cantar.fila.entregado')
          : row.done ? 'BINGO'
          : row.have + '/' + row.need);
      line.appendChild(far);

      box.appendChild(line);
    });
  }

  function paintRound() {
    const s = ui.state;

    $('roundNow').textContent = Idioma.texto(s.playing
      ? 'cantar.ronda.cerrarla'
      : s.over ? 'cantar.ronda.no-jugadas' : 'cantar.ronda.cerrada');

    show($('settle'), s.playing);
    show($('next'), !s.playing && !s.over && s.anyPlanned);
    show($('close'), !s.playing && !s.over);

    const box = $('history');
    box.textContent = '';

    s.history.forEach((line) => box.appendChild(el('div', 'history-line', line)));
  }

  function paintPleas() {
    const s = ui.state;
    const box = $('pleaList');

    box.textContent = '';
    show($('pleas'), s.pleas.length > 0);

    s.pleas.forEach((one) => {
      const wrap = el('div', 'plea');
      wrap.appendChild(el('div', 'plea-who',
        Idioma.con('cantar.anular.pide', { quien: one.who, carton: one.card })));

      const acts = el('div', 'plea-acts');

      const yes = el('button', 'soft danger-soft', Idioma.texto('cantar.anular.si'));
      yes.addEventListener('click', () => send('grant|' + one.plea));
      acts.appendChild(yes);

      const no = el('button', 'soft', Idioma.texto('cantar.anular.no'));
      no.addEventListener('click', () => send('refuse|' + one.plea));
      acts.appendChild(no);

      wrap.appendChild(acts);
      box.appendChild(wrap);
    });
  }

  /// Los que cantaron y la mesa no les da la figura por completa. No se resuelven
  /// desde aca -- no hay nada que entregar ni que rechazar mientras la mesa diga que
  /// no --, pero tienen que VERSE: alguien esta parado con su carton en la mano.
  function paintShouts() {
    const s = ui.state;
    const box = $('shoutList');
    const sinRazon = (s.shouts || []).filter((one) => !one.completed);

    box.textContent = '';
    show($('shouts'), sinRazon.length > 0);

    sinRazon.forEach((one) => {
      const wrap = el('div', 'plea');
      wrap.appendChild(el('div', 'plea-who',
        Idioma.con('cantar.canta-con-carton', { quien: one.who, carton: one.card })
        + (one.figure ? ' (' + window.Figuras.name(one.figure) + ')' : '')));
      box.appendChild(wrap);
    });
  }

  function paintBingo() {
    const s = ui.state;
    const claim = s.claim;

    if (!claim || ui.verdict) { show($('bingo'), false); return; }

    // "Lo vi yo" y "me estan cantando" no son lo mismo: en el segundo caso hay una
    // persona parada esperando, y el cartel lo dice.
    $('bingoTag').textContent = Idioma.texto(claim.shouted ? 'cantar.bingo.cantan' : 'jugador.bingo');
    $('bingo').classList.toggle('bingo-shouted', !!claim.shouted);

    $('bingoWho').textContent = Idioma.con('cantar.fila.quien-carton', { quien: claim.who, carton: claim.card });
    $('bingoWhat').textContent = Idioma.con(claim.shouted ? 'cantar.bingo.canta' : 'cantar.bingo.completo', { figura: window.Figuras.name(claim.figure) })
      + (claim.prize ? '  ·  ' + claim.prize : '');

    show($('bingo'), true);
  }

  // ── preguntar antes de lo que no se deshace ──────────────────────────────

  function ask(what, title, sub, yes) {
    ui.asked = what;

    $('askTitle').textContent = title;
    $('askSub').textContent = sub;
    $('askYes').textContent = yes;

    show($('ask'), true);
  }

  function closeAsk() { ui.asked = null; show($('ask'), false); }

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
      render();
    } else if (msg.kind === 'log') {
      const box = $('logBox');
      box.textContent = msg.text;
      box.scrollTop = box.scrollHeight;
    } else if (msg.kind === 'toast') {
      toast(msg.text);
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
    toastTimer = setTimeout(() => show(box, false), 2100);
  }

  $('back').addEventListener('click', () => send('leave'));

  $('give').addEventListener('click', () => {
    const claim = ui.state && ui.state.claim;
    if (!claim) return;

    ui.verdict = true;
    show($('bingo'), false);
    send('prize|' + claim.card + '|' + claim.figure);
    setTimeout(() => { ui.verdict = false; }, 900);
  });

  $('wrong').addEventListener('click', () => {
    const claim = ui.state && ui.state.claim;
    if (!claim) return;

    ui.verdict = true;
    show($('bingo'), false);
    send('wrong|' + claim.card + '|' + claim.figure);
    setTimeout(() => { ui.verdict = false; }, 900);
  });

  $('settle').addEventListener('click', () => {
    const s = ui.state;
    const owed = s.owed;

    if (!owed) { send('settle'); return; }

    ask('settle',
      Idioma.texto(s.anyWinner ? 'cantar.pregunta.cerrar-con-debidos' : 'cantar.pregunta.cerrar-sin-ganadores'),
      Idioma.con('cantar.pregunta.queda-sin-entregar', { que: owed }),
      Idioma.texto('cantar.cerrar-la-ronda'));
  });

  $('next').addEventListener('click', () => send('next'));

  $('close').addEventListener('click', () => ask('close',
    Idioma.texto('cantar.pregunta.terminar'),
    Idioma.texto('cantar.pregunta.terminar-sub'),
    Idioma.texto('cantar.pregunta.terminar-si')));

  $('askYes').addEventListener('click', () => {
    const what = ui.asked;
    closeAsk();
    if (what) send(what);
  });

  $('askNo').addEventListener('click', closeAsk);
  $('ask').addEventListener('click', (e) => { if (e.target === $('ask')) closeAsk(); });

  $('logFold').addEventListener('click', () => {
    const box = $('logBox');
    const open = box.classList.contains('hidden');

    show(box, open);
    $('logChevron').classList.toggle('open', open);

    if (open) send('wantlog');
  });

  window.__test = (msg) => onMessage(JSON.stringify(msg));

  window.Cantando.start(send);
  send('ready');
})();
