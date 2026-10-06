using System;
using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class ClaimPolicyTests
    {
        private const long Card789 = 789;
        private const long Card790 = 790;

        private static BingoGame OnClaimRound(params long[] cards)
        {
            BingoGame game = new BingoGame();
            foreach (long card in cards) game.PutCardInPlay(card);
            game.StartRound(new[] { "Row0" }, "OnClaim");
            return game;
        }

        private static int[] Needs(long card, string figure)
        {
            Cardface face = Cardface.Of(new CardNumber(card));
            return WinPatterns.Catalog.ByName(figure).Alternatives[0]
                .Where(cell => !cell.IsFreeCenter)
                .Select(cell => face.NumberAt(cell))
                .ToArray();
        }

        private static IEnumerable<int> OffCard(long card, int howMany, params int[] used)
        {
            Cardface face = Cardface.Of(new CardNumber(card));
            return Enumerable.Range(Ball.Lowest, Ball.Highest)
                .Where(number => !face.Has(number) && !used.Contains(number))
                .Take(howMany);
        }

        private static void DrawAll(BingoGame game, IEnumerable<int> numbers)
        {
            foreach (int number in numbers) game.RegisterDraw(number);
        }

        [TestMethod]
        public void OnClaim_ACompletedCard_DoesNotPauseByItself()
        {
            BingoGame game = OnClaimRound(Card789);

            DrawAll(game, Needs(Card789, "Row0"));

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count);
        }

        [TestMethod]
        public void OnClaim_ALegitimateClaim_PausesForValidation()
        {
            BingoGame game = OnClaimRound(Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0")));

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(1, game.PendingWinners.Count);
            Assert.AreEqual(Card789, game.PendingWinners[0].Card);
        }

        [TestMethod]
        public void OnClaim_AFalseClaim_ChangesNothing()
        {
            BingoGame game = OnClaimRound(Card789, Card790);
            DrawAll(game, Needs(Card789, "Row0"));

            Assert.IsFalse(game.ClaimBingo(game.CardInPlay(Card790), game.FigureInPlay("Row0")));

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count);
        }

        [TestMethod]
        public void OnClaim_AfterClaiming_TheOperatorValidatesAsUsual()
        {
            BingoGame game = OnClaimRound(Card789);
            DrawAll(game, Needs(Card789, "Row0"));
            game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            game.ValidateWinner(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            Assert.AreEqual("Finished", game.State);
            Assert.IsTrue(game.IsFigureClosed("Row0"));
            Assert.IsFalse(game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0")), "whoever already won the figure does not win it again");
        }

        [TestMethod]
        public void OnClaim_IfNobodyClaims_TheTombolaRunsOutWithTheFigureOpen()
        {
            BingoGame game = OnClaimRound(Card789);

            DrawAll(game, Enumerable.Range(Ball.Lowest, Ball.Highest));

            Assert.AreEqual("Finished", game.State);
            Assert.IsFalse(game.IsFigureClosed("Row0"));
            Assert.AreEqual(0, game.PendingWinners.Count);
        }

        [TestMethod]
        public void OnClaim_TwoClaimersOfTheSameFigure_AreBothPending()
        {
            (long first, long second, int shared) = TwoCardsSharingOneNumberOfRow0();
            BingoGame game = OnClaimRound(first, second);

            DrawAll(game, Needs(first, "Row0").Concat(Needs(second, "Row0")).Distinct().Where(n => n != shared));
            game.RegisterDraw(shared);
            Assert.AreEqual("Running", game.State);

            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(first), game.FigureInPlay("Row0")));
            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(second), game.FigureInPlay("Row0")));

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(2, game.PendingWinners.Count);
        }

        [TestMethod]
        public void OnClaim_ClaimingTwice_DoesNotDuplicateThePending()
        {
            BingoGame game = OnClaimRound(Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0"));
            game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            Assert.AreEqual(1, game.PendingWinners.Count);
        }

        [TestMethod]
        public void OnClaim_AtDrawIsWhenItCompleted_NotWhenItWasClaimed()
        {
            int[] needs = Needs(Card789, "Row0");
            BingoGame game = OnClaimRound(Card789);

            DrawAll(game, needs);
            DrawAll(game, OffCard(Card789, 3, needs));

            Assert.AreEqual(needs.Length + 3, game.DrawnCount);

            game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            Assert.AreEqual(needs.Length, game.PendingWinners[0].AtDraw);
        }

        private static (long, long, int) TwoCardsSharingOneNumberOfRow0()
        {
            for (long a = 1; a <= 120; a++)
            {
                int[] needsA = Needs(a, "Row0");

                for (long b = a + 1; b <= 120; b++)
                {
                    int[] shared = needsA.Intersect(Needs(b, "Row0")).ToArray();
                    if (shared.Length == 1) return (a, b, shared[0]);
                }
            }

            throw new InvalidOperationException("No pair of cards shares exactly one number of Row0.");
        }
    }

    [TestClass]
    public class BingoGameClaimPolicyTests
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
        public void BingoGame_StartRound_WithoutAPolicy_IsAutomatic()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "Row0" });

            foreach (int number in Needs(Card, "Row0")) game.RegisterDraw(number);

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual("Automatic", game.Claiming);
        }

        [TestMethod]
        public void BingoGame_StartRound_WithOnClaim_WaitsForTheShout()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "Row0" }, "OnClaim");

            foreach (int number in Needs(Card, "Row0")) game.RegisterDraw(number);

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual("OnClaim", game.Claiming);
            Assert.AreEqual(0, game.PendingWinners.Count);

            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(Card), game.FigureInPlay("Row0")));

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(1, game.PendingWinners.Count);
        }

        [TestMethod]
        public void BingoGame_StartRound_WithAnUnknownPolicy_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(
                () => game.StartRound(new[] { "Row0" }, "SiLeDaLaGana"));
        }

        [TestMethod]
        public void BingoGame_Snapshot_CarriesThePolicy()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "Row0" }, "OnClaim");

            Assert.AreEqual("OnClaim", game.Snapshot().Claiming);
        }
    }
}
