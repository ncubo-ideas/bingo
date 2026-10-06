using System;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Cards
{
    [TestClass]
    public class CardNumberTests
    {
        [TestMethod]
        public void PutCardInPlay_WithAPositiveNumber_KeepsIt()
        {
            BingoGame game = new BingoGame();

            game.PutCardInPlay(789);

            Assert.AreEqual(789L, game.CardInPlayAt(1));
            Assert.IsTrue(game.IsInPlay(789));
        }

        [TestMethod]
        public void PutCardInPlay_WithZero_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.PutCardInPlay(0));
        }

        [TestMethod]
        public void PutCardInPlay_WithANegativeNumber_Throws()
        {
            BingoGame game = new BingoGame();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => game.PutCardInPlay(-1));
        }

        [TestMethod]
        public void TheSameNumber_IsTheSameCard()
        {
            BingoGame game = new BingoGame();

            game.PutCardInPlay(789);
            game.PutCardInPlay(789);

            Assert.AreEqual(1, game.CardsInPlayCount);
        }

        [TestMethod]
        public void ADifferentNumber_IsAnotherCard()
        {
            BingoGame game = new BingoGame();

            game.PutCardInPlay(789);
            game.PutCardInPlay(790);

            Assert.AreEqual(2, game.CardsInPlayCount);
            Assert.IsFalse(game.IsInPlay(791));
        }
    }
}
