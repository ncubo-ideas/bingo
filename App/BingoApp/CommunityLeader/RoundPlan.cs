namespace BingoApp.CommunityLeader;

/// <summary>Una promesa como el padre la arma: UNA figura y el premio que da al que
/// la complete. En efectivo se reparte entre los ganadores de esa figura; un premio
/// fisico lo arreglan entre ellos.</summary>
public sealed class PromisePlan
{
    public string Figure { get; init; }

    public bool IsCash { get; init; }

    /// <summary>El monto si es en efectivo ("25"), o la descripcion del bien
    /// ("una canasta").</summary>
    public string Prize { get; init; }
}

/// <summary>Una ronda como el padre la anuncia: un punado de promesas. El premio vive
/// en la figura, no en la ronda; la ronda queda completa cuando todas entregaron.</summary>
public sealed class RoundPlan
{
    public IReadOnlyList<PromisePlan> Promises { get; init; } = Array.Empty<PromisePlan>();

    public IReadOnlyList<string> FigureList() =>
        Promises
            .Select(p => (p.Figure ?? string.Empty).Trim())
            .Where(f => f.Length > 0)
            .ToList();
}
