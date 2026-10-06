using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>Lo que el colaborador le propone al organizador cuando un jugador le
/// devuelve un carton y le pide otro. El colaborador elige los dos numeros; el
/// organizador decide contra su diario y asienta el cambio.</summary>
public sealed class ExchangeRequest
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    [JsonPropertyName("colaborador")]
    public string Collaborator { get; set; }

    [JsonPropertyName("sale")]
    public long CardLeaving { get; set; }

    [JsonPropertyName("entra")]
    public long CardComingIn { get; set; }

    /// <summary>La marca del colaborador que propone el cambio.</summary>
    [JsonPropertyName("marca")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static ExchangeRequest FromText(string json) =>
        JsonSerializer.Deserialize<ExchangeRequest>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.cambio"));
}
