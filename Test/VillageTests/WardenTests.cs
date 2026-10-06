using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class WardenTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);
        private static readonly CalendarDay OctoberFair = new CalendarDay(2026, 10, 3);

        private Elder _elder;
        private Warden _tomas;
        private Festival _august;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
            _tomas = new Warden("Tomas");
            _august = _elder.CreateFestival("Día de la Madre", MothersDay, Today);
        }

        [TestMethod]
        public void Elder_CannotAppointAWardenWithoutAName()
        {
            Assert.ThrowsException<ArgumentException>(() => _elder.AppointWarden(_august, new Warden("")));
            Assert.ThrowsException<ArgumentException>(() => _elder.AppointWarden(_august, new Warden("   ")));
            Assert.ThrowsException<ArgumentException>(() => _elder.AppointWarden(_august, new Warden(null)));

            Assert.IsFalse(_august.HasWarden);
        }

        [TestMethod]
        public void TheAppointedWarden_KeepsHisName()
        {
            _elder.AppointWarden(_august, _tomas);

            Assert.AreEqual("Tomas", _august.Warden.Name);
            Assert.AreEqual("Tomas", _august.Warden.ToString());
        }

        [TestMethod]
        public void Festival_StartsWithNobodyAppointed()
        {
            Assert.IsNull(_august.Warden);
            Assert.IsFalse(_august.HasWarden);
        }

        [TestMethod]
        public void Elder_AppointsSomebodyToKeyInWhatHeCallsOut()
        {
            _elder.AppointWarden(_august, _tomas);

            Assert.AreSame(_tomas, _august.Warden);
            Assert.IsTrue(_august.HasWarden);
        }

        [TestMethod]
        public void Elder_CannotAppointToAFestivalHeDoesNotGovern()
        {
            Festival other = new Elder("Padre Mario")
                .CreateFestival("Bingo ajeno", MothersDay, Today);

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.AppointWarden(other, _tomas));
        }

        [TestMethod]
        public void Elder_ReplacesTheWardenWhenHeAppointsAnother()
        {
            Warden rosa = new Warden("Rosa Chaves");
            _elder.AppointWarden(_august, _tomas);

            _elder.AppointWarden(_august, rosa);

            Assert.AreSame(rosa, _august.Warden, "there is one person keying in, not two");
        }

        [TestMethod]
        public void Elder_CanAppointTheSameWardenAgainWithoutComplaining()
        {
            _elder.AppointWarden(_august, _tomas);
            _elder.AppointWarden(_august, _tomas);

            Assert.AreSame(_tomas, _august.Warden);
        }

        [TestMethod]
        public void Elder_CanDismissTheWarden()
        {
            _elder.AppointWarden(_august, _tomas);

            _elder.DismissWarden(_august);

            Assert.IsNull(_august.Warden);
            Assert.IsFalse(_august.HasWarden);
        }

        [TestMethod]
        public void Elder_CannotDismissWhenNobodyWasAppointed()
        {
            Assert.ThrowsException<VillageRuleException>(() => _elder.DismissWarden(_august));
        }

        [TestMethod]
        public void Warden_ServesInMoreThanOneFestival()
        {
            Festival october = _elder.CreateFestival("Kermés de octubre", OctoberFair, Today);

            _elder.AppointWarden(_august, _tomas);
            _elder.AppointWarden(october, _tomas);

            Assert.AreSame(_tomas, _august.Warden);
            Assert.AreSame(_tomas, october.Warden);

            _elder.DismissWarden(_august);

            Assert.IsNull(_august.Warden);
            Assert.AreSame(_tomas, october.Warden, "October must not lose him");
        }

        [TestMethod]
        public void Warden_KeepsNothingButHisName()
        {
            FieldInfo[] fields = typeof(Warden).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.AreEqual(1, fields.Length,
                "a warden only keeps his name; being appointed belongs to each funcion, "
                + "so serving in August cannot follow him into October");
        }

        [TestMethod]
        public void Warden_CannotReachTheGameFromThisDomain()
        {
            MethodInfo[] declared = Array.FindAll(
                typeof(Warden).GetMethods(BindingFlags.Instance | BindingFlags.Public
                                          | BindingFlags.DeclaredOnly),
                method => !method.IsSpecialName && method.Name != "ToString");

            Assert.AreEqual(0, declared.Length,
                "a warden does have verbs — he keys in what the elder calls out and corrects a "
                + "mistyped number — but they belong to the other domain. Giving him one here would "
                + "force this assembly to reference the game, and the whole separation falls. "
                + "His verbs live in the actor that hosts both domains, not in this class.");
        }
    }
}
