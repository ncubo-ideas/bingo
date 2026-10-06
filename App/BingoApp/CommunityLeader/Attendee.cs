using BingoApp.Contract;

namespace BingoApp.CommunityLeader;

/// <summary>
/// Alguien que el organizador ya dejo entrar a un bingo. Esto NO viaja: es la
/// memoria del organizador sobre quien esta adentro, que papel trae y que cartones
/// tiene en la mano.
/// </summary>
public sealed class Attendee
{
    public string FestivalId { get; init; }

    public string Name { get; init; }

    /// <summary>Donde alcanzarlo. Cambia: se le cayo la app y volvio con otra IP, o se
    /// cambio de red. Si esto no se pudiera actualizar, el organizador le seguiria hablando
    /// a la direccion de hace una hora y los cartones nuevos no llegarian a ningun lado, sin
    /// error de por medio.</summary>
    public string ListensAt { get; set; }

    /// <summary>vende, canta o juega. Lo trajo su QR.</summary>
    public string Duty { get; init; } = Duties.Plays;

    /// <summary>La marca del papelito con el que entro. Null si entro por gracia, con un
    /// codigo que el diario ya no reconocia.</summary>
    public string Mark { get; init; }

    /// <summary>Desde que aparato entro. Se anota la primera vez y no se cambia: es lo que
    /// hace que gane el primero. Vacio en los jugadores de antes de los numeros, y ahi no se
    /// puede guardar nada.</summary>
    public string Handset { get; set; }

    public bool IsCollaborator => Duties.IsCollaborator(Duty);

    public bool IsSeller => Duties.IsSeller(Duty);

    public bool IsCaller => Duties.IsCaller(Duty);

    public List<long> Cards { get; } = new();

    public List<long> Sold { get; } = new();

    /// <summary>Como se le habla. Por su marca si la trae; por el nombre en los bingos de
    /// antes de las marcas. Dos personas que se llaman igual traen marcas distintas, y por
    /// eso dejan de compartir buzon: el carton de una no le puede aparecer a la otra.</summary>
    public string Handle => WireNames.Handle(Mark, Name);

    public string CardRole => WireNames.CardRole(FestivalId, Handle);

    public string CardTopic => WireNames.CardTopic(FestivalId, Handle);
}
