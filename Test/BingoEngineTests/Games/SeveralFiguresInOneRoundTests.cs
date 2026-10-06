using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class SeveralFiguresInOneRoundTests
    {
        private const long Card = 787;
        private const long Theirs = 789;

        private static void DrawAll(BingoGame game, IEnumerable<int> numbers)
        {
            foreach (int number in numbers) game.RegisterDraw(number);
        }

        [TestMethod]
        public void ARoundCanCarryTwoFiguresAtOnce()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow", "L" });

            CollectionAssert.AreEquivalent(
                new[] { "AnyRow", "L" }, game.FiguresInPlay.ToArray());
        }

        [TestMethod]
        public void WinningOneFigureDoesNotCloseTheOther()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow", "FourCorners" });

            DrawAll(game, BingoGame.ClosestFor(Card, "AnyRow", new int[0]));

            Assert.IsTrue(game.IsPendingWinner(Card, "AnyRow"), "la fila no se detecto");
            Assert.IsFalse(game.IsPendingWinner(Card, "FourCorners"),
                "las esquinas no deberian estar ganadas todavia");
        }

        [TestMethod]
        public void WhenAFigureIsWon_TheRoundPausesForTheOrganizer()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow", "FourCorners" });

            DrawAll(game, BingoGame.ClosestFor(Card, "AnyRow", new int[0]));

            Assert.AreEqual("AwaitingValidation", game.State,
                "la ronda tiene que pausarse hasta que el padre valide el premio");
            Assert.ThrowsException<BingoRuleException>(
                () => game.RegisterDraw(OtroNumeroLibre(game)),
                "no se puede seguir cantando con un premio sin validar");
        }

        [TestMethod]
        public void ValidatingThePrizeResumesTheRoundForTheSecondFigure()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow", "FourCorners" });

            DrawAll(game, BingoGame.ClosestFor(Card, "AnyRow", new int[0]));
            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("AnyRow"));

            Assert.AreEqual("Running", game.State, "validado el premio, la ronda sigue");
            Assert.IsTrue(game.IsFigureClosed("AnyRow"), "la fila ya se cerro");
            Assert.IsFalse(game.IsFigureClosed("FourCorners"), "las esquinas siguen abiertas");

            DrawAll(game, BingoGame.ClosestFor(Card, "FourCorners", new int[0])
                .Where(n => !game.CardMarks(Card).Contains(n)));

            Assert.IsTrue(game.IsPendingWinner(Card, "FourCorners"),
                "la segunda figura tambien tenia que ganarse");
        }

        [TestMethod]
        public void EachFigureCanHaveItsOwnWinner()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.PutCardInPlay(Theirs);
            game.StartRound(new[] { "ColB", "ColO" });

            DrawAll(game, BingoGame.ClosestFor(Card, "ColB", new int[0]));
            Assert.IsTrue(game.IsPendingWinner(Card, "ColB"), "el de la B no gano");

            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("ColB"));

            DrawAll(game, BingoGame.ClosestFor(Theirs, "ColO", new int[0])
                .Where(n => !game.CardMarks(Theirs).Contains(n)));

            Assert.IsTrue(game.IsPendingWinner(Theirs, "ColO"),
                "el de la O tambien tenia que ganar, con su propia figura");
        }

        private static int OtroNumeroLibre(BingoGame game)
        {
            for (int number = 1; number <= 75; number++)
            {
                if (!game.DrawnNumbers.Contains(number)) return number;
            }

            return 1;
        }

        [TestMethod]
        public void TheRoundKeepsRunningWhileAFigureIsStillOpen()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow", "FullCard" }, "Automatic");

            DrawAll(game, BingoGame.ClosestFor(Card, "AnyRow", new int[0]));

            Assert.AreNotEqual("Finished", game.State,
                "con FullCard todavia abierta, la ronda no puede haber terminado");
        }

        [TestMethod]
        public void ASingleFigureStillWorksTheSame()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "ColB" });

            Assert.AreEqual(1, game.FiguresInPlay.Count);

            DrawAll(game, BingoGame.ClosestFor(Card, "ColB", new int[0]));

            Assert.IsTrue(game.IsPendingWinner(Card, "ColB"));
        }

        [TestMethod]
        public void ThreeFiguresAtOnceWorkToo()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow", "AnyColumn", "FourCorners" });

            Assert.AreEqual(3, game.FiguresInPlay.Count);

            DrawAll(game, BingoGame.ClosestFor(Card, "AnyColumn", new int[0]));

            Assert.IsTrue(game.IsPendingWinner(Card, "AnyColumn"));
            Assert.IsFalse(game.IsPendingWinner(Card, "FourCorners"));
        }
    }
}
