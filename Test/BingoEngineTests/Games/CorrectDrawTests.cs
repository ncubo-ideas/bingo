using System;
using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class CorrectDrawTests
    {
        private const long Card = 789;

        private static int[] Needs(string figure)
        {
            Cardface face = Cardface.Of(new CardNumber(Card));
            return WinPatterns.Catalog.ByName(figure).Alternatives[0]
                .Where(cell => !cell.IsFreeCenter)
                .Select(cell => face.NumberAt(cell))
                .ToArray();
        }

        private static int OffCard(params int[] alreadyUsed)
        {
            Cardface face = Cardface.Of(new CardNumber(Card));
            return Enumerable.Range(Ball.Lowest, Ball.Highest)
                .First(number => !face.Has(number) && !alreadyUsed.Contains(number));
        }

        private static BingoGame StartedWith(string claimPolicy, params string[] figures)
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(figures, claimPolicy);
            return game;
        }

        private static BingoGame Started(params string[] figures) => StartedWith("Automatic", figures);

        private static void DrawAll(BingoGame game, IEnumerable<int> numbers)
        {
            foreach (int number in numbers) game.RegisterDraw(number);
        }

        [TestMethod]
        public void CorrectDraw_PutsTheRightBallInThePlaceOfTheWrongOne()
        {
            BingoGame game = Started("FullCard");
            int wrong = OffCard();
            int right = OffCard(wrong);
            DrawAll(game, new[] { 3, wrong, 7 });

            game.CorrectDraw(wrong, right);

            CollectionAssert.AreEqual(new[] { 3, right, 7 }, game.DrawnNumbers.ToArray());
            Assert.AreEqual(3, game.DrawnCount);
        }

        [TestMethod]
        public void CorrectDraw_ABallThatNeverCameOut_IsRejected()
        {
            BingoGame game = Started("FullCard");
            game.RegisterDraw(3);

            Assert.ThrowsException<BingoRuleException>(() => game.CorrectDraw(9, 11));
            Assert.AreEqual(1, game.DrawnCount);
        }

        [TestMethod]
        public void CorrectDraw_IntoABallThatAlreadyCameOut_IsRejected()
        {
            BingoGame game = Started("FullCard");
            DrawAll(game, new[] { 3, 7 });

            Assert.ThrowsException<BingoRuleException>(() => game.CorrectDraw(3, 7));
            Assert.AreEqual(2, game.DrawnCount);
        }

        [TestMethod]
        public void CorrectDraw_WhenTheRightBallCompletesTheFigure_PausesForValidation()
        {
            int[] needs = Needs("Row0");
            int wrong = OffCard(needs);
            BingoGame game = Started("Row0");

            DrawAll(game, needs.Take(needs.Length - 1));
            game.RegisterDraw(wrong);
            Assert.AreEqual("Running", game.State);

            game.CorrectDraw(wrong, needs[needs.Length - 1]);

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(1, game.PendingWinners.Count);
            Assert.AreEqual("Row0", game.PendingWinners[0].Figure);
        }

        [TestMethod]
        public void CorrectDraw_WhenTheWrongBallHadCausedThePause_ClearsIt()
        {
            int[] needs = Needs("Row0");
            BingoGame game = Started("Row0");
            DrawAll(game, needs);
            Assert.AreEqual("AwaitingValidation", game.State);

            int right = OffCard(needs);

            game.CorrectDraw(needs[needs.Length - 1], right);

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count);
            Assert.AreEqual(needs.Length, game.DrawnCount);
        }

        [TestMethod]
        public void CorrectDraw_AfterAWinnerWasValidated_Throws()
        {
            int[] needs = Needs("Row0");
            BingoGame game = Started("Row0", "Row4");
            DrawAll(game, needs);
            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("Row0"));

            Assert.AreEqual("Running", game.State);
            Assert.ThrowsException<BingoRuleException>(
                () => game.CorrectDraw(needs[0], OffCard(needs)));
        }

        [TestMethod]
        public void CorrectDraw_OnAFinishedRound_Throws()
        {
            int[] needs = Needs("Row0");
            BingoGame game = Started("Row0");
            DrawAll(game, needs);
            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("Row0"));

            Assert.AreEqual("Finished", game.State);
            Assert.ThrowsException<BingoRuleException>(
                () => game.CorrectDraw(needs[0], OffCard(needs)));
        }

        [TestMethod]
        public void CorrectDraw_KeepsADismissalStanding()
        {
            int[] needs = Needs("Row0");
            BingoGame game = Started("Row0");
            DrawAll(game, needs);
            game.DismissWinner(game.CardInPlay(Card), game.FigureInPlay("Row0"));
            Assert.AreEqual("Running", game.State);

            int spare = OffCard(needs);
            int last = needs[needs.Length - 1];

            game.CorrectDraw(last, spare);
            game.CorrectDraw(spare, last);

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count);
            Assert.IsTrue(game.WasDismissed(Card, "Row0"));
        }

        // Una ronda con DOS figuras: una fila y el carton lleno. Alguien gana la fila temprano y
        // el padre le entrega el premio; el carton lleno se sigue jugando otra media hora.
        //
        // A partir de ese premio la ronda no acepta NINGUNA correccion, ni siquiera de una bolita
        // que no tuvo nada que ver con la fila premiada. Un teclazo -- el 46 en vez del 45 -- deja
        // al carton lleno jugandose sobre una bolsa equivocada, y la unica salida es cancelar la
        // ronda entera.
        //
        // Que NO se pueda tocar lo que sostiene un premio ya entregado esta bien y no se discute.
        // Lo que estos tests miden es que la guarda no distingue: mira si hay ALGUN ganador en la
        // ronda, no si la bolita que se quiere corregir sostiene alguno.
        private static (int One, int Other) TwoNobodyNeeds()
        {
            HashSet<int> hers = new HashSet<int>(Needs("FullCard"));
            List<int> loose = new List<int>();

            for (int n = 1; n <= 75 && loose.Count < 2; n++)
                if (!hers.Contains(n)) loose.Add(n);

            if (loose.Count < 2)
            {
                Assert.Inconclusive("no encontre dos numeros fuera del carton");
                return default;
            }

            return (loose[0], loose[1]);
        }

        [TestMethod]
        [TestCategory("CorregirDespuesDelPremio")]
        public void BeforeAnyPrizeIsGiven_TheBallCanBeCorrected()
        {
            BingoGame game = StartedWith("OnClaim", "Row0", "FullCard");
            (int stray, int alsoStray) = TwoNobodyNeeds();

            game.RegisterDraw(stray);
            game.CorrectDraw(stray, alsoStray);

            CollectionAssert.AreEqual(new[] { alsoStray }, game.DrawnNumbers.ToArray(),
                "sin premios entregados, un teclazo se corrige");
        }

        [TestMethod]
        [TestCategory("CorregirDespuesDelPremio")]
        public void OnceOneFigureIsGiven_TheOtherKeepsPlayingButNothingCanBeCorrected()
        {
            BingoGame game = StartedWith("OnClaim", "Row0", "FullCard");

            DrawAll(game, Needs("Row0"));
            game.ClaimBingo(game.CardInPlay(Card), game.FigureInPlay("Row0"));
            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("Row0"));

            Assert.IsTrue(game.IsFigureClosed("Row0"), "precondicion: la fila se entrego y se cerro");
            Assert.IsFalse(game.IsFigureClosed("FullCard"),
                "precondicion: el carton lleno SIGUE en juego, la ronda no termino");

            (int stray, int alsoStray) = TwoNobodyNeeds();

            game.RegisterDraw(stray);

            BingoRuleException refused = Assert.ThrowsException<BingoRuleException>(
                () => game.CorrectDraw(stray, alsoStray),
                "la ronda se niega a corregir una bolita que no sostiene ningun premio, solo "
                + "porque OTRA figura ya se entrego. El carton lleno se sigue jugando sobre una "
                + "bolsa con un teclazo adentro, y la unica salida es cancelar la ronda.");

            StringAssert.Contains(refused.Message, "winner was already validated",
                "y el motivo que da es el premio de la otra figura");
        }
    }
}
