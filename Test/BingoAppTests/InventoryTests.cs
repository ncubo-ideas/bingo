using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BingoApp;

namespace BingoAppTests;

/// <summary>
/// Las dos direcciones, porque una sola no alcanza:
/// comprobar que toda clave pedida exista NO alcanza. Por ese hueco pasaron 31 rotulos
/// que nadie convirtio en clave y se descubrieron en un iPhone en ingles.
///
/// IDA: toda clave que piden las paginas (data-texto, Idioma.texto), el C# (Texts.T) y
/// el XAML ({local:Text}) existe en el catalogo.
///
/// VUELTA: ningun texto visible quedo cableado. Es estructural y no por idioma -- buscar
/// tildes tampoco alcanza -- y avanza archivo por archivo: los que todavia no se
/// migraron estan en <see cref="Pendientes"/>, y un archivo que se limpia tiene que salir de
/// esa lista, o la prueba falla por el otro lado.
/// </summary>
[TestClass]
public class InventoryTests
{
    /// <summary>
    /// Los archivos que TODAVIA tienen texto cableado, por PR. Cada PR de la localizacion
    /// achica esta lista; el ultimo la deja vacia. Rutas relativas a BingoApp/.
    /// </summary>
    private static readonly string[] Pendientes =
    {
        // Vacia desde el PR 6: todo texto visible entra por el catalogo.
    };

    private static readonly Regex Letras = new(@"[A-Za-zÁÉÍÓÚáéíóúñÑ]{2,}", RegexOptions.Compiled);

    private static string _app;

    /// <summary>La carpeta App/BingoApp/, buscada hacia arriba desde donde corre la suite.</summary>
    private static string App()
    {
        if (_app is not null) return _app;

        DirectoryInfo dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "App", "BingoApp", "BingoApp.csproj")))
            dir = dir.Parent;

        Assert.IsNotNull(dir, "no encontre App/BingoApp/BingoApp.csproj subiendo desde " + AppContext.BaseDirectory);
        return _app = Path.Combine(dir.FullName, "App", "BingoApp");
    }

    private static IEnumerable<string> Archivos(string patron) =>
        Directory.EnumerateFiles(App(), patron, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}Platforms{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static string Relativo(string archivo) =>
        Path.GetRelativePath(App(), archivo).Replace('\\', '/');

    private static IEnumerable<string> Paginas() => Archivos("*.html").Where(f => f.Contains("wwwroot"));

    private static IEnumerable<string> Scripts() =>
        Archivos("*.js").Where(f => f.Contains("wwwroot") && !f.EndsWith("language.js") && !f.EndsWith("patterns.js"));

    // ── ida: lo que se pide existe ───────────────────────────────────────────

    [TestMethod]
    public void EveryMarkupKeyExists()
    {
        List<string> huerfanas = new();

        foreach (string pagina in Paginas())
        {
            string html = File.ReadAllText(pagina);
            foreach (Match m in Regex.Matches(html, @"data-texto(?:-ph|-aria)?=""([^""]+)"""))
                if (!Texts.Knows(m.Groups[1].Value)) huerfanas.Add(Relativo(pagina) + ": " + m.Groups[1].Value);
        }

        Assert.AreEqual(0, huerfanas.Count, "claves sin frase:\n" + string.Join("\n", huerfanas));
    }

    [TestMethod]
    public void EveryKeyThePagesAskForExists()
    {
        List<string> huerfanas = new();

        foreach (string script in Scripts())
        {
            string js = SinComentarios(File.ReadAllText(script));
            foreach (Match m in Regex.Matches(js, @"Idioma\.(?:texto|con)\(\s*'([A-Za-z0-9._-]+)'"))
                if (!Texts.Knows(m.Groups[1].Value)) huerfanas.Add(Relativo(script) + ": " + m.Groups[1].Value);

            foreach (Match m in Regex.Matches(js, @"Idioma\.muchos\([^,]+,\s*'([A-Za-z0-9._-]+)',\s*'([A-Za-z0-9._-]+)'"))
            {
                if (!Texts.Knows(m.Groups[1].Value)) huerfanas.Add(Relativo(script) + ": " + m.Groups[1].Value);
                if (!Texts.Knows(m.Groups[2].Value)) huerfanas.Add(Relativo(script) + ": " + m.Groups[2].Value);
            }
        }

        Assert.AreEqual(0, huerfanas.Count, "claves sin frase:\n" + string.Join("\n", huerfanas));
    }

    [TestMethod]
    public void EveryKeyThePhoneAsksForExists()
    {
        List<string> huerfanas = new();

        foreach (string fuente in Archivos("*.cs"))
        {
            string cs = SinComentarios(File.ReadAllText(fuente));
            foreach (Match m in Regex.Matches(cs, @"Texts\.(?:T|ForDsl)\(\s*""([A-Za-z0-9._-]*[A-Za-z0-9])"""))
                if (!Texts.Knows(m.Groups[1].Value)) huerfanas.Add(Relativo(fuente) + ": " + m.Groups[1].Value);

            foreach (Match m in Regex.Matches(cs, @"Texts\.Plural\([^,]+,\s*""([A-Za-z0-9._-]+)"",\s*""([A-Za-z0-9._-]+)"""))
            {
                if (!Texts.Knows(m.Groups[1].Value)) huerfanas.Add(Relativo(fuente) + ": " + m.Groups[1].Value);
                if (!Texts.Knows(m.Groups[2].Value)) huerfanas.Add(Relativo(fuente) + ": " + m.Groups[2].Value);
            }
        }

        foreach (string xaml in Archivos("*.xaml"))
        {
            string texto = File.ReadAllText(xaml);
            foreach (Match m in Regex.Matches(texto, @"\{local:Text\s+([A-Za-z0-9._-]+)\}"))
                if (!Texts.Knows(m.Groups[1].Value)) huerfanas.Add(Relativo(xaml) + ": " + m.Groups[1].Value);
        }

        Assert.AreEqual(0, huerfanas.Count, "claves sin frase:\n" + string.Join("\n", huerfanas));
    }

    // ── vuelta: nada visible quedo cableado ──────────────────────────────────

    [TestMethod]
    public void NoHardcodedTextOutsideThePendingList()
    {
        Dictionary<string, List<string>> porArchivo = new(StringComparer.Ordinal);

        foreach (string pagina in Paginas()) Anotar(porArchivo, pagina, TextoCableadoEnHtml(File.ReadAllText(pagina)));
        foreach (string script in Scripts()) Anotar(porArchivo, script, TextoCableadoEnJs(File.ReadAllText(script)));
        foreach (string fuente in Archivos("*.cs")) Anotar(porArchivo, fuente, TextoCableadoEnCs(File.ReadAllText(fuente)));
        foreach (string xaml in Archivos("*.xaml")) Anotar(porArchivo, xaml, TextoCableadoEnXaml(File.ReadAllText(xaml)));

        HashSet<string> pendientes = new(Pendientes, StringComparer.Ordinal);

        string[] sucios = porArchivo.Keys.Where(a => !pendientes.Contains(a)).OrderBy(a => a).ToArray();
        string[] yaLimpios = pendientes.Where(a => !porArchivo.ContainsKey(a)).OrderBy(a => a).ToArray();

        string detalle = string.Join("\n", sucios.Select(a =>
            a + "\n    " + string.Join("\n    ", porArchivo[a].Take(12)) + (porArchivo[a].Count > 12 ? "\n    …" : "")));

        Assert.AreEqual(0, sucios.Length, "texto cableado en archivos que ya deberian estar limpios:\n" + detalle);
        Assert.AreEqual(0, yaLimpios.Length,
            "estos archivos ya estan limpios: sacalos de Pendientes:\n" + string.Join("\n", yaLimpios));
    }

    private static void Anotar(Dictionary<string, List<string>> porArchivo, string archivo, IEnumerable<string> hallazgos)
    {
        List<string> lista = hallazgos.ToList();
        if (lista.Count > 0) porArchivo[Relativo(archivo)] = lista;
    }

    /// <summary>Texto entre etiquetas con letras cuya etiqueta de apertura no trae data-texto.</summary>
    private static IEnumerable<string> TextoCableadoEnHtml(string html)
    {
        string limpio = Regex.Replace(html, @"<script[\s\S]*?</script>|<style[\s\S]*?</style>|<!--[\s\S]*?-->|<title>[\s\S]*?</title>", " ");

        foreach (Match m in Regex.Matches(limpio, @"<(/?)([A-Za-z][A-Za-z0-9-]*)([^>]*)>([^<]*)"))
        {
            string texto = m.Groups[4].Value.Trim();
            if (!Letras.IsMatch(SinEntidades(texto))) continue;
            if (m.Groups[1].Value.Length == 0 && m.Groups[3].Value.Contains("data-texto=")) continue;
            yield return Recortar(texto);
        }

        foreach (Match tag in Regex.Matches(limpio, @"<[A-Za-z][^>]*>"))
        {
            // Un placeholder o aria-label con su data-texto-ph / data-texto-aria ya esta
            // en el catalogo; el espanol que trae escrito es el respaldo.
            if (tag.Value.Contains("data-texto-ph=") || tag.Value.Contains("data-texto-aria=")) continue;

            foreach (Match m in Regex.Matches(tag.Value, @"\b(placeholder|title|aria-label)=""([^""]*)"""))
            {
                string valor = m.Groups[2].Value;
                if (!PareceProsa(valor)) continue;
                yield return m.Groups[1].Value + "=\"" + Recortar(valor) + "\"";
            }
        }
    }

    /// <summary>
    /// Literales que parecen prosa: tres palabras o mas, o con tilde o signo de frase, o
    /// una palabra con mayuscula seguida de otra. Es una heuristica a proposito -- una
    /// lista de clases CSS con dos tokens pasa, "entre rondas" tambien -- y el recorrido
    /// en el emulador cubre lo que esto no ve.
    /// </summary>
    private static IEnumerable<string> TextoCableadoEnJs(string js)
    {
        string limpio = SinComentarios(js);

        foreach (Match m in Regex.Matches(limpio, @"'((?:[^'\\\n]|\\.)*)'|""((?:[^""\\\n]|\\.)*)""|`((?:[^`\\]|\\.)*)`"))
        {
            string literal = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            if (PareceProsa(literal)) yield return Recortar(literal);
        }
    }

    /// <summary>Lo que se le dice a la persona desde el C#: la voz, los toasts, las
    /// excepciones que terminan en un toast, los rechazos y las etiquetas nativas.</summary>
    private static IEnumerable<string> TextoCableadoEnCs(string cs)
    {
        string limpio = SinComentarios(cs);

        Regex sitios = new(
            @"(?:(?:_say|\bsay|Say|PushToast|new\s+InvalidOperationException|new\s+ArgumentException)\s*\(|new\s+Refusal\([A-Za-z.]+,|(?:StatusLabel\.Text|\.Text|LastTrouble)\s*=)\s*\$?""((?:[^""\\]|\\.)*)""",
            RegexOptions.Compiled);

        foreach (Match m in sitios.Matches(limpio))
        {
            string literal = m.Groups[1].Value;
            if (Letras.IsMatch(literal) && literal.Trim().Length > 1) yield return Recortar(literal);
        }
    }

    private static IEnumerable<string> TextoCableadoEnXaml(string xaml)
    {
        string limpio = Regex.Replace(xaml, @"<!--[\s\S]*?-->", " ");

        foreach (Match m in Regex.Matches(limpio, @"\b(Text|Title|Placeholder)=""([^""{]*)"""))
        {
            string valor = m.Groups[2].Value;
            if (!Letras.IsMatch(SinEntidades(valor))) continue;
            if (m.Groups[1].Value == "Title" && valor is "BingoApp" or "Bingo") continue;
            yield return m.Groups[1].Value + "=\"" + Recortar(valor) + "\"";
        }
    }

    private static bool PareceProsa(string literal)
    {
        if (!Letras.IsMatch(literal)) return false;
        if (Regex.IsMatch(literal, @"[áéíóúñÁÉÍÓÚÑ¿¡]")) return true;

        string plano = literal.Trim();

        // Una palabra sola en MAYUSCULAS es un rotulo ("FIGURA", "INGRESAR"): se vio en el
        // emulador. BINGO es el nombre del juego y se escribe igual en los dos idiomas.
        if (Regex.IsMatch(plano, @"^[A-ZÁÉÍÓÚÑ]{4,}$") && plano != "BINGO") return true;

        // Sin un espacio adentro es una clave, una clase CSS o un id, no una frase.
        if (!plano.Contains(' ')) return false;

        // Marcado, CSS y consultas de medios: tienen letras y espacios, y no son prosa.
        if (plano.Contains('<') || plano.Contains('>')) return false;
        if (Regex.IsMatch(plano, @"^[.#][A-Za-z]")) return false;
        if (Regex.IsMatch(plano, @"\d+px|rgba?\(|gradient\(|^\(|^\d")) return false;

        string[] palabras = plano.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => Letras.IsMatch(t)).ToArray();
        if (palabras.Length >= 3) return true;
        if (Regex.IsMatch(plano, @"^[A-ZÁÉÍÓÚÑ][a-záéíóúñ]+ [A-Za-z]")) return true;
        if (palabras.Length >= 2 && Regex.IsMatch(plano, @"[.:!?]")) return true;

        return false;
    }

    private static string SinComentarios(string s)
    {
        s = Regex.Replace(s, @"/\*[\s\S]*?\*/", " ");
        return Regex.Replace(s, @"^[ \t]*//.*$", " ", RegexOptions.Multiline);
    }

    private static string SinEntidades(string s) => Regex.Replace(s, @"&#?[A-Za-z0-9]+;", " ");

    private static string Recortar(string s)
    {
        string plano = Regex.Replace(s, @"\s+", " ").Trim();
        return plano.Length > 70 ? plano.Substring(0, 70) + "…" : plano;
    }
}
