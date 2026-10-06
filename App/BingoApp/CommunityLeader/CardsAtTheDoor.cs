using System.Collections.Generic;

namespace BingoApp.CommunityLeader;

/// <summary>
/// Los montones que hace el organizador cuando alguien le muestra su codigo en la puerta:
/// lo que todavia no le habia anotado, lo que ya tenia, lo que no le corresponde, y lo que
/// el diario dice que es suyo pero su telefono no trae. Vive aparte de LiveCommunityLeader
/// a proposito: es la unica decision de toda la entrada que se puede probar sin telefonos,
/// y tiene mas filos de los que parece (el codigo puede traer repetidos, cartones ya
/// anotados, o ajenos).
/// </summary>
public sealed class CardsAtTheDoor
{
    private CardsAtTheDoor(
        IReadOnlyList<long> areNew, IReadOnlyList<long> alreadyWritten,
        IReadOnlyList<long> refused, IReadOnlyList<long> missing)
    {
        New = areNew;
        AlreadyWritten = alreadyWritten;
        Refused = refused;
        Missing = missing;
    }

    /// <summary>Lo que hay que anotarle y avisarle al telefono.</summary>
    public IReadOnlyList<long> New { get; }

    /// <summary>Lo que el organizador ya tenia escrito Y la persona tambien trae. Solo de
    /// esos dos juntos se puede decir que el telefono ya los tiene.</summary>
    public IReadOnlyList<long> AlreadyWritten { get; }

    /// <summary>Lo que el codigo dice que es suyo y el diario desmiente.</summary>
    public IReadOnlyList<long> Refused { get; }

    /// <summary>Lo que el diario le tiene comprado y su telefono NO trae: se le vendio con
    /// la app cerrada, o el aviso se perdio en el camino. Hay que anotarselo y avisarle
    /// igual que lo nuevo.</summary>
    public IReadOnlyList<long> Missing { get; }

    public bool AnythingNew => New.Count > 0 || Missing.Count > 0;

    /// <summary>
    /// Reparte los cartones que trae el codigo contra lo que el organizador ya tenia
    /// anotado, preguntandole al diario por cada uno. Un carton ajeno se rechaza solo a
    /// el: no puede dejar afuera a la persona por lo que si compro.
    /// </summary>
    public static CardsAtTheDoor Sort(
        IEnumerable<long> alreadyWritten,
        IEnumerable<long> whatTheyBring,
        System.Func<long, bool> wasSoldToThem)
        => Sort(alreadyWritten, whatTheyBring, wasSoldToThem, null);

    /// <summary>
    /// Lo mismo, pero mirando tambien para el otro lado: que dice el diario que es suyo.
    ///
    /// Sin ese cuarto argumento la cuenta es asimetrica -- solo pregunta "de lo que traes,
    /// que no tengo anotado" -- y eso deja un pozo sin salida: a quien le venden con el
    /// telefono cerrado, el carton le queda anotado al organizador (la venta se lo escribe)
    /// pero nunca en su telefono, y como el telefono no lo trae, no hay con que notar la
    /// falta. Se pierde para siempre, por mas veces que vuelva a entrar.
    /// </summary>
    public static CardsAtTheDoor Sort(
        IEnumerable<long> alreadyWritten,
        IEnumerable<long> whatTheyBring,
        System.Func<long, bool> wasSoldToThem,
        IEnumerable<long> whatTheJournalSays)
    {
        HashSet<long> mine = new(alreadyWritten ?? new List<long>());

        List<long> areNew = new();
        List<long> known = new();
        List<long> refused = new();
        List<long> missing = new();

        HashSet<long> brought = new();

        foreach (long card in whatTheyBring ?? new List<long>())
        {
            if (!brought.Add(card)) continue;

            if (mine.Contains(card)) known.Add(card);
            else if (wasSoldToThem is null || !wasSoldToThem(card)) refused.Add(card);
            else areNew.Add(card);
        }

        HashSet<long> seen = new();

        foreach (long card in whatTheJournalSays ?? new List<long>())
        {
            if (!seen.Add(card)) continue;
            if (brought.Contains(card)) continue;

            missing.Add(card);
        }

        areNew.Sort();
        known.Sort();
        refused.Sort();
        missing.Sort();

        return new CardsAtTheDoor(areNew, known, refused, missing);
    }
}
