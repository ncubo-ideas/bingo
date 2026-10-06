using System.Text.Json.Serialization;

namespace BingoApp.Collaborator;

/// <summary>Un bingo a la que el colaborador esta unido, para su lista persistida.</summary>
public sealed class JoinedFestival
{
    // Los nombres guardados se quedan como estaban: esta lista ya vive en los
    // telefonos y renombrar la llave la haria ilegible.
    [JsonPropertyName("Codigo")]
    public string Code { get; set; }

    [JsonPropertyName("MiNombre")]
    public string MyName { get; set; }
}
