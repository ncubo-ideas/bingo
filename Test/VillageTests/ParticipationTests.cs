using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class ParticipationTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);

        private static Elder AnElder() => new Elder("Padre Gerardo");

        private static Festival AFestival(Elder elder) =>
            elder.CreateFestival("Día de la Madre", MothersDay, Today);

        [TestMethod]
        public void Elder_PutsUpForSaleTheNumbersHeWasGiven()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);

            elder.PutUpForSale(festival, new long[] { 789, 1002 });

            Assert.AreEqual(2, festival.ParticipationCount);
            Assert.IsTrue(festival.IsUpForSale(789));
            Assert.IsTrue(festival.IsUpForSale(1002));
            Assert.IsFalse(festival.IsUpForSale(1));
        }

        [TestMethod]
        public void Elder_PutsUpForSaleNumbersThatAreNotConsecutive()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);

            elder.PutUpForSale(festival, new long[] { 7300000, 12, 1001 });

            Assert.AreEqual(3, festival.ParticipationCount);
            Assert.IsTrue(festival.IsUpForSale(7300000));
        }

        [TestMethod]
        public void Elder_AddsMoreNumbersAfterTheSaleStarted()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 789 });

            elder.PutUpForSale(festival, new long[] { 1002, 1001 });

            Assert.AreEqual(3, festival.ParticipationCount);
        }

        [TestMethod]
        public void Elder_WillNotPutUpTheSameNumberTwice()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 789 });

            Assert.ThrowsException<VillageRuleException>(
                () => elder.PutUpForSale(festival, new long[] { 789 }));
        }

        [TestMethod]
        public void Elder_WillNotPutUpNumbersOnAFestivalHeDoesNotGovern()
        {
            Elder elder = AnElder();
            Festival other = AFestival(new Elder("Padre Mario"));

            Assert.ThrowsException<VillageRuleException>(
                () => elder.PutUpForSale(other, new long[] { 789 }));
        }

        [TestMethod]
        public void Elder_RejectsANumberThatCannotExist()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);

            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => elder.PutUpForSale(festival, new long[] { 0 }));
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => elder.PutUpForSale(festival, new long[] { -5 }));
        }

        [TestMethod]
        public void Elder_PutsUpNothingWhenTheBatchRepeatsANumberWithinItself()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);

            Assert.ThrowsException<VillageRuleException>(
                () => elder.PutUpForSale(festival, new long[] { 789, 1002, 789 }));
            Assert.AreEqual(0, festival.ParticipationCount,
                "a batch that breaks a rule must leave the festival untouched");
        }

        [TestMethod]
        public void Participation_IsIdentifiedByItsNumber()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 789 });

            Participation participation = festival.ParticipationNumbered(789);

            Assert.AreEqual(789L, participation.Number);
            Assert.AreEqual("789", participation.ToString());
        }

        [TestMethod]
        public void Participation_WithTheSameNumberIsTheSameParticipation()
        {
            Elder elder = AnElder();
            Festival august = AFestival(elder);
            Festival october = elder.CreateFestival("Kermés", new CalendarDay(2026, 10, 3), Today);
            elder.PutUpForSale(august, new long[] { 789 });
            elder.PutUpForSale(october, new long[] { 789 });

            Participation fromAugust = august.ParticipationNumbered(789);
            Participation fromOctober = october.ParticipationNumbered(789);

            Assert.AreEqual(fromAugust, fromOctober, "the number is the identity, nothing else is");
            Assert.AreEqual(fromAugust.GetHashCode(), fromOctober.GetHashCode());

            HashSet<Participation> both = new HashSet<Participation> { fromAugust, fromOctober };
            Assert.AreEqual(1, both.Count, "a set keyed by identity must collapse them into one");
        }

        [TestMethod]
        public void Participation_WithADifferentNumberIsAnotherParticipation()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 789, 1002 });

            Assert.AreNotEqual(
                festival.ParticipationNumbered(789),
                festival.ParticipationNumbered(1002));
        }

        [TestMethod]
        public void Participation_StartsOutInTheBookWithNobody()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 789 });

            Participation participation = festival.ParticipationNumbered(789);

            Assert.AreEqual("InTheBook", participation.Whereabouts);
            Assert.IsFalse(participation.IsSold);
        }

        [TestMethod]
        public void Festival_ComplainsAboutANumberItNeverPutUpForSale()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);

            Assert.ThrowsException<VillageRuleException>(() => festival.ParticipationNumbered(789));
        }

        [TestMethod]
        public void Festival_ListsWhatIsUpForSaleLowestNumberFirst()
        {
            Elder elder = AnElder();
            Festival festival = AFestival(elder);
            elder.PutUpForSale(festival, new long[] { 1002, 12, 789 });

            long[] numbers = new long[festival.ParticipationCount];
            for (int position = 1; position <= festival.ParticipationCount; position++)
                numbers[position - 1] = festival.ParticipationAt(position).Number;

            CollectionAssert.AreEqual(new long[] { 12, 789, 1002 }, numbers);
        }
    }
}
