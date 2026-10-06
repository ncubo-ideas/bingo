using System.Text.Json;

namespace BingoApp.CommunityLeader;

/// <summary>Para contestarle a alguien con un objeto anonimo sin declarar un tipo.</summary>
internal static class AsJson
{
    public static string ToText(this object value) => JsonSerializer.Serialize(value);
}
