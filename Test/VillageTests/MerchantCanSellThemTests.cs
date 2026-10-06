using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    // La pregunta que hay que poder hacer para vender con el organizador caido.
    //
    // La colaboradora vende un carton que YA TIENE EN LA MANO, el telefono del padre no
    // contesta, y la venta se guarda para ofrecersela cuando vuelva. Entre una cosa y la otra
    // el mundo pudo moverse. Esto contesta si la venta guardada todavia se sostiene, y lo
    // contesta SIN tirar -- porque se pregunta antes de escribir, y una excepcion ahi dejaria
    // el asiento igual.
    //
    // Las condiciones son las mismas de SellWhatIsHeldBy. Lo unico distinto es el momento.
    [TestClass]
    public class MerchantCanSellThemTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);

        private Elder _elder;
        private Merchant _maria;
        private Merchant _rosa;
        private Villager _luis;
        private Festival _august;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
            _luis = new Villager("Luis Mora", "8888-3333");
            _august = _elder.CreateFestival("Día de la Madre", MothersDay, Today);
            _elder.PutUpForSale(_august, new long[] { 1, 2, 3, 4, 5 });

            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "María Rojas");
            _maria = _august.MerchantMarked("a1b2c3");
            _elder.HandTo(_august, _maria, new long[] { 1, 2 });

            _elder.Entrust(_august, "Selling", "d4e5f6");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("d4e5f6"), "Rosa Pineda");
            _rosa = _august.MerchantMarked("d4e5f6");
            _elder.HandTo(_august, _rosa, new long[] { 3 });
        }

        [TestMethod]
        public void WhatSheHolds_SheCanSell()
        {
            Assert.IsTrue(_august.MerchantCanSellThem("a1b2c3", new long[] { 1, 2 }));
        }

        [TestMethod]
        public void WhatIsNotHers_No()
        {
            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[] { 3 }),
                "el 3 es de Rosa: que Maria lo tenga anotado en su telefono no lo hace suyo");
        }

        [TestMethod]
        public void WhatStayedInTheBook_NeitherCan()
        {
            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[] { 4 }),
                "el 4 no se lo entrego nadie");
        }

        [TestMethod]
        public void WhatSheAlreadySold_IsNotSoldTwice()
        {
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[] { 1 }),
                "vender pone el carton en manos de nadie, asi que ya no lo tiene");
        }

        [TestMethod]
        public void ABatchWithOneThatIsNotHers_FailsWhole()
        {
            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[] { 1, 3 }),
                "un lote se acepta entero o no se acepta: la mitad seria peor que nada, porque "
                + "ella ya cobro los dos");
        }

        [TestMethod]
        public void TheSameCardTwiceInTheBatch_No()
        {
            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[] { 1, 1 }));
        }

        [TestMethod]
        public void WithoutCards_No()
        {
            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[0]),
                "vender nada no es una venta");
        }

        // El caso que hace falta que exista. El padre no la ve conectada, cree que se fue, y la
        // saca del bingo -- que es lo que haria cualquiera. Sus cartones vuelven al libro y el
        // se los da a otra. Cuando ella vuelve trae ventas ya cobradas de cartones que ya no
        // son suyos.
        //
        // No es que esto pase seguido: es que si pasa, hay plata de por medio, y el sistema
        // tiene que poder decir que no ANTES de asentar en vez de descubrirlo despues.
        [TestMethod]
        public void IfSheWasRemovedWhileOffline_HerSavedSalesNoLongerCount()
        {
            Assert.IsTrue(_august.MerchantCanSellThem("a1b2c3", new long[] { 1 }),
                "precondicion: mientras esta adentro, el 1 es suyo y lo puede vender");

            // El mismo orden que hace la app al sacarla: primero recogerle los cartones y
            // despues revocarle el papelito.
            _maria.GiveBack(_august, new long[] { 1, 2 });
            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[] { 1 }),
                "la sacaron, sus cartones volvieron al libro, y la venta que traia guardada ya "
                + "no se sostiene. El padre tiene que enterarse con nombre y apellido: ella ya "
                + "cobro.");
        }

        // Y si la recogida de cartones fallara y quedara revocada con cartones encima, la
        // venta que llega tarde tampoco entra: el papelito revocado manda.
        [TestMethod]
        public void RevokedButHoldingCards_NeitherCan()
        {
            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[] { 1 }),
                "el 1 le sigue quedando en la mano, pero ya no tiene con que venderlo");
        }

        [TestMethod]
        public void WithTheBingoClosed_NothingIsSold()
        {
            _elder.CloseFestival(_august);

            Assert.IsFalse(_august.MerchantCanSellThem("a1b2c3", new long[] { 1 }),
                "una venta guardada no revive un bingo que ya termino");
        }
    }
}
