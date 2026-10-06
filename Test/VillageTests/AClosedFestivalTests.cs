using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    /// <summary>
    /// Una funcion cerrada no acepta mas compromisos.
    ///
    /// La regla existia, pero solo del lado de la app: el pueblo dejaba poner cartones en
    /// venta, repartir talonarios y hasta vender despues de que la noche habia terminado.
    /// Eso no es un error de pantalla -- el diario re-ejecuta lo que se escribio, asi que
    /// una venta colada despues del cierre vuelve en cada rehidratacion, para siempre.
    ///
    /// Recoger lo que quedo en manos de alguien SI se puede despues de cerrar: eso es
    /// terminar de cerrar la noche, no abrirla otra vez.
    /// </summary>
    [TestClass]
    public class AClosedFestivalTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);

        private Elder _elder;
        private Merchant _maria;
        private Villager _luis;
        private Festival _august;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
            _maria = new Merchant("María Rojas");
            _luis = new Villager("Luis Mora", "8888-3333");
            _august = _elder.CreateFestival("Día de la Madre", MothersDay, Today);
            _elder.PutUpForSale(_august, new long[] { 1, 2, 3, 4, 5 });
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "María Rojas");
            _maria = _august.MerchantMarked("a1b2c3");
            _elder.HandTo(_august, _maria, new long[] { 1, 2 });
        }

        private void CloseIt() => _elder.CloseFestival(_august);

        [TestMethod]
        public void NobodyPutsMoreCardsUpForSaleAfterItIsOver()
        {
            CloseIt();

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.PutUpForSale(_august, new long[] { 6, 7 }),
                "la funcion ya termino: no se le pueden agregar cartones");
        }

        [TestMethod]
        public void NobodyGetsATicketBookAfterItIsOver()
        {
            CloseIt();

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.HandTo(_august, _maria, new long[] { 3 }),
                "la funcion ya termino: no se reparten mas cartones");
        }

        [TestMethod]
        public void NothingIsSoldAfterItIsOver()
        {
            CloseIt();

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.SellThemTo(_august, new long[] { 1 }, _luis),
                "la funcion ya termino: no se vende mas");
        }

        [TestMethod]
        public void NoNewErrandIsHandedOutAfterItIsOver()
        {
            CloseIt();

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Entrust(_august, "Selling", "z9y8x7"),
                "la funcion ya termino: no se dan mas papelitos");
        }

        /// <summary>Lo que SI se puede: recoger lo que quedo en la mano. Es el gesto de
        /// cerrar, y el organizador tiene que poder hacerlo despues de terminar.</summary>
        [TestMethod]
        public void WhatIsStillInHerHandsCanBeCollectedAfterItIsOver()
        {
            CloseIt();

            _maria.GiveBack(_august, new long[] { 1, 2 });

            Assert.AreEqual(0, _august.CountWith(_maria));
        }

        /// <summary>Y antes de cerrar todo sigue funcionando igual: la guarda nueva no le
        /// cambia la vida a una funcion que esta corriendo.</summary>
        [TestMethod]
        public void WhileItIsStillGoingEverythingWorksAsBefore()
        {
            _elder.PutUpForSale(_august, new long[] { 6 });
            _elder.HandTo(_august, _maria, new long[] { 3 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.AreEqual(1, _august.CountSoldBy(_maria));
        }
    }
}
