/* La tombola: las 75 casillas y los colores de banda.

   De aca NO sale una bolita nueva: eso se teclea, y el teclado pide un INGRESAR de mas
   antes de mandarla. Antes el cuadro se tocaba y el toque ERA el canto -- un dedo torcido
   cantaba un numero que ya habia viajado al carton de todos, y ademas obligaba a BUSCAR
   el numero entre 75 casillas, que es lo lento de leer una bolita fisica y encontrarla
   en la pantalla.

   Lo que si se toca es una que YA SALIO, y es para corregirla: se puede corregir
   cualquiera de la ronda y no solo la ultima, asi que hace falta un lugar donde
   senalarla, y el lugar natural es el cuadro donde se ven todas.

   La usan las DOS pantallas que cantan, y vive aca porque dos copias significan que un
   dia una de las dos pinta la bolita de otro color.

   Y el color importa: es EL MISMO que se le enciende al jugador en su carton. El "I 27"
   que uno grita tiene que coincidir con lo que el otro ve. */

window.Tombola = (function () {
  'use strict';

  const BANDS = ['#FF6B6B', '#F5A623', '#50C878', '#4DA3FF', '#9B7BFF'];
  const LETTERS = 'BINGO';

  function bandOf(n) { return Math.min(4, Math.max(0, Math.floor((n - 1) / 15))); }

  function letterOf(n) { return LETTERS[bandOf(n)]; }

  function colorOf(n) { return BANDS[bandOf(n)]; }

  /// Arma las 75 casillas dentro de `box` y devuelve el indice numero -> casilla, que es
  /// lo que hace falta despues para pintarlas sin volver a buscarlas en el arbol.
  ///
  /// Se arma POR FILAS y no por columnas, para que el grid de 5 deje cada renglon con su
  /// B I N G O: 1 16 31 46 61 / 2 17 32 47 62 / ...
  ///
  /// `whenPicked` se llama al tocar una que YA SALIO, y es para corregirla. Las que no
  /// salieron quedan apagadas de verdad (disabled), asi que de este cuadro no sale nunca
  /// una bolita nueva: la bolita se teclea. Lo unico que se puede hacer aca es senalar
  /// una que esta mal.
  function build(box, whenPicked) {
    const keys = {};

    box.textContent = '';

    for (let row = 0; row < 15; row++) {
      for (let band = 0; band < 5; band++) {
        const number = band * 15 + row + 1;

        const key = document.createElement('button');
        key.className = 'key';
        key.textContent = String(number);
        key.disabled = true;

        if (whenPicked) key.addEventListener('click', () => whenPicked(number));

        keys[number] = key;
        box.appendChild(key);
      }
    }

    return keys;
  }

  /// Las que ya salieron quedan del color de su banda; la ultima, con su aro. Y solo esas
  /// se pueden tocar: las que no salieron no tienen nada que corregir.
  function paint(keys, drawn, last, pickable) {
    const out = new Set(drawn || []);

    for (let n = 1; n <= 75; n++) {
      const key = keys[n];
      if (!key) continue;

      const isOut = out.has(n);

      key.className = 'key' + (isOut ? ' out' : '') + (n === last ? ' last' : '');
      key.style.background = isOut ? colorOf(n) : '';
      key.style.color = isOut ? '#fff' : '';
      key.disabled = !isOut || !pickable;
    }
  }

  return {
    bandOf: bandOf,
    letterOf: letterOf,
    colorOf: colorOf,
    build: build,
    paint: paint,
  };
})();
