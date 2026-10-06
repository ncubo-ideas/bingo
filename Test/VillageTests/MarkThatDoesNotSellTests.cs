using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    /// <summary>
    /// Preguntarle los cartones a un papelito que no es de vender.
    ///
    /// Esto mordio TRES veces en la app, siempre igual: alguien recorre a toda la gente o a
    /// todos los encargos, le toca uno que canta, y `NumbersWith(MerchantMarked(marca))`
    /// revienta. No contesta vacio: TIRA, asi que se cae el recorrido completo y con el lo que
    /// estaba armando. La ultima fue la peor -- el organizador perdio un bingo al arrancar y
    /// dijo que el village estaba vacio, con siete cartones vendidos y una bolita adentro.
    ///
    /// Quien canta no tiene cartones. Eso es una respuesta, no un error, y estos tests la
    /// fijan para que no haya una cuarta vez.
    /// </summary>
    [TestClass]
    public class MarkThatDoesNotSellTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);

        private Elder _elder;
        private Festival _august;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
            _august = _elder.CreateFestival("Día de la Madre", MothersDay, Today);
            _elder.PutUpForSale(_august, new long[] { 1, 2, 3, 4, 5 });

            _elder.Entrust(_august, "Selling", "vende01");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("vende01"), "María Rojas");
            _elder.HandTo(_august, _august.MerchantMarked("vende01"), new long[] { 1, 2 });

            _elder.Entrust(_august, "Calling", "canta01");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("canta01"), "Tere Solano");
        }

        [TestMethod]
        public void WhoSells_SaysWhatSheHolds()
        {
            CollectionAssert.AreEqual(
                new long[] { 1, 2 }, _august.NumbersHeldBy("vende01").ToArray());
        }

        [TestMethod]
        public void WhoCalls_SaysSheHoldsNone()
        {
            Assert.AreEqual(0, _august.NumbersHeldBy("canta01").Count,
                "quien canta no toca cartones, y eso se contesta -- no se tira");
            Assert.AreEqual(0, _august.NumbersSoldUnder("canta01").Count);
        }

        [TestMethod]
        public void AMarkThatDoesNotExist_DoesNotThrowEither()
        {
            Assert.AreEqual(0, _august.NumbersHeldBy("no-existe").Count);
            Assert.AreEqual(0, _august.NumbersSoldUnder("no-existe").Count);
            Assert.AreEqual(0, _august.NumbersHeldBy(null).Count);
            Assert.AreEqual(0, _august.NumbersHeldBy("").Count);
        }

        [TestMethod]
        public void AMarkHandedOutButNotTaken_DoesNotThrowEither()
        {
            _elder.Entrust(_august, "Selling", "sinusar");

            Assert.AreEqual(0, _august.NumbersHeldBy("sinusar").Count,
                "nadie lo tomo, asi que no hay comerciante y no hay cartones");
        }

        [TestMethod]
        public void WhatWasSold_AlsoComesOutByTheMark()
        {
            Villager luis = new Villager("Luis Mora", "8888-3333");
            _august.MerchantMarked("vende01").SellThemTo(_august, new long[] { 1 }, luis);

            CollectionAssert.AreEqual(
                new long[] { 1 }, _august.NumbersSoldUnder("vende01").ToArray());
            CollectionAssert.AreEqual(
                new long[] { 2 }, _august.NumbersHeldBy("vende01").ToArray(),
                "el vendido sale de su mano");
        }

        // Y la forma vieja sigue tirando, que es por lo que hizo falta la nueva. Si algun dia
        // deja de tirar, este test lo dice y las dos se pueden juntar.
        [TestMethod]
        public void TheOldWay_StillThrowsWithTheCallersMark()
        {
            Assert.ThrowsException<VillageRuleException>(
                () => _august.MerchantMarked("canta01"),
                "esta es la llamada que reventaba los recorridos");
        }
    }
}
