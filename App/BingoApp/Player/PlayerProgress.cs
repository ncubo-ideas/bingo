namespace BingoApp.Player;

/// <summary>Como va el jugador en el bingo que tiene al frente.</summary>
public sealed class PlayerProgress
{
    public string FestivalId { get; init; }

    public IReadOnlyCollection<int> Drawn { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> DrawOrder { get; init; } = Array.Empty<int>();

    public int LastDraw { get; init; }

    public IReadOnlyList<CardProgress> Cards { get; init; } = Array.Empty<CardProgress>();

    /// <summary>Las figuras de la ronda que suena, leidas del motor local (no del QR,
    /// que se imprimio cuando la ronda era otra).</summary>
    public IReadOnlyList<string> Playing { get; init; } = Array.Empty<string>();

    /// <summary>El conteo de rondas del motor local; cambia cuando arranca otra.</summary>
    public int RoundNumber { get; init; }

    /// <summary>El numero de ronda que anuncia el padre; 0 si no se sabe.</summary>
    public int RoundPosition { get; init; }

    /// <summary>"AnyRow $10 · FullCard una canasta": el premio vive en cada figura.</summary>
    public string PrizeLine { get; init; } = string.Empty;

    public bool BetweenRounds { get; init; }

    public bool FestivalOver { get; init; }
}
