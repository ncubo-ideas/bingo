using System;
using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class BingoGameTests
    {
        private const long Card = 789;
        private const long OtherCard = 790;

        private static int[] Needs(long cardNumber, string figure)
        {
            Cardface face = Cardface.Of(new CardNumber(cardNumber));
            return WinPatterns.Catalog.ByName(figure).Alternatives[0]
                .Where(cell => !cell.IsFreeCenter)
                .Select(cell => face.NumberAt(cell))
                .ToArray();
        }

        private static int OffCard(long cardNumber, params int[] used)
        {
            Cardface face = Cardface.Of(new CardNumber(cardNumber));
            return Enumerable.Range(Ball.Lowest, Ball.Highest)
                .First(number => !face.Has(number) && !used.Contains(number));
        }

        private static BingoGame Started(string figure, params long[] cards)
        {
            BingoGame game = new BingoGame();
            foreach (long card in cards) game.PutCardInPlay(card);
            game.StartRound(new[] { figure });
            return game;
        }

        private static void DrawAll(BingoGame game, IEnumerable<int> numbers)
        {
            foreach (int number in numbers) game.RegisterDraw(number);
        }

        [TestMethod]
        public void BingoGame_WhenNew_IsWaitingWithNoRounds()
        {
            BingoGame game = new BingoGame();

            Assert.AreEqual("Waiting", game.State);
            Assert.AreEqual(0, game.RoundNumber);
            Assert.AreEqual(0, game.CardsInPlayCount);
            Assert.AreEqual(0, game.DrawnCount);
        }

        [TestMethod]
        public void BingoGame_PutCardInPlay_CountsTheCard()
        {
            BingoGame game = new BingoGame();

            game.PutCardInPlay(Card);

            Assert.AreEqual(1, game.CardsInPlayCount);
        }

        [TestMethod]
        public void BingoGame_PutCardInPlay_TheSameCardTwice_CountsItOnce()
        {
            BingoGame game = new BingoGame();

            game.PutCardInPlay(Card);
            game.PutCardInPlay(Card);

            Assert.AreEqual(1, game.CardsInPlayCount);
        }

        [TestMethod]
        public void BingoGame_PutCardInPlay_WithANumberThatIsNotACard_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.PutCardInPlay(0));
        }

        [TestMethod]
        public void BingoGame_RegisterDraw_WithoutARound_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.RegisterDraw(42));
        }

        [TestMethod]
        public void BingoGame_StartRound_MakesItRunningAndCountsTheRound()
        {
            BingoGame game = Started("Row0", Card);

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(1, game.RoundNumber);
            CollectionAssert.AreEqual(new[] { "Row0" }, game.FiguresInPlay.ToArray());
        }

        [TestMethod]
        public void BingoGame_StartRound_WithAnUnknownFigure_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.StartRound(new[] { "Sombrero" }));
        }

        [TestMethod]
        public void BingoGame_StartRound_WithoutFigures_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.StartRound(Array.Empty<string>()));
        }

        [TestMethod]
        public void BingoGame_StartRound_WhileOneIsRunning_Throws()
        {
            BingoGame game = Started("Row0", Card);

            Assert.ThrowsException<BingoRuleException>(() => game.StartRound(new[] { "Row4" }));
        }

        [TestMethod]
        public void BingoGame_RegisterDraw_RecordsTheNumber()
        {
            BingoGame game = Started("FullCard", Card);

            game.RegisterDraw(42);

            Assert.AreEqual(1, game.DrawnCount);
            CollectionAssert.AreEqual(new[] { 42 }, game.DrawnNumbers.ToArray());
        }

        [TestMethod]
        public void BingoGame_RegisterDraw_TheSameNumberTwice_IsInert()
        {
            BingoGame game = Started("FullCard", Card);

            game.RegisterDraw(42);
            game.RegisterDraw(42);

            Assert.AreEqual(1, game.DrawnCount);
        }

        [TestMethod]
        public void BingoGame_RegisterDraw_OutsideTheRange_Throws()
        {
            BingoGame game = Started("FullCard", Card);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.RegisterDraw(76));
        }

        [TestMethod]
        public void BingoGame_WhenACardCompletesTheFigure_ReportsThePendingWinner()
        {
            BingoGame game = Started("Row0", Card);

            DrawAll(game, Needs(Card, "Row0"));

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(1, game.PendingWinners.Count);
            Assert.AreEqual(Card, game.PendingWinners[0].Card);
            Assert.AreEqual("Row0", game.PendingWinners[0].Figure);
            Assert.AreEqual(5, game.PendingWinners[0].AtDraw);
        }

        [TestMethod]
        public void BingoGame_ValidateWinner_FinishesTheRound()
        {
            BingoGame game = Started("Row0", Card);
            DrawAll(game, Needs(Card, "Row0"));

            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("Row0"));

            Assert.AreEqual("Finished", game.State);
            Assert.IsTrue(game.IsFigureClosed("Row0"));
            Assert.AreEqual(0, game.PendingWinners.Count);
        }

        [TestMethod]
        public void BingoGame_DismissWinner_ResumesTheRound()
        {
            BingoGame game = Started("Row0", Card);
            DrawAll(game, Needs(Card, "Row0"));

            game.DismissWinner(game.CardInPlay(Card), game.FigureInPlay("Row0"));

            Assert.AreEqual("Running", game.State);
            Assert.IsFalse(game.IsFigureClosed("Row0"));
        }

        [TestMethod]
        public void BingoGame_AfterARoundFinishes_TheNextStartsWithAFreshTombola()
        {
            BingoGame game = Started("Row0", Card);
            DrawAll(game, Needs(Card, "Row0"));
            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("Row0"));

            game.StartRound(new[] { "Row4" });

            Assert.AreEqual("Running", game.State);
            Assert.AreEqual(2, game.RoundNumber);
            Assert.AreEqual(0, game.DrawnCount);
            Assert.AreEqual(1, game.CardsInPlayCount);
        }

        [TestMethod]
        public void BingoGame_CorrectDraw_ReplacesTheNumberInPlace()
        {
            BingoGame game = Started("FullCard", Card);
            DrawAll(game, new[] { 3, 9, 7 });

            game.CorrectDraw(9, 11);

            CollectionAssert.AreEqual(new[] { 3, 11, 7 }, game.DrawnNumbers.ToArray());
        }

        [TestMethod]
        public void BingoGame_CorrectDraw_WithANumberThatNeverCameOut_Throws()
        {
            BingoGame game = Started("FullCard", Card);
            game.RegisterDraw(3);

            Assert.ThrowsException<BingoRuleException>(() => game.CorrectDraw(9, 11));
        }

        [TestMethod]
        public void BingoGame_CorrectDraw_IntoANumberAlreadyOut_Throws()
        {
            BingoGame game = Started("FullCard", Card);
            DrawAll(game, new[] { 3, 9 });

            Assert.ThrowsException<BingoRuleException>(() => game.CorrectDraw(3, 9));
        }

        [TestMethod]
        public void BingoGame_CorrectDraw_WithoutARound_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.CorrectDraw(3, 9));
        }

        [TestMethod]
        public void BingoGame_ClaimBingo_ForAWinner_IsTrueAndChangesNothing()
        {
            BingoGame game = Started("Row0", Card, OtherCard);
            DrawAll(game, Needs(Card, "Row0"));

            Assert.IsTrue(game.ClaimBingo(game.CardInPlay(Card), game.FigureInPlay("Row0")));
            Assert.IsFalse(game.ClaimBingo(game.CardInPlay(OtherCard), game.FigureInPlay("Row0")));
            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.AreEqual(1, game.PendingWinners.Count);
            Assert.IsFalse(game.IsFigureClosed("Row0"));
        }

        [TestMethod]
        public void BingoGame_ClaimBingo_WithoutARound_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.ClaimBingo(game.CardInPlay(Card), game.FigureInPlay("Row0")));
        }

        [TestMethod]
        public void BingoGame_PutCardInPlay_MidRound_JoinsTheRunningRound()
        {
            int[] needs = Needs(OtherCard, "Row0");
            BingoGame game = Started("Row0", Card);
            DrawAll(game, needs.Take(needs.Length - 1));

            game.PutCardInPlay(OtherCard);
            Assert.AreEqual(2, game.CardsInPlayCount);
            Assert.AreEqual("Running", game.State);

            game.RegisterDraw(needs[needs.Length - 1]);

            Assert.AreEqual("AwaitingValidation", game.State);
            Assert.IsTrue(game.PendingWinners.Any(winner => winner.Card == OtherCard));
        }

        [TestMethod]
        public void BingoGame_Snapshot_CarriesWhatAPlayerNeedsToCatchUp()
        {
            BingoGame game = Started("Row0", Card);
            int[] three = Needs(Card, "Row0").Take(3).ToArray();
            DrawAll(game, three);

            GameStateSnapshot snapshot = game.Snapshot();

            Assert.AreEqual("Running", snapshot.State);
            Assert.AreEqual(1, snapshot.RoundNumber);
            CollectionAssert.AreEqual(three, snapshot.Drawn.ToArray());
            CollectionAssert.AreEqual(new[] { "Row0" }, snapshot.FiguresInPlay.ToArray());
            Assert.AreEqual(0, snapshot.ClosedFigures.Count);
        }

        [TestMethod]
        public void BingoGame_Snapshot_BeforeAnyRound_IsWaitingAndEmpty()
        {
            GameStateSnapshot snapshot = new BingoGame().Snapshot();

            Assert.AreEqual("Waiting", snapshot.State);
            Assert.AreEqual(0, snapshot.RoundNumber);
            Assert.AreEqual(0, snapshot.Drawn.Count);
            Assert.AreEqual(0, snapshot.FiguresInPlay.Count);
        }

        [TestMethod]
        public void BingoGame_IsFigureClosed_ForAFigureNotInPlay_Throws()
        {
            BingoGame game = Started("Row0", Card);

            Assert.ThrowsException<BingoRuleException>(() => game.IsFigureClosed("Row4"));
        }

        [TestMethod]
        public void BingoGame_CardColumn_GivesTheNumbersPrintedOnTheCard()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);

            CollectionAssert.AreEqual(new[] { 12, 6, 5, 4, 13 }, game.CardColumn(Card, 0).ToArray());
            CollectionAssert.AreEqual(new[] { 24, 21, 17, 25, 28 }, game.CardColumn(Card, 1).ToArray());
            CollectionAssert.AreEqual(new[] { 44, 37, 34, 40 }, game.CardColumn(Card, 2).ToArray());
            CollectionAssert.AreEqual(new[] { 55, 53, 52, 47, 56 }, game.CardColumn(Card, 3).ToArray());
            CollectionAssert.AreEqual(new[] { 61, 70, 63, 75, 64 }, game.CardColumn(Card, 4).ToArray());
        }

        [TestMethod]
        public void BingoGame_CardColumn_SaysTheSameAsTheCardface()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(OtherCard);
            Cardface face = Cardface.Of(new CardNumber(OtherCard));

            for (int band = 0; band < 5; band++)
            {
                CollectionAssert.AreEqual(
                    face.Column(band).ToArray(), game.CardColumn(OtherCard, band).ToArray(), "band " + band);
            }
        }

        [TestMethod]
        public void BingoGame_CardColumn_ForACardThatIsNotInPlay_Throws()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);

            Assert.ThrowsException<BingoRuleException>(() => game.CardColumn(OtherCard, 0));
        }

        [TestMethod]
        public void BingoGame_CardColumn_OutsideTheBands_Throws()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.CardColumn(Card, 5));
        }

        [TestMethod]
        public void BingoGame_CardColumn_WithANumberThatIsNotACard_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.CardColumn(0, 0));
        }

        [TestMethod]
        public void BingoGame_CardMarks_BeforeAnyRound_IsEmpty()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);

            Assert.AreEqual(0, game.CardMarks(Card).Count);
        }

        [TestMethod]
        public void BingoGame_CardMarks_KeepsOnlyTheDrawsThatAreOnTheCard()
        {
            BingoGame game = Started("FullCard", Card);

            DrawAll(game, new[] { 12, 14, 24, 44 });

            CollectionAssert.AreEqual(new[] { 12, 24, 44 }, game.CardMarks(Card).ToArray());
        }

        [TestMethod]
        public void BingoGame_CardMarks_FollowsTheOrderTheBallsCameOut()
        {
            BingoGame game = Started("FullCard", Card);

            DrawAll(game, new[] { 75, 5, 37 });

            CollectionAssert.AreEqual(new[] { 75, 5, 37 }, game.CardMarks(Card).ToArray());
        }

        [TestMethod]
        public void BingoGame_CardMarks_ForTwoCards_MarksEachOneOnItsOwnFace()
        {
            BingoGame game = Started("FullCard", Card, OtherCard);

            game.RegisterDraw(12);

            CollectionAssert.AreEqual(new[] { 12 }, game.CardMarks(Card).ToArray());
            Assert.AreEqual(
                Cardface.Of(new CardNumber(OtherCard)).Has(12) ? 1 : 0, game.CardMarks(OtherCard).Count);
        }

        [TestMethod]
        public void BingoGame_CardMarks_AfterACorrectedDraw_DropsTheWrongNumber()
        {
            BingoGame game = Started("FullCard", Card);
            DrawAll(game, new[] { 12, 24 });

            game.CorrectDraw(24, 25);

            CollectionAssert.AreEqual(new[] { 12, 25 }, game.CardMarks(Card).ToArray());
        }

        [TestMethod]
        public void BingoGame_CardMarks_InANewRound_StartsEmptyAgain()
        {
            BingoGame game = Started("Row0", Card);
            DrawAll(game, Needs(Card, "Row0"));
            game.ValidateWinner(game.CardInPlay(Card), game.FigureInPlay("Row0"));

            game.StartRound(new[] { "Row4" });

            Assert.AreEqual(0, game.CardMarks(Card).Count);
        }

        [TestMethod]
        public void BingoGame_CardMarks_ForACardAddedMidRound_ShowsEveryBallAlreadyOut()
        {
            BingoGame game = Started("FullCard", Card);
            DrawAll(game, new[] { 12, 24 });

            game.PutCardInPlay(OtherCard);

            CollectionAssert.AreEqual(new[] { 12, 24 }, game.CardMarks(Card).ToArray());
            CollectionAssert.AreEqual(
                new[] { 12, 24 }.Where(number => Cardface.Of(new CardNumber(OtherCard)).Has(number)).ToArray(),
                game.CardMarks(OtherCard).ToArray());
        }

        [TestMethod]
        public void BingoGame_CardMarks_ForACardThatIsNotInPlay_Throws()
        {
            BingoGame game = Started("FullCard", Card);

            Assert.ThrowsException<BingoRuleException>(() => game.CardMarks(OtherCard));
        }
    }
}
