using System;

namespace Village
{
    public sealed class Prize
    {
        internal Prize(int position, string what)
        {
            if (string.IsNullOrWhiteSpace(what))
                throw new ArgumentException("a prize needs to say what it is", nameof(what));

            Position = position;
            What = what;
        }

        public int Position { get; }

        public string What { get; }

        public bool IsMain { get; private set; }

        public long TakenBy { get; private set; }

        public bool IsGiven => TakenBy != 0;

        internal void MarkAsMain() => IsMain = true;

        internal void LoseTheMainMark() => IsMain = false;

        internal void GiveTo(long number)
        {
            if (IsGiven)
                throw new VillageRuleException(
                    What + " was already given to the number " + TakenBy);

            TakenBy = number;
        }

        public override string ToString() => Position + ". " + What + (IsMain ? " (principal)" : "");
    }
}
