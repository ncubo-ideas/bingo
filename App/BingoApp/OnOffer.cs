using System.Text;
using System.Text.Json;
using BingoEngine;

namespace BingoApp;

/// <summary>
/// Las figuras que se le ofrecen al organizador, en el orden en que se juegan de
/// verdad: de la mas corta a la mas larga.
///
/// Es una lista CURADA a proposito -- el catalogo del motor trae ademas las variantes
/// internas (Row0, Row1, Column3...) que son piezas de las otras, y ponerlas a elegir
/// seria pedirle al organizador que sepa como esta hecho el bingo por dentro. El motor
/// sigue mandando: una figura que el no conozca no se ofrece, y la validacion de
/// verdad pasa cuando se crea el bingo o se anuncia la ronda.
///
/// COMO SE DICE cada una vive en el catalogo (texts.json, claves figura.*). El motor las
/// nombra en su idioma -- AnyLine, FourCorners -- porque ese nombre es su identidad y
/// viaja en el diario, pero a nadie se le puede pedir que lea "AnyLine" y entienda que
/// se juega. Aca queda solo la lista curada y el orden; el dicho se pide por clave, en
/// el idioma de la persona.
///
/// Y vive del lado del telefono, no de las paginas, porque el telefono ARMA FRASES con
/// estos nombres (la linea de premios que ven el organizador, la colaboradora y el
/// jugador). Si los nombres estuvieran solo en las paginas, esas frases seguirian en
/// ingles y habria dos tablas para lo mismo. Las paginas los reciben por el puente; lo
/// unico que ellas saben de su cuenta es el DIBUJITO (patterns.js).
///
/// Vive en un solo lugar porque lo usan tres pantallas y dos frases, y dos copias
/// significan que un dia una dice una cosa y otra dice otra.
/// </summary>
public static class OnOffer
{
    private static readonly string[] InOrder =
    {
        "AnyLine", "AnyRow", "AnyColumn", "Diagonal", "FourCorners", "AnySquare", "L", "FullCard",
    };

    public static IReadOnlyList<(string Figure, string Called, string Says)> Known() =>
        InOrder
            .Where(BingoGame.KnowsFigure)
            .Select(one => (one, Called(one), Says(one)))
            .ToList();

    /// <summary>Como se dice una figura. Una que no conozcamos sale con su nombre de
    /// motor: es feo, pero es cierto -- callarla dejaria un bingo viejo sin leerse.
    /// </summary>
    public static string Called(string figure)
    {
        string code = Code(figure);
        return code is null ? figure ?? string.Empty : Texts.T("figura." + code + ".nombre");
    }

    /// <summary>Como se explica una figura al elegirla.</summary>
    public static string Says(string figure)
    {
        string code = Code(figure);
        return code is null ? string.Empty : Texts.T("figura." + code + ".pista");
    }

    private static string Code(string figure)
    {
        foreach (string one in InOrder)
            if (string.Equals(one, figure, StringComparison.OrdinalIgnoreCase)) return one;

        return null;
    }

    /// <summary>Esta misma lista, en el mensaje que las paginas esperan.
    ///
    /// Lo arma OnOffer y no cada pantalla porque una pantalla que se olvide de mandarlo no
    /// falla: se queda con el nombre del motor y muestra "L" donde va "La ele". Eso paso en
    /// el Inicio, que era la unica de las cuatro que no lo mandaba.</summary>
    public static string AsMessage()
    {
        using MemoryStream buffer = new();

        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "figures");

            json.WriteStartArray("figures");
            foreach ((string figure, _, _) in Known()) json.WriteStringValue(figure);
            json.WriteEndArray();

            json.WriteStartObject("names");
            foreach ((string figure, string called, _) in Known()) json.WriteString(figure, called);
            json.WriteEndObject();

            json.WriteStartObject("hints");
            foreach ((string figure, _, string says) in Known()) json.WriteString(figure, says);
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
