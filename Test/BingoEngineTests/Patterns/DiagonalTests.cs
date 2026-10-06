using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Patterns
{
    [TestClass]
    public class DiagonalTests
    {
        private const string TheFigure = "Diagonal";

        private static readonly IReadOnlyList<Cell> TopLeftToBottomRight =
            Enumerable.Range(0, 5).Select(paso => new Cell(paso, paso)).ToList();

        private static readonly IReadOnlyList<Cell> BottomLeftToTopRight =
            Enumerable.Range(0, 5).Select(paso => new Cell(paso, 4 - paso)).ToList();

        private static IEnumerable<IReadOnlyList<Cell>> BothDiagonals => new[] { TopLeftToBottomRight, BottomLeftToTopRight };

        private static BingoGame ARoundOf(long card)
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(card);
            game.StartRound(new[] { TheFigure });
            return game;
        }

        private static int[] NumbersOf(long card, IReadOnlyList<Cell> cells)
        {
            Cardface face = Cardface.Of(new CardNumber(card));
            return cells.Where(c => !c.IsFreeCenter).Select(c => face.NumberAt(c)).ToArray();
        }

        [TestMethod]
        public void DiagonalCountsInBothDirections()
        {
            Assert.AreEqual(2, BingoGame.AlternativeCount(TheFigure),
                "Diagonal deberia aceptar las dos diagonales, no solo una");
        }

        [TestMethod]
        public void EachDiagonalAsksForFourNumbers_BecauseTheCenterIsFree()
        {
            const long card = 787;

            foreach (IReadOnlyList<Cell> diagonal in BothDiagonals)
            {
                BingoGame game = ARoundOf(card);
                int[] numbers = NumbersOf(card, diagonal);

                Assert.AreEqual(4, numbers.Length, "only 4 of its 5 cells hold a number");

                foreach (int number in numbers) game.RegisterDraw(number);

                Assert.AreEqual(4, game.WinningDrawOf(card, TheFigure));
            }
        }

        [TestMethod]
        public void CompletingTopLeftToBottomRight_Wins()
        {
            const long card = 787;

            BingoGame game = ARoundOf(card);
            foreach (int number in NumbersOf(card, TopLeftToBottomRight)) game.RegisterDraw(number);

            Assert.IsTrue(game.IsPendingWinner(card, TheFigure));
        }

        [TestMethod]
        public void CompletingBottomLeftToTopRight_AlsoWins()
        {
            const long card = 787;

            BingoGame game = ARoundOf(card);
            foreach (int number in NumbersOf(card, BottomLeftToTopRight)) game.RegisterDraw(number);

            Assert.IsTrue(game.IsPendingWinner(card, TheFigure),
                "la diagonal de la O a la B tambien tiene que ganar");
        }

        [TestMethod]
        public void Card787_WinsWithTheReversedDiagonal()
        {
            const long card = 787;
            Cardface face = Cardface.Of(new CardNumber(card));

            int arribaDerecha = face.NumberAt(new Cell(4, 0));
            int siguiente = face.NumberAt(new Cell(3, 1));
            int anterior = face.NumberAt(new Cell(1, 3));
            int abajoIzquierda = face.NumberAt(new Cell(0, 4));

            BingoGame game = ARoundOf(card);
            game.RegisterDraw(arribaDerecha);
            game.RegisterDraw(siguiente);
            game.RegisterDraw(anterior);
            game.RegisterDraw(abajoIzquierda);

            Assert.IsTrue(game.IsPendingWinner(card, TheFigure),
                "con " + arribaDerecha + ", " + siguiente + ", " + anterior + " y "
                + abajoIzquierda + " el carton completo la diagonal y tenia que ganar");
        }

        [TestMethod]
        public void ThreeOfFourDoesNotWin_OnEitherDiagonal()
        {
            const long card = 787;

            foreach (IReadOnlyList<Cell> diagonal in BothDiagonals)
            {
                BingoGame game = ARoundOf(card);
                int[] numbers = NumbersOf(card, diagonal);

                for (int i = 0; i < 3; i++) game.RegisterDraw(numbers[i]);

                Assert.IsFalse(game.IsPendingWinner(card, TheFigure));
            }
        }

        [TestMethod]
        public void TheDemoMarksTheDiagonalYouAreClosestTo()
        {
            const long card = 787;

            int[] casiToda = NumbersOf(card, BottomLeftToTopRight).Take(3).ToArray();
            IReadOnlyList<int> closest = BingoGame.ClosestFor(card, TheFigure, casiToda);

            foreach (int hers in casiToda) CollectionAssert.Contains(closest.ToArray(), hers);
        }

        [TestMethod]
        public void WhatTheDemoAsksForIsExactlyWhatWins()
        {
            const long card = 787;

            BingoGame game = ARoundOf(card);
            foreach (int needs in BingoGame.ClosestFor(card, TheFigure, new int[0]))
                game.RegisterDraw(needs);

            Assert.IsTrue(game.IsPendingWinner(card, TheFigure));
        }
    }
}
