using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>
/// El QR de un bingo: lo que hay que saber para entrar a ella. El organizador emite
/// uno por papel de colaborador; el del jugador lo re-emite quien le vendio el
/// carton, con su nombre y sus numeros adentro.
/// </summary>
public sealed class OpenFestival
{
    // NO subir esta version. FromText exige el 4 exacto, y los codigos guardados en
    // "vendedor.funciones" y "jugador.funciones" son links completos: subirla los deja
    // ilegibles de golpe y en silencio (la lista del jugador se vacia y MIS BINGOS
    // aparece sin nada). Llaves nuevas si, version nueva no: System.Text.Json ignora
    // los miembros que no conoce, asi que agregar es compatible en las dos direcciones.
    [JsonPropertyName("v")]
    public int Version { get; set; } = 4;

    [JsonPropertyName("n")]
    public string Name { get; set; } = "Bingo de agosto";

    /// <summary>El id del bingo. Nacio como "la ronda" y el QR lo sigue llamando r.</summary>
    [JsonPropertyName("r")]
    public string FestivalId { get; set; }

    [JsonPropertyName("e")]
    public string CommunityLeaderUrl { get; set; }

    [JsonPropertyName("f")]
    public string Figures { get; set; } = "AnyRow";

    /// <summary>A que vino quien escanee este codigo: vende, canta o juega.</summary>
    [JsonPropertyName("k")]
    public string Duty { get; set; } = Duties.Plays;

    /// <summary>
    /// La marca del papelito que el organizador entrego con este codigo. Sirve una sola
    /// vez: al reclamarla queda pegada al nombre de quien la uso, y el organizador corta
    /// la siguiente para el colaborador que sigue.
    /// El QR del jugador NO lleva marca (se identifica con sus cartones, que el diario
    /// sabe a quien se le vendieron). Por eso CodeFor, que re-serializa el sobre a mano
    /// en LiveCommunityLeader, no necesita conocer esta llave -- si algun dia el jugador
    /// llevara marca, hay que bajar a ESE metodo tambien.
    /// </summary>
    [JsonPropertyName("t")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    /// <summary>
    /// Para quien corto el organizador este papelito. Viaja para que la puerta le rellene
    /// el nombre: si el organizador le paso el codigo A ELLA, no tiene sentido preguntarle
    /// como se llama otra vez.
    ///
    /// No obliga a nada. Quien escanee puede corregirlo antes de entrar, y el organizador
    /// va a ver el nombre con el que entro: un papelito cortado para Sonia lo puede acabar
    /// usando su hermana, y eso no es un error que valga la pena frenar.
    ///
    /// Llave nueva, version 4 intacta: los codigos guardados de antes no la traen y
    /// System.Text.Json las ignora, asi que agregarla no rompe nada en ningun sentido.
    /// </summary>
    [JsonPropertyName("p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string MeantFor { get; set; }

    /// <summary>El ticket iroh del organizador: con esto el telefono lo encuentra por su llave,
    /// por relay si hace falta, aunque no alcance la direccion de "e". Llave nueva, version 4 intacta.</summary>
    [JsonPropertyName("i")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string IrohTicket { get; set; }

    public const string Scheme = "bingo://funcion?d=";

    /// <summary>
    /// Las tres partes de un link FIRMADO, si este sobre vino de uno. Viajan como colas
    /// del link (&amp;s= y &amp;h=) y no como llaves del JSON: asi un link sin firma es
    /// bit a bit el de siempre, y los guardados de antes siguen leyendose para siempre.
    /// Nulas cuando el codigo no trae firma, que sigue siendo un codigo valido.
    /// </summary>
    [JsonIgnore]
    public string Signature { get; set; }

    [JsonIgnore]
    public string SignerFingerprint { get; set; }

    /// <summary>El payload EXACTO que se firmo, tal como viajo. La firma se verifica
    /// sobre este texto, nunca sobre una re-serializacion.</summary>
    [JsonIgnore]
    public string SignedPayload { get; set; }

    public string[] FigureList() =>
        (Figures ?? "AnyRow").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string ToLink() =>
        Scheme + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string WebScheme => WebSchemeOf(Transport.PhoneToPhone.WebPlayerSite);

    public static string[] WebSchemes => Transport.PhoneToPhone.WebPlayerSites.Select(WebSchemeOf).ToArray();

    private static string WebSchemeOf(string site) => "https://" + site + "/#d=";

    public static bool IsFestivalLink(string text) =>
        text is not null
        && (text.TrimStart().StartsWith("bingo://funcion", StringComparison.OrdinalIgnoreCase)
            || WebSchemes.Any(scheme => text.TrimStart().StartsWith(scheme, StringComparison.OrdinalIgnoreCase)));

    public static string ForEveryone(string link)
    {
        string clean = (link ?? string.Empty).Trim();

        return clean.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            ? WebScheme + clean[Scheme.Length..]
            : clean;
    }

    public static string RawJson(string text)
    {
        string clean = (text ?? string.Empty).Trim();

        if (!IsFestivalLink(clean)) return clean;

        int cut = clean.IndexOf("d=", StringComparison.Ordinal);
        if (cut < 0) throw new ArgumentException(Texts.T("contrato.codigo.sin-datos"));

        string raw = clean[(cut + 2)..];

        // La firma y la huella viajan DESPUES del payload, como colas &s= y &h=. Sin este
        // corte, un '&' cae adentro del base64 y el link firmado se vuelve ilegible para
        // TODA la app -- incluidos los sin firma de gente que pega el link con basura atras.
        int tail = raw.IndexOf('&');
        if (tail >= 0) raw = raw[..tail];

        raw = raw.Replace('-', '+').Replace('_', '/');
        raw = raw.PadRight(raw.Length + (4 - raw.Length % 4) % 4, '=');

        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(raw));
    }

    public static OpenFestival FromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(Texts.T("contrato.codigo.vacio"));

        string clean = RawJson(text);

        OpenFestival read = JsonSerializer.Deserialize<OpenFestival>(clean)
            ?? throw new ArgumentException(Texts.T("contrato.codigo.ilegible"));

        if (read.Version != 4)
            throw new ArgumentException(Texts.T("contrato.codigo.version", ("version", read.Version.ToString())));

        if (string.IsNullOrWhiteSpace(read.CommunityLeaderUrl))
            throw new ArgumentException(Texts.T("contrato.codigo.sin-organizador"));

        if (string.IsNullOrWhiteSpace(read.FestivalId))
            throw new ArgumentException(Texts.T("contrato.codigo.sin-bingo"));

        if (IsFestivalLink(text))
        {
            read.SignedPayload = PartOf(text, "d=");
            read.Signature = PartOf(text, "&s=");
            read.SignerFingerprint = PartOf(text, "&h=");
        }

        return read;
    }

    /// <summary>Una parte del link, desde su marcador hasta el proximo '&amp;'. Null si
    /// el link no la trae, que es como viajan los codigos sin firmar.</summary>
    private static string PartOf(string link, string marker)
    {
        int at = link.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;

        string rest = link[(at + marker.Length)..];
        int end = rest.IndexOf('&');

        string part = (end < 0 ? rest : rest[..end]).Trim();
        return part.Length == 0 ? null : part;
    }
}
