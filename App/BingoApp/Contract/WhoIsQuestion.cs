using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>
/// Lo que quien vende le pregunta al organizador ANTES de cobrar: de quien es este numero.
///
/// Es una pregunta, no un movimiento: no cambia nada en el diario. Existe porque el numero
/// se puede teclear, y un caracter cambiado a media luz no se nota hasta que el carton no
/// le llega a nadie -- cuando ya no hay marcha atras.
/// </summary>
public sealed class WhoIsQuestion
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    [JsonPropertyName("vendedor")]
    public string Collaborator { get; set; }

    /// <summary>El papelito de quien pregunta. Sin el, cualquiera podria averiguar quien
    /// compro que.</summary>
    [JsonPropertyName("marca")]
    public string Mark { get; set; }

    /// <summary>El numero que se leyo o se tecleo.</summary>
    [JsonPropertyName("numero")]
    public string Buyer { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static WhoIsQuestion FromText(string json) =>
        JsonSerializer.Deserialize<WhoIsQuestion>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.pregunta"));
}
