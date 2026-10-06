using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class SoldNumbersTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay FestivalDay = new CalendarDay(2026, 8, 15);

        private static Elder AnElder() => new Elder("Padre Gerardo");

        private static Festival AFestival(Elder elder) =>
            elder.CreateFestival("Funcion de agosto", FestivalDay, Today);

        [TestMethod]
        public void Festival_HasNoSoldNumbersBeforeAnybodyBuys()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 787, 789, 791 });

            Assert.AreEqual(0, festival.SoldCount);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => festival.SoldNumberAt(1));
        }

        [TestMethod]
        public void Festival_WalksTheSoldNumbersOneByOne()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 787, 789, 791 });

            elder.SellTo(festival, festival.ParticipationNumbered(791), new Villager("Rita", "la esquina"));
            elder.SellTo(festival, festival.ParticipationNumbered(787), new Villager("Chepe", "la pulpería"));

            Assert.AreEqual(2, festival.SoldCount);
            Assert.AreEqual(787L, festival.SoldNumberAt(1));
            Assert.AreEqual(791L, festival.SoldNumberAt(2));
        }

        [TestMethod]
        public void Festival_SkipsTheNumbersThatNobodyBought()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 100, 200, 300, 400 });

            elder.SellTo(festival, festival.ParticipationNumbered(300), new Villager("Rita", "la esquina"));

            Assert.AreEqual(1, festival.SoldCount);
            Assert.AreEqual(300L, festival.SoldNumberAt(1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => festival.SoldNumberAt(2));
        }

        [TestMethod]
        public void Festival_CountsWhatItWalks()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 10, 20, 30, 40, 50 });

            Villager rita = new Villager("Rita", "la esquina");
            elder.SellTo(festival, festival.ParticipationNumbered(20), rita);
            elder.SellTo(festival, festival.ParticipationNumbered(40), rita);
            elder.SellTo(festival, festival.ParticipationNumbered(50), rita);

            for (int position = 1; position <= festival.SoldCount; position++)
                Assert.IsTrue(festival.WasBought(festival.SoldNumberAt(position)));
        }

        [TestMethod]
        public void Festival_RejectsAPositionBeforeTheFirst()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 787 });
            elder.SellTo(festival, festival.ParticipationNumbered(787), new Villager("Rita", "la esquina"));

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => festival.SoldNumberAt(0));
        }
    }
}
