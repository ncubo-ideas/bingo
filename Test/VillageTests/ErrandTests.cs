using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    [TestClass]
    public class ErrandTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 7, 30);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);
        private static readonly CalendarDay OctoberFair = new CalendarDay(2026, 10, 3);

        private Elder _elder;
        private Festival _august;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
            _august = _elder.CreateFestival("Día de la Madre", MothersDay, Today);
        }

        [TestMethod]
        public void AnErrandFromAnotherFestival_CannotBeHandedOutHere()
        {
            Festival october = _elder.CreateFestival("Kermés de octubre", OctoberFair, Today);
            Errand octobers = _elder.Entrust(october, "Selling", "a1b2c3");
            _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.ThrowsException<VillageRuleException>(() => _elder.HandTheErrandTo(_august, octobers, "Marta"));
            Assert.ThrowsException<VillageRuleException>(() => _elder.SendHome(_august, octobers));

            Assert.IsTrue(octobers.IsOnOffer, "the October errand was not touched");
            Assert.IsTrue(_august.ErrandMarked("a1b2c3").IsOnOffer, "neither was August's own");
        }

        [TestMethod]
        public void Festival_StartsWithNobodyRunningErrands()
        {
            Assert.AreEqual(0, _august.ErrandCount);
            Assert.IsNull(_august.ErrandOnOfferFor("Selling"));
            Assert.IsNull(_august.ErrandOnOfferFor("Calling"));
            Assert.IsNull(_august.WhatWasEntrustedTo("Marta"));
        }

        [TestMethod]
        public void Elder_CutsAnErrandAndItSitsOnTheTable()
        {
            Errand errand = _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.AreEqual(1, _august.ErrandCount);
            Assert.AreEqual(1, errand.Position);
            Assert.AreEqual("a1b2c3", errand.Mark);
            Assert.AreEqual("Selling", errand.Doing);
            Assert.AreEqual("Offered", errand.HowItStands);
            Assert.IsTrue(errand.IsOnOffer);
            Assert.AreEqual(string.Empty, errand.TakenBy,
                "nobody picked it up yet -- and it answers empty, not null, because the "
                + "organizer reads this out of the journal and a print cannot walk into nothing");
            Assert.AreSame(errand, _august.ErrandMarked("a1b2c3"));
            Assert.AreSame(errand, _august.ErrandOnOfferFor("Selling"));
        }

        [TestMethod]
        public void Elder_CannotEntrustSomethingNobodyDoesAtTheFestival()
        {
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Entrust(_august, "Dancing", "a1b2c3"));
        }

        [TestMethod]
        public void Elder_CannotEntrustAtAFestivalHeDoesNotGovern()
        {
            Elder anotherElder = new Elder("Padre Bartolo");
            Festival his = anotherElder.CreateFestival("Kermés de octubre", OctoberFair, Today);

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Entrust(his, "Selling", "a1b2c3"));
        }

        [TestMethod]
        public void Errand_CannotBeCutWithoutAnElder()
        {
            ConstructorInfo[] open = typeof(Errand).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public);

            Assert.AreEqual(0, open.Length,
                "an errand must only come to life through an Elder, the same way a festival "
                + "and a participation do, so it cannot have a public constructor");
        }

        [TestMethod]
        public void Errand_NeedsAMarkOnIt()
        {
            Assert.ThrowsException<ArgumentException>(() => _elder.Entrust(_august, "Selling", ""));
            Assert.ThrowsException<ArgumentException>(() => _elder.Entrust(_august, "Selling", "  "));
            Assert.ThrowsException<ArgumentException>(() => _elder.Entrust(_august, "Selling", null));
        }

        [TestMethod]
        public void Elder_CannotWriteTheSameMarkOnTwoErrands()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Entrust(_august, "Calling", "a1b2c3"),
                "two errands with the same mark would make the paper worthless: "
                + "the elder could not tell which one somebody is bringing back");
        }

        [TestMethod]
        public void Errand_TakesTheNameOfWhoeverPicksItUp()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Errand errand = _august.ErrandMarked("a1b2c3");

            Assert.AreEqual("Marta", errand.TakenBy);
            Assert.AreEqual("Taken", errand.HowItStands);
            Assert.IsTrue(errand.IsTaken);
            Assert.IsFalse(errand.IsOnOffer);
            Assert.IsNull(_august.ErrandOnOfferFor("Selling"),
                "once she took it, that paper is no longer on the table for the next one");
        }

        [TestMethod]
        public void Errand_DoesNotChangeHandsOnceSomebodyTookIt()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Rosa"),
                "the paper Marta is holding cannot also be Rosa's");
        }

        [TestMethod]
        public void Errand_LetsTheSamePersonComeBackWithTheSamePaper()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Assert.AreEqual("Marta", _august.ErrandMarked("a1b2c3").TakenBy,
                "she shows up again with the paper she already has — because her phone lost "
                + "the wifi, or because the elder restarted his. Turning her away there would "
                + "leave her outside the funcion holding cards nobody can account for");
        }

        [TestMethod]
        public void Errand_DoesNotMindHowTheNameWasWritten()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "MARTA");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "  marta  ");

            Assert.AreEqual("Selling", _august.WhatWasEntrustedTo("mArTa"),
                "the elder writes the name on the paper by hand, so a capital letter "
                + "must not be what locks somebody out of her own funcion");
        }

        [TestMethod]
        public void Errand_IsNotTakenUpByNobody()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.ThrowsException<ArgumentException>(
                () => _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), ""));
            Assert.ThrowsException<ArgumentException>(
                () => _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), null));
        }

        [TestMethod]
        public void Elder_SendsSomebodyHomeAndTheErrandStopsBeingGood()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            Errand errand = _august.ErrandMarked("a1b2c3");

            Assert.AreEqual("SentHome", errand.HowItStands);
            Assert.IsTrue(errand.WasSentHome);
            Assert.IsFalse(errand.IsTaken);

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta"),
                "the paper was taken back, so bringing it again buys nothing");
        }

        [TestMethod]
        public void Elder_CanTakeBackAnErrandNobodyPickedUp()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");

            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            Assert.IsTrue(_august.ErrandMarked("a1b2c3").WasSentHome);
            Assert.IsNull(_august.ErrandOnOfferFor("Selling"),
                "a stranger who photographed the paper on the table must not be able to use it");
        }

        [TestMethod]
        public void Elder_CannotTakeBackTheSameErrandTwice()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.SendHome(_august, _august.ErrandMarked("a1b2c3")));
        }

        [TestMethod]
        public void Errand_KeepsTheNameWrittenOnItAfterHeWasSentHome()
        {
            _elder.Entrust(_august, "Calling", "d4e5f6");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("d4e5f6"), "Tomas");
            _elder.SendHome(_august, _august.ErrandMarked("d4e5f6"));

            Assert.AreEqual("Tomas", _august.ErrandMarked("d4e5f6").TakenBy,
                "the elder has to be able to look at the papers he handed out and see "
                + "whose was which, even the ones he took back");
        }

        [TestMethod]
        public void Festival_TellsWhatEachPersonIsDoing()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.Entrust(_august, "Calling", "d4e5f6");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("d4e5f6"), "Tomas");

            Assert.AreEqual("Selling", _august.WhatWasEntrustedTo("Marta"));
            Assert.AreEqual("Calling", _august.WhatWasEntrustedTo("Tomas"));
            Assert.IsNull(_august.WhatWasEntrustedTo("Rosa"),
                "Rosa never took a paper, so she was entrusted with nothing");
        }

        [TestMethod]
        public void Festival_ForgetsWhatSomebodySentHomeWasDoing()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");
            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            Assert.IsNull(_august.WhatWasEntrustedTo("Marta"));
        }

        [TestMethod]
        public void Festival_LooksAtTheLastPaperSomebodyTook()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");
            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            _elder.Entrust(_august, "Calling", "d4e5f6");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("d4e5f6"), "Marta");

            Assert.AreEqual("Calling", _august.WhatWasEntrustedTo("Marta"),
                "he sent her home and then gave her another paper: what counts is the last one");
        }

        [TestMethod]
        public void Festival_OffersOneErrandAtATimeForEachChore()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.Entrust(_august, "Selling", "g7h8i9");

            Assert.AreEqual("a1b2c3", _august.ErrandOnOfferFor("Selling").Mark,
                "the oldest one on the table is the one the next person takes");

            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Assert.AreEqual("g7h8i9", _august.ErrandOnOfferFor("Selling").Mark,
                "she took the first one, so the next one moves up on its own");
        }

        [TestMethod]
        public void Festival_HoldsSeveralPeopleOnErrandsAtTheSameTime()
        {
            _elder.Entrust(_august, "Selling", "a1");
            _elder.Entrust(_august, "Selling", "b2");
            _elder.Entrust(_august, "Calling", "c3");
            _elder.Entrust(_august, "Calling", "d4");

            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1"), "Marta");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("b2"), "Rosa");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("c3"), "Tomas");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("d4"), "Luis");

            Assert.AreEqual(4, _august.ErrandCount);
            Assert.AreEqual("Selling", _august.WhatWasEntrustedTo("Marta"));
            Assert.AreEqual("Selling", _august.WhatWasEntrustedTo("Rosa"));
            Assert.AreEqual("Calling", _august.WhatWasEntrustedTo("Tomas"));
            Assert.AreEqual("Calling", _august.WhatWasEntrustedTo("Luis"),
                "two selling and two calling at the same funcion: the papers are a roster, "
                + "not a single chair");
        }

        [TestMethod]
        public void Festival_CountsErrandsFromOneAndComplainsInProse()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.AreEqual("a1b2c3", _august.ErrandAt(1).Mark);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _august.ErrandAt(0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _august.ErrandAt(2));
        }

        [TestMethod]
        public void Festival_ComplainsAboutAMarkItNeverHandedOut()
        {
            Assert.ThrowsException<VillageRuleException>(() => _august.ErrandMarked("nolodi"));
            Assert.ThrowsException<ArgumentException>(() => _august.ErrandMarked(""));
        }

        [TestMethod]
        public void Errand_DoesNotFollowAnybodyIntoTheNextFestival()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Festival october = _elder.CreateFestival("Kermés de octubre", OctoberFair, Today);

            Assert.AreEqual(0, october.ErrandCount);
            Assert.IsNull(october.WhatWasEntrustedTo("Marta"),
                "selling in August entrusts her with nothing in October");
            Assert.ThrowsException<VillageRuleException>(() => october.ErrandMarked("a1b2c3"));
        }

        [TestMethod]
        public void Festival_AnswersAboutErrandsWithoutNulls()
        {
            Assert.AreEqual(string.Empty, _august.MarkOnOfferFor("Selling"),
                "the organizer reads this straight out of the journal, and a print "
                + "cannot walk into nothing: no paper on the table is an empty answer");

            _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.AreEqual("a1b2c3", _august.MarkOnOfferFor("Selling"));
            Assert.AreEqual(string.Empty, _august.MarkOnOfferFor("Calling"));

            Assert.IsFalse(_august.ErrandBelongsTo("a1b2c3", "Marta"),
                "nobody took it yet, so it belongs to nobody");

            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Assert.IsTrue(_august.ErrandBelongsTo("a1b2c3", "Marta"));
            Assert.IsTrue(_august.ErrandBelongsTo("a1b2c3", "MARTA"));
            Assert.IsFalse(_august.ErrandBelongsTo("a1b2c3", "Rosa"));
            Assert.IsFalse(_august.ErrandBelongsTo("a1b2c3", ""));
            Assert.AreEqual(string.Empty, _august.MarkOnOfferFor("Selling"),
                "she took it, so there is nothing on the table until the elder cuts another");

            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            Assert.IsFalse(_august.ErrandBelongsTo("a1b2c3", "Marta"),
                "sent home means the paper stops being hers");
        }

        [TestMethod]
        public void Errand_ForSellingMakesHerAMerchantOfThisFestival()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Merchant marta = _august.MerchantMarked("a1b2c3");

            Assert.AreEqual("Marta", marta.Name);
            Assert.AreSame(marta, _august.MerchantMarked("a1b2c3"),
                "the same paper always gives back the SAME merchant: the festival tells "
                + "who holds a number by reference, so a second object with her name "
                + "would hold nothing and she could not sell what she has");
        }

        [TestMethod]
        public void Errand_KeepsHerTheSameMerchantWhenSheComesBack()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Merchant first = _august.MerchantMarked("a1b2c3");

            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "MARTA");

            Assert.AreSame(first, _august.MerchantMarked("a1b2c3"),
                "she shows up again and again, and the numbers she is holding must not "
                + "slip out of her hands because a second Marta was made");
        }

        [TestMethod]
        public void Errand_ForCallingMakesNobodyAMerchant()
        {
            _elder.Entrust(_august, "Calling", "d4e5f6");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("d4e5f6"), "Tomas");

            Assert.ThrowsException<VillageRuleException>(
                () => _august.MerchantMarked("d4e5f6"),
                "whoever keys in the numbers never touches a card");
        }

        [TestMethod]
        public void Errand_HasNoMerchantWhileItSitsOnTheTable()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.ThrowsException<VillageRuleException>(
                () => _august.MerchantMarked("a1b2c3"),
                "nobody took the paper, so there is nobody to hand cards to");
        }

        [TestMethod]
        public void Errand_StillNamesHerMerchantAfterSheWasSentHome()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Merchant marta = _august.MerchantMarked("a1b2c3");

            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));

            Assert.AreSame(marta, _august.MerchantMarked("a1b2c3"),
                "the elder still has to be able to take the cards back out of her hands, "
                + "and only the merchant who holds them can give them back");
        }

        [TestMethod]
        public void Errand_GivesTheSameNameTwoHandsWhenSheIsOnTwoPapers()
        {
            _elder.PutUpForSale(_august, new long[] { 1, 2, 3, 4 });

            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");
            _elder.HandTo(_august, _august.MerchantMarked("a1b2c3"), new long[] { 1, 2 });

            // Se reinstalo la app y escaneo el codigo nuevo: mismo nombre, otro papel.
            _elder.Entrust(_august, "Selling", "d4e5f6");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("d4e5f6"), "Marta");
            _elder.HandTo(_august, _august.MerchantMarked("d4e5f6"), new long[] { 3 });

            Assert.AreNotSame(_august.MerchantMarked("a1b2c3"), _august.MerchantMarked("d4e5f6"),
                "the paper is the identity, not the name: two papers are two merchants");

            CollectionAssert.AreEqual(new long[] { 1, 2 },
                new List<long>(_august.NumbersWith(_august.MerchantMarked("a1b2c3"))).ToArray());
            CollectionAssert.AreEqual(new long[] { 3 },
                new List<long>(_august.NumbersWith(_august.MerchantMarked("d4e5f6"))).ToArray());

            // Y por eso recoger por NOMBRE no sirve: si el que devuelve no es el que tiene,
            // el pueblo lo rechaza entero y no se puede mandar a nadie a la casa.
            Assert.ThrowsException<VillageRuleException>(
                () => _august.MerchantMarked("d4e5f6").GiveBack(_august, new long[] { 1, 2 }),
                "the second paper is not holding what the first one holds");

            Assert.AreEqual(1, _august.CountWith(_august.MerchantMarked("d4e5f6")),
                "el rechazo no le saco nada de la mano al segundo papel");
            Assert.AreEqual(2, _august.CountWith(_august.MerchantMarked("a1b2c3")),
                "ni al primero");
        }

        [TestMethod]
        public void Errand_HandsBackWhatThatVeryPaperIsHolding()
        {
            _elder.PutUpForSale(_august, new long[] { 1, 2, 3, 4 });

            _elder.Entrust(_august, "Selling", "a1b2c3");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");
            _elder.HandTo(_august, _august.MerchantMarked("a1b2c3"), new long[] { 1, 2 });

            _elder.Entrust(_august, "Selling", "d4e5f6");
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("d4e5f6"), "Marta");
            _elder.HandTo(_august, _august.MerchantMarked("d4e5f6"), new long[] { 3 });

            Merchant second = _august.MerchantMarked("d4e5f6");

            second.GiveBack(_august, _august.NumbersWith(second));
            _elder.SendHome(_august, _august.ErrandMarked("d4e5f6"));

            CollectionAssert.AreEqual(new long[] { 3, 4 },
                new List<long>(_august.NumbersInTheBook()).ToArray(),
                "what the revoked paper was holding goes back to the book, and what the "
                + "other paper still holds stays out of it");
        }

        [TestMethod]
        public void Elder_CutsAnErrandForANamedPersonWithCardsSetAside()
        {
            Errand forSonia = _elder.Entrust(_august, "Selling", "a1b2c3", "Sonia", 40);

            Assert.AreEqual("Sonia", forSonia.MeantFor);
            Assert.AreEqual(40, forSonia.CardsMeant);
            Assert.IsTrue(forSonia.IsOnOffer);
            Assert.AreEqual(string.Empty, forSonia.TakenBy,
                "cut for her, but nobody has taken it up yet");
        }

        [TestMethod]
        public void Elder_CutsSeveralErrandsForTheSameChoreAtOnce()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3", "Sonia", 40);
            _elder.Entrust(_august, "Selling", "d4e5f6", "Marta", 25);
            _elder.Entrust(_august, "Selling", "g7h8i9", "Rita", 0);

            Assert.AreEqual(3, _august.ErrandCount,
                "the elder does not have to wait for one to be taken up to cut the next");

            Assert.AreEqual("a1b2c3", _august.ErrandOnOfferMeantFor("Sonia").Mark);
            Assert.AreEqual("d4e5f6", _august.MarkOnOfferMeantFor("Marta"));
            Assert.AreEqual(25, _august.ErrandOnOfferMeantFor("marta").CardsMeant,
                "the name is looked up the way it is said, not the way it is written");
        }

        [TestMethod]
        public void Errand_MeantForSomebodyCanEndUpInSomebodyElsesHands()
        {
            _elder.Entrust(_august, "Selling", "a1b2c3", "Sonia", 40);
            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");

            Errand errand = _august.ErrandAt(1);

            Assert.AreEqual("Sonia", errand.MeantFor, "who it was cut for does not change");
            Assert.AreEqual("Marta", errand.TakenBy, "who carries it is who took it up");
            Assert.IsNull(_august.ErrandOnOfferMeantFor("Sonia"),
                "hers is no longer on the table, so asking for it again cuts another one");
        }

        [TestMethod]
        public void Errand_CutForNobodyInParticularSaysSo()
        {
            Errand errand = _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.AreEqual(string.Empty, errand.MeantFor);
            Assert.AreEqual(0, errand.CardsMeant);
            Assert.IsNull(_august.ErrandOnOfferMeantFor("Sonia"));
            Assert.IsNull(_august.ErrandOnOfferMeantFor("   "));

            // Asking for the mark of an errand that is not there answers empty, not a
            // nothing: whoever asks from a script has nothing to hold a nothing with.
            Assert.AreEqual(string.Empty, _august.MarkOnOfferMeantFor("Sonia"));
            Assert.AreEqual(string.Empty, _august.MarkOnOfferMeantFor(null));
        }

        [TestMethod]
        public void Errand_SaysWhatItIsInWords()
        {
            Errand onTheTable = _elder.Entrust(_august, "Selling", "a1b2c3");

            Assert.AreEqual("Selling on the table", onTheTable.ToString());

            _elder.HandTheErrandTo(_august, _august.ErrandMarked("a1b2c3"), "Marta");
            Assert.AreEqual("Selling in the hands of Marta", onTheTable.ToString());

            _elder.SendHome(_august, _august.ErrandMarked("a1b2c3"));
            Assert.AreEqual("Selling, Marta was sent home", onTheTable.ToString());
        }
    }
}
