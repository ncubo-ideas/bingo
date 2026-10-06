using System;

namespace BingoEngine
{
    internal sealed record Cell
    {
        internal const int FreeBand = 2;
        internal const int FreeRow = 2;

        internal Cell(int band, int row)
        {
            if (band < 0 || band >= Cardface.BandCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(band), band, "A band must be 0.." + (Cardface.BandCount - 1) + ".");
            }

            if (row < 0 || row >= Cardface.BandCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(row), row, "A row must be 0.." + (Cardface.BandCount - 1) + ".");
            }

            Band = band;
            Row = row;
        }

        internal int Band { get; }

        internal int Row { get; }

        internal bool IsFreeCenter => Band == FreeBand && Row == FreeRow;

        public override string ToString() => "(" + Band + "," + Row + ")";
    }
}
