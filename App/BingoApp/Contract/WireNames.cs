namespace BingoApp.Contract;

/// <summary>
/// Como se nombran los topicos y roles de un bingo. Todo lo que viaja
/// (bolitas, cartones) va marcado con el id de SU bingo: un jugador metido
/// en dos bingos nunca recibe cruzado.
/// </summary>
public static class WireNames
{
    public static string Safe(string festivalId) =>
        new((festivalId ?? "sinfuncion").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    /// <summary>
    /// Como se le habla a alguien: por su marca si la trae, por el nombre si no.
    ///
    /// La regla vive aca y no en cada lado porque los dos extremos tienen que dar el MISMO
    /// nombre de buzon: si el organizador publica por marca y el telefono escucha por nombre,
    /// el aviso se pierde sin que nadie vea un error. Caer al nombre deja a los bingos de
    /// antes de los papelitos con el buzon que ya tenian.
    /// </summary>
    public static string Handle(string mark, string name) =>
        string.IsNullOrWhiteSpace(mark) ? (name ?? string.Empty) : mark;

    public static string CallTopic(string festivalId) => "funcion." + festivalId + ".canta";

    public static string CardTopic(string festivalId, string name) =>
        "funcion." + festivalId + ".carton." + name.ToLowerInvariant();

    public static string PlayerRole(string festivalId) => "Jugador_" + Safe(festivalId);

    public static string CardRole(string festivalId, string name) =>
        "CartonDe" + Safe(name) + "_" + Safe(festivalId);
}
