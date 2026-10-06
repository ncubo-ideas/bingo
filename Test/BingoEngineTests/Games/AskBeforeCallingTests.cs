using BingoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoEngineTests.Games
{
    /// <summary>
    /// Las preguntas que hay que poder hacerle a la mesa ANTES de cantar.
    ///
    /// Existen porque el canto va a llegar reenviado desde el telefono de quien canta, y ahi
    /// la pregunta corre en el organizador antes de tomar el candado de escritura: si contesta
    /// que no, no se asienta nada. Un canto rechazado que dejara entrada seria una bolita
    /// fantasma en el diario de la velada, y de esas no se vuelve -- el diario no se corrige.
    ///
    /// Cada caso de aca es un caso donde RegisterDraw o CorrectDraw TIRAN. Lo que se prueba es
    /// que la pregunta conteste lo mismo que el verbo, sin tirar.
    /// </summary>
    [TestClass]
    public class AskBeforeCallingTests
    {
        private static BingoGame UnaMesaSonando()
        {
            BingoGame mesa = new BingoGame();

            mesa.PutCardInPlay(1);
            mesa.StartRound(new[] { "FourCorners" }, "OnClaim");

            return mesa;
        }

        [TestMethod]
        public void WithoutAStartedRound_NothingCanBeCalled()
        {
            BingoGame mesa = new BingoGame();
            mesa.PutCardInPlay(1);

            Assert.IsFalse(mesa.IsRunning);
            Assert.IsFalse(mesa.IsAwaitingValidation);
            Assert.IsFalse(mesa.CanRegisterDraw(7),
                "sin ronda, RegisterDraw tira: la pregunta tiene que decir lo mismo");
        }

        [TestMethod]
        public void WithTheRoundRunning_WhatHasNotComeOutCanBeCalled()
        {
            BingoGame mesa = UnaMesaSonando();

            Assert.IsTrue(mesa.IsRunning);
            Assert.IsTrue(mesa.CanRegisterDraw(7));
        }

        [TestMethod]
        public void TheSameBallTwice_No()
        {
            BingoGame mesa = UnaMesaSonando();
            mesa.RegisterDraw(7);

            Assert.IsFalse(mesa.CanRegisterDraw(7),
                "el 7 ya salio; volver a cantarlo no agrega nada y el motor lo rechaza");
            Assert.IsTrue(mesa.CanRegisterDraw(8));
        }

        [TestMethod]
        public void ANumberThatDoesNotExist_No()
        {
            BingoGame mesa = UnaMesaSonando();

            Assert.IsFalse(mesa.CanRegisterDraw(0));
            Assert.IsFalse(mesa.CanRegisterDraw(76));
            Assert.IsFalse(mesa.CanRegisterDraw(-3));
        }

        // El caso que hay que poder distinguir. Con la mesa frenada esperando que el
        // organizador resuelva un premio, cantar no se puede -- pero decirle "arranca la
        // ronda que sigue" seria mandarlo a llevarse puesto el premio de alguien.
        [TestMethod]
        public void WithTheTableHeldByAWinner_NothingCanBeCalledAndItSaysWhy()
        {
            BingoGame mesa = new BingoGame();
            mesa.PutCardInPlay(1);
            mesa.StartRound(new[] { "FourCorners" }, "Automatic");

            foreach (int corner in BingoGame.ClosestFor(1, "FourCorners", new int[0]))
                mesa.RegisterDraw(corner);

            Assert.IsTrue(mesa.IsAwaitingValidation, "alguien completo la figura");
            Assert.IsFalse(mesa.IsRunning);
            Assert.IsFalse(mesa.CanRegisterDraw(70));
        }

        [TestMethod]
        public void CorrectingWhatCameOutIntoSomethingThatDidNot_Yes()
        {
            BingoGame mesa = UnaMesaSonando();
            mesa.RegisterDraw(46);

            Assert.IsTrue(mesa.CanCorrectDraw(46, 45));
        }

        [TestMethod]
        public void CorrectingSomethingThatNeverCameOut_No()
        {
            BingoGame mesa = UnaMesaSonando();
            mesa.RegisterDraw(46);

            Assert.IsFalse(mesa.CanCorrectDraw(30, 45), "el 30 nunca salio");
        }

        [TestMethod]
        public void CorrectingIntoSomethingAlreadyOut_No()
        {
            BingoGame mesa = UnaMesaSonando();
            mesa.RegisterDraw(46);
            mesa.RegisterDraw(45);

            Assert.IsFalse(mesa.CanCorrectDraw(46, 45), "el 45 ya salio: quedarian dos iguales");
        }

        [TestMethod]
        public void CorrectingANumberIntoItself_No()
        {
            BingoGame mesa = UnaMesaSonando();
            mesa.RegisterDraw(46);

            Assert.IsFalse(mesa.CanCorrectDraw(46, 46));
        }

        [TestMethod]
        public void CorrectingWithoutARound_No()
        {
            BingoGame mesa = new BingoGame();
            mesa.PutCardInPlay(1);

            Assert.IsFalse(mesa.CanCorrectDraw(46, 45));
        }

        // Que la pregunta no sea mas PERMISIVA que el verbo. Eso es lo que no se puede
        // aflojar: si dijera que si donde el verbo tira, el canto pasaria la guarda y
        // reventaria adentro del comando -- con el candado de escritura tomado y el asiento
        // a medio hacer.
        //
        // Al reves si esta permitido, y hay un caso a proposito: la bolita repetida. El motor
        // la acepta en silencio (Draw contesta AlreadyDrawn y RegisterDraw ignora la
        // respuesta), asi que cantar el 7 dos veces no hace nada y deja un asiento inutil.
        // La guarda la rechaza: es mejor decirle a quien canta "esa ya salio" que asentir
        // callado. Este test fija esa diferencia para que sea una decision y no un descuido.
        [TestMethod]
        public void TheQuestionIsNeverMorePermissiveThanTheVerb()
        {
            BingoGame mesa = UnaMesaSonando();
            mesa.RegisterDraw(7);
            mesa.RegisterDraw(21);

            int masEstrictaEn = 0;

            for (int number = 1; number <= 75; number++)
            {
                bool sePuede = mesa.CanRegisterDraw(number);

                BingoGame otra = UnaMesaSonando();
                otra.RegisterDraw(7);
                otra.RegisterDraw(21);

                bool tiro = false;
                try { otra.RegisterDraw(number); }
                catch (System.Exception) { tiro = true; }

                if (tiro)
                {
                    Assert.IsFalse(sePuede,
                        "la bolita " + number + ": el verbo tira y la pregunta dijo que si");
                }

                if (!sePuede && !tiro) masEstrictaEn++;
            }

            Assert.AreEqual(2, masEstrictaEn,
                "las dos que ya salieron -- 7 y 21 -- son los unicos casos donde la guarda es"
                + " mas estricta que el verbo. Si aparece otro, es un descuido, no una"
                + " decision.");
        }
    }
}
