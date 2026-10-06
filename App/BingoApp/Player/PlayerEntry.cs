using System.Text.Json;
using BingoApp.Contract;

namespace BingoApp.Player;

public sealed class PlayerEntry
{
    public OpenFestival Festival { get; init; }

    /// <summary>El codigo tal cual llego, para poder guardarlo y volver a entrar.</summary>
    public string Code { get; set; }

    public string MyName { get; set; }

    public int MyPort { get; set; } = 8081;

    public List<long> MyCards { get; } = new();

    /// <summary>La marca que me distingue de otra persona que se llame igual. La acuna el
    /// organizador en mi primera compra y viaja adentro de mi codigo. Vacia en los codigos
    /// de antes de las marcas: esos se siguen conociendo por el nombre.</summary>
    public string MyMark { get; set; } = string.Empty;

    /// <summary>Tiene que dar lo MISMO que Attendee.Handle del otro lado, o el organizador
    /// me habla a un buzon que yo no estoy escuchando.</summary>
    public string MyHandle => string.IsNullOrWhiteSpace(MyMark) ? MyName : MyMark;

    public string MyCardRole => WireNames.CardRole(Festival?.FestivalId, MyHandle);

    public string MyCardTopic => WireNames.CardTopic(Festival?.FestivalId, MyHandle);

    public string PlayerRole => WireNames.PlayerRole(Festival?.FestivalId);

    public string CallTopic => WireNames.CallTopic(Festival?.FestivalId);

    public string MyGame => "juego_" + WireNames.Safe(Festival?.FestivalId);

    /// <summary>
    /// Donde guardo LA RONDA QUE DICE EL ORGANIZADOR, que no es la que lleva mi motor.
    ///
    /// Son dos contadores distintos y hay que no confundirlos: el de mi motor sube solo cada
    /// vez que rehago el tablero --y lo rehago cada vez que me reponen un carton-- asi que se
    /// adelanta sin que en el bingo haya pasado nada. El del organizador es el del bingo.
    /// Para saber si una bolita es de esta ronda hay que mirar el de el.
    ///
    /// Va como global del diario y no en memoria porque quien la compara es el DSL, cuando
    /// entra el aviso, y el DSL no ve el C#.
    /// </summary>
    public string MyRoundSlot => "ronda_" + WireNames.Safe(Festival?.FestivalId);

    public string MyUpgradeName()
    {
        char[] clean = ("funcion-" + (Festival?.FestivalId ?? "suelta")).ToCharArray();

        for (int i = 0; i < clean.Length; i++)
            if (!char.IsLetterOrDigit(clean[i]) && clean[i] != '-') clean[i] = '-';

        return new string(clean);
    }

    public JoinRequest ToJoinRequest(string myAddress, string handset) => new()
    {
        FestivalId = Festival?.FestivalId,
        Name = MyName,
        ListensAt = $"http://{myAddress}:{MyPort}",
        Cards = MyCards.Count > 0 ? MyCards.ToList() : null,
        Mark = string.IsNullOrWhiteSpace(MyMark) ? null : MyMark,
        Handset = string.IsNullOrWhiteSpace(handset) ? null : handset,
    };

    public static PlayerEntry FromCode(string text)
    {
        OpenFestival festival = OpenFestival.FromText(text);

        PlayerEntry entered = new() { Festival = festival, Code = text?.Trim() };

        try
        {
            using JsonDocument doc = JsonDocument.Parse(OpenFestival.RawJson(text));

            if (doc.RootElement.TryGetProperty("yo", out JsonElement who))
                entered.MyName = who.GetString();

            if (doc.RootElement.TryGetProperty("m", out JsonElement mark))
                entered.MyMark = mark.GetString() ?? string.Empty;

            if (doc.RootElement.TryGetProperty("c", out JsonElement cards)
                && cards.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement one in cards.EnumerateArray())
                    entered.MyCards.Add(one.GetInt64());
            }
        }
        catch (Exception)
        {
        }

        return entered;
    }
}
