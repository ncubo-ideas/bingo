using System;
using System.Collections.Generic;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class FigureHelpersTests
    {
        private const long Card = 789;

        [TestMethod]
        public void TheCatalogNamesEveryFigureTheHostCanOffer()
        {
            IReadOnlyList<string> figures = BingoGame.KnownFigures;

            CollectionAssert.Contains(figures.ToArray(), "ColB");
            CollectionAssert.Contains(figures.ToArray(), "Row0");
            CollectionAssert.Contains(figures.ToArray(), "FullCard");
            CollectionAssert.Contains(figures.ToArray(), "AnyLine");
            CollectionAssert.Contains(figures.ToArray(), "X");
        }

        [TestMethod]
        public void ItKnowsWhichFigureNamesAreReal()
        {
            Assert.IsTrue(BingoGame.KnowsFigure("ColB"));
            Assert.IsTrue(BingoGame.KnowsFigure("colb"));
            Assert.IsFalse(BingoGame.KnowsFigure("ColZ"));
            Assert.IsFalse(BingoGame.KnowsFigure(""));
        }

        [TestMethod]
        public void AColumnNeedsTheFiveNumbersOfThatColumn()
        {
            IReadOnlyList<int> needs = BingoGame.ClosestFor(Card, "ColB", new int[0]);
            IReadOnlyList<int> column = Cardface.Of(new CardNumber(Card)).Column(0);

            CollectionAssert.AreEquivalent(column.ToArray(), needs.ToArray());
        }

        [TestMethod]
        public void ARowSkipsTheFreeCenter()
        {
            IReadOnlyList<int> fila2 = BingoGame.ClosestFor(Card, "Row2", new int[0]);

            Assert.AreEqual(4, fila2.Count);
        }

        [TestMethod]
        public void FullCardNeedsTheTwentyFourNumbers()
        {
            IReadOnlyList<int> todo = BingoGame.ClosestFor(Card, "FullCard", new int[0]);

            Assert.AreEqual(Cardface.NumberCount, todo.Count);
        }

        [TestMethod]
        public void AnyLinePicksTheLineYouAreClosestTo()
        {
            IReadOnlyList<int> columnaG = Cardface.Of(new CardNumber(Card)).Column(3);
            int[] casiToda = { columnaG[0], columnaG[1], columnaG[2], columnaG[3] };

            IReadOnlyList<int> elegida = BingoGame.ClosestFor(Card, "AnyLine", casiToda);

            foreach (int hers in casiToda) CollectionAssert.Contains(elegida.ToArray(), hers);
        }

        [TestMethod]
        public void WithNothingDrawn_ItStillAnswersSomething()
        {
            foreach (string figure in BingoGame.KnownFigures)
            {
                IReadOnlyList<int> needs = BingoGame.ClosestFor(Card, figure, new int[0]);

                Assert.IsTrue(needs.Count > 0, figure + " devolvio una lista vacia");
                Assert.AreEqual(needs.Count, needs.Distinct().Count(),
                    figure + " repite numeros");
            }
        }

        [TestMethod]
        public void CompletingWhatItAsksFor_WinsTheFigure()
        {
            foreach (string figure in new[] { "ColB", "Row0", "Diagonal", "X", "FourCorners" })
            {
                BingoGame game = new BingoGame();
                game.PutCardInPlay(Card);
                game.StartRound(new[] { figure });

                foreach (int needs in BingoGame.ClosestFor(Card, figure, new int[0]))
                    game.RegisterDraw(needs);

                Assert.IsTrue(game.IsPendingWinner(Card, figure),
                    figure + " no se detecto como ganada");
            }
        }

        [TestMethod]
        public void AnUnknownFigure_IsRejected()
        {
            Assert.ThrowsException<BingoRuleException>(
                () => BingoGame.ClosestFor(Card, "ColZ", new int[0]));
        }

        [TestMethod]
        public void AGameWithoutARound_HasNoFiguresInPlay()
        {
            BingoGame game = new BingoGame();

            Assert.AreEqual(0, game.FiguresInPlayCount);
        }

        [TestMethod]
        public void TheFiguresInPlay_CanBeAskedOneByOne()
        {
            BingoGame game = new BingoGame();
            game.StartRound(new[] { "Row0", "FourCorners" });

            Assert.AreEqual(2, game.FiguresInPlayCount);
            Assert.AreEqual("Row0", game.FigureInPlayAt(1));
            Assert.AreEqual("FourCorners", game.FigureInPlayAt(2));
        }

        [TestMethod]
        public void AskingOneByOne_SaysTheSameAsTheWholeList()
        {
            BingoGame game = new BingoGame();
            game.StartRound(new[] { "AnyRow", "AnySquare", "Diagonal" });

            List<string> unaAUna = new List<string>();
            for (int posicion = 1; posicion <= game.FiguresInPlayCount; posicion++)
                unaAUna.Add(game.FigureInPlayAt(posicion));

            CollectionAssert.AreEqual(game.FiguresInPlay.ToArray(), unaAUna.ToArray());
        }

        [TestMethod]
        public void ARepeatedFigure_CountsOnce()
        {
            BingoGame game = new BingoGame();
            game.StartRound(new[] { "Row0", "Row0" });

            Assert.AreEqual(1, game.FiguresInPlayCount);
            Assert.AreEqual("Row0", game.FigureInPlayAt(1));
        }

        [TestMethod]
        public void AFigurePositionOutOfRange_IsRejected()
        {
            BingoGame game = new BingoGame();
            game.StartRound(new[] { "Row0" });

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.FigureInPlayAt(0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.FigureInPlayAt(2));
        }

        [TestMethod]
        public void AskingAFigureWithoutARound_IsRejected()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.FigureInPlayAt(1));
        }

        [TestMethod]
        public void TheNextRoundBringsItsOwnFigures()
        {
            BingoGame game = new BingoGame();
            game.StartRound(new[] { "Row0" });

            for (int ball = 1; ball <= 75; ball++) game.RegisterDraw(ball);

            game.StartRound(new[] { "FourCorners", "AnyColumn" });

            Assert.AreEqual(2, game.FiguresInPlayCount);
            Assert.AreEqual("FourCorners", game.FigureInPlayAt(1));
            Assert.AreEqual("AnyColumn", game.FigureInPlayAt(2));
        }
    }
}
