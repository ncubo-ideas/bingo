using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>Lo que alguien le dice al organizador para entrar a un bingo.</summary>
public sealed class JoinRequest
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    [JsonPropertyName("yo")]
    public string Name { get; set; }

    [JsonPropertyName("escuchoEn")]
    public string ListensAt { get; set; }

    [JsonPropertyName("cartones")]
    public List<long> Cards { get; set; }

    /// <summary>A que vino: vende, canta o juega. Se lo dijo el QR, no lo eligio el.</summary>
    [JsonPropertyName("rol")]
    public string Duty { get; set; }

    /// <summary>
    /// Con que marca dice venir. En el colaborador es la credencial del papelito que le dio
    /// el organizador -- es lo que dice "vos sos la que vende". En el jugador es su numero de
    /// comprador, que no autoriza nada: solo dice cual de las dos Ritas es.
    /// </summary>
    [JsonPropertyName("marca")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    /// <summary>
    /// Que APARATO se esta presentando. No es la marca: la marca dice quien compro, y esto
    /// dice desde donde. Un mismo telefono se presenta muchas veces -- cada vez que se abre
    /// la app -- y eso es normal; lo que no es normal es que el mismo numero de comprador
    /// aparezca desde dos aparatos, y sin esto no habria como distinguir las dos cosas.
    /// Vacio en los jugadores de antes de los numeros.
    /// </summary>
    [JsonPropertyName("aparato")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Handset { get; set; }

    public bool IsCollaborator => Duties.IsCollaborator(Duty);

    public bool IsSeller => Duties.IsSeller(Duty);

    public bool IsCaller => Duties.IsCaller(Duty);

    public string ToText() => JsonSerializer.Serialize(this);

    public static JoinRequest FromText(string json) =>
        JsonSerializer.Deserialize<JoinRequest>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.quien-se-presento"));
}
