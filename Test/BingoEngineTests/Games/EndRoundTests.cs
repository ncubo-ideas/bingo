using System;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    /// <summary>
    /// El organizador corta la ronda cuando quiere, con o sin ganadores: EndRound la
    /// deja Finished, descarta lo pendiente de validar, y abre la puerta para que
    /// StartRound arranque la que sigue con tombola fresca.
    /// </summary>
    [TestClass]
    public class EndRoundTests
    {
        private const long Card = 789;

        private static int[] Needs(long cardNumber, string figure)
        {
            Cardface face = Cardface.Of(new CardNumber(cardNumber));
            return WinPatterns.Catalog.ByName(figure).Alternatives[0]
                .Where(cell => !cell.IsFreeCenter)
                .Select(cell => face.NumberAt(cell))
                .ToArray();
        }

        private static BingoGame Started(string figure, params long[] cards)
        {
            BingoGame game = new BingoGame();
            foreach (long card in cards) game.PutCardInPlay(card);
            game.StartRound(new[] { figure });
            return game;
        }

        [TestMethod]
        public void EndRound_CutsARunningRoundShort()
        {
            BingoGame game = Started("AnyRow", Card);
            game.RegisterDraw(7);

            game.EndRound();

            Assert.AreEqual("Finished", game.State);
        }

        [TestMethod]
        public void EndRound_WithoutARound_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.EndRound());
        }

        [TestMethod]
        public void EndRound_OnAFinishedRound_DoesNothing()
        {
            BingoGame game = Started("AnyRow", Card);
            game.EndRound();

            game.EndRound();

            Assert.AreEqual("Finished", game.State);
        }

        [TestMethod]
        public void EndRound_DiscardsWhateverWasAwaitingValidation()
        {
            BingoGame game = Started("AnyRow", Card);
            foreach (int number in Needs(Card, "AnyRow")) game.RegisterDraw(number);
            Assert.AreEqual("AwaitingValidation", game.State, "el bingo quedo esperando al organizador");

            game.EndRound();

            Assert.AreEqual("Finished", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count,
                "cortar la ronda descarta lo pendiente: si nadie lo valido, nadie gano");
        }

        [TestMethod]
        public void EndRound_KeepsTheValidatedWinnersOfTheRound()
        {
            BingoGame game = Started("AnyRow", Card);
            foreach (int number in Needs(Card, "AnyRow")) game.RegisterDraw(number);
            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("AnyRow"));
            Assert.AreEqual("Finished", game.State, "una sola figura validada termina la ronda sola");

            game.EndRound();

            Assert.IsTrue(game.IsFigureClosed("AnyRow"));
        }

        [TestMethod]
        public void AfterEndRound_NoMoreBallsAreDrawn()
        {
            BingoGame game = Started("AnyRow", Card);
            game.EndRound();

            Assert.ThrowsException<BingoRuleException>(() => game.RegisterDraw(7));
        }

        [TestMethod]
        public void AfterEndRound_TheNextRoundStartsFreshWithTheSameCards()
        {
            BingoGame game = Started("AnyRow", Card);
            game.RegisterDraw(7);
            game.EndRound();

            game.StartRound(new[] { "FullCard" }, "OnClaim");

            Assert.AreEqual(2, game.RoundNumber);
            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.DrawnCount, "tombola fresca");
            Assert.AreEqual(1, game.CardsInPlayCount, "los cartones juegan toda la noche");
            Assert.AreEqual("FullCard", game.FigureInPlayAt(1));
        }
    }
}
