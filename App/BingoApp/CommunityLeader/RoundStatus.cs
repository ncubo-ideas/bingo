namespace BingoApp.CommunityLeader;

/// <summary>Una promesa como la ve la pantalla, leida del diario.</summary>
public sealed class PromiseStatus
{
    public int Position { get; init; }

    /// <summary>UNA figura.</summary>
    public string Playing { get; init; }

    public string Prize { get; init; }

    public bool IsCash { get; init; }

    /// <summary>Los cartones que ganaron ESTA figura, en el orden en que ganaron.</summary>
    public IReadOnlyList<long> Winners { get; init; } = Array.Empty<long>();

    public bool WasKept => Winners.Count > 0;

    public string PrizeText => IsCash ? "$" + Prize : Prize;
}

/// <summary>Una ronda como la ve la pantalla, leida del diario: la agenda sobrevive
/// cualquier reinicio.</summary>
public sealed class RoundStatus
{
    public int Position { get; init; }

    /// <summary>Las figuras separadas por comas, derivadas de las promesas.</summary>
    public string Playing { get; init; }

    /// <summary>Planned, Playing, Played o CalledOff.</summary>
    public string Standing { get; init; }

    public IReadOnlyList<PromiseStatus> Promises { get; init; } = Array.Empty<PromiseStatus>();

    public bool IsPlanned => string.Equals(Standing, "Planned", StringComparison.Ordinal);

    public bool IsPlaying => string.Equals(Standing, "Playing", StringComparison.Ordinal);

    public bool WasPlayed => string.Equals(Standing, "Played", StringComparison.Ordinal);

    public bool WasCalledOff => string.Equals(Standing, "CalledOff", StringComparison.Ordinal);

    public int WinnerCount => Promises.Sum(p => p.Winners.Count);

    public bool EveryPrizeWasGiven => Promises.Count > 0 && Promises.All(p => p.WasKept);

    /// <summary>"Una fila $10 · Carton lleno una canasta" — para las cabeceras. El
    /// nombre sale de OnOffer: lo que el motor llama AnyRow, la gente lo llama fila.
    /// </summary>
    public string PrizeLine =>
        string.Join("  ·  ", Promises.Select(p => OnOffer.Called(p.Playing) + " " + p.PrizeText));
}
