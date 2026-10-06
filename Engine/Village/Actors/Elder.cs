using System;
using System.Collections.Generic;

namespace Village
{
    public sealed class Elder
    {
        private readonly List<Festival> _governed = new List<Festival>();

        public Elder(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("an elder needs a name", nameof(name));

            Name = name;
        }

        public string Name { get; }

        public int FestivalCount => _governed.Count;

        public Festival StandingAt { get; private set; }

        public Festival CreateFestival(string name, CalendarDay day, CalendarDay today)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("a festival needs a name", nameof(name));
            if (day == null) throw new ArgumentNullException(nameof(day));
            if (today == null) throw new ArgumentNullException(nameof(today));

            if (day.IsBefore(today))
                throw new VillageRuleException(
                    "a festival cannot be announced for a day that already passed: " + day);

            if (AlreadyGoverns(name))
                throw new VillageRuleException("this elder already has a festival named " + name);

            Festival festival = new Festival(this, name, day);
            _governed.Add(festival);
            StandingAt = festival;
            return festival;
        }

        public void PutUpForSale(Festival festival, IEnumerable<long> numbersTheGameGaveUs)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.PutUpForSale(numbersTheGameGaveUs);
        }

        public void HandTo(Festival festival, Merchant merchant, IEnumerable<long> numbers)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.HandTo(merchant, numbers);
        }

        /// <summary>Every card at this festival costs the same, and this is where the
        /// elder says how much. It can be said again later: the price is one number for
        /// the whole festival, not a history per sale.</summary>
        public void PriceCardsAt(Festival festival, Price price)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.PriceCardsAt(price);
        }

        /// <summary>At what hour the doors open. The day already lives in the calendar;
        /// this is the rest of what one tells a neighbour.</summary>
        public void OpenTheDoorsAt(Festival festival, ClockTime doors)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.OpenTheDoorsAt(doors);
        }

        /// <summary>How wins come to light: the person shouts it ('OnClaim') or the
        /// table halts itself when a card completes ('Automatic'). Takes hold from the
        /// next round the elder begins, because the mode is set when a round starts.</summary>
        public void AnnounceWinsBy(Festival festival, WinsAnnouncement how)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.AnnounceWinsBy(how);
        }

        /// <summary>An errand cut for whoever shows up first. It stays because journals
        /// written before errands had a name on them call it exactly like this.</summary>
        public Errand Entrust(Festival festival, string doing, string mark)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            return festival.Entrust(doing, mark);
        }

        /// <summary>An errand cut for a named person, with the cards set aside for them.
        /// Several can be on the table at once: the elder writes one per person and hands
        /// each one out, instead of cutting the next only after somebody took the last.
        /// </summary>
        public Errand Entrust(Festival festival, string doing, string mark,
            string meantFor, int cardsMeant)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            return festival.Entrust(doing, mark, meantFor, cardsMeant);
        }

        public void HandTheErrandTo(Festival festival, Errand errand, string name)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.HandTheErrandTo(errand, name);
        }

        public void SendHome(Festival festival, Errand errand)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.SendHome(errand);
        }

        /// <summary>Darle efecto a lo que le plantearon: la venta se deshace y el numero
        /// vuelve al libro, listo para entregarselo a otra persona.</summary>
        public void Grant(Festival festival, Plea plea)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.Grant(plea);
        }

        /// <summary>Decir que no. La venta queda como estaba, y quien quiera puede
        /// volver a plantearlo.</summary>
        public void Refuse(Festival festival, Plea plea)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.Refuse(plea);
        }

        public void SellTo(Festival festival, Participation card, Villager buyer)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.SellFromTheBook(card, buyer);
        }

        public void AppointWarden(Festival festival, Warden warden)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.Appoint(warden);
        }

        public void DismissWarden(Festival festival)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.DismissWarden();
        }

        public Prize OfferPrize(Festival festival, string what)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            return festival.OfferPrize(what);
        }

        public void MarkAsMainPrize(Festival festival, Prize prize)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.MarkAsMainPrize(prize);
        }

        public Raffle StartRaffle(Festival festival, IEnumerable<int> prizePositions)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            return festival.StartRaffle(prizePositions);
        }

        public Round AnnounceRound(Festival festival)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            return festival.AnnounceRound();
        }

        /// <summary>Prometer un premio por UNA figura de la ultima ronda anunciada:
        /// el premio vive en la figura, no en la ronda.</summary>
        public Promise Promise(Festival festival, string playing, string form, string prize)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            return festival.MakePromise(playing, form, prize);
        }

        public void BeginRound(Festival festival)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.BeginRound();
        }

        /// <summary>El bingo ya lo dio por bueno el juego; aqui el pueblo deja escrito
        /// quien se lleva el premio de ESA figura. Si son varios, el reparto es asunto
        /// de ellos.</summary>
        public void NameWinner(Festival festival, Participation card, Promise promise)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.NameWinner(card, promise);
        }

        /// <summary>Darla por resuelta, con o sin ganadores: con alguno quedo jugada,
        /// sin ninguno quedo cancelada y el premio sin entregar.</summary>
        public void SettleRound(Festival festival)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.SettleRound();
        }

        public void CloseFestival(Festival festival)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            festival.Close();
        }

        public Festival FestivalAt(int position)
        {
            if (position < 1 || position > _governed.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "this elder governs " + _governed.Count + " festivals");

            return _governed[position - 1];
        }

        public bool Governs(Festival festival) => festival != null && _governed.Contains(festival);

        public void StandAt(Festival festival)
        {
            if (festival == null) throw new ArgumentNullException(nameof(festival));

            MustGovern(festival);
            StandingAt = festival;
        }

        private void MustGovern(Festival festival)
        {
            if (!Governs(festival))
                throw new VillageRuleException(
                    Name + " does not govern the festival named " + festival.Name);
        }

        private bool AlreadyGoverns(string name)
        {
            foreach (Festival festival in _governed)
                if (string.Equals(festival.Name, name, StringComparison.Ordinal))
                    return true;

            return false;
        }
    }
}
