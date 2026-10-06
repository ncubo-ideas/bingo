using System;
using System.Collections.Generic;

namespace BingoEngine
{
    internal sealed class DrawnSequence
    {
        private readonly List<Ball> _order = new List<Ball>();
        private readonly Dictionary<int, int> _positionOf = new Dictionary<int, int>();

        internal int Count => _order.Count;

        internal int Remaining => (Ball.Highest - Ball.Lowest + 1) - _order.Count;

        internal IReadOnlyList<Ball> InOrder => _order;

        internal bool Contains(Ball ball)
        {
            if (ball is null) throw new ArgumentNullException(nameof(ball));

            return _positionOf.ContainsKey(ball.Number);
        }

        internal int PositionOf(Ball ball)
        {
            if (ball is null) throw new ArgumentNullException(nameof(ball));

            return _positionOf.TryGetValue(ball.Number, out int position) ? position : -1;
        }

        internal void Replace(Ball wrong, Ball right)
        {
            if (wrong is null) throw new ArgumentNullException(nameof(wrong));
            if (right is null) throw new ArgumentNullException(nameof(right));

            int position = PositionOf(wrong);
            if (position < 0)
            {
                throw new BingoRuleException("Ball " + wrong + " never came out.");
            }

            if (_positionOf.ContainsKey(right.Number))
            {
                throw new BingoRuleException("Ball " + right + " already came out.");
            }

            _positionOf.Remove(wrong.Number);
            _positionOf[right.Number] = position;
            _order[position] = right;
        }

        internal bool Append(Ball ball)
        {
            if (ball is null) throw new ArgumentNullException(nameof(ball));

            if (_positionOf.ContainsKey(ball.Number)) return false;

            _positionOf[ball.Number] = _order.Count;
            _order.Add(ball);
            return true;
        }
    }
}
