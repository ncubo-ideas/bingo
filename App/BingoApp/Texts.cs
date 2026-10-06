using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BingoApp;

/// <summary>
/// Lo que la app le dice a la persona, en un solo lugar, en dos idiomas.
///
/// El texto de la app no sale de un archivo: sale de las seis paginas HTML, de las tres
/// pantallas XAML y de las tres clases Live* que componen frases (toasts, "lo que va
/// pasando", los Check del DSL). Este catalogo los sirve a todos: el C# lo lee directo y
/// las paginas lo reciben por el puente al arrancar (<see cref="AsMessage"/>).
///
/// Las reglas:
/// una clave por FRASE ENTERA, huecos con NOMBRE y nunca por posicion, plural como dos
/// claves, y una clave que falta devuelve la clave, fea a proposito, porque un hueco en
/// blanco parece diseno y se despacha.
///
/// Que NO va aca: nada que salga del diario (nombres, premios, figuras como identidad),
/// nada que viaje por el cable (codigos de Refusal, topics, valores de Duties), y ningun
/// script DSL. Eso no se traduce; ver las reglas 3 y 13 de la guia.
///
/// Sin dependencias de MAUI a proposito: BingoAppTests compila este archivo por link y
/// lee el mismo json, asi que el catalogo se prueba sin un telefono.
/// </summary>
public static class Texts
{
    /// <summary>Los dos idiomas, y solo dos. Es una decision, no una limitacion.</summary>
    public const string Spanish = "es";

    public const string English = "en";

    private static string _elegido = Spanish;

    /// <summary>
    /// El idioma vigente. El dueno es <c>AppLanguage.cs</c> (Preferences + aparato); todo lo
    /// demas lo lee de aca. El respaldo es el espanol y no el ingles: es
    /// el idioma del registro de esta app, y un telefono en portugues entiende mejor
    /// "carton" que "card".
    /// </summary>
    public static string Language
    {
        get => _elegido;
        set => _elegido = Normalize(value);
    }

    /// <summary>Cualquier etiqueta que empiece con "en" es ingles; todo lo demas, espanol.</summary>
    public static string Normalize(string tag) =>
        tag is not null && tag.Trim().StartsWith(English, StringComparison.OrdinalIgnoreCase)
            ? English
            : Spanish;

    /// <summary>
    /// Una frase entera con sus valores puestos. Los valores viajan por nombre: el orden
    /// de las palabras cambia entre los dos idiomas y un {0} que se mueve compila, corre
    /// y miente.
    /// </summary>
    public static string T(string clave, params (string Nombre, string Valor)[] valores)
    {
        string frase = Frase(clave);
        if (valores.Length == 0) return frase;

        StringBuilder sb = new(frase);
        foreach ((string nombre, string valor) in valores)
            sb.Replace("{" + nombre + "}", valor ?? string.Empty);
        return sb.ToString();
    }

    /// <summary>
    /// Singular o plural, con la cuenta en el hueco <c>{n}</c>. Son dos claves y no un
    /// sufijo (regla 7): "1 carton" / "3 cartones", "1 card" / "3 cards".
    /// </summary>
    public static string Plural(int n, string claveUno, string claveVarios,
        params (string Nombre, string Valor)[] valores)
    {
        (string Nombre, string Valor)[] todos = new (string, string)[valores.Length + 1];
        todos[0] = ("n", n.ToString(CultureInfo.InvariantCulture));
        valores.CopyTo(todos, 1);
        return T(n == 1 ? claveUno : claveVarios, todos);
    }

    /// <summary>
    /// La misma frase, lista para ir adentro de un literal <c>'…'</c> del DSL (los
    /// <c>Check(...) Error '…'</c>). El texto del Check no se persiste en el diario (el
    /// check corre aparte del comando y un rechazo no deja asiento), asi que puede ir en
    /// el idioma del lector. Lo unico que hay que cuidar es la comilla simple, que en
    /// ingles aparece en cada "don't".
    /// </summary>
    public static string ForDsl(string clave, params (string Nombre, string Valor)[] valores) =>
        T(clave, valores).Replace("'", "\\'");

    /// <summary>
    /// El catalogo del idioma vigente, plano, como mensaje del puente. Es lo que cada
    /// pantalla le empuja a su pagina en el "ready", ANTES de las figuras y del estado,
    /// para que la pagina nunca tenga que adivinar el idioma por su cuenta.
    /// La primera clave es el idioma mismo: la pagina la necesita para lo que no es una
    /// frase, como se escribe una fecha.
    /// </summary>
    public static Func<double> TopBar { get; set; }

    public static string AsMessage()
    {
        using MemoryStream buffer = new();

        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "textos");
            json.WriteString("idioma", Language);

            double arriba = TopBar?.Invoke() ?? 0;
            if (arriba > 0) json.WriteNumber("arriba", Math.Round(arriba));

            json.WriteStartObject("textos");
            foreach ((string clave, string frase) in DelIdioma()) json.WriteString(clave, frase);
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // ── lo que dice el dominio ───────────────────────────────────────────────

    /// <summary>
    /// Lo que se le muestra a la persona cuando algo revento. Village y BingoEngine se
    /// quejan en ingles y no leen cultura (regla 9): el que traduce es el telefono, y lo
    /// hace por prefijo, porque esos mensajes son "prefijo fijo + dato". Lo que no
    /// reconoce sale tal cual: es feo, pero es cierto.
    /// </summary>
    public static string Explain(Exception failure)
    {
        for (Exception one = failure; one is not null; one = one.InnerException)
        {
            // Una ArgumentException agrega " (Parameter 'x')" al final; eso es del
            // runtime, no del dominio, y taparia el final fijo que se busca.
            string message = Regex.Replace(one.Message ?? string.Empty, @" \(Parameter '[^']*'\)$", string.Empty);

            foreach ((string prefijo, string sufijo, string clave) in DelDominio)
            {
                if (!message.StartsWith(prefijo, StringComparison.Ordinal)) continue;
                if (!message.EndsWith(sufijo, StringComparison.Ordinal)) continue;

                int largo = message.Length - prefijo.Length - sufijo.Length;
                string resto = largo > 0 ? message.Substring(prefijo.Length, largo).Trim() : string.Empty;
                return T(clave, ("resto", resto));
            }
        }

        return failure?.Message ?? string.Empty;
    }

    /// <summary>
    /// Como se quejan Village y BingoEngine, leido del codigo (grep de Exception(" en
    /// los dos proyectos): un comienzo fijo, a veces un final fijo, y el dato en medio.
    /// Si alguien les cambia el texto, la frase vuelve a salir en ingles; no se rompe
    /// nada, se nota en el telefono.
    /// </summary>
    private static readonly (string Prefijo, string Sufijo, string Clave)[] DelDominio =
    {
        ("Finish the current round before starting another.", "", "dominio.termina-la-ronda"),
        ("Balls are drawn only while the round is running.", "", "dominio.bolita-fuera-de-ronda"),
        ("A finished round cannot be corrected.", "", "dominio.ronda-cerrada-no-se-corrige"),
        ("There is no pending validation.", "", "dominio.sin-validacion-pendiente"),
        ("No round has been started.", "", "dominio.sin-ronda"),
        ("A round needs at least one figure.", "", "dominio.ronda-sin-figura"),
        ("A figure name is required.", "", "dominio.figura-sin-nombre"),
        ("A figure needs a name.", "", "dominio.figura-sin-nombre"),
        ("A figure needs at least one alternative.", "", "dominio.figura-sin-alternativa"),
        ("A claim policy is required.", "", "dominio.sin-modo-de-cantar"),
        ("The free center holds no number.", "", "dominio.centro-libre"),
        ("Unknown claim policy '", "'.", "dominio.modo-de-cantar-desconocido"),
        ("Unknown figure '", "'.", "dominio.figura-desconocida"),
        ("Figure '", "' is not in play this round.", "dominio.figura"),
        ("Ball ", " never came out.", "dominio.bolita.no-salio"),
        ("Ball ", " already came out.", "dominio.bolita.ya-salio"),
        ("Card ", " is not in play.", "dominio.carton"),
        ("this elder already has a festival named ", "", "dominio.bingo-repetido"),
        ("no round is playing at ", "", "dominio.no-suena-ronda"),
        ("nobody at ", "", "dominio.nadie-hace-eso"),
        ("a raffle needs at least one prize in play", "", "dominio.rifa-sin-premio"),
        ("a promise needs a figure to play", "", "dominio.promesa-sin-figura"),
        ("a promise needs a prize to give", "", "dominio.promesa-sin-premio"),
        ("a promise is looked up by its figure", "", "dominio.promesa-sin-figura"),
        ("a prize needs to say what it is", "", "dominio.premio-sin-decir"),
        ("a festival needs a name", "", "dominio.bingo-sin-nombre"),
        ("an errand needs a mark on it", "", "dominio.codigo-sin-marca"),
        ("an errand is looked up by its mark", "", "dominio.codigo-sin-marca"),
        ("a merchant needs a name", "", "dominio.persona-sin-nombre"),
        ("a warden needs a name", "", "dominio.persona-sin-nombre"),
        ("an elder needs a name", "", "dominio.persona-sin-nombre"),
        ("a villager needs a name", "", "dominio.persona-sin-nombre"),
    };

    // ── lo que revisa la suite ───────────────────────────────────────────────

    /// <summary>Toda clave a la que le falta un idioma. Lo lee la suite, no la app.</summary>
    public static IEnumerable<string> Missing()
    {
        foreach ((string clave, (string es, string en)) in Catalogo())
        {
            if (es.Length == 0) yield return clave + ": falta 'es'";
            if (en.Length == 0) yield return clave + ": falta 'en'";
        }
    }

    /// <summary>
    /// Toda clave cuyos dos idiomas no piden los mismos huecos. Es el error clasico y no
    /// lo ve ningun compilador: alguien traduce "{carton}" como "{card}" o se lo come.
    /// Compara conjuntos y no orden.
    /// </summary>
    public static IEnumerable<string> Mismatches()
    {
        foreach ((string clave, (string es, string en)) in Catalogo())
        {
            HashSet<string> enEs = Huecos(es);
            HashSet<string> enEn = Huecos(en);
            if (enEs.SetEquals(enEn)) continue;

            string faltanEnIngles = string.Join(", ", enEs.Except(enEn));
            string faltanEnEspanol = string.Join(", ", enEn.Except(enEs));
            yield return clave + ": "
                + (faltanEnIngles.Length > 0 ? "'en' no usa " + faltanEnIngles + ". " : string.Empty)
                + (faltanEnEspanol.Length > 0 ? "'es' no usa " + faltanEnEspanol + "." : string.Empty);
        }
    }

    /// <summary>
    /// Toda clave cuyos dos idiomas dicen exactamente lo mismo y no lo declararon. Casi
    /// siempre es un copiar-pegar a medio terminar. Una clave que es igual a proposito
    /// (un simbolo, "BINGO") lo dice con un campo "igual" cuyo valor es la razon.
    /// </summary>
    public static IEnumerable<string> Identical()
    {
        foreach ((string clave, (string es, string en)) in Catalogo())
            if (es.Length > 0 && es == en && !_igualAProposito.Contains(clave)) yield return clave;
    }

    /// <summary>Todas las claves, para que la suite cruce lo que se pide con lo que hay.</summary>
    public static IEnumerable<string> Keys() => Catalogo().Keys;

    /// <summary>Si la clave existe en el catalogo.</summary>
    public static bool Knows(string clave) => Catalogo().ContainsKey(clave);

    // ── adentro ──────────────────────────────────────────────────────────────

    private static string Frase(string clave) =>
        DelIdioma().TryGetValue(clave, out string frase) && frase.Length > 0 ? frase : clave;

    private static HashSet<string> Huecos(string frase)
    {
        HashSet<string> nombres = new(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(frase, @"\{([A-Za-z][A-Za-z0-9]*)\}"))
            nombres.Add(m.Groups[1].Value);
        return nombres;
    }

    private static Dictionary<string, string> _plano;
    private static string _planoDe;

    private static Dictionary<string, string> DelIdioma()
    {
        string idioma = Language;
        if (_plano is not null && _planoDe == idioma) return _plano;

        Dictionary<string, string> d = new(StringComparer.Ordinal);
        foreach ((string clave, (string es, string en)) in Catalogo())
        {
            // Una clave sin frase en este idioma cae al otro en vez de desaparecer: a
            // medio traducir es un estado en el que este catalogo va a estar un rato, y
            // una frase que falta es peor que una en el idioma equivocado.
            string frase = idioma == Spanish ? es : en;
            d[clave] = frase.Length > 0 ? frase : (idioma == Spanish ? en : es);
        }

        _planoDe = idioma;
        return _plano = d;
    }

    private static Dictionary<string, (string Es, string En)> _catalogo;
    private static readonly HashSet<string> _igualAProposito = new(StringComparer.Ordinal);

    /// <summary>El mismo nombre en la app y en la suite: los dos csproj lo declaran con
    /// LogicalName para que esta unica linea lo encuentre en ambos.</summary>
    private const string Recurso = "bingo.texts.json";

    private static Dictionary<string, (string, string)> Catalogo()
    {
        if (_catalogo is not null) return _catalogo;

        Dictionary<string, (string, string)> d = new(StringComparer.Ordinal);
        using Stream s = typeof(Texts).Assembly.GetManifestResourceStream(Recurso);

        // Un csproj que olvido el EmbeddedResource llega aca. No revienta: cada clave
        // sale como ella misma, que es una pantalla llena de 'jugador.puerta.titulo',
        // inconfundible, y mejor que una app que no arranca por un archivo de texto.
        if (s is null) return _catalogo = d;

        using JsonDocument doc = JsonDocument.Parse(s);
        foreach (JsonProperty entrada in doc.RootElement.EnumerateObject())
        {
            // Las notas "//" del archivo no son frases.
            if (entrada.Name.StartsWith("//", StringComparison.Ordinal)) continue;
            d[entrada.Name] = (Texto(entrada.Value, Spanish), Texto(entrada.Value, English));
            if (Texto(entrada.Value, "igual").Length > 0) _igualAProposito.Add(entrada.Name);
        }

        return _catalogo = d;
    }

    private static string Texto(JsonElement entrada, string idioma) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(idioma, out JsonElement v)
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
