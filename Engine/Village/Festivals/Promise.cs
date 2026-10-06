using System;
using System.Collections.Generic;

namespace Village
{
    public sealed class Promise
    {
        private readonly List<long> _winners = new List<long>();

        internal Promise(int position, string playing, bool inCash, string prize)
        {
            Position = position;
            Playing = playing;
            IsCash = inCash;
            Prize = prize;
        }

        /// <summary>Dentro de su ronda, 1-based.</summary>
        public int Position { get; }

        /// <summary>UNA figura: la palabra del juego, que el pueblo repite sin
        /// entenderla.</summary>
        public string Playing { get; }

        public string Prize { get; }

        /// <summary>Si es en efectivo se reparte entre los ganadores de ESTA figura;
        /// si es un bien, como lo repartan es asunto de ellos, no del pueblo.</summary>
        public bool IsCash { get; }

        public int WinnerCount => _winners.Count;

        /// <summary>La promesa se cumplio: alguien gano esta figura y el premio salio.</summary>
        public bool WasKept => _winners.Count > 0;

        public long WinnerAt(int position)
        {
            if (position < 1 || position > _winners.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "the promise on " + Playing + " has " + _winners.Count + " winners");

            return _winners[position - 1];
        }

        public bool Won(long number) => _winners.Contains(number);

        public IReadOnlyList<long> NumbersThatWon() => new List<long>(_winners);

        internal void NameWinner(long number)
        {
            if (_winners.Contains(number))
                throw new VillageRuleException(
                    "the number " + number + " was already named a winner of " + Playing);

            _winners.Add(number);
        }

        public override string ToString()
        {
            string prize = IsCash ? Prize + " in cash" : Prize;

            return WasKept
                ? Playing + " for " + prize + ", kept by " + _winners.Count + " winner(s)"
                : Playing + " for " + prize;
        }
    }
}
