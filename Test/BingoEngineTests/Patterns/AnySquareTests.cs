using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Patterns
{
    [TestClass]
    public class AnySquareTests
    {
        private const string TheFigure = "AnySquare";
        private const long Card = 789;

        private static IReadOnlyList<Cell> SquareAt(int band, int row) => new List<Cell>
        {
            new Cell(band, row),
            new Cell(band + 1, row),
            new Cell(band, row + 1),
            new Cell(band + 1, row + 1)
        };

        private static IEnumerable<(int Band, int Row)> EveryPosition =>
            from band in Enumerable.Range(0, 4)
            from row in Enumerable.Range(0, 4)
            select (band, row);

        private static BingoGame ARound()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { TheFigure });
            return game;
        }

        private static int[] NumbersOf(IEnumerable<Cell> cells)
        {
            Cardface face = Cardface.Of(new CardNumber(Card));
            return cells.Where(c => !c.IsFreeCenter).Select(c => face.NumberAt(c)).ToArray();
        }

        [TestMethod]
        public void TheCatalogKnowsTheSquare()
        {
            Assert.IsTrue(BingoGame.KnowsFigure(TheFigure));
            CollectionAssert.Contains(BingoGame.KnownFigures.ToArray(), TheFigure);
        }

        [TestMethod]
        public void ThereAreSixteenPositionsInAFiveByFiveGrid()
        {
            Assert.AreEqual(16, BingoGame.AlternativeCount(TheFigure));
        }

        [TestMethod]
        public void AnyOfTheSixteenTwoByTwoSquaresWins_TheCornersIncluded()
        {
            foreach ((int band, int row) in EveryPosition)
            {
                BingoGame game = ARound();

                foreach (int number in NumbersOf(SquareAt(band, row))) game.RegisterDraw(number);

                Assert.IsTrue(game.IsPendingWinner(Card, TheFigure),
                    "the square starting at " + band + "," + row + " was not detected as a win");
            }
        }

        [TestMethod]
        public void ThreeOfFourDoesNotWin()
        {
            BingoGame game = ARound();
            int[] four = NumbersOf(SquareAt(0, 0));

            for (int i = 0; i < 3; i++) game.RegisterDraw(four[i]);

            Assert.IsFalse(game.IsPendingWinner(Card, TheFigure));
        }

        [TestMethod]
        public void TheFreeCenterCountsAsMarked_SoThoseSquaresAskForThreeNumbers()
        {
            BingoGame game = ARound();
            int[] conCentro = NumbersOf(SquareAt(1, 1));

            Assert.AreEqual(3, conCentro.Length, "solo 3 de sus casillas llevan numero");

            foreach (int number in conCentro) game.RegisterDraw(number);

            Assert.IsTrue(game.IsPendingWinner(Card, TheFigure));
            Assert.AreEqual(3, game.WinningDrawOf(Card, TheFigure));
        }

        [TestMethod]
        public void TheDemoMarksTheSquareYouAreClosestTo()
        {
            int[] elegido = NumbersOf(SquareAt(0, 0));
            int[] casiTodo = elegido.Take(3).ToArray();

            IReadOnlyList<int> closest = BingoGame.ClosestFor(Card, TheFigure, casiTodo);

            foreach (int hers in casiTodo) CollectionAssert.Contains(closest.ToArray(), hers);
            Assert.AreEqual(4, closest.Count);
        }

        [TestMethod]
        public void WhatTheDemoAsksForIsExactlyWhatWins()
        {
            BingoGame game = ARound();

            foreach (int needs in BingoGame.ClosestFor(Card, TheFigure, new int[0]))
                game.RegisterDraw(needs);

            Assert.IsTrue(game.IsPendingWinner(Card, TheFigure));
        }
    }
}
