using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class PrizeAndRaffleTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);

        private Elder _elder;
        private Festival _festival;
        private Villager _luis;
        private Villager _ana;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
            _festival = _elder.CreateFestival("Día de la Madre", MothersDay, Today);
            _luis = new Villager("Luis Mora", "8888-3333");
            _ana = new Villager("Ana Solís", "8888-2222");
            _elder.PutUpForSale(_festival, new long[] { 789, 1002, 1001 });
            _elder.SellTo(_festival, _festival.ParticipationNumbered(789), _luis);
            _elder.SellTo(_festival, _festival.ParticipationNumbered(1002), _ana);
        }

        [TestMethod]
        public void APrizeFromAnotherFestival_CannotBeTheMainPrizeHere()
        {
            Festival october = _elder.CreateFestival("Kermés de octubre", new CalendarDay(2026, 10, 3), Today);
            Prize octobers = _elder.OfferPrize(october, "Bicicleta");
            _elder.OfferPrize(_festival, "Arrocera");

            Assert.ThrowsException<VillageRuleException>(() => _elder.MarkAsMainPrize(_festival, octobers));

            Assert.AreEqual(0, _festival.MainPrizePosition);
        }

        [TestMethod]
        public void Elder_OffersPrizesAndTheyKeepTheOrderTheyCameIn()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Arrocera");
            _elder.OfferPrize(_festival, "Bicicleta montañera");

            Assert.AreEqual(3, _festival.PrizeCount);
            Assert.AreEqual("Coffee maker", _festival.PrizeAt(1).What);
            Assert.AreEqual("Arrocera", _festival.PrizeAt(2).What);
            Assert.AreEqual("Bicicleta montañera", _festival.PrizeAt(3).What);
            Assert.AreEqual(1, _festival.PrizeAt(1).Position);
        }

        [TestMethod]
        public void Elder_MarksOnePrizeAsTheMainOneEvenIfItIsNotTheFirst()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Arrocera");
            _elder.OfferPrize(_festival, "Bicicleta montañera");

            _elder.MarkAsMainPrize(_festival, _festival.PrizeAt(3));

            Assert.IsTrue(_festival.PrizeAt(3).IsMain);
            Assert.IsFalse(_festival.PrizeAt(1).IsMain);
            Assert.AreEqual(3, _festival.MainPrizePosition);
        }

        [TestMethod]
        public void Festival_KeepsAtMostOneMainPrize()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Bicicleta montañera");
            _elder.MarkAsMainPrize(_festival, _festival.PrizeAt(1));

            _elder.MarkAsMainPrize(_festival, _festival.PrizeAt(2));

            Assert.IsFalse(_festival.PrizeAt(1).IsMain, "the mark moves, it does not multiply");
            Assert.IsTrue(_festival.PrizeAt(2).IsMain);
            Assert.AreEqual(2, _festival.MainPrizePosition);
        }

        [TestMethod]
        public void Festival_HasNoMainPrizeUntilOneIsMarked()
        {
            _elder.OfferPrize(_festival, "Coffee maker");

            Assert.AreEqual(0, _festival.MainPrizePosition);
        }

        [TestMethod]
        public void Elder_RaisesOneRaffleWithTwoPrizesAtOnce()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Bicicleta montañera");

            Raffle raffle = _elder.StartRaffle(_festival, new int[] { 1, 2 });

            Assert.AreEqual(1, raffle.Number);
            Assert.AreEqual(2, raffle.PrizeCount);
            Assert.AreEqual("Coffee maker", raffle.PrizeAt(1).What);
            Assert.IsFalse(raffle.IsSettled);
            Assert.AreSame(raffle, _festival.CurrentRaffle);
        }

        [TestMethod]
        public void Elder_AssignsEachPrizeToTheNumberHeWasToldWon()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Bicicleta montañera");
            Raffle raffle = _elder.StartRaffle(_festival, new int[] { 1, 2 });

            raffle.AssignPrizeTo(1, 789);
            raffle.AssignPrizeTo(2, 1002);

            Assert.AreEqual(789L, _festival.PrizeAt(1).TakenBy);
            Assert.AreEqual(1002L, _festival.PrizeAt(2).TakenBy);
            Assert.IsTrue(_festival.PrizeAt(1).IsGiven);
            Assert.IsTrue(raffle.IsSettled);
        }

        [TestMethod]
        public void Raffle_WillNotGiveTheSamePrizeTwice()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            Raffle raffle = _elder.StartRaffle(_festival, new int[] { 1 });
            raffle.AssignPrizeTo(1, 789);

            Assert.ThrowsException<VillageRuleException>(() => raffle.AssignPrizeTo(1, 1002));
        }

        [TestMethod]
        public void Raffle_WillNotGiveAPrizeThatIsNotInPlay()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Arrocera");
            Raffle raffle = _elder.StartRaffle(_festival, new int[] { 1 });

            Assert.ThrowsException<VillageRuleException>(() => raffle.AssignPrizeTo(2, 789));
        }

        [TestMethod]
        public void Raffle_WillNotGiveAPrizeToANumberNobodyBought()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            Raffle raffle = _elder.StartRaffle(_festival, new int[] { 1 });

            Assert.ThrowsException<VillageRuleException>(() => raffle.AssignPrizeTo(1, 1001),
                "1001 was put up for sale but nobody bought it");
            Assert.ThrowsException<VillageRuleException>(() => raffle.AssignPrizeTo(1, 4444),
                "4444 was never even put up for sale");
        }

        [TestMethod]
        public void Elder_WillNotStartAnotherRaffleWhileThisOneIsPending()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Arrocera");
            _elder.StartRaffle(_festival, new int[] { 1 });

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.StartRaffle(_festival, new int[] { 2 }));
        }

        [TestMethod]
        public void Elder_StartsTheNextRaffleOnceTheLastOneIsSettled()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Arrocera");
            Raffle first = _elder.StartRaffle(_festival, new int[] { 1 });
            first.AssignPrizeTo(1, 789);

            Raffle second = _elder.StartRaffle(_festival, new int[] { 2 });

            Assert.AreEqual(2, second.Number);
            Assert.AreEqual(2, _festival.RaffleCount);
            Assert.AreSame(first, _festival.RaffleAt(1));
        }

        [TestMethod]
        public void Elder_WillNotRaffleAPrizeThatWasAlreadyGiven()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            Raffle first = _elder.StartRaffle(_festival, new int[] { 1 });
            first.AssignPrizeTo(1, 789);

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.StartRaffle(_festival, new int[] { 1 }));
        }

        [TestMethod]
        public void Elder_WillNotStartARaffleWithoutAnyPrize()
        {
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.StartRaffle(_festival, new int[0]));
        }

        [TestMethod]
        public void Elder_WillNotOfferAPrizeWithoutSayingWhatItIs()
        {
            Assert.ThrowsException<ArgumentException>(() => _elder.OfferPrize(_festival, ""));
        }

        [TestMethod]
        public void Prize_IsNotGivenUntilItIsAssigned()
        {
            _elder.OfferPrize(_festival, "Coffee maker");

            Prize prize = _festival.PrizeAt(1);

            Assert.IsFalse(prize.IsGiven);
            Assert.AreEqual(0L, prize.TakenBy);
        }

        [TestMethod]
        public void Festival_CountsWhatIsStillPendingToGiveAway()
        {
            _elder.OfferPrize(_festival, "Coffee maker");
            _elder.OfferPrize(_festival, "Arrocera");
            Raffle raffle = _elder.StartRaffle(_festival, new int[] { 1 });
            raffle.AssignPrizeTo(1, 789);

            Assert.AreEqual(1, _festival.GivenPrizeCount);
            Assert.AreEqual(1, _festival.PendingPrizeCount);
        }
    }
}
