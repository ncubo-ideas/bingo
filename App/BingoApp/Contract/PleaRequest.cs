using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>Una colaboradora le plantea al organizador que una venta se deshaga.
/// No la deshace: la plantea. Puede ser sobre una venta que hizo otra -- en la
/// puerta atiende la que este libre.</summary>
public sealed class PleaRequest
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    [JsonPropertyName("de")]
    public string From { get; set; }

    /// <summary>El carton cuya venta se quiere deshacer.</summary>
    [JsonPropertyName("carton")]
    public long Card { get; set; }

    /// <summary>La marca de quien lo plantea: sin ella el diario no sabe quien es.</summary>
    [JsonPropertyName("marca")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static PleaRequest FromText(string json) =>
        JsonSerializer.Deserialize<PleaRequest>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.pedido"));
}
