using System;

namespace BingoApp.Contract;

/// <summary>
/// El carnet del comprador: lo que Rita le ENSENA a quien le vende, en vez de que quien
/// vende la tenga que buscar en una lista. Con cuarenta ventas en la noche, buscarla es
/// una pared; que ella lo muestre cuesta lo mismo con una venta que con cuarenta.
///
/// Lleva el numero y el nombre. El nombre no identifica nada -- para eso esta el numero --
/// pero le ahorra a la colaboradora escribirlo, que es donde se pierde el tiempo en la
/// fila.
///
/// Esquema propio a proposito: 'bingo://' es el enlace que mete a alguien en un bingo,
/// y el telefono lo trata distinto. Un carnet no mete a nadie en ningun lado.
/// </summary>
public static class BuyerBadge
{
    public const string Scheme = "comprador://";

    public static string For(string mark, string name)
    {
        string clean = (mark ?? string.Empty).Trim();

        if (clean.Length == 0) return string.Empty;

        return Scheme + Uri.EscapeDataString(clean)
            + "/" + Uri.EscapeDataString((name ?? string.Empty).Trim());
    }

    public static bool Looks(string code) =>
        !string.IsNullOrWhiteSpace(code)
        && code.TrimStart().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lo que trae un carnet leido. Sin numero no es un carnet, y se devuelve
    /// vacio en vez de a medias.</summary>
    public static (string Mark, string Name) Read(string code)
    {
        if (!Looks(code)) return (string.Empty, string.Empty);

        string body = code.Trim().Substring(Scheme.Length);
        int slash = body.IndexOf('/');

        string mark = slash < 0 ? body : body.Substring(0, slash);
        string name = slash < 0 ? string.Empty : body.Substring(slash + 1);

        try
        {
            mark = Uri.UnescapeDataString(mark);
            name = Uri.UnescapeDataString(name);
        }
        catch (Exception)
        {
            return (string.Empty, string.Empty);
        }

        mark = BuyerMark.Normalize(mark);

        return mark.Length == 0 ? (string.Empty, string.Empty) : (mark, name.Trim());
    }

    /// <summary>
    /// Cual numero ensena este telefono.
    ///
    /// El del bingo en la que esta, si ese bingo ya lo conoce por uno; el suyo propio
    /// solo cuando todavia no lo conoce nadie. Al reves seria mas simple y estaria mal: el
    /// diario ya tiene escrito con que numero le vendieron, y ese asiento no se puede
    /// reescribir. Quien compro sin telefono a las ocho y lo instala a las nueve tiene que
    /// seguir siendo la misma persona.
    /// </summary>
    public static string Showing(string mineOnThisPhone, string inThisFestival) =>
        string.IsNullOrWhiteSpace(inThisFestival)
            ? (mineOnThisPhone ?? string.Empty).Trim()
            : inThisFestival.Trim();
}
