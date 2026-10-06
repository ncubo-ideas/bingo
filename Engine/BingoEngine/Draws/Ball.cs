using System;
using System.Globalization;

namespace BingoEngine
{
    internal sealed record Ball
    {
        internal const int Lowest = 1;
        internal const int Highest = 75;

        internal Ball(int number)
        {
            if (number < Lowest || number > Highest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(number), number,
                    "A ball must be in " + Lowest + ".." + Highest + ".");
            }

            Number = number;
        }

        internal int Number { get; }

        public override string ToString() => Number.ToString(CultureInfo.InvariantCulture);
    }
}
