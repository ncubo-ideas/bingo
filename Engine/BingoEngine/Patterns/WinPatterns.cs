using System;
using System.Collections.Generic;
using System.Linq;

namespace BingoEngine
{
    internal sealed class WinPatterns
    {
        private static readonly WinPatterns Instance = new WinPatterns();

        private readonly Dictionary<string, WinPattern> _byName;
        private readonly IReadOnlyList<WinPattern> _all;

        private WinPatterns()
        {
            List<WinPattern> all = new List<WinPattern>
            {
                new WinPattern("AnyRow", Popularity.VeryPopular, AllRows()),
                new WinPattern("AnyColumn", Popularity.VeryPopular, AllColumns()),
                new WinPattern("AnyLine", Popularity.VeryPopular, AllLines()),
                new WinPattern("FullCard", Popularity.VeryPopular, One(WholeCard())),
                new WinPattern("FourCorners", Popularity.Popular, One(Corners())),
                new WinPattern("ThreeCorners", Popularity.Popular, AnyThreeCorners()),
                new WinPattern("Diagonal", Popularity.Popular, BothDiagonals()),
                new WinPattern("X", Popularity.Popular, One(Cross())),
                new WinPattern("L", Popularity.Popular, One(Ell())),
                new WinPattern("AnySquare", Popularity.Popular, AnySquare())
            };

            for (int row = 0; row < Cardface.BandCount; row++)
            {
                all.Add(new WinPattern("Row" + row, Popularity.Popular, One(Row(row))));
            }

            string[] letters = { "B", "I", "N", "G", "O" };
            for (int band = 0; band < Cardface.BandCount; band++)
            {
                all.Add(new WinPattern("Col" + letters[band], Popularity.LessPopular, One(Column(band))));
            }

            _all = all;
            _byName = all.ToDictionary(pattern => pattern.Name, StringComparer.Ordinal);
        }

        internal static WinPatterns Catalog => Instance;

        internal IReadOnlyList<WinPattern> ByPopularity =>
            _all.OrderBy(pattern => pattern.Popularity).ThenBy(pattern => pattern.Name, StringComparer.Ordinal).ToList();

        internal WinPattern ByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A figure name is required.", nameof(name));

            if (!_byName.TryGetValue(name, out WinPattern pattern))
            {
                throw new BingoRuleException("Unknown figure '" + name + "'.");
            }

            return pattern;
        }

        private static IReadOnlyList<IReadOnlyList<Cell>> One(IReadOnlyList<Cell> alternative) =>
            new List<IReadOnlyList<Cell>> { alternative };

        private static IReadOnlyList<IReadOnlyList<Cell>> AllRows()
        {
            List<IReadOnlyList<Cell>> rows = new List<IReadOnlyList<Cell>>();
            for (int row = 0; row < Cardface.BandCount; row++) rows.Add(Row(row));

            return rows;
        }

        private static IReadOnlyList<IReadOnlyList<Cell>> AllColumns()
        {
            List<IReadOnlyList<Cell>> columns = new List<IReadOnlyList<Cell>>();
            for (int band = 0; band < Cardface.BandCount; band++) columns.Add(Column(band));

            return columns;
        }

        private static IReadOnlyList<IReadOnlyList<Cell>> BothDiagonals() =>
            new List<IReadOnlyList<Cell>> { DiagonalDown(), DiagonalUp() };

        private static IReadOnlyList<IReadOnlyList<Cell>> AnySquare()
        {
            List<IReadOnlyList<Cell>> squares = new List<IReadOnlyList<Cell>>();

            for (int band = 0; band < Cardface.BandCount - 1; band++)
            {
                for (int row = 0; row < Cardface.BandCount - 1; row++)
                {
                    squares.Add(new List<Cell>
                    {
                        new Cell(band, row),
                        new Cell(band + 1, row),
                        new Cell(band, row + 1),
                        new Cell(band + 1, row + 1)
                    });
                }
            }

            return squares;
        }

        private static IReadOnlyList<IReadOnlyList<Cell>> AllLines()
        {
            List<IReadOnlyList<Cell>> lines = new List<IReadOnlyList<Cell>>();
            for (int row = 0; row < Cardface.BandCount; row++) lines.Add(Row(row));
            for (int band = 0; band < Cardface.BandCount; band++) lines.Add(Column(band));
            lines.Add(DiagonalDown());
            lines.Add(DiagonalUp());
            return lines;
        }

        private static IReadOnlyList<Cell> Row(int row)
        {
            List<Cell> cells = new List<Cell>();
            for (int band = 0; band < Cardface.BandCount; band++) cells.Add(new Cell(band, row));
            return cells;
        }

        private static IReadOnlyList<Cell> Column(int band)
        {
            List<Cell> cells = new List<Cell>();
            for (int row = 0; row < Cardface.BandCount; row++) cells.Add(new Cell(band, row));
            return cells;
        }

        private static IReadOnlyList<Cell> DiagonalDown()
        {
            List<Cell> cells = new List<Cell>();
            for (int step = 0; step < Cardface.BandCount; step++) cells.Add(new Cell(step, step));
            return cells;
        }

        private static IReadOnlyList<Cell> DiagonalUp()
        {
            List<Cell> cells = new List<Cell>();
            for (int step = 0; step < Cardface.BandCount; step++)
            {
                cells.Add(new Cell(step, Cardface.BandCount - 1 - step));
            }

            return cells;
        }

        private static IReadOnlyList<Cell> Cross()
        {
            List<Cell> cells = new List<Cell>(DiagonalDown());
            foreach (Cell cell in DiagonalUp())
            {
                if (!cells.Contains(cell)) cells.Add(cell);
            }

            return cells;
        }

        private static IReadOnlyList<Cell> Ell()
        {
            List<Cell> cells = new List<Cell>(Column(0));
            foreach (Cell cell in Row(Cardface.BandCount - 1))
            {
                if (!cells.Contains(cell)) cells.Add(cell);
            }

            return cells;
        }

        private static IReadOnlyList<IReadOnlyList<Cell>> AnyThreeCorners()
        {
            IReadOnlyList<Cell> corners = Corners();
            List<IReadOnlyList<Cell>> alternatives = new List<IReadOnlyList<Cell>>();

            for (int leftOut = 0; leftOut < corners.Count; leftOut++)
            {
                List<Cell> three = new List<Cell>();
                for (int index = 0; index < corners.Count; index++)
                {
                    if (index != leftOut) three.Add(corners[index]);
                }

                alternatives.Add(three);
            }

            return alternatives;
        }

        private static IReadOnlyList<Cell> Corners() =>
            new List<Cell>
            {
                new Cell(0, 0),
                new Cell(0, Cardface.BandCount - 1),
                new Cell(Cardface.BandCount - 1, 0),
                new Cell(Cardface.BandCount - 1, Cardface.BandCount - 1)
            };

        private static IReadOnlyList<Cell> WholeCard()
        {
            List<Cell> cells = new List<Cell>();
            for (int band = 0; band < Cardface.BandCount; band++)
            {
                for (int row = 0; row < Cardface.BandCount; row++) cells.Add(new Cell(band, row));
            }

            return cells;
        }
    }
}
