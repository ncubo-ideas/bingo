namespace BingoApp.Huddle;

public sealed class HuddlePass
{
    public string CommunityLeaderTicket { get; init; }

    public string CommunityLeaderEndpointId { get; init; }

    public string InvitedBy { get; init; }

    public string CoordinationAddress { get; init; }

    public string ReplicationAddress { get; init; }

    public string CommandAddress { get; init; }

    public string ToText() => string.Join("\n", new[]
    {
        CommunityLeaderTicket, CommunityLeaderEndpointId, InvitedBy,
        CoordinationAddress, ReplicationAddress, CommandAddress,
    });

    public const string Scheme = "bingo://pase?d=";

    public string ToLink() =>
        Scheme + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ToText()))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static bool IsPassLink(string text) =>
        text is not null
        && text.TrimStart().StartsWith("bingo://pase", StringComparison.OrdinalIgnoreCase);

    public static HuddlePass FromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(Texts.T("corrillo.pase.vacio"));

        string clean = text.Trim();

        if (IsPassLink(clean))
        {
            int cut = clean.IndexOf("d=", StringComparison.Ordinal);
            if (cut < 0) throw new ArgumentException(Texts.T("corrillo.pase.sin-datos"));

            string raw = clean[(cut + 2)..].Replace('-', '+').Replace('_', '/');
            raw = raw.PadRight(raw.Length + (4 - raw.Length % 4) % 4, '=');
            clean = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(raw));
        }

        string[] parts = clean.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 6)
            throw new ArgumentException(Texts.T("corrillo.pase.lineas", ("n", parts.Length.ToString())));

        return new HuddlePass
        {
            CommunityLeaderTicket = parts[0],
            CommunityLeaderEndpointId = parts[1],
            InvitedBy = parts[2],
            CoordinationAddress = parts[3],
            ReplicationAddress = parts[4],
            CommandAddress = parts[5],
        };
    }
}
