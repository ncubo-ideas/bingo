using System;
using System.Collections.Generic;
using System.IO;
using BingoEngine;
using Choreography.Theater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Village;

namespace BingoEngineDslTests
{
    [TestClass]
    public class VillageDslEndToEndTest
    {
        private const string Mark = "casa-01";
        private const string BuyerMark = "rita-01";
        private static readonly int[] Row0OfCard789 = { 12, 24, 44, 55, 61 };

        private static string FreshRoot(string name)
        {
            string root = Path.Combine(Path.GetTempPath(), "bingo_dsl_" + name);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            return root;
        }

        private static PerformanceV2 Open(string name, string root) =>
            new PerformanceV2(name, typeof(Elder).Assembly, typeof(BingoGame).Assembly)
                .ConfigureStorage(DatabaseType.FileSystem, root)
                .Start();

        private static PerformanceV2 Reopen(PerformanceV2 actor, string name, string root)
        {
            actor.Dispose();
            return Open(name, root);
        }

        private static void CreateTheFestival(PerformanceV2 actor)
        {
            actor.Using(@"
upgrade('festival') {
    elder  = Elder(@communityLeader);
    dia    = CalendarDay(@anio, @mes, @diaMes);
    hoy    = CalendarDay(@hoyAnio, @hoyMes, @hoyDia);
    festival = elder.CreateFestival(@comoSeLlama, dia, hoy);
    mesa   = BingoGame();
}")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("communityLeader", "Bartolo");
                    p.UserParameter<int>("anio", 2026);
                    p.UserParameter<int>("mes", 8);
                    p.UserParameter<int>("diaMes", 15);
                    p.UserParameter<int>("hoyAnio", 2026);
                    p.UserParameter<int>("hoyMes", 7);
                    p.UserParameter<int>("hoyDia", 30);
                    p.UserParameter<string>("comoSeLlama", "Harvest Fair");
                })
                .PerformCommand();

            actor.Using("doorsTime = ClockTime(@hora, @minuto); elder.OpenTheDoorsAt(festival, doorsTime);")
                .WithParameters(p =>
                {
                    p.UserParameter<int>("hora", 18);
                    p.UserParameter<int>("minuto", 30);
                })
                .PerformCommand();

            actor.Using("elder.PutUpForSale(festival, @cartones);")
                .WithParameters(p =>
                    p.UserParameter<IEnumerable<long>>("cartones", new List<long> { 787, 788, 789, 790 }))
                .PerformCommand();

            actor.Using("cardPrice = Price(@precio); elder.PriceCardsAt(festival, cardPrice);")
                .WithParameters(p => p.UserParameter<long>("precio", 1000))
                .PerformCommand();

            actor.Using("winsAnnouncement = WinsAnnouncement(@modo); elder.AnnounceWinsBy(festival, winsAnnouncement);")
                .WithParameters(p => p.UserParameter<string>("modo", "Automatic"))
                .PerformCommand();

            actor.Using("elder.AnnounceRound(festival);").PerformCommand();

            actor.Using("elder.Promise(festival, @figura, @forma, @premio);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("figura", "Row0");
                    p.UserParameter<string>("forma", "Goods");
                    p.UserParameter<string>("premio", "A basket");
                })
                .PerformCommand();
        }

        private static void BeginTheRound(PerformanceV2 actor)
        {
            actor.Using("elder.BeginRound(festival); mesa.StartRound(@figurasEnJuego, @modo);")
                .WithParameters(p =>
                {
                    p.UserParameter<IEnumerable<string>>("figurasEnJuego", new List<string> { "Row0" });
                    p.UserParameter<string>("modo", "Automatic");
                })
                .PerformCommand();
        }

        private static void OpenTheDoor(PerformanceV2 actor)
        {
            actor.Using("elder.Entrust(festival, @oficio, @marca, @para, @cartones);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("oficio", "Selling");
                    p.UserParameter<string>("marca", Mark);
                    p.UserParameter<string>("para", "La casa");
                    p.UserParameter<int>("cartones", 0);
                })
                .PerformCommand();

            actor.Using("errand = festival.ErrandMarked(@marca); elder.HandTheErrandTo(festival, errand, @nombre);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", Mark);
                    p.UserParameter<string>("nombre", "La casa");
                })
                .PerformCommand();
        }

        private static void SellAtTheDoor(PerformanceV2 actor, long card)
        {
            actor.Using("elder.HandTo(festival, festival.MerchantMarked(@marca), @cartones);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", Mark);
                    p.UserParameter<IEnumerable<long>>("cartones", new List<long> { card });
                })
                .PerformCommand();

            actor.Using(@"
{
    comprador = Villager(@nombre, @contacto, @marcaComprador);
    festival.MerchantMarked(@marca).SellThemTo(festival, @cartones, comprador);
    foreach (numero in @cartones) {
        mesa.PutCardInPlay(numero);
    }
}")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", Mark);
                    p.UserParameter<string>("nombre", "Rita");
                    p.UserParameter<string>("contacto", "en el bingo");
                    p.UserParameter<string>("marcaComprador", BuyerMark);
                    p.UserParameter<IEnumerable<long>>("cartones", new List<long> { card });
                })
                .PerformCommand();
        }

        private static void Call(PerformanceV2 actor, int number, int round)
        {
            actor.Using("mesa.RegisterDraw(@numero, @ronda);")
                .WithParameters(p =>
                {
                    p.UserParameter<int>("numero", number);
                    p.UserParameter<int>("ronda", round);
                })
                .PerformCommand();
        }

        private static void GiveThePrize(PerformanceV2 actor, long card, string figure)
        {
            actor.Using("tableCard = mesa.CardInPlay(@carton); figure = mesa.FigureInPlay(@figura); "
                + "mesa.ClaimBingo(tableCard, figure); mesa.ValidateWinner(tableCard, figure);")
                .WithParameters(p =>
                {
                    p.UserParameter<int>("carton", (int)card);
                    p.UserParameter<string>("figura", figure);
                })
                .PerformCommand();

            actor.Using("card = festival.ParticipationNumbered(@carton); figurePromise = festival.PromiseInPlayOn(@figura); elder.NameWinner(festival, card, figurePromise);")
                .WithParameters(p =>
                {
                    p.UserParameter<int>("carton", (int)card);
                    p.UserParameter<string>("figura", figure);
                })
                .PerformCommand();
        }

        private static void BeforeTheFirstBall(PerformanceV2 actor, string andAlso, string command, Action<Parameters> values)
        {
            string refused = actor.Using(
                "Check(mesa.DrawnCount == 0) Error 'the round already started';" + andAlso,
                command)
                .WithParameters(values)
                .PerformCheckThenCommand();

            Assert.IsTrue(string.IsNullOrWhiteSpace(refused), "the check refused the command: " + refused);
        }

        private static void Plead(PerformanceV2 actor, long card) =>
            BeforeTheFirstBall(actor, "",
                "seller = festival.MerchantMarked(@marca); card = festival.ParticipationNumbered(@numero); seller.Plead(festival, card);",
                p =>
                {
                    p.UserParameter<string>("marca", Mark);
                    p.UserParameter<int>("numero", (int)card);
                });
        private static void AssertTheFestivalWasCreated(PerformanceV2 actor)
        {
            StringAssert.Contains(actor.PerformQry("print festival.Name n;"), "Harvest Fair");
            StringAssert.Contains(actor.PerformQry("print festival.Day.ToString() d;"), "2026-08-15");
            StringAssert.Contains(actor.PerformQry("print festival.DoorsOpenAt h;"), "18:30");
            StringAssert.Contains(actor.PerformQry("print festival.CountInTheBook n;"), "\"n\":4");
            StringAssert.Contains(actor.PerformQry("print festival.CardPrice p;"), "\"p\":1000");
            StringAssert.Contains(actor.PerformQry("print festival.WinsAnnounced w;"), "Automatic");
            StringAssert.Contains(actor.PerformQry("print festival.RoundCount n;"), "\"n\":1");
            StringAssert.Contains(actor.PerformQry("print festival.RoundAt(1).PromiseOn('Row0').Prize p;"), "A basket");
        }

        [TestMethod]
        [TestCategory("Village")]
        public void TheFestivalIsBornTheWayTheAppCreatesIt_AndSurvivesARestart()
        {
            const string name = "bingo_dsl_born";
            string root = FreshRoot("born");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);
            AssertTheFestivalWasCreated(actor);

            actor = Reopen(actor, name, root);

            try
            {
                AssertTheFestivalWasCreated(actor);
                StringAssert.Contains(actor.PerformQry("print mesa.State s;"), "Waiting");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void TheSaleAtTheDoor_LeavesTheCardSoldAndOnTheTable_AndSurvivesARestart()
        {
            const string name = "bingo_dsl_sale";
            string root = FreshRoot("sale");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);
            OpenTheDoor(actor);
            SellAtTheDoor(actor, 789);

            actor = Reopen(actor, name, root);

            try
            {
                StringAssert.Contains(actor.PerformQry("print festival.ParticipationNumbered(789).Whereabouts w;"), "Sold");
                StringAssert.Contains(actor.PerformQry("print festival.ParticipationNumbered(789).Buyer.Name b;"), "Rita");
                StringAssert.Contains(actor.PerformQry("print festival.CountBoughtBy('" + BuyerMark + "') n;"), "\"n\":1");
                StringAssert.Contains(actor.PerformQry("print festival.CountInTheBook n;"), "\"n\":3");
                StringAssert.Contains(actor.PerformQry("print mesa.CardsInPlayCount n;"), "\"n\":1");
                StringAssert.Contains(actor.PerformQry("print mesa.IsInPlay(789) i;"), "true");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void ARoundWithAPrize_TheVillageRecordsTheWinnerOfTheFigure_AndSurvivesARestart()
        {
            const string name = "bingo_dsl_prize";
            string root = FreshRoot("prize");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);
            OpenTheDoor(actor);
            SellAtTheDoor(actor, 789);
            BeginTheRound(actor);

            foreach (int number in Row0OfCard789) Call(actor, number, 1);

            StringAssert.Contains(actor.PerformQry("print mesa.State s;"), "AwaitingValidation");

            GiveThePrize(actor, 789, "Row0");
            actor.Using("elder.SettleRound(festival); mesa.EndRound();").PerformCommand();

            actor = Reopen(actor, name, root);

            try
            {
                StringAssert.Contains(actor.PerformQry("print festival.RoundAt(1).PromiseOn('Row0').Won(789) g;"), "true");
                StringAssert.Contains(actor.PerformQry("print festival.RoundAt(1).WasPlayed p;"), "true");
                StringAssert.Contains(actor.PerformQry("print mesa.State s;"), "Finished");
                StringAssert.Contains(actor.PerformQry("print mesa.IsFigureClosed('Row0') c;"), "true");
                StringAssert.Contains(actor.PerformQry("print mesa.DrawnCount d;"), "\"d\":5");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void ACardThatIsNotForSale_IsNotSold_AndTheTableDoesNotGetIt()
        {
            const string name = "bingo_dsl_foreign";
            string root = FreshRoot("foreign");

            PerformanceV2 actor = Open(name, root);

            try
            {
                CreateTheFestival(actor);
                OpenTheDoor(actor);

                Assert.ThrowsException<VillageRuleException>(() => SellAtTheDoor(actor, 999),
                    "999 was never put up for sale");

                StringAssert.Contains(actor.PerformQry("print festival.SoldCount n;"), "\"n\":0");
                StringAssert.Contains(actor.PerformQry("print mesa.CardsInPlayCount n;"), "\"n\":0");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void APleaTheElderGrants_PutsTheCardBackInTheBookAndOffTheTable_AndSurvivesARestart()
        {
            const string name = "bingo_dsl_grant";
            string root = FreshRoot("grant");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);
            OpenTheDoor(actor);
            SellAtTheDoor(actor, 789);
            Plead(actor, 789);

            BeforeTheFirstBall(actor, "",
                "plea = festival.PleaAt(@pedido); elder.Grant(festival, plea); "
                + "tableCard = mesa.CardInPlay(@numero); mesa.TakeCardOutOfPlay(tableCard);",
                p =>
                {
                    p.UserParameter<int>("pedido", 1);
                    p.UserParameter<int>("numero", 789);
                });

            actor = Reopen(actor, name, root);

            try
            {
                StringAssert.Contains(actor.PerformQry("print festival.PleaAt(1).WasGranted g;"), "true");
                StringAssert.Contains(actor.PerformQry("print festival.ParticipationNumbered(789).Whereabouts w;"), "InTheBook");
                StringAssert.Contains(actor.PerformQry("print mesa.IsInPlay(789) i;"), "false");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void APleaTheElderRefuses_LeavesTheSaleStanding_AndSurvivesARestart()
        {
            const string name = "bingo_dsl_refuse";
            string root = FreshRoot("refuse");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);
            OpenTheDoor(actor);
            SellAtTheDoor(actor, 789);
            Plead(actor, 789);

            actor.Using("plea = festival.PleaAt(@pedido); elder.Refuse(festival, plea);")
                .WithParameters(p => p.UserParameter<int>("pedido", 1))
                .PerformCommand();

            actor = Reopen(actor, name, root);

            try
            {
                StringAssert.Contains(actor.PerformQry("print festival.PleaAt(1).WasRefused r;"), "true");
                StringAssert.Contains(actor.PerformQry("print festival.ParticipationNumbered(789).Whereabouts w;"), "Sold");
                StringAssert.Contains(actor.PerformQry("print mesa.IsInPlay(789) i;"), "true");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void AnExchange_GivesTheBuyerTheNewCardOnBothDomains_AndSurvivesARestart()
        {
            const string name = "bingo_dsl_exchange";
            string root = FreshRoot("exchange");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);
            OpenTheDoor(actor);
            SellAtTheDoor(actor, 789);

            actor.Using("elder.HandTo(festival, festival.MerchantMarked(@marca), @cartones);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", Mark);
                    p.UserParameter<IEnumerable<long>>("cartones", new List<long> { 790 });
                })
                .PerformCommand();

            BeforeTheFirstBall(actor,
                "Check(festival.MerchantCanExchange(@marca, @sale, @entra)) Error 'the exchange cannot be done';",
                "seller = festival.MerchantMarked(@marca); soldCard = festival.ParticipationNumbered(@sale); "
                + "incomingCard = festival.ParticipationNumbered(@entra); seller.ExchangeFor(festival, soldCard, incomingCard); "
                + "leavingCard = mesa.CardInPlay(@sale); mesa.TakeCardOutOfPlay(leavingCard); "
                + "mesa.PutCardInPlay(@entra);",
                p =>
                {
                    p.UserParameter<string>("marca", Mark);
                    p.UserParameter<int>("sale", 789);
                    p.UserParameter<int>("entra", 790);
                });

            actor = Reopen(actor, name, root);

            try
            {
                StringAssert.Contains(actor.PerformQry("print festival.ParticipationNumbered(790).Buyer.Name b;"), "Rita");
                StringAssert.Contains(actor.PerformQry("print festival.ParticipationNumbered(789).Whereabouts w;"), "WithMerchant");
                StringAssert.Contains(actor.PerformQry("print mesa.IsInPlay(790) i;"), "true");
                StringAssert.Contains(actor.PerformQry("print mesa.IsInPlay(789) i;"), "false");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void TheElderSendsACollaboratorHome_AndTheErrandStaysClosed_AfterARestart()
        {
            const string name = "bingo_dsl_sendhome";
            string root = FreshRoot("sendhome");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);
            OpenTheDoor(actor);

            actor.Using("errand = festival.ErrandMarked(@marca); elder.SendHome(festival, errand);")
                .WithParameters(p => p.UserParameter<string>("marca", Mark))
                .PerformCommand();

            actor = Reopen(actor, name, root);

            try
            {
                StringAssert.Contains(actor.PerformQry("print festival.ErrandMarked('" + Mark + "').WasSentHome s;"), "true");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void TheElderSellsFromTheBook_TheWayTheHuddleDemoDoes_AndSurvivesARestart()
        {
            const string name = "bingo_dsl_book";
            string root = FreshRoot("book");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);

            actor.Using(
                "comprador = Villager(@nombre, @contacto); "
                + "card = festival.ParticipationNumbered(@numero); elder.SellTo(festival, card, comprador); "
                + "mesa.PutCardInPlay(@numero);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("nombre", "Chepe");
                    p.UserParameter<string>("contacto", "the corner house");
                    p.UserParameter<long>("numero", 788);
                })
                .PerformCommand();

            actor = Reopen(actor, name, root);

            try
            {
                StringAssert.Contains(actor.PerformQry("print festival.ParticipationNumbered(788).Buyer.Name b;"), "Chepe");
                StringAssert.Contains(actor.PerformQry("print mesa.IsInPlay(788) i;"), "true");
            }
            finally
            {
                actor.Dispose();
            }
        }

        [TestMethod]
        [TestCategory("Village")]
        public void TheCardWasWrong_TheTableDismissesIt_AndItCannotClaimAgainAfterARestart()
        {
            const string name = "bingo_dsl_dismiss";
            string root = FreshRoot("dismiss");

            PerformanceV2 actor = Open(name, root);
            CreateTheFestival(actor);
            OpenTheDoor(actor);
            SellAtTheDoor(actor, 789);
            BeginTheRound(actor);

            foreach (int number in Row0OfCard789) Call(actor, number, 1);

            actor.Using("tableCard = mesa.CardInPlay(@carton); figure = mesa.FigureInPlay(@figura); "
                + "mesa.ClaimBingo(tableCard, figure); mesa.DismissWinner(tableCard, figure);")
                .WithParameters(p =>
                {
                    p.UserParameter<int>("carton", 789);
                    p.UserParameter<string>("figura", "Row0");
                })
                .PerformCommand();

            actor = Reopen(actor, name, root);

            try
            {
                StringAssert.Contains(actor.PerformQry("print mesa.WasDismissed(789, 'Row0') d;"), "true");
                StringAssert.Contains(actor.PerformQry("print mesa.State s;"), "Running");
                StringAssert.Contains(actor.PerformQry("print festival.RoundAt(1).PromiseOn('Row0').Won(789) g;"), "false");
            }
            finally
            {
                actor.Dispose();
            }
        }
    }
}
