namespace BingoApp.CommunityLeader;

/// <summary>Un papelito que el organizador dio, como se lo muestra la pantalla. Sale del
/// diario, no de la memoria: por eso se puede administrar aunque la app se haya
/// reiniciado y no quede nadie en la lista de asistentes.</summary>
public sealed class HandedOut
{
    public string Mark { get; init; }

    /// <summary>vende o canta, en el vocabulario del cable.</summary>
    public string Duty { get; init; }

    /// <summary>Offered, Taken o SentHome.</summary>
    public string Standing { get; init; }

    /// <summary>Vacio si nadie lo tomo todavia.</summary>
    public string TakenBy { get; init; }

    /// <summary>Para quien lo corto el organizador. Vacio en los papelitos de antes de que
    /// se cortaran a nombre de alguien, y en los que se cortan para el que llegue.</summary>
    public string MeantFor { get; init; } = string.Empty;

    /// <summary>Los cartones que el organizador le aparto. Cero si no le aparto ninguno.
    /// </summary>
    public int CardsMeant { get; init; }

    /// <summary>Como llamarlo en pantalla: por QUIEN LO TOMO si alguien ya lo uso, y
    /// mientras nadie lo use, por la persona para la que se corto.
    ///
    /// El orden importa y antes estaba al reves. Un papelito cortado para Sonia lo puede
    /// terminar usando Marta -- Sonia no pudo venir y se lo paso --, y con la intencion
    /// mandando, el organizador le seguia entregando cartones a "Sonia". Lo que hay que
    /// ver es quien los tiene, no para quien eran.</summary>
    public string Whose => (TakenBy ?? string.Empty).Length > 0
        ? TakenBy
        : MeantFor ?? string.Empty;

    /// <summary>True cuando lo tomo alguien distinto de aquel para quien se corto. No es
    /// un error --pasarse el codigo entre vecinas es de lo mas normal-- pero el
    /// organizador tiene que notarlo.</summary>
    public bool SomebodyElse =>
        MeantFor.Length > 0
        && (TakenBy ?? string.Empty).Length > 0
        && !string.Equals(MeantFor.Trim(), TakenBy.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool OnTheTable => string.Equals(Standing, "Offered", StringComparison.Ordinal);

    public bool InSomebodysHands => string.Equals(Standing, "Taken", StringComparison.Ordinal);

    public bool SentHome => string.Equals(Standing, "SentHome", StringComparison.Ordinal);
}
