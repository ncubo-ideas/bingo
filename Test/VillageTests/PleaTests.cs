using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    /// <summary>
    /// Deshacer una venta se plantea, no se hace. Una colaboradora dice "esta venta
    /// esta mal", y el organizador es quien le da efecto o la rechaza.
    ///
    /// La escena: Rita pidio dos cartones y Maria le marco tres. El tercero quedo
    /// vendido, Rita lo tiene en su telefono, y nadie mas se lo puede vender.
    /// </summary>
    [TestClass]
    public class PleaTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);

        private Elder _elder;
        private Merchant _maria;
        private Merchant _sonia;
        private Villager _rita;
        private Festival _august;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
            _maria = new Merchant("María Rojas");
            _sonia = new Merchant("Sonia Vargas");
            _rita = new Villager("Rita Mora", "8888-3333");
            _august = _elder.CreateFestival("Día de la Madre", MothersDay, Today);

            _elder.PutUpForSale(_august, new long[] { 1, 2, 3, 4, 5 });
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });
            _elder.HandTo(_august, _sonia, new long[] { 4 });

            _maria.SellThemTo(_august, new long[] { 1, 2, 3 }, _rita);
        }

        // ── plantearla ───────────────────────────────────────────────────────────

        [TestMethod]
        public void APleaOrACardFromAnotherFestival_IsRefusedHere()
        {
            Festival october = _elder.CreateFestival("Kermés de octubre", new CalendarDay(2026, 10, 3), Today);
            _elder.PutUpForSale(october, new long[] { 3 });
            _elder.HandTo(october, _maria, new long[] { 3 });
            _maria.SellThemTo(october, new long[] { 3 }, _rita);
            Plea octobers = _maria.Plead(october, october.ParticipationNumbered(3));

            Assert.ThrowsException<VillageRuleException>(() => _elder.Grant(_august, octobers));
            Assert.ThrowsException<VillageRuleException>(() => _elder.Refuse(_august, octobers));
            Assert.ThrowsException<VillageRuleException>(() => _maria.Plead(_august, october.ParticipationNumbered(3)));

            Assert.AreEqual(0, _august.PleaCount);
            Assert.AreEqual("Sold", _august.ParticipationNumbered(3).Whereabouts);
            Assert.IsTrue(octobers.IsWaiting);
        }

        [TestMethod]
        public void SheRaisesAPleaAndNothingMovesYet()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(3));

            Assert.AreEqual(1, _august.PleaCount);
            Assert.AreEqual("Raised", _august.PleaAt(1).HowItStands);
            Assert.AreEqual(3, _august.PleaAt(1).Number);
            Assert.AreEqual("María Rojas", _august.PleaAt(1).RaisedBy);

            Assert.AreEqual(3, _august.CountSoldTo(_rita),
                "plantearla no deshace nada: el carton sigue vendido hasta que el elder resuelva");
            Assert.AreEqual("Sold", _august.ParticipationNumbered(3).Whereabouts);
        }

        [TestMethod]
        public void AnyMerchantCanRaiseIt_NotOnlyTheOneWhoSold()
        {
            _sonia.Plead(_august, _august.ParticipationNumbered(3));

            Assert.AreEqual("Sonia Vargas", _august.PleaAt(1).RaisedBy,
                "quien la plantea no tiene que ser quien vendio: en la puerta atiende la que este");
        }

        [TestMethod]
        public void NobodyPleadsOverSomethingThatWasNeverSold()
        {
            Assert.ThrowsException<VillageRuleException>(() => _maria.Plead(_august, _august.ParticipationNumbered(4)),
                "el 4 esta en la mano de Sonia, no se le vendio a nadie");
        }

        [TestMethod]
        public void TheSameSaleIsNotPleadedTwiceWhileItIsStillOpen()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(3));

            Assert.ThrowsException<VillageRuleException>(() => _sonia.Plead(_august, _august.ParticipationNumbered(3)),
                "ya hay una planteada sobre ese carton esperando al elder");
        }

        // ── el elder resuelve ────────────────────────────────────────────────────

        [TestMethod]
        public void TheElderGrantsItAndTheCardGoesBackToTheBook()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(3));

            _elder.Grant(_august, _august.PleaAt(1));

            Assert.AreEqual("Granted", _august.PleaAt(1).HowItStands);
            Assert.AreEqual("InTheBook", _august.ParticipationNumbered(3).Whereabouts,
                "vuelve al libro, no a la mano de nadie: quien vendio pudo haberse ido ya");
            Assert.IsNull(_august.ParticipationNumbered(3).Buyer);
            Assert.IsNull(_august.ParticipationNumbered(3).SoldBy);

            Assert.AreEqual(2, _august.CountSoldTo(_rita), "Rita se queda con los dos que pago");
            Assert.AreEqual(2, _august.CountSoldBy(_maria));
        }

        [TestMethod]
        public void WhatCameBackCanBeHandedOutAndSoldAgain()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(3));
            _elder.Grant(_august, _august.PleaAt(1));

            _elder.HandTo(_august, _sonia, new long[] { 3 });

            Villager ana = new Villager("Ana Solís", "8888-1111");
            _sonia.SellThemTo(_august, new long[] { 3 }, ana);

            Assert.AreSame(ana, _august.ParticipationNumbered(3).Buyer);
            Assert.AreEqual(1, _august.CountSoldBy(_sonia));
        }

        [TestMethod]
        public void TheElderRefusesItAndTheSaleStands()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(3));

            _elder.Refuse(_august, _august.PleaAt(1));

            Assert.AreEqual("Refused", _august.PleaAt(1).HowItStands);
            Assert.AreEqual(3, _august.CountSoldTo(_rita), "la venta sigue en pie");
            Assert.AreSame(_rita, _august.ParticipationNumbered(3).Buyer);
        }

        [TestMethod]
        public void ARefusedPleaLetsSomebodyRaiseItAgain()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(3));
            _elder.Refuse(_august, _august.PleaAt(1));

            _sonia.Plead(_august, _august.ParticipationNumbered(3));

            Assert.AreEqual(2, _august.PleaCount);
            Assert.AreEqual("Raised", _august.PleaAt(2).HowItStands,
                "un no del elder no cierra el tema para siempre");
        }

        [TestMethod]
        public void OnceResolvedItCannotBeResolvedAgain()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(3));
            _elder.Grant(_august, _august.PleaAt(1));

            Assert.ThrowsException<VillageRuleException>(() => _elder.Grant(_august, _august.PleaAt(1)));
            Assert.ThrowsException<VillageRuleException>(() => _elder.Refuse(_august, _august.PleaAt(1)));
        }

        [TestMethod]
        public void OnlyTheElderWhoGovernsResolvesIt()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(3));

            Elder anotherElder = new Elder("Padre Rafael");

            Assert.ThrowsException<VillageRuleException>(() => anotherElder.Grant(_august, _august.PleaAt(1)));
            Assert.ThrowsException<VillageRuleException>(() => anotherElder.Refuse(_august, _august.PleaAt(1)));
        }

        // ── la venta de puerta ───────────────────────────────────────────────────

        [TestMethod]
        public void ASaleTheElderMadeAtTheDoorCanBeUndoneToo()
        {
            _elder.SellTo(_august, _august.ParticipationNumbered(5), _rita);

            _sonia.Plead(_august, _august.ParticipationNumbered(5));
            _elder.Grant(_august, _august.PleaAt(1));

            Assert.AreEqual("InTheBook", _august.ParticipationNumbered(5).Whereabouts,
                "la vendio el padre y no quedo vendedor, pero al libro vuelve igual");
        }

        // ── lo que el elder ve ───────────────────────────────────────────────────

        [TestMethod]
        public void TheElderSeesWhatIsStillWaitingOnHim()
        {
            _maria.Plead(_august, _august.ParticipationNumbered(1));
            _maria.Plead(_august, _august.ParticipationNumbered(3));
            _elder.Refuse(_august, _august.PleaAt(1));

            CollectionAssert.AreEqual(new long[] { 3 },
                _august.NumbersPleaded().ToArray(),
                "solo las que siguen esperando: la rechazada ya no cuenta");
        }
    }
}
