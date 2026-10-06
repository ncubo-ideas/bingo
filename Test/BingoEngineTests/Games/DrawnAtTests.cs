using System;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class DrawnAtTests
    {
        private static BingoGame ARunningGame()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(789);
            game.StartRound(new[] { "ColB" });
            return game;
        }

        [TestMethod]
        public void BeforeAnyDraw_ThereIsNothingToRead()
        {
            BingoGame game = ARunningGame();

            Assert.AreEqual(0, game.DrawnCount);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.DrawnAt(1));
        }

        [TestMethod]
        public void TheBallsComeBackInTheOrderTheyWereDrawn()
        {
            BingoGame game = ARunningGame();

            game.RegisterDraw(42);
            game.RegisterDraw(7);
            game.RegisterDraw(60);

            Assert.AreEqual(3, game.DrawnCount);
            Assert.AreEqual(42, game.DrawnAt(1));
            Assert.AreEqual(7, game.DrawnAt(2));
            Assert.AreEqual(60, game.DrawnAt(3));
        }

        [TestMethod]
        public void AskingPastTheLastBall_IsRejected()
        {
            BingoGame game = ARunningGame();
            game.RegisterDraw(42);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.DrawnAt(2));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.DrawnAt(0));
        }

        [TestMethod]
        public void WalkingEveryBall_RebuildsTheWholeDraw()
        {
            BingoGame game = ARunningGame();
            int[] salieron = { 11, 23, 47, 5, 68 };

            foreach (int ball in salieron) game.RegisterDraw(ball);

            for (int posicion = 1; posicion <= game.DrawnCount; posicion++)
                Assert.AreEqual(salieron[posicion - 1], game.DrawnAt(posicion));
        }

        [TestMethod]
        public void AGameThatNeverStarted_HasNothingDrawn()
        {
            BingoGame game = new BingoGame();

            Assert.AreEqual(0, game.DrawnCount);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.DrawnAt(1));
        }
    }
}
