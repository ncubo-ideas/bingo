using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>
/// Alguien canta BINGO desde su telefono.
///
/// NO decide nada, y por eso no toca el diario: es el equivalente digital de levantar
/// la mano y gritar. El organizador ya sabe por su cuenta que cartones completaron --
/// se lo pregunta a la mesa carton por carton --, asi que esto no viene a descubrir
/// un ganador sino a decir QUIEN esta reclamando y a que hora. Con eso el letrero deja
/// de mostrar al primero del tablero y muestra a quien de verdad esta parado con el
/// carton en la mano.
///
/// Quien valida sigue siendo la mesa, cuando el organizador toca entregar o rechazar.
/// </summary>
public sealed class BingoClaim
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    /// <summary>Como se llama quien canta, para el letrero.</summary>
    [JsonPropertyName("quien")]
    public string Who { get; set; }

    /// <summary>Su numero de comprador: es lo unico que lo identifica de verdad,
    /// porque dos personas se pueden llamar igual.</summary>
    [JsonPropertyName("marca")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    [JsonPropertyName("carton")]
    public long Card { get; set; }

    /// <summary>La figura en nombre de motor ('AnyLine'), no como se dice en la mesa:
    /// del otro lado se compara contra lo que la mesa juega.</summary>
    [JsonPropertyName("figura")]
    public string Figure { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static BingoClaim FromText(string json) =>
        JsonSerializer.Deserialize<BingoClaim>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.canto-de-bingo"));
}
