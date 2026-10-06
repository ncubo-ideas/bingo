using System;
using System.Security.Cryptography;
using System.Text;

namespace BingoApp.Contract;

/// <summary>
/// El numero del comprador: lo unico que distingue a dos personas que se llaman igual.
///
/// Se disena para ser TECLEADO, no solo escaneado. En un salon parroquial la camara falla
/// -- poca luz, la pantalla de otro telefono, una camara barata -- y cuando falla, la
/// colaboradora tiene que poder escribirlo sin equivocarse. Por eso son ocho simbolos del
/// alfabeto de Crockford, que deja afuera la I, la L, la O y la U: nadie tiene que decidir
/// si eso es un cero o una O.
///
/// Ocho simbolos son 40 bits. Con doscientos telefonos en la parroquia, la probabilidad de
/// que dos acunen el mismo numero es de una en cincuenta millones; el riesgo real no es la
/// colision sino el teclazo, y contra eso no alcanza el largo: hace falta que el
/// organizador diga de quien es el numero ANTES de cobrar.
/// </summary>
public static class BuyerMark
{
    /// <summary>Sin I, L, O ni U. Las dos primeras se confunden con el uno, la O con el
    /// cero, y la U esta afuera para no formar palabras sin querer.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int Length = 8;

    /// <summary>Acunar el numero de este telefono. Pasa una sola vez en la vida de la
    /// instalacion.</summary>
    public static string Mint()
    {
        byte[] noise = RandomNumberGenerator.GetBytes(Length);
        StringBuilder minted = new StringBuilder(Length);

        foreach (byte b in noise) minted.Append(Alphabet[b % Alphabet.Length]);

        return minted.ToString();
    }

    /// <summary>
    /// Lo que la persona escribio, llevado a la forma con la que se compara.
    ///
    /// Solo toca lo que PARECE un numero tecleado (ocho simbolos despues de sacarle
    /// guiones y espacios). Cualquier otra cosa vuelve tal cual: los bingos de antes
    /// tienen marcas de doce en hexadecimal minuscula, y ponerlas en mayuscula seria
    /// convertir a esa gente en desconocidos.
    /// </summary>
    public static string Normalize(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return string.Empty;

        string trimmed = typed.Trim();
        StringBuilder bare = new StringBuilder(trimmed.Length);

        foreach (char c in trimmed) if (char.IsLetterOrDigit(c)) bare.Append(c);

        if (bare.Length != Length) return trimmed;

        StringBuilder clean = new StringBuilder(Length);

        foreach (char c in bare.ToString())
        {
            char up = char.ToUpperInvariant(c);

            if (up is 'I' or 'L') up = '1';
            else if (up == 'O') up = '0';

            if (Alphabet.IndexOf(up) < 0) return trimmed;

            clean.Append(up);
        }

        return clean.ToString();
    }

    /// <summary>Si es uno de los nuestros. Lo de antes no lo es, y esta bien: se sigue
    /// reconociendo, solo que no se puede teclear comodo.</summary>
    public static bool IsOurs(string mark) =>
        !string.IsNullOrEmpty(mark)
        && mark.Length == Length
        && AllInAlphabet(mark);

    /// <summary>Como se lee en voz alta y como se copia a un carton de papel: partido al
    /// medio, que es como la gente lee numeros largos.</summary>
    public static string Pretty(string mark) =>
        IsOurs(mark) ? mark.Substring(0, 4) + "-" + mark.Substring(4) : mark ?? string.Empty;

    private static bool AllInAlphabet(string mark)
    {
        foreach (char c in mark) if (Alphabet.IndexOf(c) < 0) return false;

        return true;
    }
}
