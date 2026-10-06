/* El dibujito de cada figura, y como se dice.

   El DIBUJO vive aca: son las casillas que hay que tapar en un carton de 5x5, y eso es
   cosa de la pagina. Es un retrato, no la regla -- la regla la valida el motor.

   El NOMBRE no vive aca: lo manda el telefono (OnOffer.cs) por el puente, porque el
   telefono tambien arma frases con esos nombres para las cabeceras. Tenerlos en los dos
   lados significa que un dia una pantalla dice una cosa y otra dice otra. Mientras no
   hayan llegado, o si es una figura que no conocemos, sale el nombre del motor: es feo,
   pero es cierto. */

window.Figuras = (function () {
  'use strict';

  const CELLS = {
    AnyLine: [10, 11, 12, 13, 14],
    AnyRow: [10, 11, 12, 13, 14],
    AnyColumn: [2, 7, 12, 17, 22],
    Diagonal: [0, 6, 12, 18, 24],
    FourCorners: [0, 4, 20, 24],
    AnySquare: [6, 7, 11, 12],
    L: [0, 5, 10, 15, 20, 21, 22, 23, 24],
    FullCard: Array.from({ length: 25 }, (_, i) => i),
  };

  const said = {};
  const told = {};

  /// Lo que el telefono manda en el mensaje 'figures'.
  function learn(names, hints) {
    Object.keys(names || {}).forEach((one) => { said[one] = names[one]; });
    Object.keys(hints || {}).forEach((one) => { told[one] = hints[one]; });
  }

  function name(figure) { return said[figure] || figure; }

  function says(figure) { return told[figure] || ''; }

  /// Que casillas tapa esta figura. Vacio si no la conocemos: mejor un dibujo apagado que
  /// uno que miente.
  ///
  /// Va aparte de grid() porque las pantallas no dibujan igual: las del organizador arman
  /// una grilla de <i> y la del jugador pinta <span> adentro de una que ya existe, y cada
  /// una tiene su CSS. Lo que se comparte es el DATO, que es donde estaba el error.
  function cells(figure) { return CELLS[figure] || []; }

  /// Las 25 casillas, con las de la figura encendidas.
  function grid(figure, cls) {
    const box = document.createElement('div');
    box.className = cls || 'pick-grid';

    const lit = cells(figure);

    for (let i = 0; i < 25; i++) {
      const cell = document.createElement('i');
      if (lit.includes(i)) cell.className = 'lit';
      box.appendChild(cell);
    }

    return box;
  }

  return { learn: learn, name: name, says: says, grid: grid, cells: cells };
})();
