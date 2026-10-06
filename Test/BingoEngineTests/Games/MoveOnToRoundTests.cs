using System;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    /// <summary>
    /// El telefono del jugador recibe la ronda nueva como UNA sola orden: las figuras
    /// vienen en un texto separado por comas, la ronda vieja se corta y la nueva
    /// arranca con tombola fresca. Tambien queda WasDismissed: el carton que el
    /// organizador rechazo se distingue del que todavia espera su premio.
    /// </summary>
    [TestClass]
    public class MoveOnToRoundTests
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

        [TestMethod]
        public void MoveOnToRound_CutsTheOldOneAndStartsTheNew()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow" }, "OnClaim");
            game.RegisterDraw(7);

            game.MoveOnToRound("FourCorners, FullCard", "OnClaim");

            Assert.AreEqual(2, game.RoundNumber);
            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.DrawnCount);
            Assert.AreEqual("FourCorners", game.FigureInPlayAt(1));
            Assert.AreEqual("FullCard", game.FigureInPlayAt(2));
        }

        [TestMethod]
        public void MoveOnToRound_WithoutARound_JustStartsTheFirst()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);

            game.MoveOnToRound("AnyRow", "OnClaim");

            Assert.AreEqual(1, game.RoundNumber);
            Assert.AreEqual("Running", game.State);
        }

        [TestMethod]
        public void MoveOnToRound_WithAFigureNobodyKnows_LeavesTheTableUntouched()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow" }, "OnClaim");
            game.RegisterDraw(7);

            Assert.ThrowsException<BingoRuleException>(
                () => game.MoveOnToRound("AnyRow,Inventada", "OnClaim"));

            Assert.AreEqual(1, game.RoundNumber, "la ronda que sonaba sigue sonando");
            Assert.AreEqual(1, game.DrawnCount);
        }

        [TestMethod]
        public void MoveOnToRound_NeedsAtLeastOneFigure()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.MoveOnToRound(" , ", "OnClaim"));
        }

        [TestMethod]
        public void WasDismissed_RemembersTheCardTheOrganizerRejected()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow" }, "OnClaim");
            foreach (int number in Needs(Card, "AnyRow")) game.RegisterDraw(number);

            game.ClaimBingo(game.CardInPlay(Card), game.FigureInPlay("AnyRow"));
            game.DismissWinner(game.CardInPlay(Card), game.FigureInPlay("AnyRow"));

            Assert.IsTrue(game.WasDismissed(Card, "AnyRow"));
            Assert.IsTrue(game.HasCompleted(Card, "AnyRow"),
                "la figura sigue completa en el papel: lo rechazado fue el premio, no las bolitas");
        }

        [TestMethod]
        public void WasDismissed_IsFalseForTheCardStillWaiting()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "AnyRow" }, "OnClaim");

            Assert.IsFalse(game.WasDismissed(Card, "AnyRow"));
        }
    }
}
