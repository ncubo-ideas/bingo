using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>Un colaborador le pasa cartones de su taco a otro que se quedo sin.
/// El que los tiene es el unico que puede darlos: el organizador no reasigna por
/// detras, solo asienta lo que ya se dieron de mano.</summary>
public sealed class HandoverRequest
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    [JsonPropertyName("de")]
    public string From { get; set; }

    [JsonPropertyName("para")]
    public string To { get; set; }

    /// <summary>El numero del colaborador que los recibe: el orden en que el
    /// organizador le corto el papel. Se dice en voz alta o se escanea, y a diferencia
    /// del nombre no se repite, asi que dos Marias dejan de ser un problema.
    /// NO es la marca: la marca autoriza y nunca sale de su telefono.</summary>
    [JsonPropertyName("paraNumero")]
    public int ToNumber { get; set; }

    [JsonPropertyName("cartones")]
    public List<long> Cards { get; set; } = new();

    /// <summary>La marca del que los esta dando: es el unico que puede darlos.</summary>
    [JsonPropertyName("marca")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static HandoverRequest FromText(string json) =>
        JsonSerializer.Deserialize<HandoverRequest>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.traspaso"));
}
