using System;
using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    [TestClass]
    public class TakeCardOutOfPlayTests
    {
        private const long Hers = 787;
        private const long Theirs = 789;

        private static BingoGame ATableWithTwoCards()
        {
            BingoGame table = new BingoGame();

            table.PutCardInPlay(Hers);
            table.PutCardInPlay(Theirs);
            table.StartRound(new[] { "AnyRow" }, "OnClaim");

            return table;
        }

        [TestMethod]
        public void ACardCanLeaveBeforeTheFirstBallIsDrawn()
        {
            BingoGame table = ATableWithTwoCards();

            table.TakeCardOutOfPlay(table.CardInPlay(Hers));

            Assert.IsFalse(table.IsInPlay(Hers));
            Assert.AreEqual(1, table.CardsInPlayCount);
            Assert.AreEqual(Theirs, table.CardInPlayAt(1));
        }

        [TestMethod]
        public void ACardThatLeftStopsBeingWatchedByTheRound()
        {
            BingoGame table = ATableWithTwoCards();

            table.TakeCardOutOfPlay(table.CardInPlay(Hers));

            Assert.ThrowsException<BingoRuleException>(() => table.CardMarks(Hers),
                "un carton que salio no puede seguir contestando por sus marcas");
        }

        [TestMethod]
        public void ACardThatLeftCannotBeDeclaredAWinner()
        {
            BingoGame table = new BingoGame();

            table.PutCardInPlay(Hers);
            table.StartRound(new[] { "AnyRow" }, "OnClaim");
            table.TakeCardOutOfPlay(table.CardInPlay(Hers));

            foreach (int ball in BingoGame.ClosestFor(Hers, "AnyRow", Array.Empty<int>()))
                table.RegisterDraw(ball);

            Assert.IsFalse(table.HasCompleted(Hers, "AnyRow"),
                "el carton salio de la ronda: sus numeros ya no le pertenecen a nadie");
            Assert.AreEqual(0, table.PendingWinners.Count);
        }

        [TestMethod]
        public void ACardCannotLeaveOnceTheRoundHasStarted()
        {
            BingoGame table = ATableWithTwoCards();

            table.RegisterDraw(12);

            BingoRuleException falla = Assert.ThrowsException<BingoRuleException>(
                () => table.TakeCardOutOfPlay(table.CardInPlay(Hers)));

            StringAssert.Contains(falla.Message, "1 ball",
                "el motivo tiene que decir cuantas bolitas ya salieron");

            Assert.IsTrue(table.IsInPlay(Hers), "y el carton se queda donde estaba");
        }

        [TestMethod]
        public void ACardThatIsNotInPlayCannotLeave()
        {
            BingoGame table = ATableWithTwoCards();

            Assert.ThrowsException<BingoRuleException>(() => table.TakeCardOutOfPlay(table.CardInPlay(791)));
        }

        [TestMethod]
        public void TheOtherCardsAreUntouched()
        {
            BingoGame table = ATableWithTwoCards();

            table.TakeCardOutOfPlay(table.CardInPlay(Hers));
            table.RegisterDraw(BingoGame.ClosestFor(Theirs, "AnyRow", Array.Empty<int>())[0]);

            Assert.AreEqual(1, table.MarkedCount(Theirs),
                "sacar un carton no le mueve una marca al que se queda");
        }

        [TestMethod]
        public void ACardThatLeftCanComeBackAndPlaysFromScratch()
        {
            BingoGame table = ATableWithTwoCards();

            table.TakeCardOutOfPlay(table.CardInPlay(Hers));
            table.PutCardInPlay(Hers);

            Assert.IsTrue(table.IsInPlay(Hers));
            Assert.AreEqual(2, table.CardsInPlayCount);

            foreach (int ball in BingoGame.ClosestFor(Hers, "AnyRow", Array.Empty<int>()))
                table.RegisterDraw(ball);

            Assert.IsTrue(table.HasCompleted(Hers, "AnyRow"),
                "vuelve entero: no arrastra nada de su paso anterior");
        }
    }
}
