using Microsoft.VisualStudio.TestTools.UnitTesting;
using BingoApp;

namespace BingoAppTests;

/// <summary>
/// El catalogo en si: que este completo, simetrico y que el lector haga lo que promete.
/// Nada de esto necesita un telefono.
/// </summary>
[TestClass]
public class TextsTests
{
    [TestInitialize]
    public void UseSpanish() => Texts.Language = Texts.Spanish;

    [TestMethod]
    public void CatalogLoads()
    {
        Assert.IsTrue(Texts.Keys().Count() >= 100, "el recurso embebido no se encontro o esta vacio");
    }

    [TestMethod]
    public void NoKeyIsMissingALanguage()
    {
        string[] faltantes = Texts.Missing().ToArray();
        Assert.AreEqual(0, faltantes.Length, string.Join("\n", faltantes));
    }

    [TestMethod]
    public void BothLanguagesAskForTheSamePlaceholders()
    {
        string[] discrepancias = Texts.Mismatches().ToArray();
        Assert.AreEqual(0, discrepancias.Length, string.Join("\n", discrepancias));
    }

    [TestMethod]
    public void NoKeyIsIdenticalInBothUnlessDeclared()
    {
        string[] identicas = Texts.Identical().ToArray();
        Assert.AreEqual(0, identicas.Length,
            "iguales en es y en sin campo 'igual': " + string.Join(", ", identicas));
    }

    [TestMethod]
    public void NoPhraseHasBrokenAccents()
    {
        List<string> rotas = new();
        foreach (string idioma in new[] { Texts.Spanish, Texts.English })
        {
            Texts.Language = idioma;
            foreach (string clave in Texts.Keys())
            {
                string frase = Texts.T(clave);
                if (frase.Contains('Ã') || frase.Contains('Â')) rotas.Add(idioma + " " + clave + ": " + frase);
            }
        }

        Assert.AreEqual(0, rotas.Count, "texto guardado en otra codificacion:\n" + string.Join("\n", rotas));
    }

    [TestMethod]
    public void PhraseFollowsTheLanguage()
    {
        Texts.Language = Texts.Spanish;
        string es = Texts.T("papel.CommunityLeader");
        Texts.Language = Texts.English;
        string en = Texts.T("papel.CommunityLeader");

        Assert.AreEqual("Organizador", es);
        Assert.AreEqual("Organizer", en);
    }

    [TestMethod]
    public void PlaceholdersGoByName()
    {
        Assert.AreEqual("ya tienes un bingo que se llama Feria",
            Texts.T("dominio.bingo-repetido", ("resto", "Feria")));
    }

    [TestMethod]
    public void UnknownKeyComesBackAsTheKey()
    {
        Assert.AreEqual("no.existe.esta.clave", Texts.T("no.existe.esta.clave"));
    }

    [TestMethod]
    public void PluralIsTwoKeys()
    {
        // Las claves de plural llegan con las paginas; aca se prueba la mecanica con una
        // pareja que existe desde el primer PR.
        Texts.Language = Texts.English;
        Assert.AreEqual("that code is not yours", Texts.Plural(1, "rechazo.no_es_tuya", "rechazo.no_es_tuya"));
    }

    [TestMethod]
    public void NormalizeOnlyKnowsTwo()
    {
        Assert.AreEqual("en", Texts.Normalize("en-US"));
        Assert.AreEqual("en", Texts.Normalize("EN"));
        Assert.AreEqual("es", Texts.Normalize("es-CR"));
        Assert.AreEqual("es", Texts.Normalize("pt-BR"));
        Assert.AreEqual("es", Texts.Normalize(null));
    }

    [TestMethod]
    public void ForDslEscapesTheSingleQuote()
    {
        Texts.Language = Texts.English;
        string frase = Texts.ForDsl("rechazo.funcion_desconocida");
        Assert.IsTrue(frase.Contains("organizer\\'s phone"), frase);
    }

    [TestMethod]
    public void BridgeMessageCarriesLanguageAndCatalog()
    {
        Texts.Language = Texts.English;
        string json = Texts.AsMessage();

        Assert.IsTrue(json.StartsWith("{\"kind\":\"textos\",\"idioma\":\"en\",\"textos\":{"), json.Substring(0, 60));
        Assert.IsTrue(json.Contains("\"papel.Player\":\"Player\""));
    }

    [TestMethod]
    public void ExplainTranslatesWhatTheDomainSays()
    {
        Assert.AreEqual("la bolita 80 no salió",
            Texts.Explain(new InvalidOperationException("Ball 80 never came out.")));
        Assert.AreEqual("la bolita 7 ya había salido",
            Texts.Explain(new InvalidOperationException("Ball 7 already came out.")));
        Assert.AreEqual("el cartón 12 no está en la mesa",
            Texts.Explain(new InvalidOperationException("Card 12 is not in play.")));
        Assert.AreEqual("no conozco la figura Zeta",
            Texts.Explain(new ArgumentException("Unknown figure 'Zeta'.", "name")));
        Assert.AreEqual("termina la ronda que suena antes de arrancar otra",
            Texts.Explain(new InvalidOperationException("Finish the current round before starting another.")));
    }

    [TestMethod]
    public void ExplainLooksInsideTheExceptionChain()
    {
        Exception envuelta = new InvalidOperationException("script failed",
            new InvalidOperationException("No round has been started."));

        Assert.AreEqual("todavía no arrancó ninguna ronda", Texts.Explain(envuelta));
    }

    [TestMethod]
    public void ExplainLetsUnknownMessagesThrough()
    {
        Assert.AreEqual("algo raro", Texts.Explain(new InvalidOperationException("algo raro")));
    }
}
