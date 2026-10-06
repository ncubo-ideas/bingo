using System;

namespace Village
{
    internal enum Chore
    {
        Selling,
        Calling
    }

    internal enum ErrandStanding
    {
        Offered,
        Taken,
        SentHome
    }

    public sealed class Errand
    {
        private readonly Chore _chore;

        private ErrandStanding _standing;

        internal Errand(int position, Chore chore, string mark, string meantFor, int cardsMeant)
        {
            Position = position;
            Mark = mark;
            _chore = chore;
            _standing = ErrandStanding.Offered;
            MeantFor = (meantFor ?? string.Empty).Trim();
            CardsMeant = cardsMeant < 0 ? 0 : cardsMeant;
        }

        public int Position { get; }

        public string Mark { get; }

        public string Doing => _chore.ToString();

        public string HowItStands => _standing.ToString();

        /// <summary>Who this errand was cut for, said when it was handed out. Empty when it
        /// was cut for whoever showed up first: the elder can still do that, and errands
        /// written down before anybody was named have nothing here.
        ///
        /// It is not a promise: whoever takes the errand up is written in TakenBy, and the
        /// two can differ -- somebody else can end up carrying Sonia's errand.</summary>
        public string MeantFor { get; } = string.Empty;

        /// <summary>How many cards the elder set aside for whoever takes this errand up.
        /// Zero when none were spoken for.</summary>
        public int CardsMeant { get; }

        public string TakenBy { get; private set; } = string.Empty;

        public Merchant Merchant { get; private set; }

        public bool IsOnOffer => _standing == ErrandStanding.Offered;

        public bool IsTaken => _standing == ErrandStanding.Taken;

        public bool WasSentHome => _standing == ErrandStanding.SentHome;

        internal bool Is(Chore chore) => _chore == chore;

        internal void TakenUpBy(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "somebody with a name takes up an errand", nameof(name));

            if (_standing == ErrandStanding.SentHome)
                throw new VillageRuleException(
                    "the errand marked " + Mark
                    + " was taken back, so the elder has to hand out another one");

            if (_standing == ErrandStanding.Taken)
            {
                if (string.Equals(TakenBy, name.Trim(), StringComparison.OrdinalIgnoreCase)) return;

                throw new VillageRuleException(
                    "the errand marked " + Mark + " is already in the hands of " + TakenBy);
            }

            TakenBy = name.Trim();
            _standing = ErrandStanding.Taken;

            if (_chore == Chore.Selling) Merchant = new Merchant(TakenBy);
        }

        internal void SendHome()
        {
            if (_standing == ErrandStanding.SentHome)
                throw new VillageRuleException(
                    "the errand marked " + Mark + " was already taken back");

            _standing = ErrandStanding.SentHome;
        }

        public override string ToString()
        {
            if (_standing == ErrandStanding.Offered) return _chore + " on the table";

            if (_standing == ErrandStanding.Taken) return _chore + " in the hands of " + TakenBy;

            return TakenBy.Length == 0
                ? _chore + ", taken back before anybody picked it up"
                : _chore + ", " + TakenBy + " was sent home";
        }
    }
}
