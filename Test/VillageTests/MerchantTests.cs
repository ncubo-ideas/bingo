using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class MerchantTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);
        private static readonly CalendarDay OctoberFair = new CalendarDay(2026, 10, 3);

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
            _elder.PutUpForSale(_august, new long[] { 1, 2, 3, 4, 5, 789 });
        }

        [TestMethod]
        public void Elder_CanHandHerASecondBatchWithoutTouchingWhatSheAlreadySold()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            _elder.HandTo(_august, _maria, new long[] { 4, 5 });

            Assert.AreEqual(4, _august.CountWith(_maria));
            Assert.AreEqual(1, _august.CountSoldBy(_maria));
            Assert.AreEqual("WithMerchant", _august.ParticipationNumbered(4).Whereabouts);
            Assert.AreEqual("Sold", _august.ParticipationNumbered(1).Whereabouts);
        }

        [TestMethod]
        public void ANewMerchantObjectWithTheSameName_DoesNotInheritTheFirstBatch()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });

            Merchant theSecondMaria = new Merchant("María Rojas");
            _elder.HandTo(_august, theSecondMaria, new long[] { 4, 5 });

            Assert.AreEqual(3, _august.CountWith(_maria));
            Assert.AreEqual(2, _august.CountWith(theSecondMaria));

            Assert.ThrowsException<VillageRuleException>(
                () => theSecondMaria.SellThemTo(_august, new long[] { 1 }, _luis));
        }

        [TestMethod]
        public void AMerchantPassesWhatSheHasLeftToAnotherWhoRanOut()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, sonia, new long[] { 1, 2, 3 });
            _elder.HandTo(_august, _maria, new long[] { 4 });
            _maria.SellThemTo(_august, new long[] { 4 }, _luis);

            sonia.PassTo(_august, _maria, new long[] { 1, 2 });

            Assert.AreEqual(2, _august.CountWith(_maria), "a Maria le quedan los dos que le pasaron");
            Assert.AreEqual(1, _august.CountWith(sonia), "y a Sonia el que no paso");
            Assert.AreSame(_maria, _august.ParticipationNumbered(1).HeldBy);
            Assert.AreEqual("WithMerchant", _august.ParticipationNumbered(1).Whereabouts);
        }

        [TestMethod]
        public void WhatWasPassedOnIsSoldByWhoeverEndsUpSellingIt()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, sonia, new long[] { 1 });
            sonia.PassTo(_august, _maria, new long[] { 1 });

            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.AreSame(_maria, _august.ParticipationNumbered(1).SoldBy);
            Assert.AreEqual(1, _august.CountSoldBy(_maria));
            Assert.AreEqual(0, _august.CountSoldBy(sonia),
                "Sonia lo tuvo en la mano pero no lo vendio");
        }

        [TestMethod]
        public void AMerchantCannotPassWhatSheIsNotHolding()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, _maria, new long[] { 1 });

            Assert.ThrowsException<VillageRuleException>(
                () => sonia.PassTo(_august, _maria, new long[] { 1 }));
        }

        [TestMethod]
        public void AMerchantCannotPassOnWhatSheAlreadySold()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, sonia, new long[] { 1 });
            sonia.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.ThrowsException<VillageRuleException>(
                () => sonia.PassTo(_august, _maria, new long[] { 1 }));
        }

        [TestMethod]
        public void AMerchantCannotPassNumbersToHerself()
        {
            _elder.HandTo(_august, _maria, new long[] { 1 });

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.PassTo(_august, _maria, new long[] { 1 }));
        }

        [TestMethod]
        public void ABatchThatCannotBeHonouredMovesNothing()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, sonia, new long[] { 1, 2 });
            _elder.HandTo(_august, _maria, new long[] { 3 });

            Assert.ThrowsException<VillageRuleException>(
                () => sonia.PassTo(_august, _maria, new long[] { 1, 3 }));

            Assert.AreEqual(2, _august.CountWith(sonia),
                "el 3 no era de Sonia, asi que el 1 tampoco se movio");
            Assert.AreSame(sonia, _august.ParticipationNumbered(1).HeldBy);
        }

        [TestMethod]
        public void AHandOutThatCannotBeHonouredMovesNothing()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, sonia, new long[] { 1 });

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.HandTo(_august, _maria, new long[] { 2, 1 }));

            Assert.AreEqual(0, _august.CountWith(_maria),
                "el 1 ya era de Sonia, asi que el 2 tampoco salio del libro");
            Assert.AreEqual("InTheBook", _august.ParticipationNumbered(2).Whereabouts);
        }

        [TestMethod]
        public void TheSameNumberTwiceInOneHandOutMovesNothing()
        {
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.HandTo(_august, _maria, new long[] { 1, 1 }));

            Assert.AreEqual(0, _august.CountWith(_maria));
            Assert.AreEqual("InTheBook", _august.ParticipationNumbered(1).Whereabouts);
        }

        [TestMethod]
        public void AGiveBackThatCannotBeHonouredMovesNothing()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.GiveBack(_august, new long[] { 2, 1 }));

            Assert.AreEqual(2, _august.CountWith(_maria),
                "el 1 ya estaba vendido, asi que el 2 se queda en su mano");
            Assert.AreSame(_maria, _august.ParticipationNumbered(2).HeldBy);
        }

        [TestMethod]
        public void OnlyTheOneHoldingThemCanPassThemOn()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, sonia, new long[] { 1 });

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.HandTo(_august, _maria, new long[] { 1 }),
                "ni el organizador puede reasignar por detras: los cartones se pasan de mano");
        }

        [TestMethod]
        public void Merchant_ExchangesASoldNumberForOneSheIsHolding()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            _maria.ExchangeFor(_august, _august.ParticipationNumbered(1), _august.ParticipationNumbered(2));

            Participation whatSheGot = _august.ParticipationNumbered(2);
            Assert.AreEqual("Sold", whatSheGot.Whereabouts);
            Assert.AreSame(_luis, whatSheGot.Buyer, "el comprador es el mismo, no uno nuevo");
            Assert.AreSame(_maria, whatSheGot.SoldBy);

            Participation whatCameBack = _august.ParticipationNumbered(1);
            Assert.AreEqual("WithMerchant", whatCameBack.Whereabouts, "el devuelto vuelve a su taco");
            Assert.AreSame(_maria, whatCameBack.HeldBy);
            Assert.IsNull(whatCameBack.Buyer, "un carton en la mano no tiene comprador");
            Assert.IsNull(whatCameBack.SoldBy);
        }

        [TestMethod]
        public void AnExchangeDoesNotChangeHowManySheSoldNorHowManySheHolds()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            _maria.ExchangeFor(_august, _august.ParticipationNumbered(1), _august.ParticipationNumbered(2));

            Assert.AreEqual(1, _august.CountSoldBy(_maria), "sigue siendo una sola venta");
            Assert.AreEqual(2, _august.CountWith(_maria), "y sigue teniendo dos en la mano");
            Assert.AreEqual(1, _august.CountSoldTo(_luis), "Luis compro uno, no dos");
        }

        [TestMethod]
        public void TheExchangedNumberGoesBackToHerPileAndCanBeSoldAgain()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);
            _maria.ExchangeFor(_august, _august.ParticipationNumbered(1), _august.ParticipationNumbered(2));

            Villager ana = new Villager("Ana Solís", "8888-2222");
            _maria.SellThemTo(_august, new long[] { 1 }, ana);

            Assert.AreSame(ana, _august.ParticipationNumbered(1).Buyer);
            Assert.AreEqual(2, _august.CountSoldBy(_maria));
        }

        [TestMethod]
        public void AnyMerchantCanExchange_NotOnlyTheOneWhoSoldIt()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, _maria, new long[] { 1 });
            _elder.HandTo(_august, sonia, new long[] { 2 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            sonia.ExchangeFor(_august, _august.ParticipationNumbered(1), _august.ParticipationNumbered(2));

            Assert.AreSame(_luis, _august.ParticipationNumbered(2).Buyer);
            Assert.AreSame(sonia, _august.ParticipationNumbered(2).SoldBy,
                "la venta pasa a nombre de quien entrego el reemplazo");
            Assert.AreSame(sonia, _august.ParticipationNumbered(1).HeldBy,
                "y el devuelto queda en el taco de quien lo recibio, no en el de quien lo vendio");

            Assert.AreEqual(0, _august.CountSoldBy(_maria),
                "la venta ya no es de Maria: el carton que ella vendio volvio al taco de Sonia");
            Assert.AreEqual(1, _august.CountSoldBy(sonia));

            Assert.AreEqual(0, _august.CountWith(_maria),
                "Maria no recupera nada: el devuelto se lo quedo quien atendio el cambio");
            Assert.AreEqual(1, _august.CountWith(sonia),
                "Sonia entrego el suyo y recibio el devuelto, asi que sigue con uno en la mano");
        }

        [TestMethod]
        public void Merchant_CannotGiveAwayAReplacementSheIsNotHolding()
        {
            Merchant sonia = new Merchant("Sonia Vargas");

            _elder.HandTo(_august, _maria, new long[] { 1 });
            _elder.HandTo(_august, sonia, new long[] { 2 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.ExchangeFor(_august, _august.ParticipationNumbered(1), _august.ParticipationNumbered(2)));
        }

        [TestMethod]
        public void Merchant_CannotExchangeSomethingThatWasNeverSold()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2 });

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.ExchangeFor(_august, _august.ParticipationNumbered(1), _august.ParticipationNumbered(2)));
        }

        [TestMethod]
        public void Merchant_CannotExchangeANumberForItself()
        {
            _elder.HandTo(_august, _maria, new long[] { 1 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.ExchangeFor(_august, _august.ParticipationNumbered(1), _august.ParticipationNumbered(1)));
        }

        [TestMethod]
        public void AnExchangeNeverTouchesAnotherFestival()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Festival october = _elder.CreateFestival("Kermés de octubre", OctoberFair, Today);
            _elder.PutUpForSale(october, new long[] { 1, 2 });
            _elder.HandTo(october, _maria, new long[] { 1, 2 });
            _maria.SellThemTo(october, new long[] { 1 }, _luis);

            _maria.ExchangeFor(_august, _august.ParticipationNumbered(1), _august.ParticipationNumbered(2));

            Assert.AreEqual("Sold", october.ParticipationNumbered(1).Whereabouts,
                "el mismo numero en otra funcion es otro carton y no se mueve");
            Assert.AreEqual("WithMerchant", october.ParticipationNumbered(2).Whereabouts);
        }

        [TestMethod]
        public void Elder_CannotHandCardsToAMerchantWithoutAName()
        {
            Assert.ThrowsException<ArgumentException>(
                () => _elder.HandTo(_august, new Merchant(""), new long[] { 1 }));
            Assert.ThrowsException<ArgumentException>(
                () => _elder.HandTo(_august, new Merchant(null), new long[] { 1 }));

            Assert.AreEqual("InTheBook", _august.ParticipationNumbered(1).Whereabouts);
        }

        [TestMethod]
        public void TheSale_KeepsHowToReachTheBuyer()
        {
            _elder.HandTo(_august, _maria, new long[] { 1 });

            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.AreEqual("Luis Mora", _august.ParticipationNumbered(1).Buyer.Name);
            Assert.AreEqual("8888-3333", _august.ParticipationNumbered(1).Buyer.Contact);
        }

        [TestMethod]
        public void Elder_HandsNumbersToAMerchantToGoSellThem()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3, 4, 5 });

            Assert.AreEqual(5, _august.CountWith(_maria));
            Assert.AreEqual("WithMerchant", _august.ParticipationNumbered(3).Whereabouts);
            Assert.AreSame(_maria, _august.ParticipationNumbered(3).HeldBy);
        }

        [TestMethod]
        public void Elder_WillNotHandANumberThatWasNeverPutUpForSale()
        {
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.HandTo(_august, _maria, new long[] { 9999 }));
        }

        [TestMethod]
        public void Elder_WillNotHandTheSameNumberToTwoMerchants()
        {
            Merchant rosa = new Merchant("Rosa Chaves");
            _elder.HandTo(_august, _maria, new long[] { 1 });

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.HandTo(_august, rosa, new long[] { 1 }));
        }

        [TestMethod]
        public void Merchant_SellsWhatSheWasGiven()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3, 4, 5 });

            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Participation sold = _august.ParticipationNumbered(1);
            Assert.AreEqual("Sold", sold.Whereabouts);
            Assert.IsTrue(sold.IsSold);
            Assert.AreSame(_luis, sold.Buyer);
            Assert.AreEqual(1, _august.CountSoldBy(_maria));
            Assert.AreEqual(4, _august.CountWith(_maria), "selling takes it out of her hand");
        }

        [TestMethod]
        public void Merchant_CannotSellWhatSheDoesNotHold()
        {
            _elder.HandTo(_august, _maria, new long[] { 1 });

            Assert.ThrowsException<VillageRuleException>(() => _maria.SellThemTo(_august, new long[] { 789 }, _luis));
        }

        [TestMethod]
        public void Merchant_CannotSellTheSameNumberTwice()
        {
            _elder.HandTo(_august, _maria, new long[] { 1 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.SellThemTo(_august, new long[] { 1 }, new Villager("Ana Solís", "8888-2222")));
        }

        [TestMethod]
        public void Merchant_GivesBackWhatSheDidNotSell()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3, 4, 5 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);
            _maria.SellThemTo(_august, new long[] { 2 }, _luis);

            _maria.GiveBack(_august, new long[] { 3, 4, 5 });

            Assert.AreEqual(0, _august.CountWith(_maria));
            Assert.AreEqual(2, _august.CountSoldBy(_maria));
            Assert.AreEqual("InTheBook", _august.ParticipationNumbered(3).Whereabouts);
            Assert.IsNull(_august.ParticipationNumbered(3).HeldBy);
        }

        [TestMethod]
        public void Merchant_CannotGiveBackWhatSheAlreadySold()
        {
            _elder.HandTo(_august, _maria, new long[] { 1 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.ThrowsException<VillageRuleException>(() => _maria.GiveBack(_august, new long[] { 1 }));
        }

        [TestMethod]
        public void Elder_SellsAtTheDoorWithoutAnyMerchant()
        {
            _elder.SellTo(_august, _august.ParticipationNumbered(789), _luis);

            Assert.IsTrue(_august.ParticipationNumbered(789).IsSold);
            Assert.AreSame(_luis, _august.ParticipationNumbered(789).Buyer);
            Assert.AreEqual(1, _august.CountSoldTo(_luis));
        }

        [TestMethod]
        public void Elder_CannotSellWhatAMerchantIsHolding()
        {
            _elder.HandTo(_august, _maria, new long[] { 1 });

            Assert.ThrowsException<VillageRuleException>(() => _elder.SellTo(_august, _august.ParticipationNumbered(1), _luis));
        }

        [TestMethod]
        public void Festival_CountsWhatEachBuyerBought()
        {
            Villager ana = new Villager("Ana Solís", "8888-2222");
            _elder.SellTo(_august, _august.ParticipationNumbered(1), _luis);
            _elder.SellTo(_august, _august.ParticipationNumbered(2), ana);
            _elder.SellTo(_august, _august.ParticipationNumbered(3), ana);

            Assert.AreEqual(1, _august.CountSoldTo(_luis));
            Assert.AreEqual(2, _august.CountSoldTo(ana));
            Assert.AreEqual(3, _august.SoldCount);
        }

        [TestMethod]
        public void Merchant_LivesLongerThanTheFestivalSheSoldAt()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3, 4, 5 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);
            _maria.SellThemTo(_august, new long[] { 2 }, _luis);
            _maria.GiveBack(_august, new long[] { 3, 4, 5 });

            Festival october = _elder.CreateFestival("Kermés de octubre", OctoberFair, Today);
            _elder.PutUpForSale(october, new long[] { 40, 41, 42 });
            _elder.HandTo(october, _maria, new long[] { 40, 41, 42 });
            _maria.SellThemTo(october, new long[] { 40 }, _luis);

            Assert.AreEqual(2, _august.CountSoldBy(_maria), "August must not move when she sells in October");
            Assert.AreEqual(0, _august.CountWith(_maria));
            Assert.AreEqual(1, october.CountSoldBy(_maria));
            Assert.AreEqual(2, october.CountWith(_maria));
        }

        [TestMethod]
        public void Merchant_KeepsNothingButHerName()
        {
            FieldInfo[] fields = typeof(Merchant).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.AreEqual(1, fields.Length,
                "a merchant only keeps her name; what she is holding belongs to each festival, "
                + "so she cannot carry August's numbers into October");
        }

        [TestMethod]
        public void TheFestivalAnswersWhatIsWithHerInOneQuestion()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });
            _maria.SellThemTo(_august, new long[] { 2 }, _luis);

            Merchant sonia = new Merchant("Sonia Vargas");
            _elder.HandTo(_august, sonia, new long[] { 4 });

            CollectionAssert.AreEqual(new long[] { 1, 3 },
                _august.NumbersWith(_maria).ToArray(),
                "lo vendido ya no esta en su mano, y lo de Sonia no es suyo");

            CollectionAssert.AreEqual(new long[] { 2 }, _august.NumbersSoldBy(_maria).ToArray());
            Assert.AreEqual(0, _august.NumbersSoldBy(sonia).Count);
            CollectionAssert.AreEqual(new long[] { 4 }, _august.NumbersWith(sonia).ToArray());
        }

        [TestMethod]
        public void TheFestivalListsEverythingUpForSaleLowestFirst()
        {
            long[] theWholeBook = new long[] { 1, 2, 3, 4, 5, 789 };

            CollectionAssert.AreEqual(theWholeBook, _august.NumbersUpForSale().ToArray());
            Assert.AreEqual(6, _august.CountInTheBook);

            _elder.HandTo(_august, _maria, new long[] { 3, 1 });

            Assert.AreEqual(4, _august.CountInTheBook, "dos salieron del libro");
            CollectionAssert.AreEqual(theWholeBook, _august.NumbersUpForSale().ToArray(),
                "seguir en venta no depende de quien los tenga en la mano");

            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Assert.AreEqual(4, _august.CountInTheBook, "vender no devuelve nada al libro");
        }

        [TestMethod]
        public void TheFestivalNamesWhatIsStillInTheBookLowestFirst()
        {
            CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 5, 789 },
                _august.NumbersInTheBook().ToArray(),
                "nobody has taken anything yet, so the whole book is still in the book");

            _elder.HandTo(_august, _maria, new long[] { 3, 1 });

            CollectionAssert.AreEqual(new long[] { 2, 4, 5, 789 },
                _august.NumbersInTheBook().ToArray(),
                "what is in her hands cannot be handed to anybody else");

            _maria.SellThemTo(_august, new long[] { 1 }, _luis);
            _maria.GiveBack(_august, new long[] { 3 });

            CollectionAssert.AreEqual(new long[] { 2, 3, 4, 5, 789 },
                _august.NumbersInTheBook().ToArray(),
                "the one she gave back goes back to its place in the book and the one "
                + "she sold never comes back; counting alone could not tell the two apart, "
                + "which is why whoever hands out the next batch has to be told the names");

            Assert.AreEqual(_august.CountInTheBook, _august.NumbersInTheBook().Count,
                "counting them and naming them have to say the same thing");
        }

        [TestMethod]
        public void SheSellsSeveralNumbersToTheSameBuyerInOneGo()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3, 4, 5 });

            _maria.SellThemTo(_august, new long[] { 1, 2, 3 }, _luis);

            Assert.AreEqual(3, _august.CountSoldBy(_maria));
            Assert.AreEqual(2, _august.CountWith(_maria));
            Assert.AreEqual(3, _august.CountSoldTo(_luis),
                "one buyer, not one buyer per number");
        }

        [TestMethod]
        public void ASaleThatCannotBeHonouredSellsNothing()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });

            Merchant sonia = new Merchant("Sonia Vargas");
            _elder.HandTo(_august, sonia, new long[] { 4 });

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.SellThemTo(_august, new long[] { 1, 2, 4 }, _luis));

            Assert.AreEqual(0, _august.SoldCount,
                "the substrate does not roll back: whatever this leaves half-applied "
                + "stays half-applied and replays that way on every rehydration");
            Assert.AreEqual(3, _august.CountWith(_maria));
            Assert.AreEqual("WithMerchant", _august.ParticipationNumbered(1).Whereabouts);
        }

        [TestMethod]
        public void TheSameNumberTwiceInOneSaleSellsNothing()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.SellThemTo(_august, new long[] { 1, 1 }, _luis));

            Assert.AreEqual(0, _august.SoldCount);
            Assert.AreEqual(3, _august.CountWith(_maria));
        }

        [TestMethod]
        public void SheCannotSellNothingAtAll()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.SellThemTo(_august, new long[] { }, _luis));
        }

        [TestMethod]
        public void ABatchSaleCannotIncludeSomethingSheAlreadySold()
        {
            _elder.HandTo(_august, _maria, new long[] { 1, 2, 3 });
            _maria.SellThemTo(_august, new long[] { 1 }, _luis);

            Villager ana = new Villager("Ana Solís", "8888-1111");

            Assert.ThrowsException<VillageRuleException>(
                () => _maria.SellThemTo(_august, new long[] { 2, 1 }, ana));

            Assert.AreEqual(1, _august.SoldCount);
            Assert.AreEqual(0, _august.CountSoldTo(ana));
            Assert.AreEqual("WithMerchant", _august.ParticipationNumbered(2).Whereabouts,
                "the 2 is validated after the 1 in the batch, so it must not have moved");
        }
    }
}
