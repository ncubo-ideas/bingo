using System;
using System.Globalization;

namespace Village
{
    internal enum Custody
    {
        InTheBook,
        WithMerchant,
        Sold
    }

    public sealed class Participation : IEquatable<Participation>
    {
        private Custody _custody;

        internal Participation(long number)
        {
            if (number <= 0)
                throw new ArgumentOutOfRangeException(nameof(number), number,
                    "a participation number must be greater than zero");

            Number = number;
            _custody = Custody.InTheBook;
        }

        public long Number { get; }

        public string Whereabouts => _custody.ToString();

        public bool IsSold => _custody == Custody.Sold;

        public Merchant HeldBy { get; private set; }

        public Merchant SoldBy { get; private set; }

        public Villager Buyer { get; private set; }

        internal void HandTo(Merchant merchant)
        {
            if (_custody == Custody.Sold)
                throw new VillageRuleException(
                    "the number " + Number + " is already sold, it cannot be handed to anybody");

            if (_custody == Custody.WithMerchant)
                throw new VillageRuleException(
                    "the number " + Number + " is already with " + HeldBy.Name);

            HeldBy = merchant;
            _custody = Custody.WithMerchant;
        }

        internal void GoBackToTheBook()
        {
            if (_custody != Custody.WithMerchant)
                throw new VillageRuleException(
                    "the number " + Number + " is not with anybody, so it cannot be given back");

            HeldBy = null;
            _custody = Custody.InTheBook;
        }

        internal void PassTo(Merchant from, Merchant to)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));

            if (_custody == Custody.Sold)
                throw new VillageRuleException(
                    "the number " + Number + " is already sold, it cannot be passed on");

            if (!ReferenceEquals(HeldBy, from))
                throw new VillageRuleException(
                    from.Name + " is not holding the number " + Number + ", so it cannot be passed on");

            HeldBy = to;
        }

        /// <summary>La venta no ocurrio: el numero vuelve al libro y queda sin dueno.
        /// Al LIBRO y no a una mano, porque quien lo vendio pudo haberse ido -- y porque
        /// una venta de puerta no tuvo vendedor a quien devolverselo.</summary>
        internal void GoBackToTheBookUndoingTheSale()
        {
            if (_custody != Custody.Sold)
                throw new VillageRuleException(
                    "the number " + Number + " was not sold, so there is nothing to undo");

            Buyer = null;
            SoldBy = null;
            HeldBy = null;
            _custody = Custody.InTheBook;
        }

        internal void GoBackToTheMerchant(Merchant merchant)
        {
            if (merchant == null) throw new ArgumentNullException(nameof(merchant));

            if (_custody != Custody.Sold)
                throw new VillageRuleException(
                    "the number " + Number + " was not sold, so nobody can bring it back");

            Buyer = null;
            SoldBy = null;
            HeldBy = merchant;
            _custody = Custody.WithMerchant;
        }

        internal void SellTo(Villager buyer, Merchant soldBy)
        {
            if (buyer == null) throw new ArgumentNullException(nameof(buyer));

            if (_custody == Custody.Sold)
                throw new VillageRuleException(
                    "the number " + Number + " was already sold to " + Buyer.Name);

            Buyer = buyer;
            SoldBy = soldBy;
            HeldBy = null;
            _custody = Custody.Sold;
        }

        public bool Equals(Participation other) => other != null && other.Number == Number;

        public override bool Equals(object obj) => Equals(obj as Participation);

        public override int GetHashCode() => Number.GetHashCode();

        public override string ToString() => Number.ToString(CultureInfo.InvariantCulture);
    }
}
