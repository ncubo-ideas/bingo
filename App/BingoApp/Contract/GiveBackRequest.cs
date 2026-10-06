using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>Un colaborador le devuelve al organizador los cartones que no vendio.
/// Lo pide quien los tiene: el organizador no los recoge por detras, solo asienta
/// lo que ella ya solto.</summary>
public sealed class GiveBackRequest
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    [JsonPropertyName("de")]
    public string From { get; set; }

    [JsonPropertyName("cartones")]
    public List<long> Cards { get; set; } = new();

    /// <summary>La marca del que los devuelve: es el unico que puede soltarlos.</summary>
    [JsonPropertyName("marca")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static GiveBackRequest FromText(string json) =>
        JsonSerializer.Deserialize<GiveBackRequest>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.devolucion"));
}
