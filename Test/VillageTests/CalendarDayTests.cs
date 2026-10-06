using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class CalendarDayTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);

        private Elder _elder;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
        }

        [TestMethod]
        public void TheFestival_KeepsTheDayItWasAnnouncedFor()
        {
            Festival august = _elder.CreateFestival("Día de la Madre", new CalendarDay(2026, 8, 15), Today);

            Assert.AreEqual(2026, august.Day.Year);
            Assert.AreEqual(8, august.Day.Month);
            Assert.AreEqual(15, august.Day.Day);
        }

        [TestMethod]
        public void TheFestivalDay_ReadsAsAnIsoDate()
        {
            Festival august = _elder.CreateFestival("Día de la Madre", new CalendarDay(2026, 8, 15), Today);

            Assert.AreEqual("2026-08-15", august.Day.ToString());
        }

        [TestMethod]
        public void ADayThatAlreadyPassed_IsNamedAsAnIsoDateInTheRefusal()
        {
            VillageRuleException refused = Assert.ThrowsException<VillageRuleException>(
                () => _elder.CreateFestival("Too late", new CalendarDay(2026, 7, 29), Today));

            StringAssert.Contains(refused.Message, "2026-07-29");
        }

        [TestMethod]
        public void AFestivalCannotBeAnnouncedForADayThatDoesNotExist()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => _elder.CreateFestival("February the thirtieth", new CalendarDay(2026, 2, 30), Today));
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => _elder.CreateFestival("Month thirteen", new CalendarDay(2026, 13, 1), Today));
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => _elder.CreateFestival("Day zero", new CalendarDay(2026, 8, 0), Today));

            Assert.AreEqual(0, _elder.FestivalCount);
        }

        [TestMethod]
        public void AFestivalCanBeAnnouncedForALeapDay()
        {
            Festival leap = _elder.CreateFestival("Leap day", new CalendarDay(2028, 2, 29), Today);

            Assert.AreEqual("2028-02-29", leap.Day.ToString());
        }

        [TestMethod]
        public void TheFestival_CountsTheDaysLeft()
        {
            Festival august = _elder.CreateFestival("Día de la Madre", new CalendarDay(2026, 8, 15), Today);
            Festival tonight = _elder.CreateFestival("Tonight", Today, Today);

            Assert.AreEqual(16, august.DaysUntil(Today));
            Assert.AreEqual(0, tonight.DaysUntil(Today));
            Assert.AreEqual(-1, tonight.DaysUntil(new CalendarDay(2026, 7, 31)));
        }

        [TestMethod]
        public void TheFestival_CountsTheDaysLeftAcrossAMonthAndAYear()
        {
            Festival september = _elder.CreateFestival("September", new CalendarDay(2026, 9, 1), Today);
            Festival newYear = _elder.CreateFestival("New year", new CalendarDay(2027, 1, 1), Today);

            Assert.AreEqual(1, september.DaysUntil(new CalendarDay(2026, 8, 31)));
            Assert.AreEqual(1, newYear.DaysUntil(new CalendarDay(2026, 12, 31)));
        }

        [TestMethod]
        public void TheElder_AnnouncesForTodayOrLaterButNotForYesterday()
        {
            _elder.CreateFestival("Today", Today, Today);
            _elder.CreateFestival("Tomorrow", new CalendarDay(2026, 7, 31), Today);

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.CreateFestival("Yesterday", new CalendarDay(2026, 7, 29), Today));
            Assert.AreEqual(2, _elder.FestivalCount);
        }

        [TestMethod]
        public void TheFestivalDay_IsTheSameDayWhenTheDateMatches()
        {
            Festival august = _elder.CreateFestival("Día de la Madre", new CalendarDay(2026, 8, 15), Today);
            CalendarDay sameDate = new CalendarDay(2026, 8, 15);

            Assert.AreEqual(sameDate, august.Day);
            Assert.AreEqual(sameDate.GetHashCode(), august.Day.GetHashCode());
            Assert.AreNotEqual(new CalendarDay(2026, 8, 16), august.Day);
        }
    }
}
