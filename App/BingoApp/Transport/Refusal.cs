namespace BingoApp.Transport;

/// <summary>
/// Un "no" del organizador con una razon que la maquina puede leer. El texto es para la
/// persona; el codigo es para que el telefono del colaborador sepa que hacer: volver a
/// presentarse, borrar el bingo de su lista, o pedir otro codigo.
/// Cualquier otra excepcion sigue viajando como 403, con el codigo generico.
/// </summary>
public sealed class Refusal : Exception
{
    public Refusal(string because, string message) : base(message) => Because = because;

    public string Because { get; }

    // Los codigos viajan en el cable: no se tocan.

    /// <summary>Un "no" sin codigo propio. Es lo que llega de un organizador viejo.</summary>
    public const string Plain = "no_pude";

    public const string NoSuchFestival = "funcion_desconocida";

    public const string NoMark = "sin_marca";

    public const string MarkTaken = "marca_tomada";

    public const string SentHome = "mandado_a_casa";

    public const string NotYourMark = "no_es_tuya";

    public const string OtherDuty = "otro_papel";

    public const string NameTaken = "nombre_tomado";

    public const string Closed = "funcion_terminada";

    /// <summary>El numero del comprador ya lo esta usando otro aparato. Gana el primero: el
    /// carton se queda donde estaba.</summary>
    public const string AnotherHandset = "otro_aparato";

    /// <summary>
    /// Como se le dice el "no" a quien lo recibe, EN SU IDIOMA. El codigo cruza el cable
    /// y se traduce de este lado; el texto que compuso el otro telefono (en el idioma del
    /// otro) queda de respaldo solo para el "no" sin codigo, que es lo que manda un
    /// organizador viejo o una caida de red.
    /// </summary>
    public static string Dicho(string because, string why)
    {
        string code = string.IsNullOrWhiteSpace(because) ? Plain : because.Trim();

        if (code != Plain && Texts.Knows("rechazo." + code)) return Texts.T("rechazo." + code);

        return Texts.T("rechazo.no_pude",
            ("motivo", string.IsNullOrWhiteSpace(why) ? Texts.T("rechazo.no_pude.sin-motivo") : why));
    }
}
