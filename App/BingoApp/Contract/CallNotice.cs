using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>La bolita que el colaborador digito y le manda al organizador.</summary>
public sealed class CallNotice
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    [JsonPropertyName("quien")]
    public string Who { get; set; }

    [JsonPropertyName("numero")]
    public int Number { get; set; }

    /// <summary>La bolita que esta reemplazando, cuando el canto es una correccion.
    /// Cero cuando es un canto normal.
    ///
    /// Corregir viaja por el mismo camino que cantar a proposito: es el mismo gesto, con
    /// el mismo papelito y el mismo permiso. Una ruta aparte tendria que repetir las tres
    /// verificaciones, y el dia que una de las dos cambie solo va a cambiar una.</summary>
    [JsonPropertyName("envezde")]
    public int Instead { get; set; }

    /// <summary>Con que papelito viene. Sin el, el organizador no le cree el nombre.</summary>
    [JsonPropertyName("marca")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static CallNotice FromText(string json) =>
        JsonSerializer.Deserialize<CallNotice>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.canto"));
}
