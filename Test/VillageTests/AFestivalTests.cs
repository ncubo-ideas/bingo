using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class AFestivalTests
    {
        private static readonly CalendarDay July30 = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);
        private static readonly CalendarDay OctoberFair = new CalendarDay(2026, 10, 3);

        [TestMethod]
        public void TheElder_PricesTheCardsAndCanChangeHisMind()
        {
            Elder elder = new Elder("Padre Gerardo");
            Festival festival = elder.CreateFestival("Día de la Madre", MothersDay, July30);

            Assert.AreEqual(0, festival.CardPrice, "no price until somebody says one");

            elder.PriceCardsAt(festival, new Price(2000));
            Assert.AreEqual(2000, festival.CardPrice);

            // El precio es UN numero del festival, no una historia: decirlo de nuevo
            // lo cambia para todo el bingo.
            elder.PriceCardsAt(festival, new Price(2500));
            Assert.AreEqual(2500, festival.CardPrice);

            Assert.ThrowsException<VillageRuleException>(
                () => elder.PriceCardsAt(festival, new Price(0)));
            Assert.ThrowsException<VillageRuleException>(
                () => elder.PriceCardsAt(festival, new Price(-50)));
        }

        [TestMethod]
        public void TheElder_SaysAtWhatHourTheDoorsOpen()
        {
            Elder elder = new Elder("Padre Gerardo");
            Festival festival = elder.CreateFestival("Dia de la Madre", MothersDay, July30);

            Assert.AreEqual(string.Empty, festival.DoorsOpenAt,
                "the calendar knows the day; the hour is said apart, or not at all");

            elder.OpenTheDoorsAt(festival, new ClockTime(19, 30));
            Assert.AreEqual("19:30", festival.DoorsOpenAt);

            // Medianoche en punto es una hora de verdad, y se escribe con sus ceros: la
            // pantalla la parte por el ':' y un "0:0" la dejaria sin leer.
            elder.OpenTheDoorsAt(festival, new ClockTime(0, 0));
            Assert.AreEqual("00:00", festival.DoorsOpenAt);

            Assert.ThrowsException<VillageRuleException>(
                () => elder.OpenTheDoorsAt(festival, new ClockTime(24, 0)));
            Assert.ThrowsException<VillageRuleException>(
                () => elder.OpenTheDoorsAt(festival, new ClockTime(-1, 0)));
            Assert.ThrowsException<VillageRuleException>(
                () => elder.OpenTheDoorsAt(festival, new ClockTime(19, 60)));
        }

        [TestMethod]
        public void TheElder_ChoosesHowWinsAreAnnounced()
        {
            Elder elder = new Elder("Padre Gerardo");
            Festival festival = elder.CreateFestival("Día de la Madre", MothersDay, July30);

            Assert.AreEqual(string.Empty, festival.WinsAnnounced,
                "unset until the elder chooses, so the game keeps doing what it did");

            elder.AnnounceWinsBy(festival, new WinsAnnouncement("Automatic"));
            Assert.AreEqual("Automatic", festival.WinsAnnounced);

            elder.AnnounceWinsBy(festival, new WinsAnnouncement("OnClaim"));
            Assert.AreEqual("OnClaim", festival.WinsAnnounced);

            // Los dos valores son palabras del motor y van EXACTAS: la mesa las parsea
            // sin perdon, asi que aceptar variantes aca seria fallar alla.
            Assert.ThrowsException<VillageRuleException>(
                () => elder.AnnounceWinsBy(festival, new WinsAnnouncement("automatico")));
            Assert.ThrowsException<VillageRuleException>(
                () => elder.AnnounceWinsBy(festival, new WinsAnnouncement("")));
        }

        [TestMethod]
        public void AFestival_FromTheDayItIsAnnouncedToTheLastPrizeGivenAway()
        {
            Elder elder = new Elder("Padre Gerardo");
            Merchant maria = new Merchant("María Rojas");
            Villager luis = new Villager("Luis Mora", "8888-3333");
            Villager ana = new Villager("Ana Solís", "ana@correo.cr");

            // El padre anuncia la funcion y le pone fecha. Todavia nadie juega nada.
            Festival festival = elder.CreateFestival("Día de la Madre", MothersDay, July30);

            Assert.AreEqual("Announced", festival.State);
            Assert.AreEqual(16, festival.DaysUntil(July30));
            Assert.AreSame(festival, elder.StandingAt);

            // El juego le da los numeros y el los pone a la venta. No los invento el.
            elder.PutUpForSale(festival, new long[] { 789, 1002, 1001, 12, 7300000 });

            Assert.AreEqual(5, festival.ParticipationCount);
            Assert.AreEqual(12L, festival.ParticipationAt(1).Number);
            Assert.AreEqual(7300000L, festival.ParticipationAt(5).Number);

            // Ya empezada la venta le pide dos numeros mas al juego.
            elder.PutUpForSale(festival, new long[] { 40, 41 });

            Assert.AreEqual(7, festival.ParticipationCount);

            // «le doy un papelito a cada quien, para saber a quien le encargue que»
            elder.Entrust(festival, "Selling", "a1b2c3");
            elder.HandTheErrandTo(festival, festival.ErrandMarked("a1b2c3"), "María Rojas");

            Assert.AreEqual("Selling", festival.WhatWasEntrustedTo("María Rojas"));
            Assert.IsNull(festival.ErrandOnOfferFor("Selling"), "ese papelito ya es de ella");

            // «me dieron cinco para andarlos vendiendo»
            elder.HandTo(festival, maria, new long[] { 789, 1002, 1001, 12, 40 });

            Assert.AreEqual(5, festival.CountWith(maria));
            Assert.AreEqual("WithMerchant", festival.ParticipationNumbered(789).Whereabouts);
            Assert.AreSame(maria, festival.ParticipationNumbered(789).HeldBy);

            // «ya vendi uno, padre, el 789»
            maria.SellThemTo(festival, new long[] { 789 }, luis);
            maria.SellThemTo(festival, new long[] { 1002 }, ana);

            Assert.AreEqual(2, festival.CountSoldBy(maria));
            Assert.AreEqual(3, festival.CountWith(maria));
            Assert.AreSame(luis, festival.ParticipationNumbered(789).Buyer);
            Assert.AreSame(maria, festival.ParticipationNumbered(789).SoldBy);

            // «los otros tres no los vendi, me devuelve los tres cartones»
            maria.GiveBack(festival, new long[] { 1001, 12, 40 });

            // Y con los cartones de vuelta, el padre le pide el papelito.
            elder.SendHome(festival, festival.ErrandMarked("a1b2c3"));

            Assert.IsNull(festival.WhatWasEntrustedTo("María Rojas"),
                "sin papelito ya no tiene nada encargado, aunque el diario recuerde que vendio");
            Assert.AreEqual("María Rojas", festival.ErrandMarked("a1b2c3").TakenBy,
                "el papel guarda el nombre: es como el padre lleva cuenta de lo que dio");

            Assert.AreEqual(0, festival.CountWith(maria));
            Assert.AreEqual("InTheBook", festival.ParticipationNumbered(1001).Whereabouts);
            Assert.IsNull(festival.ParticipationNumbered(1001).HeldBy);

            // «vendeme un carton, y entonces le da el uno» — el padre vende en la puerta
            elder.SellTo(festival, festival.ParticipationNumbered(41), luis);

            Assert.AreEqual(3, festival.SoldCount);
            Assert.AreEqual(2, festival.CountSoldTo(luis));
            Assert.AreEqual(1, festival.CountSoldTo(ana));
            Assert.IsNull(festival.ParticipationNumbered(41).SoldBy, "nobody sold it for him");

            // «el padre tiene que ir metiendo los premios»
            elder.OfferPrize(festival, "Coffee maker");
            elder.OfferPrize(festival, "Arrocera");
            elder.OfferPrize(festival, "Bicicleta montañera");

            // «una lista ordenada con, ay, este es el principal» — y esta de tercero
            elder.MarkAsMainPrize(festival, festival.PrizeAt(3));

            Assert.AreEqual(3, festival.PrizeCount);
            Assert.AreEqual(3, festival.MainPrizePosition);
            Assert.AreEqual("Bicicleta montañera", festival.PrizeAt(3).What);

            // «rifan dos premios al mismo tiempo, pero la forma de ganar los dos es distinta»
            Raffle firstRaffle = elder.StartRaffle(festival, new int[] { 1, 3 });

            Assert.AreEqual(1, firstRaffle.Number);
            Assert.AreEqual(2, firstRaffle.PrizeCount);
            Assert.IsFalse(firstRaffle.IsSettled);

            // «el organizador tiene que decir que premio le vas a asignar a los dos ganadores»
            firstRaffle.AssignPrizeTo(1, 789);
            firstRaffle.AssignPrizeTo(3, 1002);

            Assert.IsTrue(firstRaffle.IsSettled);
            Assert.AreEqual(789L, festival.PrizeAt(1).TakenBy);
            Assert.AreEqual(1002L, festival.PrizeAt(3).TakenBy);
            Assert.AreEqual(2, festival.GivenPrizeCount);
            Assert.AreEqual(1, festival.PendingPrizeCount);

            // «y ahora el organizador le dice al bingo, tenemos que jugar otro»
            Raffle secondRaffle = elder.StartRaffle(festival, new int[] { 2 });
            secondRaffle.AssignPrizeTo(2, 41);

            Assert.AreEqual(2, festival.RaffleCount);
            Assert.AreEqual(0, festival.PendingPrizeCount);
            Assert.AreEqual(41L, festival.PrizeAt(2).TakenBy);

            // El cuadre del padre al cerrar
            Assert.AreEqual(7, festival.ParticipationCount);
            Assert.AreEqual(3, festival.SoldCount);
            Assert.AreEqual(3, festival.GivenPrizeCount);
            Assert.AreEqual(2, festival.CountSoldBy(maria));
        }

        [TestMethod]
        public void AFestival_DoesNotCarryAnythingIntoTheNextOne()
        {
            Elder elder = new Elder("Padre Gerardo");
            Merchant maria = new Merchant("María Rojas");
            Villager luis = new Villager("Luis Mora", "8888-3333");

            Festival mothersDay = elder.CreateFestival("Día de la Madre", MothersDay, July30);
            elder.PutUpForSale(mothersDay, new long[] { 789, 1002 });
            elder.HandTo(mothersDay, maria, new long[] { 789, 1002 });
            maria.SellThemTo(mothersDay, new long[] { 789 }, luis);
            elder.OfferPrize(mothersDay, "Coffee maker");
            elder.StartRaffle(mothersDay, new int[] { 1 }).AssignPrizeTo(1, 789);

            Festival octoberFair = elder.CreateFestival("Kermés de octubre", OctoberFair, July30);
            elder.PutUpForSale(octoberFair, new long[] { 789, 55 });
            elder.HandTo(octoberFair, maria, new long[] { 789, 55 });
            maria.SellThemTo(octoberFair, new long[] { 55 }, luis);

            // La misma gente, dos funciones, nada se cruza.
            Assert.AreEqual(1, mothersDay.CountSoldBy(maria));
            Assert.AreEqual(1, octoberFair.CountSoldBy(maria));
            Assert.AreEqual(1, mothersDay.CountWith(maria));
            Assert.AreEqual(1, octoberFair.CountWith(maria));

            // El 789 se vendio en agosto y en octubre sigue en manos de doña María.
            Assert.IsTrue(mothersDay.ParticipationNumbered(789).IsSold);
            Assert.IsFalse(octoberFair.ParticipationNumbered(789).IsSold);

            // El premio de agosto no aparece en octubre.
            Assert.AreEqual(1, mothersDay.PrizeCount);
            Assert.AreEqual(0, octoberFair.PrizeCount);
            Assert.AreEqual(1, mothersDay.RaffleCount);
            Assert.AreEqual(0, octoberFair.RaffleCount);
            Assert.IsNull(octoberFair.CurrentRaffle);

            Assert.AreEqual(2, elder.FestivalCount);
            Assert.AreSame(octoberFair, elder.StandingAt);
        }

        [TestMethod]
        public void AFestival_NeverNamesAnythingFromTheGame()
        {
            System.Reflection.Assembly domain = typeof(Festival).Assembly;

            Assert.AreEqual(0, domain.GetReferencedAssemblies().Length -
                System.Array.FindAll(domain.GetReferencedAssemblies(),
                    reference => reference.Name == "System.Runtime" ||
                                 reference.Name.StartsWith("System.")).Length,
                "the organizer domain must not reference anything but the framework");
        }
    }
}
