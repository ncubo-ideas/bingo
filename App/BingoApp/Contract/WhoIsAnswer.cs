using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>Lo que el organizador contesta sobre un numero de comprador. Sale del diario,
/// que es lo unico que sabe distinguir dos personas del mismo nombre.</summary>
public sealed class WhoIsAnswer
{
    [JsonPropertyName("marca")]
    public string Mark { get; set; }

    /// <summary>Si esa persona ya compro en este bingo. Falso quiere decir "comprador
    /// nuevo"; que el organizador no conteste es otra cosa y no se dice por aca.</summary>
    [JsonPropertyName("conocido")]
    public bool Known { get; set; }

    [JsonPropertyName("quien")]
    public string Who { get; set; }

    [JsonPropertyName("cuantos")]
    public int HowMany { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static WhoIsAnswer FromText(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<WhoIsAnswer>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
