using System;
using System.IO;
using BingoEngine;
using Choreography.Theater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace BingoEngineDslTests
{
    [TestClass]
    public class MultiJournalProbe
    {
        private static PerformanceV1 NewActor(string name) =>
            new PerformanceV1(name, typeof(BingoGame).Assembly);

        private static string FreshRoot(string name)
        {
            string root = Path.Combine(Path.GetTempPath(), "bingo_multi_" + name);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            return root;
        }

        private static PerformanceV1 Open(string name, string root)
        {
            PerformanceV1 actor = NewActor(name);
            actor.ConfigureStorage(DatabaseType.FileSystem, root);
            actor.Start();
            return actor;
        }

        private static void Cmd(PerformanceV1 actor, string script) =>
            actor.PerformCmd(script, "0.0.0.0", "anonymous");

        private static string Cards(PerformanceV1 actor) =>
            actor.PerformQry("print game.CardsInPlayCount n;");

        [TestMethod]
        public void TwoFestivalsInDifferentFoldersDoNotMix()
        {
            string rootA = FreshRoot("nomix_a");
            string rootB = FreshRoot("nomix_b");

            PerformanceV1 a = Open("festival_a", rootA);
            Cmd(a, "game = BingoGame(); game.PutCardInPlay(789);");
            Console.WriteLine("A recien escrita   : " + Cards(a));
            a.Dispose();

            PerformanceV1 b = Open("festival_b", rootB);
            Cmd(b, "game = BingoGame(); game.PutCardInPlay(111); game.PutCardInPlay(222);");
            Console.WriteLine("B recien escrita   : " + Cards(b));
            b.Dispose();

            PerformanceV1 aAgain = Open("festival_a", rootA);
            string aRehydrated = Cards(aAgain);
            Console.WriteLine("A rehidratada      : " + aRehydrated);
            aAgain.Dispose();

            PerformanceV1 bAgain = Open("festival_b", rootB);
            string bRehydrated = Cards(bAgain);
            Console.WriteLine("B rehidratada      : " + bRehydrated);
            bAgain.Dispose();

            Console.WriteLine("carpetas bajo rootA: " + string.Join(",", SubfolderNames(rootA)));
            Console.WriteLine("carpetas bajo rootB: " + string.Join(",", SubfolderNames(rootB)));

            StringAssert.Contains(aRehydrated, "\"n\":1", "la velada A no conservo su unico carton");
            StringAssert.Contains(bRehydrated, "\"n\":2", "la velada B no conservo sus dos cartones");
        }

        [TestMethod]
        public void TwoLiveFestivalsAtOnceInTheSameProcess()
        {
            string rootA = FreshRoot("alive_a");
            string rootB = FreshRoot("alive_b");

            PerformanceV1 a = Open("festival_a", rootA);
            PerformanceV1 b = Open("festival_b", rootB);

            try
            {
                Cmd(a, "game = BingoGame(); game.PutCardInPlay(789);");
                Cmd(b, "game = BingoGame(); game.PutCardInPlay(111); game.PutCardInPlay(222);");

                string a1 = Cards(a);
                string b1 = Cards(b);
                Cmd(a, "game.PutCardInPlay(790);");
                string b2 = Cards(b);
                string a2 = Cards(a);

                Console.WriteLine("A (1er lectura)    : " + a1);
                Console.WriteLine("B (1er lectura)    : " + b1);
                Console.WriteLine("B despues de A++   : " + b2);
                Console.WriteLine("A despues de A++   : " + a2);

                StringAssert.Contains(a1, "\"n\":1", "A viva no vio su propio carton");
                StringAssert.Contains(b1, "\"n\":2", "B viva no vio sus dos cartones");
                StringAssert.Contains(b2, "\"n\":2", "B cambio cuando escribi en A");
                StringAssert.Contains(a2, "\"n\":2", "A no registro el carton nuevo");
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        [TestMethod]
        public void SameActorNameInDifferentFoldersAreDifferentFestivals()
        {
            string rootA = FreshRoot("samename_a");
            string rootB = FreshRoot("samename_b");

            PerformanceV1 a = Open("festival", rootA);
            PerformanceV1 b = Open("festival", rootB);

            try
            {
                Cmd(a, "game = BingoGame(); game.PutCardInPlay(789);");
                Cmd(b, "game = BingoGame(); game.PutCardInPlay(111); game.PutCardInPlay(222);");

                string aCards = Cards(a);
                string bCards = Cards(b);
                Console.WriteLine("mismo nombre, A    : " + aCards);
                Console.WriteLine("mismo nombre, B    : " + bCards);
                Console.WriteLine("A.Name=" + a.Name + " B.Name=" + b.Name);
                Console.WriteLine("subcarpetas rootA  : " + string.Join(",", SubfolderNames(rootA)));
                Console.WriteLine("subcarpetas rootB  : " + string.Join(",", SubfolderNames(rootB)));

                StringAssert.Contains(aCards, "\"n\":1",
                    "con el mismo nombre de actor, la carpeta NO alcanza para separar el estado");
                StringAssert.Contains(bCards, "\"n\":2",
                    "con el mismo nombre de actor, la carpeta NO alcanza para separar el estado");
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        [TestMethod]
        public void AClosedFestivalCanBeOpenedAgain()
        {
            string rootA = FreshRoot("reopen_a");
            string rootB = FreshRoot("reopen_b");

            PerformanceV1 a = Open("festival_a", rootA);
            Cmd(a, "game = BingoGame(); game.PutCardInPlay(789); game.StartRound({'Row0'});");
            Cmd(a, "game.RegisterDraw(12); game.RegisterDraw(24);");
            Console.WriteLine("A antes de cerrar  : " + Cards(a) + " / " + a.PerformQry("print game.DrawnCount d;"));
            a.Dispose();

            PerformanceV1 b = Open("festival_b", rootB);
            Cmd(b, "game = BingoGame(); game.PutCardInPlay(111); game.PutCardInPlay(222);");
            Console.WriteLine("B escrita          : " + Cards(b));
            b.Dispose();

            PerformanceV1 aReopened = Open("festival_a", rootA);
            string cards = Cards(aReopened);
            string drawn = aReopened.PerformQry("print game.DrawnCount d;");
            string state = aReopened.PerformQry("print game.State s;");
            Console.WriteLine("A reabierta        : " + cards + " / " + drawn + " / " + state);
            aReopened.Dispose();

            StringAssert.Contains(cards, "\"n\":1", "la velada A perdio su carton al ir y volver");
            StringAssert.Contains(drawn, "\"d\":2", "la velada A perdio las bolas cantadas al ir y volver");
            StringAssert.Contains(state, "Running", "la velada A perdio el estado de la ronda");
        }

        private static string[] SubfolderNames(string root)
        {
            string[] dirs = Directory.GetDirectories(root);
            string[] names = new string[dirs.Length];
            for (int i = 0; i < dirs.Length; i++) names[i] = Path.GetFileName(dirs[i]);
            return names;
        }
    }
}
