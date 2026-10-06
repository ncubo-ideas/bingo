using System;
using System.Collections.Generic;

namespace Village
{
    internal enum RoundStanding
    {
        Planned,
        Playing,
        Played,
        CalledOff
    }

    public sealed class Round
    {
        private readonly List<Promise> _promises = new List<Promise>();

        private RoundStanding _standing;

        internal Round(int position)
        {
            Position = position;
            _standing = RoundStanding.Planned;
        }

        public int Position { get; }

        /// <summary>Las figuras de la ronda, separadas por comas: derivado de sus
        /// promesas, porque el premio vive en cada figura y no en la ronda.</summary>
        public string Playing
        {
            get
            {
                List<string> words = new List<string>();

                foreach (Promise promise in _promises) words.Add(promise.Playing);

                return string.Join(",", words);
            }
        }

        public string HowItStands => _standing.ToString();

        public bool IsPlanned => _standing == RoundStanding.Planned;

        public bool IsPlaying => _standing == RoundStanding.Playing;

        public bool WasPlayed => _standing == RoundStanding.Played;

        public bool WasCalledOff => _standing == RoundStanding.CalledOff;

        public int PromiseCount => _promises.Count;

        public Promise PromiseAt(int position)
        {
            if (position < 1 || position > _promises.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "round " + Position + " holds " + _promises.Count + " promises");

            return _promises[position - 1];
        }

        public Promise PromiseOn(string figure)
        {
            if (string.IsNullOrWhiteSpace(figure))
                throw new ArgumentException("a promise is looked up by its figure", nameof(figure));

            string looked = figure.Trim();

            foreach (Promise promise in _promises)
            {
                if (string.Equals(promise.Playing, looked, StringComparison.OrdinalIgnoreCase))
                    return promise;
            }

            throw new VillageRuleException(
                "round " + Position + " promises nothing on " + looked);
        }

        public bool PlaysFor(string figure)
        {
            if (string.IsNullOrWhiteSpace(figure)) return false;

            string looked = figure.Trim();

            foreach (Promise promise in _promises)
            {
                if (string.Equals(promise.Playing, looked, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>La ronda quedo completa: cada figura entrego su premio.</summary>
        public bool EveryPrizeWasGiven
        {
            get
            {
                if (_promises.Count == 0) return false;

                foreach (Promise promise in _promises)
                    if (!promise.WasKept) return false;

                return true;
            }
        }

        public int WinnerCount
        {
            get
            {
                int winners = 0;

                foreach (Promise promise in _promises) winners += promise.WinnerCount;

                return winners;
            }
        }

        internal Promise MakePromise(string playing, bool inCash, string prize)
        {
            if (_standing != RoundStanding.Planned)
                throw new VillageRuleException(
                    "round " + Position + " stands " + HowItStands
                    + "; promises are made before it begins");

            string word = playing.Trim();

            if (PlaysFor(word))
                throw new VillageRuleException(
                    "round " + Position + " already promises something on " + word);

            Promise promise = new Promise(_promises.Count + 1, word, inCash, prize.Trim());
            _promises.Add(promise);
            return promise;
        }

        internal void Begin()
        {
            if (_standing != RoundStanding.Planned)
                throw new VillageRuleException(
                    "round " + Position + " cannot begin again: it stands " + HowItStands);

            if (_promises.Count == 0)
                throw new VillageRuleException(
                    "round " + Position + " promises nothing, so there is nothing to play");

            _standing = RoundStanding.Playing;
        }

        internal void NameWinner(Promise promise, long number)
        {
            if (_standing != RoundStanding.Playing)
                throw new VillageRuleException(
                    "round " + Position + " is not playing, so nobody can win it");

            if (promise == null) throw new ArgumentNullException(nameof(promise));

            if (!_promises.Contains(promise))
                throw new VillageRuleException(
                    "the promise on " + promise.Playing + " is not one of round " + Position);

            promise.NameWinner(number);
        }

        internal void Settle()
        {
            if (_standing != RoundStanding.Playing)
                throw new VillageRuleException(
                    "round " + Position + " is not playing, so there is nothing to settle");

            _standing = WinnerCount > 0 ? RoundStanding.Played : RoundStanding.CalledOff;
        }

        public override string ToString()
        {
            string promised = string.Join(" and ", _promises);

            if (_standing == RoundStanding.Planned)
                return "round " + Position + ", " + promised + ", still to come";

            if (_standing == RoundStanding.Playing)
                return "round " + Position + ", " + promised + ", playing now";

            if (_standing == RoundStanding.CalledOff)
                return "round " + Position + ", called off with every prize left ungiven";

            return "round " + Position + ", " + promised;
        }
    }
}
