using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    // Dos cartones completan LA MISMA figura CON LA MISMA BOLITA. En una parroquia con
    // doscientos cartones y una figura corta como una fila eso es lo normal, no un caso de
    // borde: sale el 68, gritan dos, y las dos tienen razon.
    //
    // El pueblo sabe repartir -- una Promise admite varios ganadores y divide el efectivo --
    // asi que la pregunta es si la MESA les da turno a los dos. La respuesta depende de la
    // politica con la que arranco la ronda, y por eso hay dos tests con la MISMA escena.
    //
    // Ojo con lo que estos tests NO dicen: si uno completa cinco bolitas despues del otro, que
    // pierda esta bien. Eso no es injusticia, es haber llegado tarde. Lo que se mide aca es el
    // empate.
    [TestClass]
    public class TwoWinnersTests
    {
        private static int[] Needs(long card, string figure)
        {
            Cardface face = Cardface.Of(new CardNumber(card));
            return WinPatterns.Catalog.ByName(figure).Alternatives[0]
                .Where(cell => !cell.IsFreeCenter)
                .Select(cell => face.NumberAt(cell))
                .ToArray();
        }

        // Dos cartones cuyas filas COMPARTEN un numero, y ese numero compartido. Se buscan en
        // vez de fijarlos a mano porque la cara de un carton sale de su numero: fijar un par
        // seria fijar tambien el algoritmo que las genera.
        private static (long One, long Other, int Shared) TwoThatTieOn(string figure)
        {
            for (long a = 1; a <= 60; a++)
            {
                HashSet<int> mine = new HashSet<int>(Needs(a, figure));

                for (long b = a + 1; b <= 60; b++)
                {
                    foreach (int number in Needs(b, figure))
                        if (mine.Contains(number)) return (a, b, number);
                }
            }

            Assert.Inconclusive("no encontre dos cartones que compartan un numero en " + figure);
            return default;
        }

        // Todo lo que las dos filas necesitan MENOS el numero compartido, que se canta al final
        // para que las dos se completen con la misma bolita.
        private static List<int> EverythingButTheTie(long one, long other, string figure, int shared)
        {
            HashSet<int> all = new HashSet<int>(Needs(one, figure));
            foreach (int number in Needs(other, figure)) all.Add(number);
            all.Remove(shared);
            return all.ToList();
        }

        private static BingoGame Arrange(string how, string figure, out long one, out long other)
        {
            (one, other, int shared) = TwoThatTieOn(figure);

            BingoGame game = new BingoGame();
            game.PutCardInPlay(one);
            game.PutCardInPlay(other);
            game.StartRound(new[] { figure }, how);

            foreach (int number in EverythingButTheTie(one, other, figure, shared))
                game.RegisterDraw(number);

            game.RegisterDraw(shared);

            return game;
        }

        // El motor NO tiene la culpa: si los dos cantan ANTES de que el padre entregue, los
        // atiende a los dos. Este test lo deja escrito para que nadie salga a arreglar el
        // motor por este sintoma.
        [TestMethod]
        [TestCategory("DosGanadores")]
        public void OnClaim_IfBothClaimBeforeThePrizeIsGiven_TheEngineServesBoth()
        {
            BingoGame game = Arrange("OnClaim", "Row0", out long one, out long other);

            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(one), game.FigureInPlay("Row0")));
            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(other), game.FigureInPlay("Row0")));

            game.ValidateWinner(game.CardInPlay(one), game.FigureInPlay("Row0"));

            Assert.IsFalse(game.IsFigureClosed("Row0"),
                "con los dos cantados, entregarle al primero NO cierra la figura");

            game.ValidateWinner(game.CardInPlay(other), game.FigureInPlay("Row0"));

            Assert.IsTrue(game.IsFigureClosed("Row0"));
        }

        // Y este es el bug de verdad, que es NUESTRO y no del motor.
        //
        // En la app nadie canta: el organizador ve el letrero y toca un boton, y ese boton
        // manda 'ClaimBingo(carton); ValidateWinner(carton);' EN EL MISMO COMANDO. Asi el
        // carton entra y sale de la lista de pendientes en el mismo instante, la lista nunca
        // llega a tener dos, y ResolvePauseIfDone cierra al verla vacia.
        //
        // El segundo, que empato con la misma bolita, deja de poder ganar sin haber tenido
        // turno. Y el letrero lo elige con FirstOrDefault, asi que tampoco se vio nunca.
        [TestMethod]
        [TestCategory("DosGanadores")]
        public void OnClaim_TheWayTheAppUsesIt_TheSecondTiedCardCanNoLongerWin()
        {
            BingoGame game = Arrange("OnClaim", "Row0", out long one, out long other);

            // Tal cual GiveThePrize: cantar y entregar, juntos, por el primero nomas.
            game.ClaimBingo(game.CardInPlay(one), game.FigureInPlay("Row0"));
            game.ValidateWinner(game.CardInPlay(one), game.FigureInPlay("Row0"));

            Assert.IsTrue(game.IsFigureClosed("Row0"),
                "la figura se cerro con el primero, sin que el segundo llegara a estar pendiente");

            Assert.IsFalse(game.ClaimBingo(game.CardInPlay(other), game.FigureInPlay("Row0")),
                "el segundo empato con la MISMA bolita y ya no puede ganar. Su canto no produce "
                + "nada: ni pendiente, ni rechazo, ni renglon. En la parroquia son dos personas "
                + "gritando y una sola existiendo para la app.");
        }

        // La misma escena con la otra politica, que es la salida que NO toca el motor: la mesa
        // los encuentra a los dos al caer la bolita, la figura no cierra mientras quede alguien
        // esperando, y el padre los despacha uno por uno.
        [TestMethod]
        [TestCategory("DosGanadores")]
        public void Automatic_WhenTheyTie_BothReachTheOrganizerAndTheFigureWaitsForBoth()
        {
            BingoGame game = Arrange("Automatic", "Row0", out long one, out long other);

            Assert.AreEqual(2, game.PendingWinners.Count,
                "la mesa encontro a los DOS sin que nadie tuviera que cantar");

            game.ValidateWinner(game.CardInPlay(one), game.FigureInPlay("Row0"));

            Assert.IsFalse(game.IsFigureClosed("Row0"),
                "la figura sigue abierta: todavia queda alguien esperando al padre");

            game.ValidateWinner(game.CardInPlay(other), game.FigureInPlay("Row0"));

            Assert.IsTrue(game.IsFigureClosed("Row0"),
                "recien ahora, despachados los dos, se cierra");
        }
    }
}
