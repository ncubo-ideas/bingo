using System;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class HasCompletedTests
    {
        private const long Card = 791;

        private static BingoGame APlayersGame(params string[] figures)
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(figures, "OnClaim");
            return game;
        }

        private static void DrawAll(BingoGame game, System.Collections.Generic.IEnumerable<int> numbers)
        {
            foreach (int number in numbers) game.RegisterDraw(number);
        }

        [TestMethod]
        public void BeforeCompletingIt_SaysNo()
        {
            BingoGame game = APlayersGame("FourCorners");

            Assert.IsFalse(game.HasCompleted(Card, "FourCorners"));
        }

        [TestMethod]
        public void OnCompletingIt_SaysYes()
        {
            BingoGame game = APlayersGame("FourCorners");
            DrawAll(game, BingoGame.ClosestFor(Card, "FourCorners", new int[0]));

            Assert.IsTrue(game.HasCompleted(Card, "FourCorners"));
        }

        [TestMethod]
        public void AskingChangesNothing()
        {
            BingoGame game = APlayersGame("FourCorners");
            DrawAll(game, BingoGame.ClosestFor(Card, "FourCorners", new int[0]));

            string antes = game.State;
            int bolitasAntes = game.DrawnCount;

            for (int veces = 0; veces < 5; veces++) game.HasCompleted(Card, "FourCorners");

            Assert.AreEqual(antes, game.State, "preguntar movio el estado");
            Assert.AreEqual(bolitasAntes, game.DrawnCount);
            Assert.AreEqual(0, game.PendingWinners.Count, "preguntar creo un ganador pendiente");
        }

        [TestMethod]
        public void WithOnClaim_TheRoundDoesNotPauseWhenAFigureIsCompleted()
        {
            BingoGame game = APlayersGame("FourCorners", "L");
            DrawAll(game, BingoGame.ClosestFor(Card, "FourCorners", new int[0]));

            Assert.AreEqual("Running", game.State,
                "con OnClaim el juego del jugador no tiene que pausarse");
        }

        [TestMethod]
        public void TheCornersBug_AfterWinningTheCornersBallsKeepComingIn()
        {
            BingoGame game = APlayersGame("FourCorners", "L");
            DrawAll(game, BingoGame.ClosestFor(Card, "FourCorners", new int[0]));

            Assert.IsTrue(game.HasCompleted(Card, "FourCorners"), "las esquinas tenian que estar");

            int[] siguientes = Enumerable.Range(1, 75)
                .Where(n => !game.DrawnNumbers.Contains(n))
                .Take(6)
                .ToArray();

            foreach (int ball in siguientes)
            {
                game.RegisterDraw(ball);
            }

            Assert.AreEqual(game.DrawnCount, 4 + siguientes.Length,
                "las bolitas de despues del premio tambien tienen que entrar");
        }

        [TestMethod]
        public void WithAutomatic_ItAlsoAnswersRight()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "FourCorners" });

            Assert.IsFalse(game.HasCompleted(Card, "FourCorners"));

            DrawAll(game, BingoGame.ClosestFor(Card, "FourCorners", new int[0]));

            Assert.IsTrue(game.HasCompleted(Card, "FourCorners"));
        }

        [TestMethod]
        public void ACardNotInPlay_CompletesNothing()
        {
            BingoGame game = APlayersGame("FourCorners");
            DrawAll(game, BingoGame.ClosestFor(Card, "FourCorners", new int[0]));

            Assert.IsFalse(game.HasCompleted(999, "FourCorners"));
        }

        [TestMethod]
        public void WithoutARound_AnswersNoWithoutThrowing()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);

            Assert.IsFalse(game.HasCompleted(Card, "FourCorners"));
        }

        [TestMethod]
        public void EachFigureIsAnsweredSeparately()
        {
            BingoGame game = APlayersGame("FourCorners", "AnyRow");
            DrawAll(game, BingoGame.ClosestFor(Card, "AnyRow", new int[0]));

            Assert.IsTrue(game.HasCompleted(Card, "AnyRow"));
            Assert.IsFalse(game.HasCompleted(Card, "FourCorners"));
        }

        [TestMethod]
        public void AFigureThatDoesNotExist_IsRejected()
        {
            BingoGame game = APlayersGame("FourCorners");

            Assert.ThrowsException<BingoRuleException>(() => game.HasCompleted(Card, "ColZ"));
        }
    }
}
