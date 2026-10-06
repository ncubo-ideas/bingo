using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class ElderTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);

        private static Elder AnElder() => new Elder("Padre Gerardo");

        [TestMethod]
        public void Elder_KeepsHisName()
        {
            Assert.AreEqual("Padre Gerardo", AnElder().Name);
        }

        [TestMethod]
        public void Elder_RefusesToExistWithoutAName()
        {
            Assert.ThrowsException<ArgumentException>(() => new Elder(""));
            Assert.ThrowsException<ArgumentException>(() => new Elder("   "));
            Assert.ThrowsException<ArgumentException>(() => new Elder(null));
        }

        [TestMethod]
        public void Elder_StartsOutGoverningNothing()
        {
            Assert.AreEqual(0, AnElder().FestivalCount);
        }

        [TestMethod]
        public void Elder_CreatesAFestivalWithItsNameAndDay()
        {
            Elder elder = AnElder();

            Festival festival = elder.CreateFestival("Día de la Madre", MothersDay, Today);

            Assert.AreEqual("Día de la Madre", festival.Name);
            Assert.AreEqual(MothersDay, festival.Day);
            Assert.AreEqual(1, elder.FestivalCount);
        }

        [TestMethod]
        public void Elder_GovernsTheFestivalsHeCreated()
        {
            Elder elder = AnElder();
            Elder anotherElder = new Elder("Padre Mario");

            Festival festival = elder.CreateFestival("Día de la Madre", MothersDay, Today);

            Assert.IsTrue(elder.Governs(festival));
            Assert.IsFalse(anotherElder.Governs(festival));
            Assert.AreSame(elder, festival.Elder);
        }

        [TestMethod]
        public void Elder_CanCreateSeveralFestivals()
        {
            Elder elder = AnElder();

            elder.CreateFestival("Día de la Madre", MothersDay, Today);
            elder.CreateFestival("Kermés de octubre", new CalendarDay(2026, 10, 3), Today);

            Assert.AreEqual(2, elder.FestivalCount);
            Assert.AreEqual("Día de la Madre", elder.FestivalAt(1).Name);
            Assert.AreEqual("Kermés de octubre", elder.FestivalAt(2).Name);
        }

        [TestMethod]
        public void Elder_WillNotAnnounceAFestivalForADayThatAlreadyPassed()
        {
            Elder elder = AnElder();

            Assert.ThrowsException<VillageRuleException>(
                () => elder.CreateFestival("Bingo de ayer", new CalendarDay(2026, 7, 29), Today));
        }

        [TestMethod]
        public void Elder_CanAnnounceAFestivalForToday()
        {
            Festival festival = AnElder().CreateFestival("Bingo de hoy", Today, Today);

            Assert.AreEqual(0, festival.DaysUntil(Today));
        }

        [TestMethod]
        public void Elder_RefusesTwoFestivalsWithTheSameName()
        {
            Elder elder = AnElder();
            elder.CreateFestival("Día de la Madre", MothersDay, Today);

            Assert.ThrowsException<VillageRuleException>(
                () => elder.CreateFestival("Día de la Madre", new CalendarDay(2026, 10, 3), Today));
        }

        [TestMethod]
        public void Elder_KnowsWhichFestivalHeIsStandingAt()
        {
            Elder elder = AnElder();
            Festival august = elder.CreateFestival("Día de la Madre", MothersDay, Today);
            Festival october = elder.CreateFestival("Kermés de octubre", new CalendarDay(2026, 10, 3), Today);

            Assert.AreSame(october, elder.StandingAt, "creating a festival should leave him standing at it");

            elder.StandAt(august);

            Assert.AreSame(august, elder.StandingAt);
        }

        [TestMethod]
        public void Elder_CannotStandAtAFestivalHeDoesNotGovern()
        {
            Elder elder = AnElder();
            Festival other = new Elder("Padre Mario").CreateFestival("Bingo ajeno", MothersDay, Today);

            Assert.ThrowsException<VillageRuleException>(() => elder.StandAt(other));
        }

        [TestMethod]
        public void Festival_CannotBeCreatedWithoutAnElder()
        {
            ConstructorInfo[] publicConstructors = typeof(Festival)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            Assert.AreEqual(0, publicConstructors.Length,
                "a festival must only come to life through an Elder, so it cannot have a public constructor");
        }

        [TestMethod]
        public void Festival_TellsHowManyDaysAreLeft()
        {
            Festival festival = AnElder().CreateFestival("Día de la Madre", MothersDay, Today);

            Assert.AreEqual(16, festival.DaysUntil(Today));
            Assert.AreEqual(1, festival.DaysUntil(new CalendarDay(2026, 8, 14)));
        }

        [TestMethod]
        public void Festival_IsAnnouncedUntilTheElderOpensIt()
        {
            Elder elder = AnElder();
            Festival festival = elder.CreateFestival("Día de la Madre", MothersDay, Today);

            Assert.AreEqual("Announced", festival.State);
        }

        [TestMethod]
        public void Elder_ListsHisFestivalsInTheOrderHeCreatedThem()
        {
            Elder elder = AnElder();
            elder.CreateFestival("Día de la Madre", MothersDay, Today);
            elder.CreateFestival("Kermés de octubre", new CalendarDay(2026, 10, 3), Today);

            string[] names = Enumerable.Range(1, elder.FestivalCount)
                .Select(position => elder.FestivalAt(position).Name)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "Día de la Madre", "Kermés de octubre" }, names);
        }
    }
}
