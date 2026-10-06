using System;

namespace Village
{
    public sealed class WinsAnnouncement : IEquatable<WinsAnnouncement>
    {
        public WinsAnnouncement(string how)
        {
            string said = (how ?? string.Empty).Trim();

            if (said != "Automatic" && said != "OnClaim")
                throw new VillageRuleException(
                    "wins are announced 'Automatic' or 'OnClaim', not '" + how + "'");

            How = said;
        }

        public string How { get; }

        public bool Equals(WinsAnnouncement other) =>
            other != null && string.Equals(other.How, How, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as WinsAnnouncement);

        public override int GetHashCode() => How.GetHashCode(StringComparison.Ordinal);

        public override string ToString() => How;
    }
}
