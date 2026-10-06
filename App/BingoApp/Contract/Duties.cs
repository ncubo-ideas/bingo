namespace BingoApp.Contract;

/// <summary>
/// A que vino cada quien. El QR lo dice: el organizador genera uno para quien
/// vende y otro para quien canta, y al jugador lo mete quien le vendio el
/// carton. Sin esto el papel seria de palabra y cualquiera podria hacer lo del
/// otro.
/// </summary>
public static class Duties
{
    // Los valores viajan en el QR: no se tocan.
    public const string Sells = "vende";

    public const string Calls = "canta";

    public const string Plays = "juega";

    public static bool IsCollaborator(string duty) => IsSeller(duty) || IsCaller(duty);

    public static bool IsSeller(string duty) =>
        string.Equals(duty, Sells, StringComparison.OrdinalIgnoreCase);

    public static bool IsCaller(string duty) =>
        string.Equals(duty, Calls, StringComparison.OrdinalIgnoreCase);

    public static string Clean(string duty) =>
        IsSeller(duty) ? Sells : IsCaller(duty) ? Calls : Plays;

    /// <summary>Como lo llama el pueblo: el oficio que va en el encargo del diario.</summary>
    public static string AsChore(string duty) => IsCaller(duty) ? "Calling" : "Selling";

    /// <summary>El camino de vuelta, del oficio del pueblo al papel del cable.</summary>
    public static string FromChore(string chore) =>
        string.Equals(chore, "Calling", StringComparison.OrdinalIgnoreCase) ? Calls : Sells;

    /// <summary>Como se lo decimos a la gente, en su idioma. El valor del QR no se
    /// traduce; la clave del catalogo se arma con el valor limpio.</summary>
    public static string Describe(string duty) => Texts.T("oficio." + Clean(duty));
}
