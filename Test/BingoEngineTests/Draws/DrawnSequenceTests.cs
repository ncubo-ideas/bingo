using System;
using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Draws
{
    [TestClass]
    public class BallTests
    {
        private static BingoGame Started()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(789);
            game.StartRound(new[] { "FullCard" });
            return game;
        }

        [TestMethod]
        public void RegisterDraw_InsideTheRange_KeepsTheNumber()
        {
            BingoGame game = Started();

            game.RegisterDraw(42);

            Assert.AreEqual(42, game.DrawnAt(1));
        }

        [TestMethod]
        public void RegisterDraw_AtTheEdgesOfTheRange_IsAccepted()
        {
            BingoGame game = Started();

            game.RegisterDraw(1);
            game.RegisterDraw(75);

            CollectionAssert.AreEqual(new[] { 1, 75 }, game.DrawnNumbers.ToArray());
        }

        [TestMethod]
        public void RegisterDraw_BelowTheRange_Throws()
        {
            BingoGame game = Started();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.RegisterDraw(0));
        }

        [TestMethod]
        public void RegisterDraw_AboveTheRange_Throws()
        {
            BingoGame game = Started();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.RegisterDraw(76));
        }

        [TestMethod]
        public void CanRegisterDraw_OutsideTheRange_IsFalse()
        {
            BingoGame game = Started();

            Assert.IsFalse(game.CanRegisterDraw(0));
            Assert.IsFalse(game.CanRegisterDraw(76));
            Assert.IsTrue(game.CanRegisterDraw(1));
            Assert.IsTrue(game.CanRegisterDraw(75));
        }
    }

    [TestClass]
    public class DrawnSequenceTests
    {
        private static BingoGame Started()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(789);
            game.StartRound(new[] { "FullCard" });
            return game;
        }

        [TestMethod]
        public void ANewRound_HasNoBallsOut()
        {
            BingoGame game = Started();

            Assert.AreEqual(0, game.DrawnCount);
            Assert.AreEqual(0, game.DrawnNumbers.Count);
        }

        [TestMethod]
        public void RegisterDraw_RecordsTheBall()
        {
            BingoGame game = Started();

            game.RegisterDraw(42);

            Assert.AreEqual(1, game.DrawnCount);
            Assert.IsFalse(game.CanRegisterDraw(42), "a ball that already came out cannot be called again");
        }

        [TestMethod]
        public void RegisterDraw_KeepsTheOrderTheyCameOut()
        {
            BingoGame game = Started();

            game.RegisterDraw(42);
            game.RegisterDraw(7);
            game.RegisterDraw(75);

            CollectionAssert.AreEqual(new[] { 42, 7, 75 }, game.DrawnNumbers.ToArray());
            Assert.AreEqual(7, game.DrawnAt(2));
        }

        [TestMethod]
        public void RegisterDraw_TheSameBallTwice_IsInert()
        {
            BingoGame game = Started();
            game.RegisterDraw(42);

            game.RegisterDraw(42);

            Assert.AreEqual(1, game.DrawnCount);
        }

        [TestMethod]
        public void ABallThatNeverCameOut_CanStillBeCalledButNotCorrected()
        {
            BingoGame game = Started();
            game.RegisterDraw(42);

            Assert.IsTrue(game.CanRegisterDraw(43));
            Assert.IsFalse(game.CanCorrectDraw(43, 44));
            Assert.ThrowsException<BingoRuleException>(() => game.CorrectDraw(43, 44));
        }

        [TestMethod]
        public void TheTombola_HoldsSeventyFiveBalls()
        {
            BingoGame game = Started();

            for (int number = 1; number <= 74; number++) game.RegisterDraw(number);

            Assert.IsTrue(game.IsRunning, "with one ball still in the tombola the round goes on");
            Assert.IsTrue(game.CanRegisterDraw(75));
        }
    }
}
