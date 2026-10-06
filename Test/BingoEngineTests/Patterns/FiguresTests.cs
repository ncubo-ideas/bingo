using System;
using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Patterns
{
    [TestClass]
    public class FiguresTests
    {
        private const long Card = 789;

        private static readonly int[] Row0 = { 12, 24, 44, 55, 61 };
        private static readonly int[] ColumnB = { 12, 6, 5, 4, 13 };
        private static readonly int[] DiagonalDown = { 12, 21, 47, 64 };
        private static readonly int[] Ell = { 12, 6, 5, 4, 13, 28, 40, 56, 64 };

        private const int TopLeft = 12;
        private const int BottomLeft = 13;
        private const int TopRight = 61;
        private const int BottomRight = 64;

        private static BingoGame RoundOf(params string[] figures)
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(figures);
            return game;
        }

        private static bool Wins(string figure, params int[] numbers)
        {
            BingoGame game = RoundOf(figure);
            foreach (int number in numbers) game.RegisterDraw(number);
            return game.IsPendingWinner(Card, figure);
        }

        [TestMethod]
        public void Row0_WinsWhenEveryCellIsCovered()
        {
            BingoGame game = RoundOf("Row0");

            foreach (int number in Row0) game.RegisterDraw(number);

            Assert.IsTrue(game.IsPendingWinner(Card, "Row0"));
            Assert.AreEqual(Row0.Length, game.WinningDrawOf(Card, "Row0"));
        }

        [TestMethod]
        public void Row0_DoesNotWinWithOneCellMissing()
        {
            Assert.IsFalse(Wins("Row0", 12, 24, 44, 55));
        }

        [TestMethod]
        public void Diagonal_TakesTheFreeCenterAsCovered()
        {
            Assert.IsTrue(Wins("Diagonal", DiagonalDown));
        }

        [TestMethod]
        public void AnyLine_WinsWithAnyOneOfItsLines()
        {
            Assert.IsTrue(Wins("AnyLine", Row0));
            Assert.IsTrue(Wins("AnyLine", ColumnB));
            Assert.IsTrue(Wins("AnyLine", DiagonalDown));
            Assert.IsFalse(Wins("AnyLine", 12, 24, 44));
        }

        [TestMethod]
        public void AnyLine_HasTwelveAlternatives()
        {
            Assert.AreEqual(12, BingoGame.AlternativeCount("AnyLine"));
        }

        [TestMethod]
        public void Row0_ForCard789_MatchesTheFrozenVector()
        {
            CollectionAssert.AreEqual(Row0, BingoGame.ClosestFor(Card, "Row0", new int[0]).ToArray());
        }

        [TestMethod]
        public void FourCorners_ForCard789_MatchesTheFrozenVector()
        {
            CollectionAssert.AreEqual(
                new[] { TopLeft, BottomLeft, TopRight, BottomRight }.OrderBy(n => n).ToArray(),
                BingoGame.ClosestFor(Card, "FourCorners", new int[0]).OrderBy(n => n).ToArray());
            Assert.IsTrue(Wins("FourCorners", TopLeft, BottomLeft, TopRight, BottomRight));
        }

        [TestMethod]
        public void StartRound_ByName_PlaysThatFigure()
        {
            BingoGame game = RoundOf("FullCard");

            CollectionAssert.AreEqual(new[] { "FullCard" }, game.FiguresInPlay.ToArray());
        }

        [TestMethod]
        public void StartRound_WithAnUnknownName_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.StartRound(new[] { "Sombrero" }));
        }

        [TestMethod]
        public void StartRound_IsCaseSensitiveAndDoesNotGuess()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<BingoRuleException>(() => game.StartRound(new[] { "row0" }));
        }

        [TestMethod]
        public void TheCatalog_HasThePopularFigures()
        {
            string[] names = BingoGame.KnownFigures.ToArray();

            CollectionAssert.Contains(names, "AnyLine");
            CollectionAssert.Contains(names, "Row0");
            CollectionAssert.Contains(names, "ColB");
            CollectionAssert.Contains(names, "Diagonal");
            CollectionAssert.Contains(names, "X");
            CollectionAssert.Contains(names, "FourCorners");
            CollectionAssert.Contains(names, "FullCard");
        }

        [TestMethod]
        public void EveryKnownFigure_CanBePlayed()
        {
            foreach (string figure in BingoGame.KnownFigures)
            {
                BingoGame game = RoundOf(figure);

                CollectionAssert.AreEqual(new[] { figure }, game.FiguresInPlay.ToArray(), figure);
            }
        }

        [TestMethod]
        public void KnownFigures_PutsTheMostPopularFirstAndTheColumnsLast()
        {
            string[] names = BingoGame.KnownFigures.ToArray();

            CollectionAssert.AreEqual(
                new[] { "AnyColumn", "AnyLine", "AnyRow", "FullCard" }, names.Take(4).ToArray());
            CollectionAssert.AreEqual(
                new[] { "ColB", "ColG", "ColI", "ColN", "ColO" }, names.Skip(names.Length - 5).ToArray());
        }

        [TestMethod]
        public void FullCard_AsksForEveryNumberOfTheCard()
        {
            BingoGame game = RoundOf("FullCard");
            int[] everyNumber = Enumerable.Range(0, 5).SelectMany(band => game.CardColumn(Card, band)).ToArray();

            Assert.AreEqual(1, BingoGame.AlternativeCount("FullCard"));
            Assert.AreEqual(24, everyNumber.Length);
            CollectionAssert.AreEquivalent(everyNumber, BingoGame.ClosestFor(Card, "FullCard", new int[0]).ToArray());

            foreach (int number in everyNumber.Take(23)) game.RegisterDraw(number);
            Assert.IsFalse(game.IsPendingWinner(Card, "FullCard"));

            game.RegisterDraw(everyNumber[23]);
            Assert.IsTrue(game.IsPendingWinner(Card, "FullCard"));
        }

        [TestMethod]
        public void TheCatalog_HasLAndThreeCorners()
        {
            Assert.IsTrue(BingoGame.KnowsFigure("L"));
            Assert.IsTrue(BingoGame.KnowsFigure("ThreeCorners"));
        }

        [TestMethod]
        public void L_IsTheLeftColumnPlusTheBottomRow()
        {
            BingoGame game = RoundOf("L");
            IEnumerable<int> leftColumn = game.CardColumn(Card, 0);
            IEnumerable<int> bottomRow = Enumerable.Range(0, 5).Select(band => game.CardColumn(Card, band).Last());

            Assert.AreEqual(1, BingoGame.AlternativeCount("L"));
            CollectionAssert.AreEquivalent(
                leftColumn.Union(bottomRow).ToArray(),
                BingoGame.ClosestFor(Card, "L", new int[0]).ToArray());
        }

        [TestMethod]
        public void L_NeverTouchesTheFreeCenter()
        {
            Assert.AreEqual(9, BingoGame.ClosestFor(Card, "L", new int[0]).Count,
                "all 9 cells of the L hold a number");
        }

        [TestMethod]
        public void L_ForCard789_MatchesTheFrozenVector()
        {
            CollectionAssert.AreEquivalent(Ell, BingoGame.ClosestFor(Card, "L", new int[0]).ToArray());
        }

        [TestMethod]
        public void L_WinsWithItsNineNumbers()
        {
            Assert.IsTrue(Wins("L", Ell));
            Assert.IsFalse(Wins("L", Ell.Take(8).ToArray()));
        }

        [TestMethod]
        public void ThreeCorners_HasOneAlternativePerCornerLeftOut()
        {
            Assert.AreEqual(4, BingoGame.AlternativeCount("ThreeCorners"));
        }

        [TestMethod]
        public void ThreeCorners_WinsWithAnyThreeOfTheFour()
        {
            Assert.IsTrue(Wins("ThreeCorners", TopLeft, BottomLeft, TopRight));
            Assert.IsTrue(Wins("ThreeCorners", TopLeft, BottomLeft, BottomRight));
            Assert.IsTrue(Wins("ThreeCorners", TopLeft, TopRight, BottomRight));
            Assert.IsTrue(Wins("ThreeCorners", BottomLeft, TopRight, BottomRight));
        }

        [TestMethod]
        public void ThreeCorners_DoesNotWinWithOnlyTwo()
        {
            Assert.IsFalse(Wins("ThreeCorners", TopLeft, BottomLeft));
        }

        [TestMethod]
        public void ThreeCorners_ForCard789_AreTheCornersOfTheCard()
        {
            BingoGame game = RoundOf("ThreeCorners");

            Assert.AreEqual(TopLeft, game.CardColumn(Card, 0).First());
            Assert.AreEqual(BottomLeft, game.CardColumn(Card, 0).Last());
            Assert.AreEqual(TopRight, game.CardColumn(Card, 4).First());
            Assert.AreEqual(BottomRight, game.CardColumn(Card, 4).Last());
        }

        [TestMethod]
        public void ThreeCorners_IsEasierThanFourCorners()
        {
            BingoGame game = RoundOf("ThreeCorners", "FourCorners");

            foreach (int number in new[] { TopLeft, BottomLeft, TopRight }) game.RegisterDraw(number);

            Assert.IsTrue(game.IsPendingWinner(Card, "ThreeCorners"));
            Assert.IsFalse(game.IsPendingWinner(Card, "FourCorners"));
        }
    }
}
