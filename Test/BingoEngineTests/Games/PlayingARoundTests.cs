using System;
using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class PlayingARoundTests
    {
        private const long Card789 = 789;
        private const long Card790 = 790;

        private static int[] Needs(long card, string figure)
        {
            Cardface face = Cardface.Of(new CardNumber(card));
            return WinPatterns.Catalog.ByName(figure).Alternatives[0]
                .Where(cell => !cell.IsFreeCenter)
                .Select(cell => face.NumberAt(cell))
                .ToArray();
        }

        private static BingoGame Started(string figure, params long[] cards) =>
            Started(new[] { figure }, cards);

        private static BingoGame Started(string[] figures, params long[] cards)
        {
            BingoGame game = new BingoGame();
            foreach (long card in cards) game.PutCardInPlay(card);
            game.StartRound(figures);
            return game;
        }

        private static void DrawAll(BingoGame game, IEnumerable<int> numbers)
        {
            foreach (int number in numbers) game.RegisterDraw(number);
        }

        [TestMethod]
        public void StartRound_IsRunningWithNothingPending()
        {
            BingoGame game = Started("Row0", Card789);

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count);
            Assert.AreEqual(0, game.DrawnCount);
        }

        [TestMethod]
        public void StartRound_WithoutFigures_Throws()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card789);

            Assert.ThrowsException<BingoRuleException>(() => game.StartRound(Array.Empty<string>()));
        }

        [TestMethod]
        public void ARuleOfTheGame_IsABingoException()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            BingoException refused = Assert.ThrowsException<BingoRuleException>(() => game.RegisterDraw(2));

            Assert.IsInstanceOfType(refused, typeof(BingoException));
        }

        [TestMethod]
        public void RegisterDraw_RecordsTheBall()
        {
            BingoGame game = Started("FullCard", Card789);

            game.RegisterDraw(3);

            Assert.AreEqual(1, game.DrawnCount);
        }

        [TestMethod]
        public void RegisterDraw_TheSameBallTwice_IsInert()
        {
            BingoGame game = Started("FullCard", Card789);

            game.RegisterDraw(3);
            game.RegisterDraw(3);

            Assert.AreEqual(1, game.DrawnCount);
            CollectionAssert.AreEqual(new[] { 3 }, game.DrawnNumbers.ToArray());
        }

        [TestMethod]
        public void RegisterDraw_WhenAwaitingValidation_Throws()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.ThrowsException<BingoRuleException>(() => game.RegisterDraw(2));
        }

        [TestMethod]
        public void RegisterDraw_WhenACardCompletesTheFigure_PausesForValidation()
        {
            BingoGame game = Started("Row0", Card789);

            DrawAll(game, Needs(Card789, "Row0"));

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(1, game.PendingWinners.Count);
            Assert.AreEqual(Card789, game.PendingWinners[0].Card);
            Assert.AreEqual("Row0", game.PendingWinners[0].Figure);
            Assert.AreEqual(5, game.PendingWinners[0].AtDraw);
        }

        [TestMethod]
        public void RegisterDraw_WhenNobodyCompletes_KeepsRunning()
        {
            BingoGame game = Started("Row0", Card789);

            DrawAll(game, Needs(Card789, "Row0").Take(4));

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count);
        }

        [TestMethod]
        public void ACardThatArrivesAlreadyComplete_DoesNotWin()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card790, "Row0"));

            Assert.AreEqual("Running", game.State);

            game.PutCardInPlay(Card790);

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count);
        }

        [TestMethod]
        public void ACardThatArrivesMidRound_WinsOnlyWithALaterBall()
        {
            int[] needs = Needs(Card790, "Row0");
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, needs.Take(needs.Length - 1));

            game.PutCardInPlay(Card790);
            Assert.AreEqual("Running", game.State);

            game.RegisterDraw(needs[needs.Length - 1]);

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.IsTrue(game.PendingWinners.Any(winner => winner.Card == Card790));
        }

        [TestMethod]
        public void ValidateWinner_ClosesTheFigureAndFinishesTheRound()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            game.ValidateWinner(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            Assert.AreEqual("Finished", game.State);
            Assert.IsTrue(game.IsFigureClosed("Row0"));
            Assert.AreEqual(0, game.PendingWinners.Count);
            Assert.IsFalse(game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0")), "whoever already won the figure does not win it again");
        }

        [TestMethod]
        public void ValidateWinner_ACardThatIsNotPending_Throws()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            Assert.ThrowsException<BingoRuleException>(() => game.ValidateWinner(game.CardInPlay(Card790), game.FigureInPlay("Row0")));
        }

        [TestMethod]
        public void DismissWinner_ResumesWithoutClosingTheFigure()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            game.DismissWinner(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            Assert.AreEqual("Running", game.State);
            Assert.IsFalse(game.IsFigureClosed("Row0"));
            Assert.AreEqual(0, game.PendingWinners.Count);
        }

        [TestMethod]
        public void DismissWinner_ExcludesTheCardFromBeingDetectedAgain()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));
            game.DismissWinner(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            int spare = Enumerable.Range(1, 75)
                .First(number => !Cardface.Of(new CardNumber(Card789)).Has(number));
            game.RegisterDraw(spare);

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(0, game.PendingWinners.Count);
        }

        [TestMethod]
        public void WithTwoFigures_TheRoundDoesNotFinishUntilBothAreClosed()
        {
            BingoGame game = Started(new[] { "Row0", "Row4" }, Card789);

            DrawAll(game, Needs(Card789, "Row0"));
            game.ValidateWinner(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            Assert.IsTrue(game.IsFigureClosed("Row0"));
            Assert.IsFalse(game.IsFigureClosed("Row4"));
            Assert.AreEqual("Running", game.State);

            DrawAll(game, Needs(Card789, "Row4"));
            game.ValidateWinner(game.CardInPlay(Card789), game.FigureInPlay("Row4"));

            Assert.AreEqual("Finished", game.State);
        }

        [TestMethod]
        public void WhenOneBallClosesTwoFigures_BothArePending()
        {
            BingoGame game = Started(new[] { "Row0", "ColB" }, Card789);
            Cardface face = Cardface.Of(new CardNumber(Card789));
            int shared = face.NumberAt(new Cell(0, 0));

            IEnumerable<int> everythingElse = Needs(Card789, "Row0")
                .Concat(Needs(Card789, "ColB"))
                .Distinct()
                .Where(number => number != shared);

            DrawAll(game, everythingElse);
            Assert.AreEqual("Running", game.State);

            game.RegisterDraw(shared);

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(2, game.PendingWinners.Count);
            CollectionAssert.AreEquivalent(
                new[] { "Row0", "ColB" },
                game.PendingWinners.Select(winner => winner.Figure).ToArray());
        }

        [TestMethod]
        public void WhenOneBallCompletesTwoCards_BothArePending()
        {
            (long first, long second, int shared) = TwoCardsSharingOneNumberOfRow0();

            BingoGame game = Started("Row0", first, second);

            IEnumerable<int> everythingElse = Needs(first, "Row0")
                .Concat(Needs(second, "Row0"))
                .Distinct()
                .Where(number => number != shared);

            DrawAll(game, everythingElse);
            Assert.AreEqual("Running", game.State);

            game.RegisterDraw(shared);

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(2, game.PendingWinners.Count);
            CollectionAssert.AreEquivalent(
                new[] { first, second },
                game.PendingWinners.Select(winner => winner.Card).ToArray());
        }

        [TestMethod]
        public void CoWinnersOfTheSameFigure_CloseItTogether()
        {
            (long first, long second, int shared) = TwoCardsSharingOneNumberOfRow0();

            BingoGame game = Started("Row0", first, second);
            DrawAll(game, Needs(first, "Row0").Concat(Needs(second, "Row0")).Distinct().Where(n => n != shared));
            game.RegisterDraw(shared);

            game.ValidateWinner(game.CardInPlay(first), game.FigureInPlay("Row0"));
            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.IsFalse(game.IsFigureClosed("Row0"));

            game.ValidateWinner(game.CardInPlay(second), game.FigureInPlay("Row0"));

            Assert.AreEqual("Finished", game.State);
            Assert.IsTrue(game.IsFigureClosed("Row0"));
            Assert.IsFalse(game.ClaimBingo(game.CardInPlay(first), game.FigureInPlay("Row0")));
            Assert.IsFalse(game.ClaimBingo(game.CardInPlay(second), game.FigureInPlay("Row0")));
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

        [TestMethod]
        public void WhenTheTombolaRunsOut_TheRoundFinishesWithTheFigureStillOpen()
        {
            BingoGame game = Started("Row0");

            DrawAll(game, Enumerable.Range(Ball.Lowest, Ball.Highest));

            Assert.AreEqual("Finished", game.State);
            Assert.IsFalse(game.IsFigureClosed("Row0"));
            Assert.AreEqual(0, game.PendingWinners.Count);
            Assert.AreEqual(Ball.Highest, game.DrawnCount);
        }

        [TestMethod]
        public void ClaimBingo_ForALegitimateWinner_IsTrue()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0")));
        }

        [TestMethod]
        public void ClaimBingo_ForACardThatDidNotWin_IsFalse()
        {
            BingoGame game = Started("Row0", Card789, Card790);
            DrawAll(game, Needs(Card789, "Row0"));

            Assert.IsFalse(game.ClaimBingo(game.CardInPlay(Card790), game.FigureInPlay("Row0")));
        }

        [TestMethod]
        public void ClaimBingo_ChangesNothing()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));
            int pendingBefore = game.PendingWinners.Count;

            game.ClaimBingo(game.CardInPlay(Card789), game.FigureInPlay("Row0"));

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(pendingBefore, game.PendingWinners.Count);
            Assert.IsFalse(game.IsFigureClosed("Row0"));
        }

        [TestMethod]
        public void ClaimBingo_ForACardNotInTheRound_IsRefused()
        {
            BingoGame game = Started("Row0", Card789);
            DrawAll(game, Needs(Card789, "Row0"));

            BingoRuleException refused = Assert.ThrowsException<BingoRuleException>(() => game.CardInPlay(12345));

            StringAssert.Contains(refused.Message, "is not in play");
            Assert.AreEqual(1, game.PendingWinners.Count, "the real winner is still waiting");
        }

        [TestMethod]
        public void FigureInPlay_ForAFigureOutsideTheRound_IsRefused()
        {
            BingoGame game = Started("Row0", Card789);

            Assert.ThrowsException<BingoRuleException>(() => game.FigureInPlay("FullCard"));
            Assert.AreEqual("Row0", game.FigureInPlay("Row0").Name);
        }
    }
}
