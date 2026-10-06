using System;
using System.Collections.Generic;

namespace Village
{
    public sealed class Merchant
    {
        public Merchant(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("a merchant needs a name", nameof(name));

            Name = name;
        }

        public string Name { get; }

        public void SellThemTo(Festival festival, IEnumerable<long> numbers, Villager buyer)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            festival.SellWhatIsHeldBy(this, numbers, buyer);
        }

        public void PassTo(Festival festival, Merchant other, IEnumerable<long> numbers)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            festival.PassBetweenMerchants(this, other, numbers);
        }

        public void ExchangeFor(Festival festival, Participation sold, Participation replacement)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            festival.ExchangeSoldFor(this, sold, replacement);
        }

        /// <summary>Plantearle al elder que una venta se deshaga. Ella no la deshace:
        /// la plantea. Puede ser sobre una venta que hizo otra -- en la puerta atiende
        /// la que este libre.</summary>
        public Plea Plead(Festival festival, Participation card)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            return festival.Plead(this, card);
        }

        public void GiveBack(Festival festival, IEnumerable<long> numbers)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            festival.TakeBackFrom(this, numbers);
        }

        public override string ToString() => Name;
    }
}
