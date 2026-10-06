namespace BingoApp.CommunityLeader;

/// <summary>Lo que el organizador ve de un bingo suya. Todo esto se le pregunta al
/// diario de ese bingo: no hay indice que pueda quedar desfasado.</summary>
public sealed class FestivalStatus
{
    public string Id { get; init; }

    public string Name { get; init; }

    public string Figures { get; init; }

    public string CreatedAt { get; init; }

    public string State { get; init; }

    public int Draws { get; init; }

    public int SoldCards { get; init; }

    /// <summary>Cuantos cartones hay en el bingo. Sin esto, "148 vendidos" no dice
    /// nada: la pregunta es 148 de cuantos.</summary>
    public int Cards { get; init; }

    /// <summary>Lo que vale un carton, en plata entera. Cero mientras el organizador no
    /// le haya puesto precio: es como las pantallas saben que no hay nada que sumar.</summary>
    public long CardPrice { get; init; }

    /// <summary>El dia que se juega, "yyyy-MM-dd". Vacio en los bingos de antes de que
    /// se preguntara la fecha: esos nacieron con el dia en que se crearon.</summary>
    public string PlaysOn { get; init; }

    /// <summary>La hora que abre, "HH:mm", o vacio si no la dijeron.</summary>
    public string DoorsOpenAt { get; init; }

    public int People { get; init; }

    /// <summary>Cuantas rondas anuncio el padre.</summary>
    public int Rounds { get; init; }

    /// <summary>La que suena, 1-based; 0 si ninguna (entre rondas o bingo viejo).</summary>
    public int CurrentRound { get; init; }
}
