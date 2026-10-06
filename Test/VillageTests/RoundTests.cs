using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Village;

namespace VillageTests
{
    /// <summary>
    /// La funcion se juega por rondas, y el premio vive en la FIGURA: cada ronda es un
    /// puñado de promesas — esta figura da esto — y queda completa cuando todas se
    /// cumplieron. En efectivo se reparte entre los ganadores de esa figura; un bien
    /// lo reparten ellos como quieran: el pueblo solo deja escrito quienes ganaron.
    ///
    /// La escena: el padre anuncia dos rondas. La primera promete la fila por $10 y el
    /// cuadrito por $25; la segunda, carton lleno por una canasta. Rita ya compro los
    /// cartones 1, 2 y 3.
    /// </summary>
    [TestClass]
    public class RoundTests
    {
        private static readonly CalendarDay Today = new CalendarDay(2026, 8, 11);
        private static readonly CalendarDay MothersDay = new CalendarDay(2026, 8, 15);

        private Elder _elder;
        private Merchant _maria;
        private Villager _rita;
        private Festival _festival;

        [TestInitialize]
        public void Setup()
        {
            _elder = new Elder("Padre Gerardo");
            _maria = new Merchant("María Rojas");
            _rita = new Villager("Rita Mora", "8888-3333");
            _festival = _elder.CreateFestival("Día de la Madre", MothersDay, Today);

            _elder.PutUpForSale(_festival, new long[] { 1, 2, 3, 4, 5 });
            _elder.HandTo(_festival, _maria, new long[] { 1, 2, 3, 4 });
            _maria.SellThemTo(_festival, new long[] { 1, 2, 3 }, _rita);

            _elder.AnnounceRound(_festival);
            _elder.Promise(_festival, "AnyRow", "Cash", "10");
            _elder.Promise(_festival, "AnySquare", "Cash", "25");

            _elder.AnnounceRound(_festival);
            _elder.Promise(_festival, "FullCard", "Goods", "una canasta");
        }

        // ── anunciar y prometer ──────────────────────────────────────────────────

        [TestMethod]
        public void TheElderAnnouncesRoundsMadeOfPromises()
        {
            Assert.AreEqual(2, _festival.RoundCount);

            Round first = _festival.RoundAt(1);
            Assert.AreEqual(2, first.PromiseCount);
            Assert.AreEqual("AnyRow,AnySquare", first.Playing,
                "las figuras de la ronda se derivan de sus promesas");
            Assert.AreEqual("10", first.PromiseOn("AnyRow").Prize);
            Assert.IsTrue(first.PromiseOn("AnyRow").IsCash);
            Assert.AreEqual("25", first.PromiseOn("AnySquare").Prize);
            Assert.AreEqual("Planned", first.HowItStands);

            Round second = _festival.RoundAt(2);
            Assert.AreEqual("una canasta", second.PromiseAt(1).Prize);
            Assert.IsFalse(second.PromiseAt(1).IsCash,
                "la canasta no es plata: el pueblo distingue el efectivo de lo material");

            Assert.AreEqual(0, _festival.CurrentRoundPosition,
                "anunciar y prometer no arrancan nada");
        }

        [TestMethod]
        public void APromiseLandsOnTheLastRoundStillOnTheTable()
        {
            _elder.Promise(_festival, "Diagonal", "Cash", "5");

            Assert.AreEqual(2, _festival.RoundAt(2).PromiseCount,
                "la promesa cae en la ULTIMA ronda anunciada, no en la primera");
            Assert.AreEqual(2, _festival.RoundAt(1).PromiseCount);
        }

        [TestMethod]
        public void NobodyPromisesWithoutARoundOnTheTable()
        {
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Promise(_festival, "Diagonal", "Cash", "5"),
                "las dos rondas ya se jugaron: hay que anunciar otra antes de prometer");
        }

        [TestMethod]
        public void APromiseOfAnotherRound_CannotBeWonInTheOneThatPlays()
        {
            _elder.BeginRound(_festival);
            Promise ofTheSecondRound = _festival.RoundAt(2).PromiseOn("FullCard");

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.NameWinner(_festival, _festival.ParticipationNumbered(1), ofTheSecondRound));

            Assert.AreEqual(0, ofTheSecondRound.WinnerCount);
            Assert.AreEqual(0, _festival.RoundAt(1).WinnerCount);
        }

        [TestMethod]
        public void ARuleOfTheVillage_IsAVillageException()
        {
            VillageException refused = Assert.ThrowsException<VillageRuleException>(
                () => _elder.Promise(_festival, "AnyRow,AnySquare", "Cash", "10"));

            Assert.IsInstanceOfType(refused, typeof(VillageException));
        }

        [TestMethod]
        public void APromiseTakesOneFigureWithItsOwnPrize()
        {
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Promise(_festival, "AnyRow,AnySquare", "Cash", "10"),
                "una figura por promesa: cada una con su premio");

            Assert.ThrowsException<ArgumentException>(
                () => _elder.Promise(_festival, " ", "Cash", "10"));

            Assert.ThrowsException<ArgumentException>(
                () => _elder.Promise(_festival, "Diagonal", "Cash", " "));

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Promise(_festival, "Diagonal", "loteria", "5"),
                "el pueblo solo conoce dos formas de premio");
        }

        [TestMethod]
        public void TheSameFigureIsNotPromisedTwiceInARound()
        {
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Promise(_festival, "FullCard", "Cash", "5"),
                "la ronda 2 ya promete algo por el carton lleno");
        }

        [TestMethod]
        public void PromisesAreMadeBeforeTheRoundBegins()
        {
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);

            // La que sigue en la mesa es la ronda 2; la 1 ya se jugo.
            _elder.Promise(_festival, "Diagonal", "Cash", "5");

            Assert.AreEqual(2, _festival.RoundAt(2).PromiseCount);
        }

        // ── empezarlas ───────────────────────────────────────────────────────────

        [TestMethod]
        public void TheFirstPlannedRoundIsTheOneThatBegins()
        {
            _elder.BeginRound(_festival);

            Assert.AreEqual("Playing", _festival.RoundAt(1).HowItStands);
            Assert.AreEqual(1, _festival.CurrentRoundPosition);
            Assert.AreEqual("Planned", _festival.RoundAt(2).HowItStands);
        }

        [TestMethod]
        public void ARoundWithNothingPromisedDoesNotBegin()
        {
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);

            _elder.AnnounceRound(_festival);

            Assert.ThrowsException<VillageRuleException>(() => _elder.BeginRound(_festival),
                "una ronda sin promesas no tiene nada que jugar");
        }

        [TestMethod]
        public void ARoundDoesNotBeginWhileAnotherIsPlaying()
        {
            _elder.BeginRound(_festival);

            Assert.ThrowsException<VillageRuleException>(() => _elder.BeginRound(_festival),
                "primero se resuelve la que suena, despues arranca la que sigue");
        }

        [TestMethod]
        public void WithNothingPlannedThereIsNothingToBegin()
        {
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);

            Assert.ThrowsException<VillageRuleException>(() => _elder.BeginRound(_festival),
                "la agenda se agoto: el padre tiene que anunciar otra ronda o cerrar");
        }

        // ── los ganadores, figura por figura ─────────────────────────────────────

        [TestMethod]
        public void TheWinnerIsNamedOnTheFigureHeCompleted()
        {
            _elder.BeginRound(_festival);

            _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow"));

            Promise theRow = _festival.RoundAt(1).PromiseOn("AnyRow");
            Assert.IsTrue(theRow.WasKept);
            Assert.AreEqual(3, theRow.WinnerAt(1));
            Assert.IsTrue(theRow.Won(3));

            Assert.IsFalse(_festival.RoundAt(1).PromiseOn("AnySquare").WasKept,
                "el cuadrito sigue esperando a su ganador: cada figura da lo suyo");
        }

        [TestMethod]
        public void TheSameCardCanWinTwoFiguresOfTheRound()
        {
            _elder.BeginRound(_festival);

            _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow"));
            _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnySquare"));

            Assert.IsTrue(_festival.RoundAt(1).PromiseOn("AnyRow").Won(3));
            Assert.IsTrue(_festival.RoundAt(1).PromiseOn("AnySquare").Won(3),
                "dos figuras, dos premios: el mismo carton puede llevarse los dos");
        }

        [TestMethod]
        public void SeveralWinnersShareThePrizeOfTheirFigure()
        {
            _elder.BeginRound(_festival);

            _elder.NameWinner(_festival, _festival.ParticipationNumbered(1), _festival.PromiseInPlayOn("AnyRow"));
            _elder.NameWinner(_festival, _festival.ParticipationNumbered(2), _festival.PromiseInPlayOn("AnyRow"));

            Promise theRow = _festival.RoundAt(1).PromiseOn("AnyRow");
            Assert.AreEqual(2, theRow.WinnerCount,
                "el premio es de la figura: como lo repartan entre los dos es asunto de ellos");
            CollectionAssert.AreEqual(new long[] { 1, 2 }, theRow.NumbersThatWon().ToArray(),
                "los nombra en el orden en que ganaron");
        }

        [TestMethod]
        public void NobodyWinsAFigureTheRoundDoesNotPromise()
        {
            _elder.BeginRound(_festival);

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("FullCard")),
                "el carton lleno es de la ronda 2, no de esta");
        }

        [TestMethod]
        public void OnlyABoughtNumberCanWin()
        {
            _elder.BeginRound(_festival);

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.NameWinner(_festival, _festival.ParticipationNumbered(4), _festival.PromiseInPlayOn("AnyRow")),
                "el 4 esta en la mano de Maria, nadie lo compro");
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.NameWinner(_festival, _festival.ParticipationNumbered(5), _festival.PromiseInPlayOn("AnyRow")),
                "el 5 sigue en el libro");
        }

        [TestMethod]
        public void TheSameNumberIsNotNamedTwiceOnTheSameFigure()
        {
            _elder.BeginRound(_festival);
            _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow"));

            Assert.ThrowsException<VillageRuleException>(
                () => _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow")));
        }

        [TestMethod]
        public void NobodyWinsARoundThatIsNotPlaying()
        {
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow")),
                "ninguna ronda suena todavia");
        }

        // ── resolverlas ──────────────────────────────────────────────────────────

        [TestMethod]
        public void TheRoundIsCompleteWhenEveryPromiseWasKept()
        {
            _elder.BeginRound(_festival);
            _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow"));

            Assert.IsFalse(_festival.RoundAt(1).EveryPrizeWasGiven,
                "el cuadrito todavia debe su premio");

            _elder.NameWinner(_festival, _festival.ParticipationNumbered(1), _festival.PromiseInPlayOn("AnySquare"));

            Assert.IsTrue(_festival.RoundAt(1).EveryPrizeWasGiven,
                "todas las figuras entregaron: la ronda esta completa");
        }

        [TestMethod]
        public void SettledWithWinnersItWasPlayed()
        {
            _elder.BeginRound(_festival);
            _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow"));

            _elder.SettleRound(_festival);

            Assert.AreEqual("Played", _festival.RoundAt(1).HowItStands);
            Assert.AreEqual(0, _festival.CurrentRoundPosition);
            Assert.IsFalse(_festival.RoundAt(1).PromiseOn("AnySquare").WasKept,
                "la promesa que no se cumplio queda escrita, con su premio sin entregar");
            Assert.AreEqual("25", _festival.RoundAt(1).PromiseOn("AnySquare").Prize);
        }

        [TestMethod]
        public void SettledWithoutWinnersItWasCalledOff()
        {
            _elder.BeginRound(_festival);

            _elder.SettleRound(_festival);

            Assert.AreEqual("CalledOff", _festival.RoundAt(1).HowItStands);
            Assert.AreEqual(0, _festival.RoundAt(1).WinnerCount);
        }

        [TestMethod]
        public void ASettledRoundDoesNotComeBack()
        {
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);

            Assert.ThrowsException<VillageRuleException>(() => _elder.SettleRound(_festival),
                "ya no hay ronda sonando que resolver");
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow")),
                "tarde: la ronda ya quedo resuelta");
        }

        // ── terminar todo ────────────────────────────────────────────────────────

        [TestMethod]
        public void TheElderClosesTheFestivalWhenNothingIsPlaying()
        {
            _elder.BeginRound(_festival);
            _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow"));
            _elder.SettleRound(_festival);

            _elder.CloseFestival(_festival);

            Assert.AreEqual("Closed", _festival.State);
            Assert.IsTrue(_festival.IsOver);
            Assert.AreEqual("Planned", _festival.RoundAt(2).HowItStands,
                "la ronda que nunca se jugo queda anunciada y nada mas: la historia es honesta");
        }

        [TestMethod]
        public void TheFestivalDoesNotCloseMidRound()
        {
            _elder.BeginRound(_festival);

            Assert.ThrowsException<VillageRuleException>(() => _elder.CloseFestival(_festival),
                "primero se resuelve la ronda que suena");
        }

        [TestMethod]
        public void AClosedFestivalIsOverForGood()
        {
            _elder.CloseFestival(_festival);

            Assert.ThrowsException<VillageRuleException>(() => _elder.CloseFestival(_festival));
            Assert.ThrowsException<VillageRuleException>(() => _elder.AnnounceRound(_festival));
            Assert.ThrowsException<VillageRuleException>(
                () => _elder.Promise(_festival, "AnyRow", "Cash", "5"));
            Assert.ThrowsException<VillageRuleException>(() => _elder.BeginRound(_festival));
        }

        // ── el gobierno y el print ───────────────────────────────────────────────

        [TestMethod]
        public void OnlyTheGoverningElderRunsTheRounds()
        {
            Elder anotherElder = new Elder("Padre Anselmo");

            Assert.ThrowsException<VillageRuleException>(() => anotherElder.AnnounceRound(_festival));
            Assert.ThrowsException<VillageRuleException>(
                () => anotherElder.Promise(_festival, "AnyRow", "Cash", "5"));
            Assert.ThrowsException<VillageRuleException>(() => anotherElder.BeginRound(_festival));
            Assert.ThrowsException<VillageRuleException>(
                () => anotherElder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow")));
            Assert.ThrowsException<VillageRuleException>(() => anotherElder.SettleRound(_festival));
            Assert.ThrowsException<VillageRuleException>(() => anotherElder.CloseFestival(_festival));
        }

        [TestMethod]
        public void TheRosterAnswersWithoutNulls()
        {
            Round round = _festival.RoundAt(1);
            Promise promise = round.PromiseAt(1);

            Assert.IsNotNull(round.Playing);
            Assert.IsNotNull(round.HowItStands);
            Assert.IsNotNull(promise.Playing);
            Assert.IsNotNull(promise.Prize);
            Assert.IsFalse(string.IsNullOrWhiteSpace(round.ToString()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(promise.ToString()));

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _festival.RoundAt(3));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => round.PromiseAt(3));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => promise.WinnerAt(1),
                "todavia nadie gano nada");
            Assert.ThrowsException<VillageRuleException>(() => round.PromiseOn("Diagonal"));
        }

        [TestMethod]
        public void WhatWasPromisedIsToldAsAStory()
        {
            Assert.AreEqual(
                "round 1, AnyRow for 10 in cash and AnySquare for 25 in cash, still to come",
                _festival.RoundAt(1).ToString());

            _elder.BeginRound(_festival);
            _elder.NameWinner(_festival, _festival.ParticipationNumbered(3), _festival.PromiseInPlayOn("AnyRow"));

            Assert.AreEqual(
                "round 1, AnyRow for 10 in cash, kept by 1 winner(s) and AnySquare for 25 in cash, playing now",
                _festival.RoundAt(1).ToString());

            _elder.SettleRound(_festival);
            _elder.BeginRound(_festival);
            _elder.SettleRound(_festival);

            Assert.AreEqual(
                "round 2, called off with every prize left ungiven",
                _festival.RoundAt(2).ToString());
        }
    }
}
