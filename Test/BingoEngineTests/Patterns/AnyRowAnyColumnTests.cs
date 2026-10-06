using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Patterns
{
    [TestClass]
    public class AnyRowAnyColumnTests
    {
        private const long Card = 787;

        private static BingoGame ARoundOf(string figure)
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { figure });
            return game;
        }

        private static int[] NumbersOf(IEnumerable<Cell> cells)
        {
            Cardface face = Cardface.Of(new CardNumber(Card));
            return cells.Where(c => !c.IsFreeCenter).Select(c => face.NumberAt(c)).ToArray();
        }

        private static int[] Row(int row) =>
            NumbersOf(Enumerable.Range(0, 5).Select(band => new Cell(band, row)));

        private static int[] Column(int band) =>
            NumbersOf(Enumerable.Range(0, 5).Select(row => new Cell(band, row)));

        private static int[] DiagonalAbajo() =>
            NumbersOf(Enumerable.Range(0, 5).Select(paso => new Cell(paso, paso)));


        [TestMethod]
        public void TheHorizontalLineIsInTheCatalog()
        {
            Assert.IsTrue(BingoGame.KnowsFigure("AnyRow"));
            CollectionAssert.Contains(BingoGame.KnownFigures.ToArray(), "AnyRow");
        }

        [TestMethod]
        public void TheHorizontalLineIsTheFiveRows_NotOneMore()
        {
            Assert.AreEqual(5, BingoGame.AlternativeCount("AnyRow"));

            for (int row = 0; row < 5; row++)
            {
                BingoGame game = ARoundOf("AnyRow");
                int[] cells = Row(row);
                foreach (int number in cells.Take(cells.Length - 1)) game.RegisterDraw(number);

                Assert.IsFalse(game.IsPendingWinner(Card, "AnyRow"),
                    "row " + row + " is one number short and still won");
            }
        }

        [TestMethod]
        public void AnyOfTheFiveRowsWins()
        {
            for (int row = 0; row < 5; row++)
            {
                BingoGame game = ARoundOf("AnyRow");
                foreach (int number in Row(row)) game.RegisterDraw(number);

                Assert.IsTrue(game.IsPendingWinner(Card, "AnyRow"),
                    "la fila " + row + " no se detecto como ganada");
            }
        }

        [TestMethod]
        public void AColumnDoesNotWinTheHorizontalLine()
        {
            BingoGame game = ARoundOf("AnyRow");
            foreach (int number in Column(0)) game.RegisterDraw(number);

            Assert.IsFalse(game.IsPendingWinner(Card, "AnyRow"));
        }

        [TestMethod]
        public void ADiagonalDoesNotWinTheHorizontalLine()
        {
            BingoGame game = ARoundOf("AnyRow");
            foreach (int number in DiagonalAbajo()) game.RegisterDraw(number);

            Assert.IsFalse(game.IsPendingWinner(Card, "AnyRow"));
        }


        [TestMethod]
        public void TheVerticalLineIsInTheCatalog()
        {
            Assert.IsTrue(BingoGame.KnowsFigure("AnyColumn"));
            CollectionAssert.Contains(BingoGame.KnownFigures.ToArray(), "AnyColumn");
        }

        [TestMethod]
        public void TheVerticalLineIsTheFiveColumns_NotOneMore()
        {
            Assert.AreEqual(5, BingoGame.AlternativeCount("AnyColumn"));

            for (int band = 0; band < 5; band++)
            {
                BingoGame game = ARoundOf("AnyColumn");
                int[] cells = Column(band);
                foreach (int number in cells.Take(cells.Length - 1)) game.RegisterDraw(number);

                Assert.IsFalse(game.IsPendingWinner(Card, "AnyColumn"),
                    "column " + band + " is one number short and still won");
            }
        }

        [TestMethod]
        public void AnyOfTheFiveColumnsWins()
        {
            for (int band = 0; band < 5; band++)
            {
                BingoGame game = ARoundOf("AnyColumn");
                foreach (int number in Column(band)) game.RegisterDraw(number);

                Assert.IsTrue(game.IsPendingWinner(Card, "AnyColumn"),
                    "la columna " + band + " no se detecto como ganada");
            }
        }

        [TestMethod]
        public void ARowDoesNotWinTheVerticalLine()
        {
            BingoGame game = ARoundOf("AnyColumn");
            foreach (int number in Row(0)) game.RegisterDraw(number);

            Assert.IsFalse(game.IsPendingWinner(Card, "AnyColumn"));
        }


        [TestMethod]
        public void TheMiddleRowAndColumnAskForFourNumbers()
        {
            BingoGame rowGame = ARoundOf("AnyRow");
            foreach (int number in Row(2)) rowGame.RegisterDraw(number);

            Assert.AreEqual(4, rowGame.DrawnCount);
            Assert.IsTrue(rowGame.IsPendingWinner(Card, "AnyRow"), "la fila del medio cruza el centro libre");

            BingoGame columnGame = ARoundOf("AnyColumn");
            foreach (int number in columnGame.CardColumn(Card, 2)) columnGame.RegisterDraw(number);

            Assert.AreEqual(4, columnGame.DrawnCount);
            Assert.IsTrue(columnGame.IsPendingWinner(Card, "AnyColumn"), "la columna del medio cruza el centro libre");
        }


        [TestMethod]
        public void WhatTheDemoAsksForIsExactlyWhatWins()
        {
            foreach (string figure in new[] { "AnyRow", "AnyColumn" })
            {
                BingoGame game = ARoundOf(figure);

                foreach (int needs in BingoGame.ClosestFor(Card, figure, new int[0]))
                    game.RegisterDraw(needs);

                Assert.IsTrue(game.IsPendingWinner(Card, figure), figure + " no gano");
            }
        }

        [TestMethod]
        public void AnyLineStillAcceptsTheDiagonals()
        {
            BingoGame game = ARoundOf("AnyLine");
            foreach (int number in DiagonalAbajo()) game.RegisterDraw(number);

            Assert.IsTrue(game.IsPendingWinner(Card, "AnyLine"),
                "AnyLine tiene que seguir aceptando diagonales");
            Assert.AreEqual(12, BingoGame.AlternativeCount("AnyLine"));
        }
    }
}
