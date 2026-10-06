namespace BingoApp.Player;

/// <summary>Como va un carton del jugador, figura por figura. Waiting son los numeros
/// que le faltan a la variante mas cercana de la figura — los sabe el motor (ClosestFor),
/// no la pagina: el dibujito de la pagina es un retrato, y para decir "escucha el 52"
/// hace falta la variante de verdad.</summary>
public sealed class CardProgress
{
    public long Number { get; init; }

    public IReadOnlyList<(string Figure, int Have, int Need, bool Called, IReadOnlyList<int> Waiting)> Figures { get; init; }
        = Array.Empty<(string, int, int, bool, IReadOnlyList<int>)>();
}
