(function () {
  'use strict';

  /// Las pestanias de cada oficio, en su orden. La barra se arma de UNA de las dos
  /// listas: las que no son de su oficio no existen, no estan escondidas. El oficio no
  /// se elige aca ni se recuerda del telefono -- viene en el codigo del organizador.
  const TABS = {
    vende: ['sell', 'sold', 'other'],
    canta: ['call', 'other'],
  };

  const ui = {
    state: null,
    tab: null,
    picked: [],      // los cartones que van en la venta de ahora
    cuantos: 0,      // cuantos dijo que lleva; 0 mientras no eligio
    vendiendo: false,// si la venta a pantalla completa esta arriba
    pasar: 0,        // cuantos le pasa a una colega
    codigos: {},     // el codigo que se le dio a cada comprador, por marca
    pickedSold: 0,   // el vendido elegido, 0 si ninguno
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


  // ── la puerta y las dos caras ────────────────────────────────────────────

  function goTab(which) {
    const cambio = ui.tab !== which;
    ui.tab = which;

    ['sell', 'sold', 'call', 'other'].forEach((v) => show($(v), v === which));


    document.querySelectorAll('.htab').forEach((tab) => {
      tab.classList.toggle('on', tab.dataset.go === which);
    });

    // Solo al CAMBIAR de pestania. Antes se hacia en cada dibujado, y como cada bolita
    // que canta el organizador redibuja la pantalla, la pagina se le iba al tope cada
    // pocos segundos -- con el teclado abierto y la fila esperando.
    if (cambio) $('body').scrollTop = 0;

    // Cambiar de pestania cierra lo que este puesto encima: dejarlo abierto arriba de otra
    // pestania seria volver a ella sin saber por que.
    if (cambio) { cerrarLaVenta(); verPanel(null); }
  }

  /// De que se ve la pantalla: la puerta sola, o la cara del oficio que le dieron.
  function layOut() {
    const s = ui.state;
    const inside = !!s && s.inside;
    const duty = inside ? s.duty : null;
    const mine = duty === 'canta' ? TABS.canta : TABS.vende;

    show($('door'), !inside);
    show($('head'), inside);

    // La franja del ensayo, arriba de todo: es el cartel y la salida a la vez. La clase
    // en el body es para que la cabecera deje de pagar la zona del reloj, que con la
    // franja visible ya la pago ella.
    const practice = inside && !!s.practice;
    show($('ensRibbon'), practice);
    document.body.classList.toggle('practice', practice);

    // Salirse de la funcion cierra lo que este encima: si no, la capa quedaria arriba de
    // la puerta.
    if (!inside) { cerrarLaVenta(); verPanel(null); }
    show($('tabs'), inside);
    $('body').classList.toggle('inside', inside);

    // El taco suelto ya no se muestra: la cuenta y el rango viven en la tarjeta de Vender.
    // El marcado se queda -- paintCustody recorta ui.picked, y #verTodos tiene un listener
    // registrado al arrancar que revienta si el id desaparece.
    show($('custody'), false);

    // La cabecera se pinta del color de QUIEN mira: verde si vende, morado si le toca
    // cantar. Cantar es trabajo del organizador prestado, y ya se marca asi en su
    // pestania; sin esto, quien canta abria una cabecera de vendedora.
    $('head').classList.toggle('head-canta', duty === 'canta');

    document.querySelectorAll('.htab').forEach((tab) => {
      show(tab, inside && mine.includes(tab.dataset.go));
    });

    if (!inside) {
      ['sell', 'sold', 'call', 'other'].forEach((v) => show($(v), false));
      return;
    }

    if (!mine.includes(ui.tab)) goTab(mine[0]);
    else goTab(ui.tab);

    // Mis cartones abre siempre en su menu: si quedo adentro de un panel, volver por la
    // pestania y encontrarse el teclado de pasar cartones no es lo que esperaba.
    if (ui.tab !== 'other') verPanel(null);
  }

  // ── lo que se pinta ──────────────────────────────────────────────────────

  function render() {
    const s = ui.state;
    if (!s) return;

    layOut();

    if (!s.inside) { paintMine(); return; }

    $('who').textContent = s.who;
        $('myDuty').textContent = Idioma.texto(s.duty === 'canta' ? 'colaborador.oficio.cantas' : 'colaborador.oficio.vendes')
      + '  ·  ' + s.festivalName;
    $('myNumber').textContent = s.myNumber > 0 ? '#' + s.myNumber : '';

        const line = s.over ? Idioma.texto('colaborador.termino')
      : s.between ? Idioma.texto('inicio.estado.entre-rondas')
      : s.roundLine;
    $('roundLine').textContent = line;
    show($('roundLine'), line.length > 0);

    paintCustody();
    paintSold();
    paintCall();
    paintMyNumber();
    paintDeudas();
    ableSell();

    // Al reescribir paintSold se perdio esta llamada, y con ella el teclado de pasar
    // cartones: la caja quedaba dibujada y VACIA, una barra blanca sin teclas.
    ableUndo();

    // Cada retrato que baja vuelve a habilitar el boton: si la venta entro, la capa del
    // codigo ya se abrio encima; si rebota, ella tiene que poder intentar otra vez.
    desbloquearVenta();

    // Va DESPUES de ableUndo y desbloquearVenta a proposito: si la tarjeta reventara, no
    // puede llevarse puesto el teclado de pasar cartones ni dejar trabado el boton de anotar.
    paintMisCartones();

    // La puerta de la venta. Sin cartones no hay venta que abrir, y el boton lo dice en
    // vez de dejarla entrar a una pantalla donde no puede hacer nada.
    const enLaBolsa = taco().length;
    $('abrirVenta').disabled = enLaBolsa === 0;
        $('abrirVentaQue').textContent = Idioma.texto(enLaBolsa === 0 ? 'colaborador.venta.no-te-quedan' : 'detalle.anotar.vender');

    // Cada puerta de "Mis cartones" dice como esta lo suyo, sin tener que entrar. Es lo
    // unico que vale la pena de la lista de acciones de la maqueta: la lista en si seria
    // repetir las pestanias, pero el numero vivo debajo del titulo si nos faltaba.
        $('subNumero').textContent = (s.myNumber > 0 ? '#' + s.myNumber + ' · ' : '') + Idioma.texto('colaborador.mis.para-recibir-mas');

        $('subPasar').textContent = enLaBolsa > 0
      ? Idioma.con('colaborador.mis.disponibles-para-pasar', { n: enLaBolsa })
      : Idioma.texto('colaborador.mis.no-tienes-para-pasar');

        $('subDevolver').textContent = enLaBolsa > 0
      ? Idioma.con('colaborador.mis.sin-vender-al-organizador', { n: enLaBolsa })
      : Idioma.texto('colaborador.mis.no-tienes-cartones');

    if (ui.paso === undefined) irAlPaso('stepCartones');
    else if (ui.paso === 'stepCartones') paintCuantos();
  }

  /// En que va la noche antes de tocar nada. La pastilla lleva el RANGO y no la cantidad:
  /// "del 6 al 30" es el bloque de papeles que tiene en la mano.
  function paintMisCartones() {
    const caja = $('misCartones');
    const quedan = taco().length;
    const entregados = ui.state.sold.length;

    caja.textContent = '';

    const arriba = el('div', 'tarjeta-top');
        arriba.appendChild(el('div', 'tarjeta-titulo', Idioma.texto('colaborador.mis.tus-cartones')));
    if (quedan > 0) arriba.appendChild(el('span', 'tarjeta-pill', comoSeDice(taco())));
    caja.appendChild(arriba);

    const cifras = el('div', 'cifras');
    [[entregados, Idioma.texto('colaborador.mis.vendidos')], [quedan, Idioma.texto('colaborador.mis.disponibles')]]
      .forEach(([cuanto, que], i) => {
        if (i > 0) cifras.appendChild(el('div', 'cifra-raya'));

        const uno = el('div', 'cifra');
        uno.appendChild(el('div', 'cifra-num', String(cuanto)));
        uno.appendChild(el('div', 'cifra-que', que));
        cifras.appendChild(uno);
      });
    caja.appendChild(cifras);

    // La barra cierra la cuenta sin hacer cuentas. Con la bolsa vacia se llena entera, que
    // es como termina la noche.
    const total = entregados + quedan;
    const barra = el('div', 'barrita');
    const lleno = el('i');
    lleno.style.width = (total > 0 ? Math.round((entregados / total) * 100) : 0) + '%';
    barra.appendChild(lleno);
    caja.appendChild(barra);
  }

  function paintMine() {
    const s = ui.state;
    const box = $('mineList');
    box.textContent = '';

    show($('mineWrap'), s.mine.length > 0);

    s.mine.forEach((one) => {
      const row = el('button', 'mine-one' + (one.now ? ' now' : ''));

      const what = el('div');

      what.appendChild(el('div', 'mine-name', one.name));

      // La pastilla va con el oficio y no con el nombre: al lado del nombre lo parte
      // en dos renglones, y el nombre es lo que ella busca primero.
      const under = el('div', 'mine-top');
      under.appendChild(el('div', 'mine-duty' + (one.duty === 'canta' ? ' canta' : ''),
                Idioma.texto(one.duty === 'canta' ? 'colaborador.oficio.cantas' : 'colaborador.oficio.vendes')));
      if (one.practice) under.appendChild(el('span', 'ens-pill', '\u{1F3AD} ' + Idioma.texto('colaborador.ensayo.pastilla')));
      if (one.now) under.appendChild(el('span', 'chip chip-here', Idioma.texto('colaborador.aqui-estas')));
      what.appendChild(under);
      row.appendChild(what);

      row.appendChild(el('div', 'mine-go', '›'));

      row.addEventListener('click', () => send('again|' + one.id + '|' + myName()));
      box.appendChild(row);
    });

    // La invitacion a ensayar se esconde mientras ya haya un ensayo en la lista: a ese
    // se ENTRA por la lista, como a cualquier bingo, y dos ensayos a la vez no ayudan
    // a nadie.
    show($('ensInvite'), !s.mine.some((one) => one.practice));
  }

  /// El taco ORDENADO. El organizador lo entrega en tira (InTheBook.Take sobre una lista
  /// ordenada), asi que casi siempre es un bloque seguido -- igual que el monton de
  /// papeles en la mano. De ahi sale todo lo demas: cuantos le da, cuales despega.
  function taco() { return ui.state.custody.slice().sort((a, b) => a - b); }

  /// Los N de arriba del monton. Ella dice cuantos; esto elige cuales.
  function losDeArriba(cuantos) { return taco().slice(0, cuantos); }

  /// "del 45 al 122". Si hay huecos -- una devolucion suelta, un cambio -- se dice,
  /// porque entonces el bloque de papeles ya no coincide con el rango.
  function comoSeDice(cartones) {
    if (cartones.length === 0) return '';
        if (cartones.length === 1) return Idioma.con('colaborador.rango.uno', { a: cartones[0] });
    if (cartones.length === 2) return Idioma.con('colaborador.rango.dos', { a: cartones[0], b: cartones[1] });

    const seguido = cartones[cartones.length - 1] - cartones[0] === cartones.length - 1;
    return seguido
      ? Idioma.con('colaborador.rango.seguido', { a: cartones[0], b: cartones[cartones.length - 1] })
      : cartones.join(', ');
  }

  function paintCustody() {
    const s = ui.state;
    const mios = taco();

    // Lo que ya no tiene en la mano deja de estar tocado: un carton vendido que
    // siguiera marcado dejaria el boton de pasar apuntando a nada.
    ui.picked = ui.picked.filter((c) => s.custody.includes(c));

    // Cien fichas eran un muro y contarlas imposible. El rango dice lo mismo en una
    // linea y se compara de un vistazo con el bloque que tiene en la mano.
    const cuenta = $('howManyIHave');
    cuenta.textContent = '';

    if (mios.length === 0) {
      cuenta.appendChild(el('b', null, '0'));
            cuenta.appendChild(el('span', null, Idioma.texto('colaborador.taco.no-te-quedan')));
    } else {
      cuenta.appendChild(el('b', null, String(mios.length)));
      cuenta.appendChild(el('span', null,
                Idioma.muchos(mios.length, 'jugador.cartones.uno', 'jugador.cartones.varios') + ' · ' + comoSeDice(mios)));
    }

    // La barra es contra el maximo que tuvo en la noche: sirve para ver cuanto le
    // queda del bloque sin leer numeros.
    ui.masQueTuvo = Math.max(ui.masQueTuvo || 0, mios.length);
    $('barraTaco').style.width =
      (ui.masQueTuvo === 0 ? 0 : Math.round((mios.length / ui.masQueTuvo) * 100)) + '%';

    paintTablero(mios);
  }

  /// Los cartones uno por uno, agrupados de a doce: se lee como el tablero de bingo que
  /// ya conocen. Solo cuando ella lo pide -- entra plegado.
  function paintTablero(mios) {
    const box = $('cardChips');
    box.textContent = '';

    if (mios.length === 0) return;

    for (let desde = 0; desde < mios.length; desde += 12) {
      const tanda = mios.slice(desde, desde + 12);
      box.appendChild(el('div', 'decena',
        Idioma.con('colaborador.mis.tanda', { desde: tanda[0], hasta: tanda[tanda.length - 1] })));
      tanda.forEach((card) => box.appendChild(el('div', 'tc', String(card))));
    }
  }

  function paintSold() {
    const s = ui.state;

    if (!ui.tab && s.duty !== 'canta') ui.tab = 'sell';

    // Si el vendido elegido ya no esta en la lista, se suelta.
    if (!s.sold.some((one) => one.card === ui.pickedSold)) ui.pickedSold = 0;

    // Por VENTA y no por carton. El diario los guarda uno por uno, pero lo que pasó en
    // el mundo fue "le di cuatro a Luis": mostrarlo como cuatro renglones es contarle
    // otra historia, y con cien cartones la lista se vuelve inmirable.
    const ventas = [];
    s.sold.forEach((one) => {
      const ultima = ventas[ventas.length - 1];
      if (ultima && ultima.mark === one.mark && ultima.who === one.who) {
        ultima.cards.push(one.card);
      } else {
        ventas.push({ who: one.who, mark: one.mark, cards: [one.card] });
      }
    });

    ventas.forEach((v) => v.cards.sort((a, b) => a - b));

    // Las dos cifras de ESTA pantalla, con su forma: cajita de icono, el numero grande y
    // el rotulo en dos renglones, separadas por una raya fina. Las de la bolsa las lleva
    // Vender, que es la casa; estas dos son de lo vendido y no se repiten con aquellas.
    // Se calculan sobre TODO lo vendido, no sobre lo buscado: los totales de la noche no
    // cambian porque ella este buscando a alguien.
    const cuentas = $('howMuchISold');
    cuentas.textContent = '';
    show(cuentas, ventas.length > 0);

    [[CARTON, s.sold.length, Idioma.texto('colaborador.vendidos.cartones'), Idioma.texto('colaborador.vendidos.vendidos')],
     [PERSONA, ventas.length,
      Idioma.texto(ventas.length === 1 ? 'colaborador.vendidos.venta' : 'colaborador.vendidos.ventas'),
      Idioma.texto(ventas.length === 1 ? 'colaborador.vendidos.realizada' : 'colaborador.vendidos.realizadas')]]
      .forEach(([dibujo, cuanto, arriba, abajo], i) => {
        if (i > 0) cuentas.appendChild(el('div', 'cifra-raya'));

        const una = el('div', 'dos-cifra');
        const caja = el('div', 'dos-icono');
        caja.appendChild(icono(dibujo, 18, 1.8));
        una.appendChild(caja);

        const texto = el('div');
        texto.appendChild(el('div', 'cifra-num', String(cuanto)));
        texto.appendChild(el('div', 'cifra-que', arriba));
        texto.appendChild(el('div', 'cifra-que', abajo));
        una.appendChild(texto);

        cuentas.appendChild(una);
      });

    // El buscador, adaptado a la lista por ventas. Lo buscado se compara sin
    // acentos ni mayusculas -- nadie escribe "Jimenez" igual dos veces -- y un numero
    // pelado busca CARTONES y nombres, no marcas: las marcas traen digitos adentro, asi
    // que buscar "2" devolvia ventas cuyos codigos tenian un 2. La marca se busca cuando
    // lo tecleado no parece un numero.
    const looked = flat($('soldFind').value);
    const asNumber = /^\d+$/.test(looked);

    const mostradas = looked.length === 0
      ? ventas
      : ventas.filter((v) => flat(v.who).includes(looked)
        || v.cards.some((c) => String(c).startsWith(looked))
        || (!asNumber && flat(v.mark).includes(looked)));

    show($('soldFind'), s.sold.length > 0);

    const none = $('soldNone');
    show(none, ventas.length > 0 && mostradas.length === 0);
        none.textContent = Idioma.con('colaborador.ventas.ninguna-con', { que: $('soldFind').value.trim() });

    const box = $('soldList');
    box.textContent = '';

    // El vacio con sentido: sin esto la pantalla quedaba muda y a medio llenar, que se
    // lee como que algo fallo y no como que todavia no vendio.
    if (ventas.length === 0) {
      const nada = el('div', 'nada-aun');
            nada.appendChild(el('b', null, Idioma.texto('colaborador.ventas.ninguna-todavia')));
      nada.appendChild(document.createTextNode(Idioma.texto('colaborador.ventas.apareceran')));
      box.appendChild(nada);
      return;
    }

    mostradas.forEach((v) => {
      const row = el('button', 'sold-one');

      // Las INICIALES, no una letra: "JP" distingue a Juan Pablo de Julia, y una J sola no.
      row.appendChild(el('div', 'sold-avatar', iniciales(v.who)));

      const what = el('div', 'sold-what');
      what.appendChild(el('div', 'sold-who', v.who));

      if (v.mark) what.appendChild(el('div', 'sold-mark', v.mark));

      // Los numeros de carton, en fichas. Hasta cuatro uno por uno -- que es lo que se
      // compara contra el papel -- y de ahi en adelante el rango, para que una venta de
      // cuarenta no empuje a las demas fuera de la pantalla.
      const fichas = el('div', 'sold-fichas');

      if (v.cards.length <= 4) v.cards.forEach((c) => fichas.appendChild(el('span', 'ficha-num', '#' + c)));
      else fichas.appendChild(el('span', 'ficha-num', comoSeDice(v.cards)));

            fichas.appendChild(el('span', 'sold-cuenta', Idioma.muchos(v.cards.length, 'jugador.cartones.uno', 'jugador.cartones.varios')));
      what.appendChild(fichas);

      row.appendChild(what);

      // El chevron al borde, centrado en el alto de la fila, como en la maqueta.
      row.appendChild(el('span', 'sold-go', '›'));

      row.addEventListener('click', () => abrirVenta(v));

      box.appendChild(row);
    });
  }

  /// Que puede hacer con una venta. Cambiar y anular solo valen ANTES de la primera
  /// bolita: el dato ya baja del organizador (s.drawn) y hasta ahora no se usaba, asi
  /// que los dos botones se veian habilitados y el rechazo llegaba despues.
  function abrirVenta(v) {
    ui.pickedSold = v.cards[0];
    ui.ventaAbierta = v;

    $('ventaQuien').textContent = v.who;
    $('undoTitle').textContent = v.mark || '';
    show($('undoTitle'), !!v.mark);
    $('ventaDonde2').textContent = ui.state.festivalName;
        $('ventaCuantos').textContent = Idioma.muchos(v.cards.length, 'jugador.cartones.uno', 'jugador.cartones.varios');

    // "Que paso" arranca cerrado en cada venta que se abre: si quedara desplegado de la
    // anterior, lo primero que veria al entrar seria como reclamar.
    show($('quePaso'), false);
    show($('algoMal'), true);

    // Los cartones de ESTA venta, con el mismo molde que la revision. Hasta seis uno por
    // uno; mas que eso, el rango.
    const fichas = $('ventaFichas');
    fichas.textContent = '';

    if (v.cards.length <= 6) v.cards.forEach((c) => fichas.appendChild(el('span', 'ficha-num grande', '#' + c)));
    else fichas.appendChild(el('span', 'ficha-num grande', comoSeDice(v.cards)));

    // drawn es una LISTA, no un contador: con `> 0` el tope se levantaba solo desde la
    // segunda bolita, porque [7, 12] se vuelve "7,12" y la comparacion da false.
    const arranco = (ui.state.drawn || []).length > 0;

    // Con la ronda arrancada las correcciones NO EXISTEN: ni apagadas ni con aviso. Un
    // cartel rojo explicando algo que nadie intento hacer es ruido, y una opcion que no se
    // puede tocar no gana nada con verse. La puerta entera desaparece.
    show($('algoMal'), !arranco);

    // Cambiar pide UN carton elegido en Vender, que es el de reemplazo: eso si se muestra
    // apagado, porque no es un imposible sino un paso previo que la bajada explica.
    const sinReemplazo = (ui.picked || []).length !== 1;

    $('exchange').disabled = sinReemplazo;
    $('plead').disabled = false;

        $('exchangeSub').textContent = Idioma.texto(sinReemplazo ? 'colaborador.detalle.selecciona-antes' : 'colaborador.detalle.reemplazo-de-tu-taco');

    // El boton del codigo SIEMPRE esta -- es la accion frecuente y ancla la pantalla --
    // pero apagado si el codigo no se guardo en esta sesion: escondido dejaba el detalle
    // sin nada que hacer arriba del pliegue.
    $('verCodigo').disabled = !ui.codigos[v.mark];

    show($('ventaLayer'), true);
  }

  function paintCall() {
    const s = ui.state;
    if (s.duty !== 'canta') return;

    window.Cantando.paint({
      drawn: s.drawn,
      last: s.last,
      sounding: !s.over && !s.between,
      games: s.games,
            asleepTitle: Idioma.texto(s.over ? 'cantar.dormido.termino' : 'cantar.dormido.sin-ronda'),
      asleepSub: Idioma.texto(s.over ? 'colaborador.cantar.ya-no-se-canta' : 'colaborador.cantar.organizador-arranca'),
    });
  }

  function paintMyNumber() {
    const s = ui.state;

    $('myNumberBig').textContent = s.myNumber > 0 ? '#' + s.myNumber : '';
    $('myNumberQr').src = s.myQr ? 'data:image/png;base64,' + s.myQr : '';
  }

  // ── que se puede apretar ─────────────────────────────────────────────────

  // ── la venta, paso por paso ──────────────────────────────────────────────
  //
  // Tres pantallas, una pregunta cada una. El orden sigue la conversacion real: la
  // persona llega y dice "dame tres". Ella NO elige cartones -- dice CUANTOS, y
  // losDeArriba() agarra los del tope del monton. Con cien en la bolsa, tocarlos uno
  // por uno y contarlos era el trabajo mas lento de la noche.

  /// Cuando el organizador no esta, la venta YA NO SE FRENA: los cartones que ella tiene en
  /// la mano se los entrego el, y no hay dos vendedoras que puedan pisarse ahi. Se cobra y
  /// se anota. Se avisa ACA y no antes -- en los pasos anteriores todavia no hay nada que
  /// confirmar -- y solo cuando lo SABEMOS, que es distinto de advertir por las dudas.
  ///
  /// El boton solo se apaga si de verdad no hay donde anotarla: sin copia del diario no hay
  /// libreta, y prometerle que cobre seria mentirle.
  function paintSolo() {
    const solo = !!(ui.state && ui.state.solo);
    const puedeSola = !!(ui.state && ui.state.canSellAlone);

    $('soloAviso').classList.toggle('hidden', !solo || !puedeSola);
    $('doSell').disabled = solo && !puedeSola;
        $('doSell').textContent = Idioma.texto(solo && !puedeSola ? 'colaborador.venta.falta-el-organizador' : 'colaborador.venta.confirmar');
  }

  /// Las dos deudas, en la primera pantalla que ella mira.
  ///
  /// "Le debo N" es plata cobrada que el organizador todavia no sabe: se va sola cuando el
  /// vuelve, asi que es un aviso calmo. "Rebotó" es plata cobrada por un carton que no
  /// existe: esa NO se va sola -- hay que ir a buscar a la persona --, por eso lleva el
  /// nombre, la hora, los cartones y el motivo. Sin esos cuatro datos el aviso no sirve
  /// para nada, porque el problema se resuelve hablando y hay que saber con quien.
  function paintDeudas() {
    const debe = (ui.state && ui.state.waiting) || 0;
    const malas = (ui.state && ui.state.bounced) || [];

    const box = $('debeContar');
    box.classList.toggle('hidden', debe === 0);
    if (debe > 0) {
            box.textContent = Idioma.muchos(debe, 'colaborador.deuda.una', 'colaborador.deuda.varias');
    }

    const caja = $('rebotadas');
    caja.classList.toggle('hidden', malas.length === 0);
    if (malas.length === 0) return;

    caja.textContent = '';

    const titulo = document.createElement('div');
        titulo.textContent = Idioma.muchos(malas.length, 'colaborador.rebote.una', 'colaborador.rebote.varias');
    caja.appendChild(titulo);

    for (const mala of malas) {
      const linea = document.createElement('div');
      linea.className = 'rebote';
      const cartones = (mala.cards || []).join(', ');
            linea.textContent = mala.when + ' · ' + mala.buyer
        + ' · ' + Idioma.con(mala.cards && mala.cards.length === 1 ? 'colaborador.rebote.carton' : 'colaborador.rebote.cartones', { cuales: cartones })
        + ' — ' + mala.why;
      caja.appendChild(linea);
    }

    const pie = document.createElement('div');
    pie.className = 'rebote-pie';
        pie.textContent = Idioma.texto('colaborador.rebote.hablalo');
    caja.appendChild(pie);
  }

  const PASOS = ['stepCartones', 'stepJugador', 'stepConfirmar'];

  /// El titulo de la cabecera cambia con el paso, como en la maqueta: los dos primeros
  /// siguen siendo la venta, y el ultimo ya es una revision.
  const TITULOS = {
        stepCartones: 'detalle.anotar.vender',
    stepJugador: 'detalle.anotar.vender',
    stepConfirmar: 'colaborador.venta.revisa',
  };

  function irAlPaso(cual) {
    ui.paso = cual;
    PASOS.forEach((p) => show($(p), p === cual));

    $('ventaTitulo').textContent = Idioma.texto(TITULOS[cual]);
    $('ventaDonde').textContent = ui.state ? ui.state.festivalName : '';

    if (cual === 'stepCartones') paintCuantos();
    if (cual === 'stepJugador') paintQuien();
    if (cual === 'stepConfirmar') { paintResumen(); paintSolo(); }

    // Cada paso empieza arriba: el anterior pudo quedar scrolleado.
    const cuerpo = $(cual).querySelector('.hoja-cuerpo');
    if (cuerpo) cuerpo.scrollTop = 0;

    paintPasos();
  }

  /// La venta se abre ENCIMA de la pantalla y tapa la barra de pestanias. Mientras esta
  /// arriba la unica salida es la flecha, que retrocede un paso por vez: es lo que hace que
  /// cada paso tenga la pantalla entera para una sola pregunta.
  function abrirLaVenta() {
    ui.vendiendo = true;
    show($('ventaFull'), true);
    ventaDesdeCero();
  }

  function cerrarLaVenta() {
    ui.vendiendo = false;
    show($('ventaFull'), false);
  }

  /// Vuelve a foja cero. La usan abrir la venta, el 'mejor no' y el cierre de una venta ya
  /// anotada.
  function ventaDesdeCero() {
    ui.cuantos = 0;
    ui.picked = [];
    modoDeCompra(false);
    show($('buyerKnown'), false);
    $('buyerNumber').value = '';
    $('buyer').value = '';
    $('contact').value = '';
    irAlPaso('stepCartones');
  }

  /// El eco de lo que ya contesto, arriba de la pregunta que sigue: sin esto el segundo
  /// paso no dice cuantos cartones se estan vendiendo.
  function paintQuien() {
    $('cuantosLleva').textContent = Idioma.muchos(ui.picked.length, 'jugador.cartones.uno', 'jugador.cartones.varios');
    ableSell();
  }

  /// Un icono de trazo, del mismo molde que los del HTML. Hace falta en JS porque el
  /// indicador de pasos cambia de dibujo: lo pendiente muestra su propio icono y lo
  /// contestado muestra el visto.
  function icono(dibujos, lado, grosor) {
    const NS = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('width', lado);
    svg.setAttribute('height', lado);
    svg.setAttribute('fill', 'none');

    dibujos.forEach((d) => {
      const p = document.createElementNS(NS, 'path');
      p.setAttribute('d', d);
      p.setAttribute('stroke', 'currentColor');
      p.setAttribute('stroke-width', grosor || 2.2);
      p.setAttribute('stroke-linecap', 'round');
      p.setAttribute('stroke-linejoin', 'round');
      svg.appendChild(p);
    });

    return svg;
  }

  const VISTO = 'M5 12.5l4.5 4.5L19 7';

  const CARTON = ['M6.4 6.5h11.2a2 2 0 012 2v7a2 2 0 01-2 2H6.4a2 2 0 01-2-2v-7a2 2 0 012-2z',
                  'M14.6 6.5v11'];

  const PERSONA = ['M12 11.6a3.3 3.3 0 100-6.6 3.3 3.3 0 000 6.6',
                   'M5.2 19.6c0-3.5 3.1-5.9 6.8-5.9s6.8 2.4 6.8 5.9'];

  /// Sin acentos y en minusculas, para buscar como la gente escribe.
  function flat(said) {
    return (said || '').trim().toLowerCase()
      .normalize('NFD').replace(/[̀-ͯ]/g, '');
  }

  /// Las iniciales de un nombre: "Juan Pablo" da JP y "Fiona" da FI. Una sola letra no
  /// distingue a dos personas que empiezan igual, y en una lista eso pasa toda la noche.
  function iniciales(nombre) {
    const partes = (nombre || '?').trim().split(/\s+/);

    if (partes.length > 1) return (partes[0].charAt(0) + partes[1].charAt(0)).toUpperCase();

    return partes[0].slice(0, 2).toUpperCase();
  }

  /// Los dibujos de las marcas. El del carton es EL MISMO de la pestania Vender -- rectangulo
  /// con la linea punteada -- y no un boleto de dos muescas: a este tamano las muescas se
  /// vuelven una mancha, y ademas conviene que el mismo objeto se dibuje siempre igual.
  const PASOS_QUE = [
        { que: 'crear.paso.cartones', dibujo: ['M6.4 6.5h11.2a2 2 0 012 2v7a2 2 0 01-2 2H6.4a2 2 0 01-2-2v-7a2 2 0 012-2z', 'M14.6 6.5v11'] },
        { que: 'colaborador.paso.jugador', dibujo: ['M12 11.6a3.3 3.3 0 100-6.6 3.3 3.3 0 000 6.6', 'M5.2 19.6c0-3.5 3.1-5.9 6.8-5.9s6.8 2.4 6.8 5.9'] },
        { que: 'colaborador.paso.confirmar', dibujo: [VISTO] },
  ];

  /// El paso ANDADO y el paso EN QUE ESTA van encendidos; solo el andado cambia su dibujo
  /// por el visto. Nunca se llama desde ableSell: ahi entraria en la cadena de render antes
  /// de ableUndo y desbloquearVenta, y un adorno que revienta se los llevaria puestos.
  function paintPasos() {
    const caja = $('pasosVenta');
    caja.textContent = '';

    const donde = Math.max(0, PASOS.indexOf(ui.paso));
    const cuantos = (ui.picked || []).length;

    // Mismo criterio que el boton de seguir: cada camino pide lo suyo.
    const aQuien = ui.primeraVez
      ? $('buyer').value.trim().length > 0
      : $('buyerNumber').value.trim().length > 0;

    // A que paso se PUEDE ir tocando la marca. Volver atras siempre se puede; ir adelante
    // solo si lo que ese paso necesita ya esta contestado, que es la misma regla que decide
    // si el boton de seguir esta encendido. Sin esto, tocar "Confirmar" desde el primer
    // paso llevaria a una revision sin comprador.
    const sePuede = [true, cuantos > 0, cuantos > 0 && aQuien];

    PASOS_QUE.forEach((paso, i) => {
      if (i > 0) caja.appendChild(el('div', 'paso-linea' + (donde > i - 1 ? ' va' : '')));

      const marca = el('button', 'paso-marca'
        + (donde >= i ? ' va' : '')
        + (donde === i ? ' aqui' : ''));

      marca.type = 'button';
      marca.disabled = !sePuede[i] || donde === i;
      marca.addEventListener('click', () => irAlPaso(PASOS[i]));

      const bola = el('div', 'paso-bola');

      // El visto va mas grueso que el resto: es una marca, no un dibujo.
      bola.appendChild(donde > i ? icono([VISTO], 15, 2.8) : icono(paso.dibujo, 15, 2));
      marca.appendChild(bola);
      marca.appendChild(el('div', 'paso-que', Idioma.texto(paso.que)));
      caja.appendChild(marca);
    });
  }

  /// La escalera de cantidades. Escribir cuarenta es mejor que tocar la flecha cuarenta
  /// veces, pero tocar UNA vez es mejor que escribir: esto cubre las compras grandes que
  /// se repiten toda la noche sin abrir el teclado del sistema.
  const ATAJOS = [5, 10, 20, 25, 50];

  function paintCuantosMas() {
    const hay = taco().length;
    const caja = $('cuantosMas');

    // Solo los que CABEN en la bolsa. Dibujar un 50 apagado cuando tiene 25 es ruido: ella
    // tendria que aprender por que no se puede tocar.
    const caben = ATAJOS.filter((n) => n <= hay);

    show(caja, caben.length > 0);
    caja.textContent = '';

    caben.forEach((n) => {
      const b = el('button', 'cn cn-mas' + (ui.cuantos === n ? ' on' : ''), String(n));
      b.addEventListener('click', () => elegirCuantos(n));
      caja.appendChild(b);
    });
  }

  /// El control de cantidad de la maqueta: pastilla, pregunta, y menos / numero / mas.
  /// El tope de la bolsa se aplica ANTES de que toque, igual que antes: el '+' se apaga al
  /// llegar al ultimo carton y lo escrito se recorta en vivo.
  function paintCantidad() {
    const hay = taco().length;
    const n = ui.cuantos;
    const campo = $('cuantosVan');

    // El campo NO se pisa mientras ella lo esta escribiendo: si no, borrarle el 4 a un 40
    // le devolveria el 40 puesto por el codigo y no podria seguir escribiendo.
    if (document.activeElement !== campo) campo.value = n > 0 ? String(n) : '';

    $('menosUno').disabled = n <= 1;
    $('masUno').disabled = n >= hay;

        $('pastillaTaco').textContent = Idioma.muchos(hay, 'colaborador.venta.disponible.uno', 'colaborador.venta.disponible.varios');
    show($('pastillaTaco'), hay > 0);
    show($('contador'), hay > 0);

    $('cuantosDice').textContent = n > 0
            ? Idioma.muchos(n, 'colaborador.venta.seleccionado.uno', 'colaborador.venta.seleccionado.varios')
      : '';
  }

  function paintCuantos() {
    const hay = taco().length;

    // El tope antes de dibujar, y arranque en uno. No pisa lo que este escribiendo: con el
    // campo en foco se respeta el renglon vacio. La guarda va en positivo porque
    // `ui.cuantos <= 0` no atrapa un undefined, que es como llega la primera vez.
    if (ui.cuantos > hay) fijarCuantos(hay);
    else if (!(ui.cuantos > 0) && hay > 0 && document.activeElement !== $('cuantosVan')) fijarCuantos(1);

    paintCantidad();
    paintCuantosMas();

    // Ya solo el caso vacio. "Tenes N en la bolsa" repetia lo que dice la tarjeta del taco
    // aca arriba -- que ademas trae el rango -- y con el saldo de despues abajo el mismo
    // numero quedaba escrito TRES veces en la misma pantalla.
    const sinNada = hay === 0;
        $('topeTaco').textContent = sinNada ? Idioma.texto('colaborador.venta.pide-mas') : '';
    show($('topeTaco'), sinNada);

    paintCuales();
    paintPasos();
  }

  /// Lo que depende de la cantidad elegida. Los numeros de carton NO van aca: viven en la
  /// revision, que es cuando los papeles cambian de mano.
  function paintCuales() {
    // Apagado y no escondido: el boton ocupa su lugar desde el principio.
    $('aQuien').disabled = ui.picked.length === 0;

    // El saldo de DESPUES: al final de la noche tiene que devolver lo que le sobro.
    show($('saldoLuego'), ui.picked.length > 0);
        $('saldoLuego').textContent = Idioma.con('colaborador.venta.te-quedarian', {
      cuantos: Idioma.muchos(Math.max(0, taco().length - ui.picked.length), 'colaborador.venta.disponible.uno', 'colaborador.venta.disponible.varios') });
  }

  /// Fija la cantidad SIN repintar. Existe para que paintCuantos pueda aplicar el tope sin
  /// llamarse a si misma, que seria recursion infinita.
  function fijarCuantos(n) {
    ui.cuantos = n;
    ui.picked = losDeArriba(n);
  }

  function elegirCuantos(n) {
    fijarCuantos(n);
    paintCuantos();
  }

  /// Una frase, no una ficha. El bloque ambar con el numero adentro y el rango debajo era
  /// mucho aparato para decir dos datos, y los NUMEROS de carton aca no le sirven a nadie:
  /// ella ya los despego en el paso anterior, y quien compra no los pide por numero.
  /// Lo que tiene que confirmar es cuantos y a quien.
  /// La revision, con la forma de la maqueta. Antes eran tres renglones de datos sueltos
  /// -- "1 cartón / para Fff / el 6" -- que se leian como un recibo. Lo que ella hace es
  /// poner a la PERSONA arriba, con su cara, y debajo lo que se lleva: la pregunta que se
  /// contesta antes de confirmar no es "cuantos" sino "a quien le estoy dando esto".
  function paintResumen() {
    const caja = $('resumenVenta');
    const nombre = $('buyer').value.trim();
    const marca = $('buyerNumber').value.trim();

    caja.textContent = '';

    // Se identifica de UNA de dos formas y la pantalla dice cual: con nombre va la inicial
    // y el nombre; sin nombre el grande es EL NUMERO, que es lo unico que lo identifica.
    const avatar = el('div', 'quien-avatar');

    if (nombre) {
      avatar.textContent = nombre.charAt(0).toUpperCase();
      caja.appendChild(avatar);
      caja.appendChild(el('div', 'quien-nombre', nombre));
      if (marca) caja.appendChild(el('div', 'quien-marca', marca));
    } else {
      avatar.appendChild(icono(
        ['M12 11.6a3.3 3.3 0 100-6.6 3.3 3.3 0 000 6.6',
         'M5.2 19.6c0-3.5 3.1-5.9 6.8-5.9s6.8 2.4 6.8 5.9'], 24, 1.9));
      caja.appendChild(avatar);
      caja.appendChild(el('div', 'quien-nombre marca', marca));
            caja.appendChild(el('div', 'quien-marca', Idioma.texto('colaborador.venta.lo-identifica-su-numero')));
    }

    // La flecha dice "recibe" sin escribirlo, y separa a la persona de lo que se lleva.
    const flecha = el('div', 'recibe-flecha');
    flecha.appendChild(icono(['M12 4v14', 'M6 13l6 6 6-6'], 22, 1.8));
    caja.appendChild(flecha);

    caja.appendChild(el('div', 'recibe-que',
            Idioma.con('colaborador.venta.recibe', { cuantos: Idioma.muchos(ui.picked.length, 'jugador.cartones.uno', 'jugador.cartones.varios') })));

    // Las fichas con el numero de cada carton: es el ultimo vistazo contra el taco, y de
    // paso es donde vive el dato que sacamos del paso de la cantidad. Hasta seis van una
    // por una, que es lo concreto; mas que eso seria un muro y el rango dice lo mismo.
    const fichas = el('div', 'fichas-carton');

    if (ui.picked.length <= 6) ui.picked.forEach((c) => fichas.appendChild(fichaCarton(String(c))));
    else fichas.appendChild(fichaCarton(comoSeDice(ui.picked)));

    caja.appendChild(fichas);
  }

  /// Una ficha de carton: el dibujo del carton y el numero, en pastilla.
  function fichaCarton(texto) {
    const ficha = el('span', 'ficha-pill');

    ficha.appendChild(icono(
      ['M6.4 6.5h11.2a2 2 0 012 2v7a2 2 0 01-2 2H6.4a2 2 0 01-2-2v-7a2 2 0 012-2z',
       'M14.6 6.5v11'], 13, 1.6));
    ficha.appendChild(el('span', null, texto));

    return ficha;
  }

  /// Para seguir hace falta saber a QUIEN: o su numero, o su nombre si es la primera vez.
  /// Cada camino pide LO SUYO: por numero, el numero; por primera vez, el nombre. El nombre
  /// no es opcional -- el dominio rechaza la venta con "falta el nombre del comprador".
  function ableSell() {
    const mark = $('buyerNumber').value.trim();
    const name = $('buyer').value.trim();

    $('aConfirmar').disabled = ui.primeraVez ? name.length === 0 : mark.length === 0;
  }

  /// Pasar y devolver van por CANTIDAD, igual que vender: con cien cartones en la bolsa,
  /// tocarlos uno por uno para devolver setenta no es un camino.
  function ableUndo() {
    const hay = taco().length;

    $('giveBack').disabled = hay === 0;
        $('giveBack').textContent = hay === 0
      ? Idioma.texto('colaborador.mis.no-tienes-cartones')
      : Idioma.con('colaborador.devolver.n', { cuantos: Idioma.muchos(hay, 'jugador.cartones.uno', 'jugador.cartones.varios') });

    $('handover').disabled = ui.pasar === 0 || $('colleague').value.trim().length === 0;
        $('handover').textContent = ui.pasar > 0
      ? Idioma.con('colaborador.pasar.n', { cuantos: Idioma.muchos(ui.pasar, 'jugador.cartones.uno', 'jugador.cartones.varios') })
      : Idioma.texto('colaborador.mis.pasar-cartones');

    // Sin taco no hay nada que pasar, y la pantalla lo dice en vez de dejarla probando.
    show($('sinTacoPasar'), hay === 0);
    show($('conTacoPasar'), hay > 0);
    show($('piePasar'), hay > 0);

    paintPasar();
  }

  /// Cuantos le pasa a una colega. Es EL MISMO contador de la venta -- menos / numero
  /// escribible / mas, con la escalera debajo -- y no el teclado de doce teclas que habia
  /// aca: la pregunta es la misma, y contestarla con dos aparatos distintos segun la
  /// pantalla era lo que hacia sentir esta parte como de otra app.
  function paintPasar() {
    const hay = taco().length;
    const campo = $('cuantosPasar');

    // El tope, antes de dibujar: si le vendieron mientras tenia el panel abierto, lo que
    // pensaba pasar se recorta solo a lo que le queda.
    if (ui.pasar > hay) ui.pasar = hay;

    const n = ui.pasar || 0;

    if (document.activeElement !== campo) campo.value = n > 0 ? String(n) : '';

    $('menosPasar').disabled = n <= 0;
    $('masPasar').disabled = n >= hay;

    const caja = $('atajosPasar');
    const caben = ATAJOS.filter((x) => x <= hay);

    show(caja, hay > 0);
    caja.textContent = '';

    caben.forEach((x) => {
      const b = el('button', 'cn cn-mas' + (n === x ? ' on' : ''), String(x));
      b.addEventListener('click', () => { ui.pasar = x; ableUndo(); });
      caja.appendChild(b);
    });

    // "Todos" al final. Pasarle TODO el taco a una colega es un caso entero -- se va de la
    // funcion, se le acaba la noche -- y con cien cartones no se llega tocando la flecha.
    if (hay > 0 && caben.indexOf(hay) < 0) {
            const todos = el('button', 'cn cn-mas' + (n === hay ? ' on' : ''), Idioma.texto('colaborador.pasar.todos'));
      todos.addEventListener('click', () => { ui.pasar = hay; ableUndo(); });
      caja.appendChild(todos);
    }

    // Los ULTIMOS del taco: asi las dos quedan con una tira seguida y pueden seguir
    // contando por rango.
    const suyos = losDeAbajo(n);
    show($('cualesPasar'), suyos.length > 0);
    $('cualesPasar').textContent = '';

    if (suyos.length > 0) {
            $('cualesPasar').appendChild(el('span', 'cuales-t', Idioma.texto('colaborador.pasar.vas-a-pasar')));
      $('cualesPasar').appendChild(el('div', 'cuales-r', comoSeDice(suyos)));
    }

    // Como queda ELLA. De la otra no se puede decir nada: su taco no viaja hasta aca.
    show($('saldoPasar'), n > 0);
        $('saldoPasar').textContent = Idioma.con('colaborador.pasar.te-quedarian-de', { n: hay - n, total: hay });
  }

  /// Los que le pasa: los ultimos de la bolsa.
  function losDeAbajo(cuantos) {
    const mios = taco();
    return mios.slice(Math.max(0, mios.length - cuantos));
  }

  function myName() { return $('name').value.trim(); }

  function pickedText() { return ui.picked.join(','); }

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
      return;
    }

    if (msg.kind === 'figures') {
      window.Figuras.learn(msg.names, msg.hints);
      if (ui.state) render();
      return;
    }

    // El registro corrido se fue de la pantalla, pero el canal NO se puede seguir tirando
    // entero: por aca viaja todo lo que el C# tiene para decir cuando algo sale mal.
    if (msg.kind === 'log') {
      // Siempre se destraba el boton. Cuando la venta rebota, el C# no manda retrato nuevo
      // -- y sin retrato no hay render, que es lo unico que lo desbloqueaba. Resultado:
      // #doSell se quedaba en 'Anotando…' PARA SIEMPRE, sin reintento y sin salida.
      desbloquearVenta();

      // Y lo que fallo se dice. Antes moria en silencio: la vendedora veia el boton volver
      // de 'Anotando…' a 'Anotar la venta' y tenia que adivinar por que.
      if (msg.bad && msg.last) toast(msg.last);
      return;
    }

    // El codigo de la venta se GUARDA, no solo se muestra. Antes, una vez cerrada la
    // capa no habia forma de volver a ensenarlo NUNCA: si la persona no alcanzo a
    // escanear, ese carton se quedaba sin telefono y no habia vuelta atras.
    if (msg.kind === 'code' && msg.mark) ui.codigos[msg.mark] = msg.text;

    if (msg.kind === 'toast') { toast(msg.text); return; }

    if (msg.kind === 'known') {
      const known = $('buyerKnown');
      show(known, !!msg.name);
            known.textContent = msg.name ? Idioma.con('colaborador.venta.ya-le-vendiste', { nombre: msg.name }) : '';
      if (msg.name && $('buyer').value.trim().length === 0) {
        $('buyer').value = msg.name;
        ableSell();
      }
      return;
    }

    if (msg.kind === 'code') {
      abrirCodigo(msg.forWhom, ui.enVenta || 0, msg.png, msg.text);
      return;
    }

    if (msg.kind === 'needname') {
      // Red de seguridad: el campo del nombre esta escondido porque el codigo lo trae --
      // el organizador esta obligado a escribirlo al anotar. Si aun asi llega uno sin
      // nombre, recien aca aparece el campo, y se dice por que.
      //
      // Y se pide volver a entrar, no solo escribir: el codigo escaneado no se guardo, asi
      // que con la camara hay que apuntarla de nuevo. Pegado a mano sigue en su casilla.
      show($('nameWrap'), true);
            $('nameHint').textContent = Idioma.texto('colaborador.puerta.codigo-sin-nombre');
      $('name').focus();
      toast(Idioma.texto('colaborador.puerta.escribe-y-vuelve'));
      return;
    }

    if (msg.kind === 'scanned') {
      // Lo que volvio de la camara, segun a que se la mando.
      if (msg.what === 'buyer') { $('buyerNumber').value = msg.text; askWhoIs(); }
      if (msg.what === 'colleague') $('colleague').value = msg.text;
      if (msg.what === 'code') {
        $('code').value = msg.text;
        show($('byHandBox'), true);
        $('byHandChevron').classList.add('open');
      }

      // El codigo venia cortado a nombre de alguien. No se pisa lo que ella ya
      // escribio: si se tomo el trabajo de escribir su nombre, ese vale.
      if (msg.what === 'name' && $('name').value.trim().length === 0) {
        $('name').value = msg.text;
      }

      // Y la pista dice la verdad. Antes prometia "con ese nombre te va a ver el
      // organizador" incluso con el nombre puesto por el, cuando en su lista mandaba
      // el suyo. Ahora manda el de quien entra, asi que la promesa se cumple.
      if (msg.what === 'name' && (msg.text || '').length > 0) {
                $('nameHint').textContent = Idioma.con('colaborador.puerta.codigo-cortado-para', { nombre: msg.text });
      }
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
    toastTimer = setTimeout(() => show(box, false), 2200);
  }

  function fold(button, box, chevron) {
    button.addEventListener('click', () => {
      const open = box.classList.contains('hidden');
      show(box, open);
      chevron.classList.toggle('open', open);
    });
  }

  fold($('byHandFold'), $('byHandBox'), $('byHandChevron'));

  // ── los pasos de la venta ────────────────────────────────────────────────

  $('verTodos').addEventListener('click', () => {
    const box = $('cardChips');
    const abierto = box.classList.contains('hidden');
    show(box, abierto);
    show($('leyendaTaco'), abierto);
        $('verTodos').textContent = Idioma.texto(abierto ? 'colaborador.taco.ocultar' : 'colaborador.taco.ver-todos');
  });

  // ── mis cartones: el menu y sus tres pantallas ───────────────────────────

  const PANELES = ['panelNumero', 'panelPasar', 'panelDevolver'];

  const PANEL_TITULO = {
        panelNumero: 'colaborador.numero.titulo',
    panelPasar: 'colaborador.mis.pasar-cartones',
    panelDevolver: 'detalle.repartir.devolver',
  };

  /// Cada opcion se abre ENCIMA, con su cabecera y su flecha, igual que la venta. El menu
  /// se queda dibujado debajo -- ya no hace falta esconderlo -- y la salida es la flecha de
  /// arriba, que es donde la gente la busca.
  function verPanel(cual) {
    show($('panelFull'), !!cual);
    PANELES.forEach((p) => show($(p), p === cual));

    if (!cual) return;

    $('panelTitulo').textContent = Idioma.texto(PANEL_TITULO[cual]);
    $('panelDonde').textContent = ui.state ? ui.state.festivalName : '';
  }

  document.querySelectorAll('.opcion').forEach((b) => {
    b.addEventListener('click', () => verPanel(b.dataset.panel));
  });

  $('panelAtras').addEventListener('click', () => verPanel(null));

  $('menosUno').addEventListener('click', () => elegirCuantos(Math.max(1, ui.cuantos - 1)));

  $('masUno').addEventListener('click', () =>
    elegirCuantos(Math.min(taco().length, ui.cuantos + 1)));

  /// Lo escrito se recorta EN VIVO contra la bolsa: si escribe 90 y tiene 78, el campo se
  /// planta en 78 y ella lo ve mientras escribe, en vez de enterarse al tocar Siguiente.
  /// Es el mismo tope que aplicaba el teclado viejo, en el aparato nuevo.
  $('cuantosVan').addEventListener('input', () => {
    const hay = taco().length;
    const limpio = $('cuantosVan').value.replace(/\D/g, '').slice(0, 3);
    let n = parseInt(limpio, 10);

    if (isNaN(n) || n < 0) n = 0;
    if (n > hay) n = hay;

    $('cuantosVan').value = n > 0 ? String(n) : '';
    elegirCuantos(n);
  });

  /// Al salir del campo vacio vuelve a uno: dejar la venta en cero cartones no es un
  /// estado en el que sirva quedarse.
  $('cuantosVan').addEventListener('blur', () => {
    if (ui.cuantos <= 0 && taco().length > 0) elegirCuantos(1);
  });

  $('abrirVenta').addEventListener('click', abrirLaVenta);

  $('soldFind').addEventListener('input', paintSold);

  /// La flecha retrocede UN paso; desde el primero cierra la venta y devuelve las
  /// pestanias. Es la regla de la maqueta.
  $('ventaAtras').addEventListener('click', () => {
    const donde = PASOS.indexOf(ui.paso);

    if (donde <= 0) { cerrarLaVenta(); return; }

    irAlPaso(PASOS[donde - 1]);
  });

  $('aQuien').addEventListener('click', () => irAlPaso('stepJugador'));

  /// Los dos modos NO conviven: o se identifica por numero, o se le acuña uno. Tenerlos
  /// juntos era lo que hacia que la pantalla se leyera como formulario -- cinco piezas
  /// apiladas de las que solo una servia en cada venta.
  function modoDeCompra(primeraVez) {
    ui.primeraVez = primeraVez;
    show($('modoNumero'), !primeraVez);
    show($('cajaNueva'), primeraVez);
  }

  $('sinNumero').addEventListener('click', () => {
    modoDeCompra(true);

    // El numero deja de contar apenas se cambia de modo: si no, un numero a medio escribir
    // seguiria habilitando el boton de seguir desde el otro modo.
    $('buyerNumber').value = '';
    show($('buyerKnown'), false);

    $('buyer').focus();
    ableSell();
    paintPasos();
  });

  $('volverNumero').addEventListener('click', () => {
    modoDeCompra(false);

    $('buyer').value = '';
    $('contact').value = '';

    $('buyerNumber').focus();
    ableSell();
    paintPasos();
  });

  $('aConfirmar').addEventListener('click', () => irAlPaso('stepConfirmar'));

  /// 'mejor no' ABANDONA la venta entera, no vuelve al primer paso: para volver un paso
  /// esta la flecha. Si no, desde el ultimo paso no habia forma de salirse.
  $('cancelarVenta').addEventListener('click', () => { ventaDesdeCero(); cerrarLaVenta(); });

  // la puerta
  // Sin exigir el nombre: el codigo lo trae. Pedirlo antes de escanear era pedirle a
  // la persona lo que estaba a punto de recibir, y encima trababa la camara. Si el
  // codigo resulta anonimo, el telefono contesta 'needname' y ahi si se pide.
  $('scan').addEventListener('click', () => send('scan|' + myName()));

  $('byHand').addEventListener('click', () => {
        if ($('code').value.trim().length === 0) { toast(Idioma.texto('colaborador.puerta.pega-el-codigo')); return; }
    send('enter|' + myName() + '|' + $('code').value.trim());
  });

  // ── el ensayo ────────────────────────────────────────────────────────────
  //
  // El oficio de verdad viene en el codigo y no se elige; el del ensayo SI, porque el
  // papelito se lo corta uno mismo. De ahi en adelante no hay nada nuestro: el telefono
  // devuelve un codigo y se entra por la puerta de siempre.

  let ensOficio = 'vende';

  function pintarEnsayo() {
    $('ensVende').classList.toggle('va', ensOficio === 'vende');
    $('ensCanta').classList.toggle('vc', ensOficio === 'canta');

        $('ensNote').textContent = Idioma.texto(ensOficio === 'vende' ? 'colaborador.ensayo.nota-vende' : 'colaborador.ensayo.nota-canta');
  }

  function hojaEnsayo(abierta) {
    show($('ensDim'), abierta);
    show($('ensSheet'), abierta);
    if (abierta) pintarEnsayo();
  }

  function hojaTerminar(abierta) {
    show($('ensDim'), abierta);
    show($('ensConfirm'), abierta);
  }

  $('ensQuiero').addEventListener('click', () => hojaEnsayo(true));

  $('ensVende').addEventListener('click', () => { ensOficio = 'vende'; pintarEnsayo(); });
  $('ensCanta').addEventListener('click', () => { ensOficio = 'canta'; pintarEnsayo(); });

  $('ensGo').addEventListener('click', () => {
    hojaEnsayo(false);
        toast(Idioma.texto('colaborador.ensayo.armando'));
    send('ensayo|' + ensOficio);
  });

  $('ensFin').addEventListener('click', () => hojaTerminar(true));

  $('ensBorrar').addEventListener('click', () => {
    hojaTerminar(false);
    send('ensayoFin');
  });

  $('ensSeguir').addEventListener('click', () => hojaTerminar(false));

  // El fondo oscuro cierra lo que este arriba, como en cualquier hoja.
  $('ensDim').addEventListener('click', () => { hojaEnsayo(false); hojaTerminar(false); });

  // vender
  let askTimer = null;

  function askWhoIs() {
    const mark = $('buyerNumber').value.trim();
    show($('buyerKnown'), false);
    if (mark.length >= 8) send('whois|' + mark);
  }

  $('buyerNumber').addEventListener('input', () => {
    ableSell();
    paintPasos();
    clearTimeout(askTimer);
    askTimer = setTimeout(askWhoIs, 400);
  });

  $('buyer').addEventListener('input', () => { ableSell(); paintPasos(); });

  $('soldFind').addEventListener('input', paintSold);

  $('scanBuyer').addEventListener('click', () => send('scanbuyer'));

  // El boton se apaga mientras el mensaje viaja. Antes no se apagaba nada y la pantalla
  // quedaba identica: la conducta obvia era volver a tocar, y eso era vender dos veces.
  $('doSell').addEventListener('click', () => {
    if ($('doSell').disabled) return;

    $('doSell').disabled = true;
    $('doSell').textContent = Idioma.texto('colaborador.venta.confirmando');

    // Cuantos van en ESTA venta, guardado ahora. Para cuando vuelve el codigo, el retrato
    // nuevo ya paso por paintCustody, que saca de ui.picked los cartones que dejaron de
    // estar en su custodia -- o sea justo los que se acaba de vender. Leerlo despues daba
    // siempre cero.
    ui.enVenta = ui.picked.length;

    send('sell|' + [$('buyerNumber').value.trim(), $('buyer').value.trim(),
      $('contact').value.trim(), pickedText()].join('|'));
  });

  /// La capa del codigo. El QR es lo unico que de verdad importa aca: es lo que la persona
  /// escanea para que le aparezcan sus cartones. Todo lo demas -- el sello, el nombre, la
  /// cuenta -- esta para que ella confirme de un vistazo que le esta mostrando el codigo
  /// correcto a la persona correcta.
  function abrirCodigo(quien, cuantos, png, texto) {
        $('forWhom').textContent = quien || Idioma.texto('colaborador.codigo.la-persona');

    $('codeQue').textContent = cuantos > 0 ? Idioma.muchos(cuantos, 'jugador.cartones.uno', 'jugador.cartones.varios') : '';
    show($('codeQue'), cuantos > 0);

    $('codeQr').src = png ? 'data:image/png;base64,' + png : '';
    show($('codeQr'), !!png);

    $('codeText').textContent = texto || '';
    show($('codeLayer'), true);
  }

  /// Vuelve a dejarlo tocable. Lo llama el retrato que baja del C#: si la venta entro,
  /// la capa del codigo se abre sola y de ahi sale por 'listo, siguiente persona'; si
  /// rebota, ella tiene que poder intentar de nuevo.
  function desbloquearVenta() {
    // La misma regla que paintSolo, y por la misma razon: sin organizador la venta se anota,
    // asi que el boton sigue sirviendo. Se traba solo cuando no hay donde anotarla -- ahi si
    // volveria a prometer algo que no puede cumplir, y justo despues de que ella le dijo un
    // precio a alguien.
    const solo = !!(ui.state && ui.state.solo);
    const puedeSola = !!(ui.state && ui.state.canSellAlone);

    $('doSell').disabled = solo && !puedeSola;
        $('doSell').textContent = Idioma.texto(solo && !puedeSola ? 'colaborador.venta.falta-el-organizador' : 'colaborador.venta.confirmar');
  }

  // La capa del codigo se cierra SOLO por aca, y aca es el unico lugar que limpia el
  // nombre del comprador: cualquier otro camino lo arrastraria a la venta siguiente.
  $('next').addEventListener('click', () => {
    show($('codeLayer'), false);
    send('codeclosed');
    desbloquearVenta();
    ventaDesdeCero();
    cerrarLaVenta();
    $('buyerNumber').value = '';
    $('buyer').value = '';
    $('contact').value = '';
    show($('buyerKnown'), false);
    ui.picked = [];
    paintCustody();
    ableSell();
    ableUndo();
  });

  $('copyCode').addEventListener('click', () => send('copy|' + $('codeText').textContent));

  // vendidos
  $('exchange').addEventListener('click', () => {
    if (ui.pickedSold === 0 || ui.picked.length !== 1) return;
    send('exchange|' + ui.pickedSold + '|' + ui.picked[0]);
  });

  $('plead').addEventListener('click', () => {
    if (ui.pickedSold === 0) return;
    send('plead|' + ui.pickedSold);
  });

  // lo demas
  $('cerrarVenta').addEventListener('click', () => show($('ventaLayer'), false));

  /// Corregir una venta vive detras de una puerta: pasa casi nunca, y tenerlo a la vista
  /// junto a lo de todos los dias invita a tocarlo por curiosidad.
  $('algoMal').addEventListener('click', () => {
    show($('quePaso'), true);
    show($('algoMal'), false);
  });

  /// Volver a ensenarle el codigo a alguien que ya compro. Sale de lo guardado en esta
  /// sesion, asi que no hace falta tocar el C#.
  $('verCodigo').addEventListener('click', () => {
    const v = ui.ventaAbierta;
    if (!v || !ui.codigos[v.mark]) return;

    show($('ventaLayer'), false);

    // Sin PNG: de esta venta guardamos el texto del codigo, no la imagen. Se manda '' a
    // proposito para que el QR se ESCONDA -- dejarlo con la imagen anterior le pondria
    // enfrente el codigo de otro comprador, y alguien se llevaria cartones ajenos.
    abrirCodigo(v.who, v.cards.length, '', ui.codigos[v.mark]);
  });

  $('colleague').addEventListener('input', ableUndo);

  $('scanColleague').addEventListener('click', () => send('scancolleague'));

  $('menosPasar').addEventListener('click', () => {
    ui.pasar = Math.max(0, (ui.pasar || 0) - 1);
    ableUndo();
  });

  $('masPasar').addEventListener('click', () => {
    ui.pasar = Math.min(taco().length, (ui.pasar || 0) + 1);
    ableUndo();
  });

  /// Recortado en vivo contra la bolsa, igual que en la venta.
  $('cuantosPasar').addEventListener('input', () => {
    const hay = taco().length;
    const limpio = $('cuantosPasar').value.replace(/\D/g, '').slice(0, 3);
    let n = parseInt(limpio, 10);

    if (isNaN(n) || n < 0) n = 0;
    if (n > hay) n = hay;

    $('cuantosPasar').value = n > 0 ? String(n) : '';
    ui.pasar = n;
    ableUndo();
  });

  $('handover').addEventListener('click', () => {
    const to = parseInt($('colleague').value, 10);
        if (!Number.isFinite(to) || to <= 0) { toast(Idioma.texto('colaborador.pasar.ingresa-el-numero')); return; }

    send('handover|' + to + '|' + losDeAbajo(ui.pasar).join(','));

    // Se limpia el panel al mandar. Antes quedaba el numero anterior escrito y el rango de
    // abajo recalculado sobre el taco NUEVO: dos datos que ya no eran de nada.
    ui.pasar = 0;
    $('colleague').value = '';
    ableUndo();
  });

  $('giveBack').addEventListener('click', () => send('giveback|' + taco().join(',')));

  $('another').addEventListener('click', () => send('another'));

  $('back').addEventListener('click', () => send('another'));

  window.Cantando.start(send);

  document.querySelectorAll('.htab').forEach((tab) => {
    tab.addEventListener('click', () => goTab(tab.dataset.go));
  });

  // El teclado tapa el campo que se esta llenando: adentro de un WebView de Android
  // hay que subirlo a mano.
  document.querySelectorAll('input').forEach((field) => {
    field.addEventListener('focus', () => {
      setTimeout(() => field.scrollIntoView({ block: 'center', behavior: 'smooth' }), 260);
    });
  });

  window.__test = (msg) => onMessage(JSON.stringify(msg));


  send('ready');
})();
