using System.Diagnostics;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    // Lo que hace la pantalla del organizador en CADA repintada: por cada carton y por cada
    // figura, preguntarle al motor que numeros le faltan. Con treinta cartones y dos figuras
    // son sesenta caras; con doscientos son cuatrocientas. Y repinta cada vez que cae una
    // bolita, que es cuando mas ocupada esta.
    //
    // Este test no mide el motor: mide lo que la pantalla le pide al motor. Medido en escritorio:
    // 1,7 ms sin el cache de caras y 0,3 ms con el. El umbral de abajo es un TECHO contra una
    // regresion --un telefono es bastante mas lento que esto--, no una meta.
    //
    // Y que quede dicho: esto NO explica los cuadros de seis segundos que vimos en una velada.
    // Ese sigue sin causa probada. Esta pieza costaba y se arreglo; no se arreglo por eso.
    // Esta aca porque es
    // donde estan las piezas, y porque un numero medido envejece mejor que una sospecha.
    [TestClass]
    public class CardfaceCacheTests
    {
        [TestMethod]
        [TestCategory("Repintada")]
        public void RepaintingTwoHundredCards_TakesNoTimeAtAll()
        {
            int[] drawn = { 7, 21, 33, 53, 68, 15, 73 };

            // La primera vuelta paga el calculo de las doscientas caras, que es lo que en una
            // velada pasa una sola vez.
            for (long card = 1; card <= 200; card++)
            {
                BingoGame.ClosestFor(card, "Row0", drawn);
                BingoGame.ClosestFor(card, "FourCorners", drawn);
            }

            Stopwatch clock = Stopwatch.StartNew();

            for (int repaint = 0; repaint < 10; repaint++)
            {
                for (long card = 1; card <= 200; card++)
                {
                    BingoGame.ClosestFor(card, "Row0", drawn);
                    BingoGame.ClosestFor(card, "FourCorners", drawn);
                }
            }

            clock.Stop();

            double perRepaint = clock.Elapsed.TotalMilliseconds / 10;

            Assert.IsTrue(perRepaint < 50,
                "una repintada de doscientos cartones tardo " + perRepaint.ToString("F1")
                + " ms. Esto corre cada vez que cae una bolita: si tarda, la app se traba justo "
                + "cuando mas pasa.");
        }
    }
}
