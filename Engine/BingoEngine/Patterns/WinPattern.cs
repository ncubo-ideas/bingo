using System;
using System.Collections.Generic;

namespace BingoEngine
{
    internal enum Popularity
    {
        VeryPopular = 0,
        Popular = 1,
        LessPopular = 2
    }

    public sealed class WinPattern
    {
        private readonly IReadOnlyList<IReadOnlyList<Cell>> _alternatives;

        internal WinPattern(string name, Popularity popularity, IReadOnlyList<IReadOnlyList<Cell>> alternatives)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A figure needs a name.", nameof(name));
            if (alternatives is null) throw new ArgumentNullException(nameof(alternatives));
            if (alternatives.Count == 0) throw new ArgumentException("A figure needs at least one alternative.", nameof(alternatives));

            Name = name;
            Popularity = popularity;
            _alternatives = alternatives;
        }

        public string Name { get; }

        internal Popularity Popularity { get; }

        internal IReadOnlyList<IReadOnlyList<Cell>> Alternatives => _alternatives;

        internal bool Completes(Cardface face, DrawnSequence drawn)
        {
            if (drawn is null) throw new ArgumentNullException(nameof(drawn));

            return CompletesWithin(face, drawn, drawn.Count);
        }

        internal bool CompletesWithin(Cardface face, DrawnSequence drawn, int drawCount)
        {
            if (face is null) throw new ArgumentNullException(nameof(face));
            if (drawn is null) throw new ArgumentNullException(nameof(drawn));

            foreach (IReadOnlyList<Cell> alternative in _alternatives)
            {
                if (IsFullyCovered(alternative, face, drawn, drawCount)) return true;
            }

            return false;
        }

        private static bool IsFullyCovered(
            IReadOnlyList<Cell> alternative, Cardface face, DrawnSequence drawn, int drawCount)
        {
            foreach (Cell cell in alternative)
            {
                if (!IsCovered(cell, face, drawn, drawCount)) return false;
            }

            return true;
        }

        private static bool IsCovered(Cell cell, Cardface face, DrawnSequence drawn, int drawCount)
        {
            if (cell.IsFreeCenter) return true;

            int position = drawn.PositionOf(new Ball(face.NumberAt(cell)));
            return position >= 0 && position < drawCount;
        }
    }
}
