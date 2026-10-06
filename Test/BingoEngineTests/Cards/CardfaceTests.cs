using System;
using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Cards
{
    [TestClass]
    public class CardfaceTests
    {
        private const long Card = 789;

        private static readonly int[][] Bands =
        {
            new[] { 1, 15 },
            new[] { 16, 30 },
            new[] { 31, 45 },
            new[] { 46, 60 },
            new[] { 61, 75 }
        };

        private static BingoGame WithCards(long from, long to)
        {
            BingoGame game = new BingoGame();
            for (long card = from; card <= to; card++) game.PutCardInPlay(card);
            return game;
        }

        private static int[][] FaceOf(BingoGame game, long card) =>
            Enumerable.Range(0, 5).Select(band => game.CardColumn(card, band).ToArray()).ToArray();

        [TestMethod]
        public void TheSameCardInTwoGames_ShowsTheSameFace()
        {
            int[][] first = FaceOf(WithCards(Card, Card), Card);
            int[][] second = FaceOf(WithCards(Card, Card), Card);

            for (int band = 0; band < 5; band++)
            {
                CollectionAssert.AreEqual(first[band], second[band], "band " + band);
            }
        }

        [TestMethod]
        public void DifferentCards_ShowDifferentFaces()
        {
            BingoGame game = WithCards(1, 2);

            CollectionAssert.AreNotEqual(
                FaceOf(game, 1).SelectMany(column => column).ToArray(),
                FaceOf(game, 2).SelectMany(column => column).ToArray());
        }

        [TestMethod]
        public void EveryCard_HasTwentyFourNumbersThatNeverRepeat()
        {
            BingoGame game = WithCards(1, 300);

            for (long card = 1; card <= 300; card++)
            {
                int[] numbers = FaceOf(game, card).SelectMany(column => column).ToArray();

                Assert.AreEqual(24, numbers.Length, "card number " + card);
                Assert.AreEqual(24, numbers.Distinct().Count(), "card number " + card + " repeats a number");
            }
        }

        [TestMethod]
        public void EveryCard_KeepsEveryNumberInsideItsBand()
        {
            BingoGame game = WithCards(1, 300);

            for (long card = 1; card <= 300; card++)
            {
                int[][] face = FaceOf(game, card);

                for (int band = 0; band < 5; band++)
                {
                    foreach (int number in face[band])
                    {
                        Assert.IsTrue(
                            number >= Bands[band][0] && number <= Bands[band][1],
                            "card number " + card + " put " + number + " in band " + band);
                    }
                }
            }
        }

        [TestMethod]
        public void EveryCard_HasTheBingo75Distribution()
        {
            int[] expected = { 5, 5, 4, 5, 5 };
            BingoGame game = WithCards(1, 300);

            for (long card = 1; card <= 300; card++)
            {
                for (int band = 0; band < 5; band++)
                {
                    Assert.AreEqual(
                        expected[band], game.CardColumn(card, band).Count,
                        "card number " + card + " band " + band);
                }
            }
        }

        [TestMethod]
        public void TheNumbersInsideAColumn_AreNotSorted()
        {
            BingoGame game = WithCards(1, 200);
            int columns = 0;
            int ascending = 0;

            for (long card = 1; card <= 200; card++)
            {
                for (int band = 0; band < 5; band++)
                {
                    IReadOnlyList<int> column = game.CardColumn(card, band);
                    columns++;
                    if (column.SequenceEqual(column.OrderBy(x => x))) ascending++;
                }
            }

            Assert.IsTrue(
                ascending * 10 < columns,
                "columns look sorted: " + ascending + " of " + columns + " are ascending");
        }

        [TestMethod]
        public void CardColumn_BelowOrAboveTheBands_Throws()
        {
            BingoGame game = WithCards(Card, Card);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.CardColumn(Card, -1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.CardColumn(Card, 5));
        }

        [TestMethod]
        public void TheRowsBelowTheFreeCenter_TakeTheMiddleColumnOneStepUp()
        {
            CollectionAssert.AreEqual(new[] { 4, 25, 34, 47, 75 }, BingoGame.ClosestFor(Card, "Row3", new int[0]).ToArray());
            CollectionAssert.AreEqual(new[] { 13, 28, 40, 56, 64 }, BingoGame.ClosestFor(Card, "Row4", new int[0]).ToArray());
        }

        [TestMethod]
        public void TheMiddleRow_SkipsTheFreeCenter()
        {
            CollectionAssert.AreEqual(new[] { 5, 17, 52, 63 }, BingoGame.ClosestFor(Card, "Row2", new int[0]).ToArray());
        }
    }
}
