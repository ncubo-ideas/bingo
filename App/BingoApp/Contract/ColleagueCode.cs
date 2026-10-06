using System;
using System.Globalization;

namespace BingoApp.Contract;

/// <summary>
/// Lo que una colaboradora le ensena a otra para que le pase cartones: el bingo y su
/// NUMERO. Nada mas.
///
/// No lleva la marca a proposito. La marca es la credencial -- es lo que dice "vos sos
/// la que vende" --, asi que si viajara al telefono de otra, esa otra podria vender a su
/// nombre. El numero no autoriza nada: solo sirve para que el organizador sepa a cual de
/// las dos Marias le estan pasando los cartones.
/// </summary>
public static class ColleagueCode
{
    public const string Scheme = "colega://";

    public static string For(string festivalId, int number) =>
        Scheme + (festivalId ?? string.Empty).Trim() + "/"
        + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>El numero que trae el codigo, o cero si no es de este bingo.</summary>
    public static int NumberIn(string code, string festivalId)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;

        string read = code.Trim();

        if (!read.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return 0;

        string[] parts = read.Substring(Scheme.Length).Split('/');

        if (parts.Length != 2) return 0;

        if (!string.Equals(parts[0], (festivalId ?? string.Empty).Trim(), StringComparison.Ordinal))
            return 0;

        return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int number) && number > 0 ? number : 0;
    }
}
