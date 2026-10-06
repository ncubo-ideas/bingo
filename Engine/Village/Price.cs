using System;
using System.Globalization;

namespace Village
{
    public sealed class Price : IEquatable<Price>
    {
        public Price(long amount)
        {
            if (amount <= 0)
                throw new VillageRuleException("a card is priced above zero, or not priced at all");

            Amount = amount;
        }

        public long Amount { get; }

        public bool Equals(Price other) => other != null && other.Amount == Amount;

        public override bool Equals(object obj) => Equals(obj as Price);

        public override int GetHashCode() => Amount.GetHashCode();

        public override string ToString() => Amount.ToString(CultureInfo.InvariantCulture);
    }
}
