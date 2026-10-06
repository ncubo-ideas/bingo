using System.Collections.Generic;
using System.Text.Json;

namespace BingoApp.Contract;

/// <summary>
/// Lo que el diario contesta, leido.
///
/// El sustrato devuelve las consultas como JSON con una etiqueta por cada 'print', y esto
/// es lo unico que sabe abrirlo. Vivia copiado caracter por caracter en el organizador y en
/// el jugador: el mismo parseo mantenido en dos lugares, que ya habian empezado a
/// separarse. Aca esta una sola vez.
///
/// La diferencia entre las dos familias es a proposito y es la que importa:
///
/// Read/AsInt/AsLong/AsBool ADIVINAN. Devuelven null, cero o falso tanto cuando el diario
/// contesta eso como cuando no se le pudo leer la respuesta. Sirven para pintar -- una
/// pantalla con un cero de mas se ve rara y ya --, pero una GUARDA escrita sobre ellos se
/// abre justo cuando algo anda mal.
///
/// MustRead/MustReadWhether se NIEGAN. Son para las respuestas de las que depende una
/// guarda: la de los papelitos tomaba un cero como "este bingo no tiene ninguno" y dejaba
/// pasar sin revisar, y la del cierre tomaba un falso como "el bingo sigue" y aceptaba
/// ventas en un bingo terminado, que quedan asentadas para siempre.
/// </summary>
public static class JournalAnswer
{
    public static string Read(string json, string label)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(label, out JsonElement value)
                ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString())
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static int AsInt(string json, string label) =>
        int.TryParse(Read(json, label), out int n) ? n : 0;

    public static long AsLong(string json, string label) =>
        long.TryParse(Read(json, label), out long n) ? n : 0L;

    public static bool AsBool(string json, string label) =>
        string.Equals(Read(json, label), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>El numero del que depende una guarda. Si no se pudo leer, se niega en vez de
    /// adivinar un cero.</summary>
    public static int MustRead(string json, string label) =>
        int.TryParse(Read(json, label), out int n)
            ? n
            : throw new Transport.Refusal(Transport.Refusal.Plain,
                "no pude leer el diario para saber si te puedo dejar pasar; intenta de nuevo");

    public static bool MustReadWhether(string json, string label)
    {
        string said = Read(json, label);

        if (string.Equals(said, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(said, "false", StringComparison.OrdinalIgnoreCase)) return false;

        throw new Transport.Refusal(Transport.Refusal.Plain,
            "no pude leer el diario para saber si el bingo sigue; intenta de nuevo");
    }

    /// <summary>Los numeros de un 'foreach' del DSL: llegan como un arreglo de objetos de
    /// una sola propiedad cada uno, y lo que interesa son los valores.</summary>
    public static List<long> NumbersIn(string json)
    {
        List<long> numbers = new();

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    foreach (JsonProperty inside in item.EnumerateObject())
                        if (inside.Value.TryGetInt64(out long number)) numbers.Add(number);
                }
            }
        }
        catch (Exception)
        {
            return numbers;
        }

        return numbers;
    }
}
