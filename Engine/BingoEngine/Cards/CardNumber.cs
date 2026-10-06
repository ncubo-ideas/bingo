using System;
using System.Globalization;

namespace BingoEngine
{
    public sealed record CardNumber
    {
        public CardNumber(long value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "A card number must be greater than zero.");
            }

            Value = value;
        }

        public long Value { get; }

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }
}
