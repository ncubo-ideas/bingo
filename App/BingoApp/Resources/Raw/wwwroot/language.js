/* Lo que la pagina dice, en el idioma de la persona.

   El idioma NO se resuelve aca: lo resuelve el telefono (AppLanguage.cs) una sola vez y se lo
   manda a la pagina por el puente, en el 'ready', como un mensaje `textos` con el catalogo
   plano del idioma vigente. Dos resolvedores serian dos verdades sobre lo mismo -- la
   pagina en ingles y los toasts del C# en espanol -- y eso es lo que este archivo existe
   para evitar.

   Mientras el catalogo no llega, texto() devuelve la clave entre llaves y el marcado
   conserva el espanol que trae escrito: un hueco tiene que verse feo, no en blanco.

   Aca viven tambien las cosas que no son una frase pero cambian con el idioma: como se
   escribe un mes, un dia, una hora, un plural. Antes cada pagina tenia su copia de MONTHS
   y de many(), y dos copias son dos maneras de decir lo mismo. */

window.Idioma = (function () {
  'use strict';

  const MESES = {
    es: ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio',
      'agosto', 'setiembre', 'octubre', 'noviembre', 'diciembre'],
    en: ['January', 'February', 'March', 'April', 'May', 'June', 'July',
      'August', 'September', 'October', 'November', 'December'],
  };

  const MESES_CORTOS = {
    es: ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'set', 'oct', 'nov', 'dic'],
    en: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'],
  };

  const DIAS = {
    es: ['dom', 'lun', 'mar', 'mié', 'jue', 'vie', 'sáb'],
    en: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'],
  };

  const DIAS_LARGOS = {
    es: ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'],
    en: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
  };

  let etiqueta = 'es';
  let catalogo = {};
  const oyentes = [];

  const velo = document.createElement('style');
  velo.textContent = 'html.sin-textos body > * { visibility: hidden; }';
  document.head.appendChild(velo);
  document.documentElement.classList.add('sin-textos');
  setTimeout(() => document.documentElement.classList.remove('sin-textos'), 1500);

  /// Lo que el telefono manda en el mensaje 'textos'. Se puede llamar mas de una vez:
  /// cuando la persona cambia el idioma con el selector llega otro catalogo.
  function aprender(msg) {
    etiqueta = (msg && msg.idioma === 'en') ? 'en' : 'es';
    catalogo = (msg && msg.textos) || {};
    document.documentElement.lang = etiqueta;
    if (msg && msg.arriba > 0) document.documentElement.style.setProperty('--arriba', msg.arriba + 'px');
    pintarLoEstatico();
    document.documentElement.classList.remove('sin-textos');
    oyentes.forEach((cb) => { try { cb(etiqueta); } catch (e) { /* una pagina que falla no calla a las demas */ } });
  }

  /// Quien quiera repintar lo dinamico cuando cambie el idioma se anota aca.
  function alCambiar(cb) { if (typeof cb === 'function') oyentes.push(cb); }

  function llego() { return Object.keys(catalogo).length > 0; }

  /// Una frase entera por su clave. Si falta, la clave entre llaves: se ve, que es lo
  /// que se quiere.
  function texto(clave) {
    const dicho = catalogo[clave];
    return typeof dicho === 'string' && dicho.length > 0 ? dicho : '{' + clave + '}';
  }

  /// La misma frase con sus huecos llenos. Los huecos van por nombre, nunca por
  /// posicion: el orden de las palabras cambia entre los dos idiomas.
  function con(clave, huecos) {
    let dicho = texto(clave);
    Object.keys(huecos || {}).forEach((hueco) => {
      dicho = dicho.split('{' + hueco + '}').join(String(huecos[hueco]));
    });
    return dicho;
  }

  /// Singular o plural con la cuenta adelante: "1 cartón" / "3 cartones". Son dos
  /// claves, no un sufijo; la cuenta va en el hueco {n}.
  function muchos(n, claveUno, claveVarios) {
    return con(n === 1 ? claveUno : claveVarios, { n: n });
  }

  /// Repinta lo que esta escrito en el marcado: un nodo con data-texto cambia su texto;
  /// data-texto-ph cambia el placeholder; data-texto-aria, el aria-label.
  function pintarLoEstatico() {
    if (!llego()) return;

    document.querySelectorAll('[data-texto]').forEach((el) => {
      el.textContent = texto(el.getAttribute('data-texto'));
    });
    document.querySelectorAll('[data-texto-ph]').forEach((el) => {
      el.placeholder = texto(el.getAttribute('data-texto-ph'));
    });
    document.querySelectorAll('[data-texto-aria]').forEach((el) => {
      el.setAttribute('aria-label', texto(el.getAttribute('data-texto-aria')));
    });
  }

  // ── lo que no es una frase pero cambia con el idioma ─────────────────────

  function mes(i) { return (MESES[etiqueta] || MESES.es)[i] || ''; }

  function mesCorto(i) { return (MESES_CORTOS[etiqueta] || MESES_CORTOS.es)[i] || ''; }

  function diaSemana(i) { return (DIAS[etiqueta] || DIAS.es)[i] || ''; }

  function diaSemanaLargo(i) { return (DIAS_LARGOS[etiqueta] || DIAS_LARGOS.es)[i] || ''; }

  /// "5 de agosto" / "August 5".
  function fecha(day) {
    if (!(day instanceof Date)) return '';
    return etiqueta === 'en'
      ? mes(day.getMonth()) + ' ' + day.getDate()
      : day.getDate() + ' de ' + mes(day.getMonth());
  }

  /// "sáb 5 de agosto" / "Sat, August 5".
  function fechaConDia(day) {
    if (!(day instanceof Date)) return '';
    return etiqueta === 'en'
      ? diaSemana(day.getDay()) + ', ' + fecha(day)
      : diaSemana(day.getDay()) + ' ' + fecha(day);
  }

  /// "sábado 5 de agosto" / "Saturday, August 5".
  function fechaLarga(day) {
    if (!(day instanceof Date)) return '';
    return etiqueta === 'en'
      ? diaSemanaLargo(day.getDay()) + ', ' + fecha(day)
      : diaSemanaLargo(day.getDay()) + ' ' + fecha(day);
  }

  /// La hora como la dice la gente: "7:30 pm" en los dos idiomas. Las doce del dia
  /// son "12:00 pm" y la medianoche "12:00 am".
  function hora(h, m) {
    const mitad = h < 12 ? 'am' : 'pm';
    const doce = h % 12 === 0 ? 12 : h % 12;
    return doce + ':' + String(m).padStart(2, '0') + ' ' + mitad;
  }

  return {
    aprender: aprender,
    alCambiar: alCambiar,
    llego: llego,
    get etiqueta() { return etiqueta; },
    texto: texto,
    con: con,
    muchos: muchos,
    pintarLoEstatico: pintarLoEstatico,
    mes: mes,
    mesCorto: mesCorto,
    diaSemana: diaSemana,
    diaSemanaLargo: diaSemanaLargo,
    fecha: fecha,
    fechaConDia: fechaConDia,
    fechaLarga: fechaLarga,
    hora: hora,
  };
})();
