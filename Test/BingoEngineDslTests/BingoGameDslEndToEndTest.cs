using System;
using System.IO;
using System.Reflection;
using BingoEngine;
using Choreography.Theater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace BingoEngineDslTests
{
    [TestClass]
    public class BingoGameDslEndToEndTest
    {
        private const string Row0OfCard789 =
            "game.RegisterDraw(12); game.RegisterDraw(24); game.RegisterDraw(44); " +
            "game.RegisterDraw(55); game.RegisterDraw(61);";

        private static PerformanceV1 NewActor(string name) =>
            new PerformanceV1(name, typeof(BingoGame).Assembly);

        private static string FreshRoot(string name)
        {
            string root = Path.Combine(Path.GetTempPath(), "bingo_dsl_" + name);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            return root;
        }

        [TestMethod]
        public void Dsl_CanCreateTheGameAndPutACardInPlay()
        {
            PerformanceV1 actor = NewActor("bingo_dsl_open");

            actor.PerformCmd("game = BingoGame(); game.PutCardInPlay(789);");

            StringAssert.Contains(actor.PerformQry("print game.CardsInPlayCount n;"), "\"n\":1");
            StringAssert.Contains(actor.PerformQry("print game.State s;"), "Waiting");
        }

        [TestMethod]
        public void Dsl_PlaysARoundUntilSomebodyWins()
        {
            PerformanceV1 actor = NewActor("bingo_dsl_round");
            actor.PerformCmd("game = BingoGame(); game.PutCardInPlay(789); game.StartRound({'Row0'});");

            StringAssert.Contains(actor.PerformQry("print game.State s;"), "Running");
            StringAssert.Contains(actor.PerformQry("print game.RoundNumber r;"), "\"r\":1");

            actor.PerformCmd(Row0OfCard789);

            StringAssert.Contains(actor.PerformQry("print game.State s;"), "AwaitingValidation");
            StringAssert.Contains(actor.PerformQry("print game.DrawnCount d;"), "\"d\":5");
        }

        [TestMethod]
        public void Dsl_ValidatesTheWinnerAndFinishesTheRound()
        {
            PerformanceV1 actor = NewActor("bingo_dsl_validate");
            actor.PerformCmd("game = BingoGame(); game.PutCardInPlay(789); game.StartRound({'Row0'});");
            actor.PerformCmd(Row0OfCard789);

            actor.PerformCmd("game.ValidateWinner(game.CardInPlay(789), game.FigureInPlay('Row0'));");

            StringAssert.Contains(actor.PerformQry("print game.State s;"), "Finished");
            StringAssert.Contains(actor.PerformQry("print game.IsFigureClosed('Row0') c;"), "true");
        }

        [TestMethod]
        public void Dsl_ClaimBingoIsCrossCheckedAndDecidesNothing()
        {
            PerformanceV1 actor = NewActor("bingo_dsl_claim");
            actor.PerformCmd(
                "game = BingoGame(); game.PutCardInPlay(789); game.PutCardInPlay(790); " +
                "game.StartRound({'Row0'},'OnClaim');");
            actor.PerformCmd(Row0OfCard789);

            StringAssert.Contains(actor.PerformQry("print game.State s;"), "Running");
            StringAssert.Contains(actor.PerformQry("print game.ClaimBingo(game.CardInPlay(789), game.FigureInPlay('Row0')) w;"), "true");
            StringAssert.Contains(actor.PerformQry("print game.ClaimBingo(game.CardInPlay(790), game.FigureInPlay('Row0')) w;"), "false");
        }

        [TestMethod]
        public void Dsl_CorrectsAMistypedBall()
        {
            PerformanceV1 actor = NewActor("bingo_dsl_correct");
            actor.PerformCmd("game = BingoGame(); game.PutCardInPlay(789); game.StartRound({'Row0'});");
            actor.PerformCmd("game.RegisterDraw(12); game.RegisterDraw(24); game.RegisterDraw(22);");

            StringAssert.Contains(actor.PerformQry("print game.State s;"), "Running");

            actor.PerformCmd("game.CorrectDraw(22,44);");

            StringAssert.Contains(actor.PerformQry("print game.DrawnCount d;"), "\"d\":3");
            StringAssert.Contains(actor.PerformQry("print game.State s;"), "Running");
        }

        [TestMethod]
        public void Dsl_ShowsTheFiveColumnsOfTheCard()
        {
            PerformanceV1 actor = NewActor("bingo_dsl_cardface");
            actor.PerformCmd("game = BingoGame(); game.PutCardInPlay(789);");

            string reading = actor.PerformQry(
                "print game.CardColumn(789,0) B; print game.CardColumn(789,1) I; "
                + "print game.CardColumn(789,2) N; print game.CardColumn(789,3) G; "
                + "print game.CardColumn(789,4) O;");

            StringAssert.Contains(reading, "\"B\":[12,6,5,4,13]");
            StringAssert.Contains(reading, "\"I\":[24,21,17,25,28]");
            StringAssert.Contains(reading, "\"N\":[44,37,34,40]");
            StringAssert.Contains(reading, "\"G\":[55,53,52,47,56]");
            StringAssert.Contains(reading, "\"O\":[61,70,63,75,64]");
        }

        [TestMethod]
        public void Dsl_MarksOnlyTheBallsThatAreOnTheCard()
        {
            PerformanceV1 actor = NewActor("bingo_dsl_marks");
            actor.PerformCmd(
                "game = BingoGame(); game.PutCardInPlay(789); game.StartRound({'FullCard'});");

            StringAssert.Contains(actor.PerformQry("print game.CardMarks(789) m;"), "\"m\":[]");

            actor.PerformCmd("game.RegisterDraw(12); game.RegisterDraw(7); game.RegisterDraw(24);");

            StringAssert.Contains(actor.PerformQry("print game.CardMarks(789) m;"), "\"m\":[12,24]");
        }

        [TestMethod]
        public void Dsl_ReadingACardThatIsNotInPlay_Fails()
        {
            PerformanceV1 actor = NewActor("bingo_dsl_foreign_card");
            actor.PerformCmd("game = BingoGame(); game.PutCardInPlay(789);");

            TargetInvocationException failure = Assert.ThrowsException<TargetInvocationException>(
                () => actor.PerformQry("print game.CardColumn(790,0) B;"));

            Assert.IsInstanceOfType(failure.InnerException, typeof(BingoRuleException));
            StringAssert.Contains(failure.InnerException.Message, "790");
        }

        [TestMethod]
        public void Dsl_TheCardSurvivesRehydration()
        {
            string root = FreshRoot("cardface_rehydrate");

            PerformanceV1 seed = NewActor("bingo_dsl_cardface_rehydrate");
            seed.ConfigureStorage(DatabaseType.FileSystem, root);
            seed.Start();
            seed.PerformCmd(
                "game = BingoGame(); game.PutCardInPlay(789); game.StartRound({'FullCard'}); "
                + "game.RegisterDraw(12);",
                "0.0.0.0", "anonymous");
            seed.Dispose();

            PerformanceV1 rehydrated = NewActor("bingo_dsl_cardface_rehydrate");
            rehydrated.ConfigureStorage(DatabaseType.FileSystem, root);
            rehydrated.Start();

            StringAssert.Contains(
                rehydrated.PerformQry("print game.CardColumn(789,0) B;"), "\"B\":[12,6,5,4,13]");
            StringAssert.Contains(rehydrated.PerformQry("print game.CardMarks(789) m;"), "\"m\":[12]");
            rehydrated.Dispose();
        }

        [TestMethod]
        public void Dsl_TheGameSurvivesRehydration()
        {
            string root = FreshRoot("rehydrate");

            PerformanceV1 seed = NewActor("bingo_dsl_rehydrate");
            seed.ConfigureStorage(DatabaseType.FileSystem, root);
            seed.Start();
            seed.PerformCmd(
                "game = BingoGame(); game.PutCardInPlay(789); game.StartRound({'Row0'});",
                "0.0.0.0", "anonymous");
            seed.PerformCmd(Row0OfCard789, "0.0.0.0", "anonymous");
            StringAssert.Contains(seed.PerformQry("print game.State s;"), "AwaitingValidation");
            seed.Dispose();

            PerformanceV1 rehydrated = NewActor("bingo_dsl_rehydrate");
            rehydrated.ConfigureStorage(DatabaseType.FileSystem, root);
            rehydrated.Start();

            StringAssert.Contains(rehydrated.PerformQry("print game.State s;"), "AwaitingValidation");
            StringAssert.Contains(rehydrated.PerformQry("print game.DrawnCount d;"), "\"d\":5");
            StringAssert.Contains(rehydrated.PerformQry("print game.CardsInPlayCount n;"), "\"n\":1");
            rehydrated.Dispose();
        }

        [TestMethod]
        public void Dsl_TheStateComesFromTheJournalAndNotFromTheActorName()
        {
            string seeded = FreshRoot("provenance_seeded");
            string empty = FreshRoot("provenance_empty");

            PerformanceV1 seed = NewActor("bingo_dsl_provenance");
            seed.ConfigureStorage(DatabaseType.FileSystem, seeded);
            seed.Start();
            seed.PerformCmd("game = BingoGame(); game.PutCardInPlay(789);", "0.0.0.0", "anonymous");
            StringAssert.Contains(seed.PerformQry("print game.CardsInPlayCount n;"), "\"n\":1");
            seed.Dispose();

            PerformanceV1 sameNameEmptyJournal = NewActor("bingo_dsl_provenance");
            sameNameEmptyJournal.ConfigureStorage(DatabaseType.FileSystem, empty);
            sameNameEmptyJournal.Start();

            try
            {
                Assert.ThrowsException<LanguageException>(
                    () => sameNameEmptyJournal.PerformQry("print game.CardsInPlayCount n;"),
                    "the same actor name over an EMPTY journal answered instead of failing, so the state "
                    + "is not coming from the journal");
            }
            finally
            {
                sameNameEmptyJournal.Dispose();
            }
        }

        [TestMethod]
        public void Dsl_ThePlayersTableTakesACardOutTheWayTheTellDoes_AndTheCardStaysOutAfterARestart()
        {
            string root = FreshRoot("player_take_out");

            PerformanceV1 seed = NewActor("bingo_dsl_player_take_out");
            seed.ConfigureStorage(DatabaseType.FileSystem, root);
            seed.Start();
            seed.PerformCmd("juego_prueba = BingoGame(); juego_prueba.PutCardInPlay(789); juego_prueba.PutCardInPlay(790);",
                "0.0.0.0", "anonymous");
            seed.PerformCmd("tableCard = juego_prueba.CardInPlay(789); juego_prueba.TakeCardOutOfPlay(tableCard);",
                "0.0.0.0", "anonymous");
            seed.Dispose();

            PerformanceV1 rehydrated = NewActor("bingo_dsl_player_take_out");
            rehydrated.ConfigureStorage(DatabaseType.FileSystem, root);
            rehydrated.Start();

            StringAssert.Contains(rehydrated.PerformQry("print juego_prueba.IsInPlay(789) i;"), "false");
            StringAssert.Contains(rehydrated.PerformQry("print juego_prueba.CardsInPlayCount n;"), "\"n\":1");
            rehydrated.Dispose();
        }
    }
}
