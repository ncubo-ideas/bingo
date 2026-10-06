using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class HowDidIDoTests
    {
        private const long Mine = 789;
        private const long Someone = 790;
        private const string Figure = "Row0";

        private static int[] Needs(long cardNumber, string figure)
        {
            Cardface face = Cardface.Of(new CardNumber(cardNumber));
            return WinPatterns.Catalog.ByName(figure).Alternatives[0]
                .Where(cell => !cell.IsFreeCenter)
                .Select(cell => face.NumberAt(cell))
                .ToArray();
        }

        private static BingoGame AGameOnClaim(params long[] cards)
        {
            BingoGame game = new BingoGame();
            foreach (long card in cards) game.PutCardInPlay(card);
            game.StartRound(new[] { Figure }, "OnClaim");
            return game;
        }

        [TestMethod]
        public void BeforeAnybodyWins_NobodyIsAPendingWinner()
        {
            BingoGame game = AGameOnClaim(Mine);

            Assert.IsFalse(game.IsPendingWinner(Mine, Figure));
            Assert.AreEqual(0, game.WinningDrawOf(Mine, Figure));
        }

        [TestMethod]
        public void AfterMyCardClaims_ItIsAPendingWinner()
        {
            BingoGame game = AGameOnClaim(Mine);
            foreach (int number in Needs(Mine, Figure)) game.RegisterDraw(number);

            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(Mine), game.FigureInPlay(Figure)));

            Assert.IsTrue(game.IsPendingWinner(Mine, Figure));
            Assert.IsTrue(game.WinningDrawOf(Mine, Figure) > 0);
        }

        [TestMethod]
        public void AnotherCardsWin_IsNotMine()
        {
            BingoGame game = AGameOnClaim(Mine, Someone);
            foreach (int number in Needs(Someone, Figure)) game.RegisterDraw(number);
            game.ClaimBingo(game.CardInPlay(Someone), game.FigureInPlay(Figure));

            Assert.IsTrue(game.IsPendingWinner(Someone, Figure));
            Assert.IsFalse(game.IsPendingWinner(Mine, Figure));
        }

        [TestMethod]
        public void AskingDoesNotChangeAnything()
        {
            BingoGame game = AGameOnClaim(Mine);
            foreach (int number in Needs(Mine, Figure)) game.RegisterDraw(number);

            string before = game.State;
            int drawnBefore = game.DrawnCount;

            for (int veces = 0; veces < 5; veces++)
            {
                game.IsPendingWinner(Mine, Figure);
                game.WinningDrawOf(Mine, Figure);
                game.MarkedCount(Mine);
            }

            Assert.AreEqual(before, game.State);
            Assert.AreEqual(drawnBefore, game.DrawnCount);
            Assert.IsFalse(game.IsPendingWinner(Mine, Figure));
        }

        [TestMethod]
        public void AGameThatNeverStarted_AnswersWithoutThrowing()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Mine);

            Assert.IsFalse(game.IsPendingWinner(Mine, Figure));
            Assert.AreEqual(0, game.WinningDrawOf(Mine, Figure));
            Assert.AreEqual(0, game.MarkedCount(Mine));
        }

        [TestMethod]
        public void AnUnknownFigure_IsNotAWin()
        {
            BingoGame game = AGameOnClaim(Mine);
            foreach (int number in Needs(Mine, Figure)) game.RegisterDraw(number);
            game.ClaimBingo(game.CardInPlay(Mine), game.FigureInPlay(Figure));

            Assert.IsFalse(game.IsPendingWinner(Mine, "ColB"));
        }
    }
}
