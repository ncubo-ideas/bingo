using System.Linq;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class CardsInPlayTests
    {
        private const long Card = 789;
        private const long OtherCard = 790;

        [TestMethod]
        public void ANewGame_HasNothingInPlay()
        {
            BingoGame game = new BingoGame();

            Assert.IsFalse(game.IsInPlay(Card));
            Assert.AreEqual(0, game.CardsInPlayCount);
        }

        [TestMethod]
        public void AGame_TellsWhichCardIsInPlayAndWhichIsNot()
        {
            BingoGame game = new BingoGame();

            game.PutCardInPlay(Card);

            Assert.IsTrue(game.IsInPlay(Card));
            Assert.IsFalse(game.IsInPlay(OtherCard));
        }

        [TestMethod]
        public void PuttingTheSameCardTwice_ChangesNothing()
        {
            BingoGame game = new BingoGame();

            game.PutCardInPlay(Card);
            game.PutCardInPlay(Card);

            Assert.IsTrue(game.IsInPlay(Card));
            Assert.AreEqual(1, game.CardsInPlayCount);
        }

        [TestMethod]
        public void AGame_KnowsWhatIsInPlayAfterTheRoundStarted()
        {
            BingoGame game = new BingoGame();
            game.PutCardInPlay(Card);
            game.StartRound(new[] { "Row0" });

            game.PutCardInPlay(OtherCard);

            Assert.IsTrue(game.IsInPlay(Card));
            Assert.IsTrue(game.IsInPlay(OtherCard));
            Assert.AreEqual(2, game.CardsInPlayCount);
        }

        [TestMethod]
        public void AGame_ListsEverythingItHasOnTheTable()
        {
            BingoGame game = new BingoGame();

            Assert.AreEqual(0, game.NumbersInPlay().Count);

            game.PutCardInPlay(Card);
            game.PutCardInPlay(OtherCard);

            CollectionAssert.AreEquivalent(
                new long[] { Card, OtherCard }, game.NumbersInPlay().ToArray());

            game.TakeCardOutOfPlay(game.CardInPlay(Card));

            CollectionAssert.AreEquivalent(
                new long[] { OtherCard }, game.NumbersInPlay().ToArray(),
                "lo que salio de la mesa ya no esta jugando");
        }
    }
}
