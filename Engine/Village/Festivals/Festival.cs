using System;
using System.Collections.Generic;

namespace Village
{
    internal enum FestivalState
    {
        Announced,
        Open,
        Closed,
        Cancelled
    }

    public sealed class Festival
    {
        private readonly SortedDictionary<long, Participation> _participations =
            new SortedDictionary<long, Participation>();

        private readonly List<Prize> _prizes = new List<Prize>();

        private readonly List<Raffle> _raffles = new List<Raffle>();

        private readonly List<Errand> _errands = new List<Errand>();

        private readonly List<Plea> _pleas = new List<Plea>();

        private readonly List<Round> _rounds = new List<Round>();

        private FestivalState _state;

        internal Festival(Elder elder, string name, CalendarDay day)
        {
            Elder = elder;
            Name = name;
            Day = day;
            _state = FestivalState.Announced;
        }

        public Elder Elder { get; }

        public string Name { get; }

        public CalendarDay Day { get; }

        public string State => _state.ToString();

        public int ParticipationCount => _participations.Count;

        public int DaysUntil(CalendarDay today)
        {
            if (today == null) throw new ArgumentNullException(nameof(today));

            return today.DaysUntil(Day);
        }

        public bool IsUpForSale(long number) => _participations.ContainsKey(number);

        public Participation ParticipationNumbered(long number)
        {
            Participation participation;
            if (!_participations.TryGetValue(number, out participation))
                throw new VillageRuleException(
                    "the number " + number + " was never put up for sale at " + Name);

            return participation;
        }

        public Participation ParticipationAt(int position)
        {
            if (position < 1 || position > _participations.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    Name + " has " + _participations.Count + " participations up for sale");

            int walked = 0;
            foreach (Participation participation in _participations.Values)
            {
                walked++;
                if (walked == position) return participation;
            }

            throw new InvalidOperationException("unreachable");
        }

        public int SoldCount
        {
            get
            {
                int sold = 0;
                foreach (Participation participation in _participations.Values)
                    if (participation.IsSold) sold++;

                return sold;
            }
        }

        public long SoldNumberAt(int position)
        {
            if (position < 1)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "positions start at 1");

            int walked = 0;
            foreach (Participation participation in _participations.Values)
            {
                if (!participation.IsSold) continue;

                walked++;
                if (walked == position) return participation.Number;
            }

            throw new ArgumentOutOfRangeException(nameof(position), position,
                Name + " has sold " + walked + " participations");
        }

        public IReadOnlyList<long> NumbersWith(Merchant merchant)
        {
            if (merchant == null) throw new ArgumentNullException(nameof(merchant));

            List<long> theirs = new List<long>();

            foreach (Participation participation in _participations.Values)
            {
                if (ReferenceEquals(participation.HeldBy, merchant)) theirs.Add(participation.Number);
            }

            theirs.Sort();

            return theirs;
        }

        public IReadOnlyList<long> NumbersSoldBy(Merchant merchant)
        {
            if (merchant == null) throw new ArgumentNullException(nameof(merchant));

            List<long> theirs = new List<long>();

            foreach (Participation participation in _participations.Values)
            {
                if (ReferenceEquals(participation.SoldBy, merchant)) theirs.Add(participation.Number);
            }

            theirs.Sort();

            return theirs;
        }

        /// <summary>Cuantos cartones lleva comprados quien trae esta marca. Preguntar por
        /// el comprador como objeto no sirve para esto: cada venta crea un vecino nuevo,
        /// asi que dos compras de la misma persona son dos compradores distintos. La marca
        /// es lo unico que las une.</summary>
        public int CountBoughtBy(string mark) => NumbersBoughtBy(mark).Count;

        /// <summary>El carton numero 'position' (contando desde 1) de quien trae esta
        /// marca. Va de a uno porque print no sabe serializar una coleccion.</summary>
        public long BoughtByAt(string mark, int position)
        {
            IReadOnlyList<long> theirs = NumbersBoughtBy(mark);

            if (position < 1 || position > theirs.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "whoever is marked " + mark + " has bought " + theirs.Count + " participations");

            return theirs[position - 1];
        }

        public IReadOnlyList<long> NumbersBoughtBy(string mark)
        {
            List<long> theirs = new List<long>();

            if (string.IsNullOrWhiteSpace(mark)) return theirs;

            string looking = mark.Trim();

            foreach (Participation participation in _participations.Values)
            {
                if (!participation.IsSold) continue;
                if (participation.Buyer == null) continue;

                if (string.Equals(participation.Buyer.Mark, looking, StringComparison.Ordinal))
                    theirs.Add(participation.Number);
            }

            theirs.Sort();

            return theirs;
        }

        public int CountInTheBook => NumbersInTheBook().Count;

        public IReadOnlyList<long> NumbersInTheBook()
        {
            List<long> loose = new List<long>();

            foreach (Participation participation in _participations.Values)
            {
                if (participation.HeldBy == null && !participation.IsSold)
                    loose.Add(participation.Number);
            }

            loose.Sort();

            return loose;
        }

        /// <summary>
        /// Los cartones que ese papelito tiene en la mano. Vacio si el papelito no es de
        /// vender, o si no existe.
        ///
        /// Existe para que se pueda PREGUNTAR. `NumbersWith(MerchantMarked(marca))` es la
        /// forma de siempre y tira dos veces: si la marca no esta, y si el encargo no es de
        /// vender. Lo segundo mordio tres veces en la app, siempre igual -- alguien recorre
        /// a toda la gente o a todos los encargos y le toca uno que canta. Y no falla
        /// contestando vacio: revienta el query entero, asi que se cae el recorrido completo
        /// y con el lo que estaba armando. La ultima vez el organizador perdio un bingo al
        /// arrancar y dijo que el village estaba vacio.
        ///
        /// Quien canta no tiene cartones, y eso es una respuesta, no un error.
        /// </summary>
        public IReadOnlyList<long> NumbersHeldBy(string mark)
        {
            Merchant merchant = MerchantHoldingOrNull(mark);

            return merchant == null ? new List<long>() : NumbersWith(merchant);
        }

        /// <summary>Lo que ese papelito vendio. Vacio donde NumbersHeldBy contesta vacio, y
        /// por lo mismo.</summary>
        public IReadOnlyList<long> NumbersSoldUnder(string mark)
        {
            Merchant merchant = MerchantHoldingOrNull(mark);

            return merchant == null ? new List<long>() : NumbersSoldBy(merchant);
        }

        /// <summary>Todo lo vendido, sin importar quien lo vendio. NumbersSoldBy contesta por
        /// comerciante y se queda corto: el elder tambien vende del libro, y esas ventas no
        /// son de nadie. Esta es la lista que hace falta para reconstruir de una sola lectura
        /// quien compro que.</summary>
        public IReadOnlyList<long> NumbersSold()
        {
            List<long> sold = new List<long>();

            foreach (Participation participation in _participations.Values)
                if (participation.IsSold) sold.Add(participation.Number);

            sold.Sort();

            return sold;
        }

        public IReadOnlyList<long> NumbersUpForSale()
        {
            List<long> all = new List<long>(_participations.Keys);

            all.Sort();

            return all;
        }

        public int CountWith(Merchant merchant)
        {
            if (merchant == null) throw new ArgumentNullException(nameof(merchant));

            int held = 0;
            foreach (Participation participation in _participations.Values)
                if (ReferenceEquals(participation.HeldBy, merchant)) held++;

            return held;
        }

        public int CountSoldBy(Merchant merchant)
        {
            if (merchant == null) throw new ArgumentNullException(nameof(merchant));

            int sold = 0;
            foreach (Participation participation in _participations.Values)
                if (participation.IsSold && ReferenceEquals(participation.SoldBy, merchant)) sold++;

            return sold;
        }

        public int CountSoldTo(Villager buyer)
        {
            if (buyer == null) throw new ArgumentNullException(nameof(buyer));

            int bought = 0;
            foreach (Participation participation in _participations.Values)
                if (ReferenceEquals(participation.Buyer, buyer)) bought++;

            return bought;
        }

        public Warden Warden { get; private set; }

        public bool HasWarden => Warden != null;

        internal void Appoint(Warden warden)
        {
            if (warden == null) throw new ArgumentNullException(nameof(warden));

            Warden = warden;
        }

        internal void DismissWarden()
        {
            if (Warden == null)
                throw new VillageRuleException(
                    "nobody was appointed to key in at " + Name);

            Warden = null;
        }

        public int PleaCount => _pleas.Count;

        public Plea PleaAt(int position)
        {
            if (position < 1 || position > _pleas.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    Name + " has " + _pleas.Count + " pleas");

            return _pleas[position - 1];
        }

        /// <summary>Las ventas que estan esperando que el elder resuelva.</summary>
        public IReadOnlyList<long> NumbersPleaded()
        {
            List<long> waiting = new List<long>();

            foreach (Plea plea in _pleas)
                if (plea.IsWaiting) waiting.Add(plea.Number);

            waiting.Sort();

            return waiting;
        }

        internal Plea Plead(Merchant merchant, Participation card)
        {
            MustStillBeGoing();

            if (merchant == null) throw new ArgumentNullException(nameof(merchant));

            Participation participation = Owned(card);
            long number = card.Number;

            if (!participation.IsSold)
                throw new VillageRuleException(
                    "the number " + number + " was not sold, so there is nothing to undo");

            foreach (Plea already in _pleas)
            {
                if (already.Number == number && already.IsWaiting)
                    throw new VillageRuleException(
                        "somebody already asked to undo the sale of " + number
                        + ", and the elder has not settled it yet");
            }

            Plea plea = new Plea(_pleas.Count + 1, number, merchant.Name);
            _pleas.Add(plea);
            return plea;
        }

        internal void Grant(Plea plea)
        {
            Owned(plea);

            ParticipationNumbered(plea.Number).GoBackToTheBookUndoingTheSale();

            plea.Grant();
        }

        internal void Refuse(Plea plea) => Owned(plea).Refuse();

        public int ErrandCount => _errands.Count;

        public Errand ErrandAt(int position)
        {
            if (position < 1 || position > _errands.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    Name + " has handed out " + _errands.Count + " errands");

            return _errands[position - 1];
        }

        public Errand ErrandMarked(string mark)
        {
            if (string.IsNullOrWhiteSpace(mark))
                throw new ArgumentException("an errand is looked up by its mark", nameof(mark));

            string looked = mark.Trim();

            foreach (Errand errand in _errands)
                if (string.Equals(errand.Mark, looked, StringComparison.Ordinal)) return errand;

            throw new VillageRuleException(
                "no errand marked " + looked + " was handed out at " + Name);
        }

        public Errand ErrandOnOfferFor(string doing)
        {
            Chore chore = ChoreFrom(doing);

            foreach (Errand errand in _errands)
                if (errand.IsOnOffer && errand.Is(chore)) return errand;

            return null;
        }

        public string MarkOnOfferFor(string doing)
        {
            Errand errand = ErrandOnOfferFor(doing);

            return errand == null ? string.Empty : errand.Mark;
        }

        public Merchant MerchantMarked(string mark)
        {
            Errand errand = ErrandMarked(mark);

            if (errand.Merchant == null)
                throw new VillageRuleException(
                    "the errand marked " + errand.Mark + " was not handed out to sell, "
                    + "so there is no merchant to hold anything with it");

            return errand.Merchant;
        }

        /// <summary>
        /// Whether the errand marked <paramref name="mark"/> is this person's, still standing,
        /// and handed out for the chore they are about to do.
        ///
        /// It answers false where the other lookups throw. That is the whole point: this is
        /// meant to be asked as a guard before a command runs, and a guard has to be able to
        /// say no. An unknown mark, one already sent home, a name that does not match or a
        /// chore the errand was not handed out for are all plain noes, not accidents.
        /// </summary>
        public bool ErrandLetsThemDo(string mark, string name, string chore)
        {
            if (string.IsNullOrWhiteSpace(mark)) return false;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (string.IsNullOrWhiteSpace(chore)) return false;

            string looked = mark.Trim();

            foreach (Errand errand in _errands)
            {
                if (!string.Equals(errand.Mark, looked, StringComparison.Ordinal)) continue;

                return errand.IsTaken
                    && string.Equals(errand.TakenBy, name.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(errand.Doing, chore.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Whether the merchant marked <paramref name="mark"/> is holding every one of
        /// <paramref name="numbers"/>, with none of them sold and none of them named twice.
        ///
        /// These are the same conditions TakeBackFrom enforces, asked instead of thrown, so a
        /// guard can turn them down before the command takes the write lock. Kept beside the
        /// verb on purpose: the day the verb learns a new condition, this has to learn it too.
        /// </summary>
        public bool MerchantCanGiveBack(string mark, IEnumerable<long> numbers)
        {
            Merchant merchant = MerchantHoldingOrNull(mark);

            return merchant != null && EveryOneIsHeldBy(merchant, numbers);
        }

        /// <summary>
        /// Whether the merchant marked <paramref name="mark"/> can hand every one of
        /// <paramref name="numbers"/> over to the one marked <paramref name="toMark"/>.
        ///
        /// Same conditions as PassBetweenMerchants, asked instead of thrown. Nobody passes
        /// anything to themselves, and the festival has to still be going.
        /// </summary>
        public bool MerchantCanPassTo(string mark, string toMark, IEnumerable<long> numbers)
        {
            if (_state == FestivalState.Closed) return false;

            Merchant from = MerchantHoldingOrNull(mark);
            Merchant to = MerchantHoldingOrNull(toMark);

            if (from == null || to == null) return false;
            if (ReferenceEquals(from, to)) return false;

            return EveryOneIsHeldBy(from, numbers);
        }

        /// <summary>
        /// Whether the merchant marked <paramref name="mark"/> can sell every one of
        /// <paramref name="numbers"/>: they are up for sale, in her hand, and none of them
        /// already sold.
        ///
        /// Same conditions as SellWhatIsHeldBy, asked instead of thrown. This one is asked in
        /// an odd moment and that is the point of it existing: a sale made while the elder was
        /// away is kept and offered later, and by then the world may have moved. It answers
        /// whether it still stands, without leaving a journal entry for a sale that does not.
        /// </summary>
        public bool MerchantCanSellThem(string mark, IEnumerable<long> numbers)
        {
            if (_state == FestivalState.Closed) return false;

            Errand errand = ErrandMarkedOrNull(mark);

            // Que la hayan mandado a la casa no le saca los cartones de la mano por si solo:
            // el elder recoge primero y revoca despues, y si la recogida fallara quedaria
            // revocada con cartones encima. Una venta que llega tarde no se apoya en eso.
            if (errand == null || !errand.IsTaken) return false;

            return errand.Merchant != null && EveryOneIsHeldBy(errand.Merchant, numbers);
        }

        /// <summary>
        /// Whether the sale of <paramref name="number"/> can still be pleaded against: it was
        /// put up for sale, it is sold, and nobody is already waiting on an answer for it.
        ///
        /// Same conditions as Plead, asked instead of thrown, so a guard can turn the plea
        /// down without leaving a journal entry behind.
        /// </summary>
        public bool CanPleadToUndo(long number)
        {
            if (_state == FestivalState.Closed) return false;

            Participation participation;
            if (!_participations.TryGetValue(number, out participation)) return false;

            if (!participation.IsSold) return false;

            foreach (Plea already in _pleas)
                if (already.Number == number && already.IsWaiting) return false;

            return true;
        }

        /// <summary>
        /// Whether the merchant marked <paramref name="mark"/> can swap the sold
        /// <paramref name="soldNumber"/> for <paramref name="replacementNumber"/> out of their
        /// own hand.
        ///
        /// Same conditions as ExchangeSoldFor, asked instead of thrown. Note the two halves
        /// pull opposite ways: the one leaving has to BE sold, the one coming in has to be
        /// held and NOT sold.
        /// </summary>
        public bool MerchantCanExchange(string mark, long soldNumber, long replacementNumber)
        {
            if (_state == FestivalState.Closed) return false;
            if (soldNumber == replacementNumber) return false;

            Merchant merchant = MerchantHoldingOrNull(mark);

            if (merchant == null) return false;

            Participation sold;
            if (!_participations.TryGetValue(soldNumber, out sold)) return false;
            if (!sold.IsSold) return false;

            Participation replacement;
            if (!_participations.TryGetValue(replacementNumber, out replacement)) return false;
            if (replacement.IsSold) return false;

            return ReferenceEquals(replacement.HeldBy, merchant);
        }

        /// <summary>The merchant behind a mark, or null where MerchantMarked would throw.</summary>
        private Merchant MerchantHoldingOrNull(string mark) => ErrandMarkedOrNull(mark)?.Merchant;

        /// <summary>The errand behind a mark, or null where ErrandMarked would throw.</summary>
        private Errand ErrandMarkedOrNull(string mark)
        {
            if (string.IsNullOrWhiteSpace(mark)) return null;

            string looked = mark.Trim();

            foreach (Errand errand in _errands)
                if (string.Equals(errand.Mark, looked, StringComparison.Ordinal))
                    return errand;

            return null;
        }

        /// <summary>Every number named once, none sold, all of them in that merchant's hand.
        /// An empty batch is false: asking to move nothing is not a thing anyone means.</summary>
        private bool EveryOneIsHeldBy(Merchant merchant, IEnumerable<long> numbers)
        {
            if (numbers == null) return false;

            HashSet<long> seen = new HashSet<long>();

            foreach (long number in numbers)
            {
                if (!seen.Add(number)) return false;

                Participation participation;
                if (!_participations.TryGetValue(number, out participation)) return false;

                if (participation.IsSold) return false;
                if (!ReferenceEquals(participation.HeldBy, merchant)) return false;
            }

            return seen.Count > 0;
        }

        public bool ErrandBelongsTo(string mark, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            Errand errand = ErrandMarked(mark);

            return errand.IsTaken
                && string.Equals(errand.TakenBy, name.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public string WhatWasEntrustedTo(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            string looked = name.Trim();

            for (int position = _errands.Count - 1; position >= 0; position--)
            {
                Errand errand = _errands[position];

                if (!string.Equals(errand.TakenBy, looked, StringComparison.OrdinalIgnoreCase))
                    continue;

                return errand.IsTaken ? errand.Doing : null;
            }

            return null;
        }

        internal Errand Entrust(string doing, string mark) => Entrust(doing, mark, null, 0);

        internal Errand Entrust(string doing, string mark, string meantFor, int cardsMeant)
        {
            MustStillBeGoing();

            Chore chore = ChoreFrom(doing);

            if (string.IsNullOrWhiteSpace(mark))
                throw new ArgumentException("an errand needs a mark on it", nameof(mark));

            string written = mark.Trim();

            foreach (Errand already in _errands)
                if (string.Equals(already.Mark, written, StringComparison.Ordinal))
                    throw new VillageRuleException(
                        "the mark " + written + " is already on another errand at " + Name);

            Errand errand = new Errand(_errands.Count + 1, chore, written, meantFor, cardsMeant);
            _errands.Add(errand);
            return errand;
        }

        /// <summary>The errand cut for this name and still on the table. Null when nobody
        /// was named, or when the one that was is already in somebody's hands: the elder
        /// hands out one errand per person, so a second one for the same name is a second
        /// errand, not this one again.</summary>
        public Errand ErrandOnOfferMeantFor(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            string looked = name.Trim();

            foreach (Errand errand in _errands)
                if (errand.IsOnOffer
                    && string.Equals(errand.MeantFor, looked, StringComparison.OrdinalIgnoreCase))
                    return errand;

            return null;
        }

        /// <summary>The mark of that errand, empty when there is none. It sits beside the
        /// one that returns the errand for the same reason MarkOnOfferFor does: whoever
        /// only wants the mark should not have to hold a nothing and ask it for one.
        /// </summary>
        public string MarkOnOfferMeantFor(string name)
        {
            Errand errand = ErrandOnOfferMeantFor(name);

            return errand == null ? string.Empty : errand.Mark;
        }

        internal void HandTheErrandTo(Errand errand, string name) => Owned(errand).TakenUpBy(name);

        internal void SendHome(Errand errand) => Owned(errand).SendHome();

        private Errand Owned(Errand errand)
        {
            if (errand == null) throw new ArgumentNullException(nameof(errand));

            if (!_errands.Contains(errand))
                throw new VillageRuleException(
                    "no errand marked " + errand.Mark + " was handed out at " + Name);

            return errand;
        }

        private Plea Owned(Plea plea)
        {
            if (plea == null) throw new ArgumentNullException(nameof(plea));

            if (!_pleas.Contains(plea))
                throw new VillageRuleException(
                    "plea " + plea.Position + " was not raised at " + Name);

            return plea;
        }

        private Participation Owned(Participation card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));

            if (!_participations.TryGetValue(card.Number, out Participation mine) || !ReferenceEquals(mine, card))
                throw new VillageRuleException(
                    "the number " + card.Number + " was never put up for sale at " + Name);

            return card;
        }

        public int RoundCount => _rounds.Count;

        /// <summary>
        /// El pulso de la funcion: lo que puede cambiar lo que ve el organizador, en un
        /// solo dato, para que el reloj de su pantalla pregunte BARATO antes de preguntar
        /// caro. Va aparte del pulso de la mesa porque son dos dominios: la mesa sabe de
        /// bolitas y cartones, la funcion sabe de rondas, premios y reclamos.
        ///
        /// Los premios entregados se cuentan y no se listan: alcanza con que el numero
        /// cambie para que valga la pena volver a mirar.
        /// </summary>
        public string Pulse
        {
            get
            {
                int kept = 0;
                int winners = 0;
                int promises = 0;

                foreach (Round round in _rounds)
                {
                    promises += round.PromiseCount;
                    winners += round.WinnerCount;

                    for (int position = 1; position <= round.PromiseCount; position++)
                        if (round.PromiseAt(position).WasKept) kept++;
                }

                return _state
                    + "|" + _rounds.Count
                    + "|" + CurrentRoundPosition
                    + "|" + promises
                    + "|" + kept
                    + "|" + winners
                    + "|" + _pleas.Count
                    + "|" + SoldCount;
            }
        }

        public Round RoundAt(int position)
        {
            if (position < 1 || position > _rounds.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    Name + " has announced " + _rounds.Count + " rounds");

            return _rounds[position - 1];
        }

        public int CurrentRoundPosition
        {
            get
            {
                foreach (Round round in _rounds)
                    if (round.IsPlaying) return round.Position;

                return 0;
            }
        }

        public bool IsOver => _state == FestivalState.Closed;

        /// <summary>What one card costs, in whole money. Zero until the elder prices
        /// them: a festival can open and even sell before anybody said a number, and
        /// zero is how the screens know there is nothing to add up yet.</summary>
        public long CardPrice { get; private set; }

        internal void PriceCardsAt(Price price)
        {
            MustStillBeGoing();

            if (price == null) throw new ArgumentNullException(nameof(price));

            CardPrice = price.Amount;
        }

        /// <summary>The hour the doors open, written as HH:mm. A day is all the calendar
        /// knows, and a day is enough for every rule here -- but nobody tells a neighbour
        /// "the bingo is Saturday" and stops there. Empty until the elder says it.</summary>
        public string DoorsOpenAt { get; private set; } = string.Empty;

        internal void OpenTheDoorsAt(ClockTime doors)
        {
            MustStillBeGoing();

            if (doors == null) throw new ArgumentNullException(nameof(doors));

            DoorsOpenAt = doors.ToString();
        }

        /// <summary>How a win comes to light at this festival: 'OnClaim' when the person
        /// shouts it and the elder verifies, 'Automatic' when the table halts itself the
        /// moment a card completes. Empty until the elder chooses; the game falls back
        /// to whatever it did before anybody could choose.</summary>
        public string WinsAnnounced { get; private set; } = string.Empty;

        internal void AnnounceWinsBy(WinsAnnouncement how)
        {
            MustStillBeGoing();

            if (how == null) throw new ArgumentNullException(nameof(how));

            WinsAnnounced = how.How;
        }

        internal Round AnnounceRound()
        {
            MustStillBeGoing();

            Round round = new Round(_rounds.Count + 1);
            _rounds.Add(round);
            return round;
        }

        /// <summary>El premio vive en la figura, no en la ronda: cada promesa dice que
        /// figura se juega y que se da al que la complete. Se promete sobre la ULTIMA
        /// ronda anunciada, mientras siga sin arrancar.</summary>
        internal Promise MakePromise(string playing, string form, string prize)
        {
            MustStillBeGoing();

            if (string.IsNullOrWhiteSpace(playing))
                throw new ArgumentException("a promise needs a figure to play", nameof(playing));

            if (playing.Contains(","))
                throw new VillageRuleException("one figure per promise; promise each one with its own prize");

            if (string.IsNullOrWhiteSpace(prize))
                throw new ArgumentException("a promise needs a prize to give", nameof(prize));

            bool inCash = InCashFrom(form);

            for (int position = _rounds.Count - 1; position >= 0; position--)
            {
                if (!_rounds[position].IsPlanned) continue;

                return _rounds[position].MakePromise(playing, inCash, prize);
            }

            throw new VillageRuleException(
                "no round is on the table at " + Name + "; announce one before promising");
        }

        internal void BeginRound()
        {
            MustStillBeGoing();

            int playing = CurrentRoundPosition;
            if (playing != 0)
                throw new VillageRuleException(
                    "round " + playing + " is still playing at " + Name
                    + ", settle it before beginning another");

            foreach (Round round in _rounds)
            {
                if (!round.IsPlanned) continue;

                round.Begin();
                return;
            }

            throw new VillageRuleException(
                "no round is left to begin at " + Name + ", the elder has to announce one");
        }

        public Promise PromiseInPlayOn(string figure) => RoundThatIsPlaying().PromiseOn(figure);

        internal void NameWinner(Participation card, Promise promise)
        {
            Round round = RoundThatIsPlaying();
            long number = Owned(card).Number;

            if (!WasBought(number))
                throw new VillageRuleException(
                    "the number " + number + " was not bought by anybody, so it cannot win round "
                    + round.Position);

            round.NameWinner(promise, number);
        }

        internal void SettleRound() => RoundThatIsPlaying().Settle();

        internal void Close()
        {
            MustStillBeGoing();

            int playing = CurrentRoundPosition;
            if (playing != 0)
                throw new VillageRuleException(
                    "round " + playing + " is still playing at " + Name
                    + ", settle it before closing the festival");

            _state = FestivalState.Closed;
        }

        private Round RoundThatIsPlaying()
        {
            foreach (Round round in _rounds)
                if (round.IsPlaying) return round;

            throw new VillageRuleException("no round is playing at " + Name);
        }

        private void MustStillBeGoing()
        {
            if (_state == FestivalState.Closed)
                throw new VillageRuleException(Name + " is already over");
        }

        private bool InCashFrom(string form)
        {
            if (string.Equals(form, "Cash", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(form, "Goods", StringComparison.OrdinalIgnoreCase))
                return false;

            throw new VillageRuleException(
                "prizes at " + Name + " come in cash or in goods, not " + form);
        }

        private Chore ChoreFrom(string doing)
        {
            if (string.Equals(doing, "Selling", StringComparison.OrdinalIgnoreCase))
                return Chore.Selling;

            if (string.Equals(doing, "Calling", StringComparison.OrdinalIgnoreCase))
                return Chore.Calling;

            throw new VillageRuleException("nobody at " + Name + " does that: " + doing);
        }

        public int PrizeCount => _prizes.Count;

        public int MainPrizePosition
        {
            get
            {
                foreach (Prize prize in _prizes)
                    if (prize.IsMain) return prize.Position;

                return 0;
            }
        }

        public int GivenPrizeCount
        {
            get
            {
                int given = 0;
                foreach (Prize prize in _prizes)
                    if (prize.IsGiven) given++;

                return given;
            }
        }

        public int PendingPrizeCount => _prizes.Count - GivenPrizeCount;

        public int RaffleCount => _raffles.Count;

        public Raffle CurrentRaffle => _raffles.Count == 0 ? null : _raffles[_raffles.Count - 1];

        public Prize PrizeAt(int position)
        {
            if (position < 1 || position > _prizes.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    Name + " has " + _prizes.Count + " prizes on the table");

            return _prizes[position - 1];
        }

        public Raffle RaffleAt(int number)
        {
            if (number < 1 || number > _raffles.Count)
                throw new ArgumentOutOfRangeException(nameof(number), number,
                    Name + " has held " + _raffles.Count + " raffles");

            return _raffles[number - 1];
        }

        public bool WasBought(long number) =>
            _participations.ContainsKey(number) && _participations[number].IsSold;

        internal Prize OfferPrize(string what)
        {
            Prize prize = new Prize(_prizes.Count + 1, what);
            _prizes.Add(prize);
            return prize;
        }

        internal void MarkAsMainPrize(Prize chosen)
        {
            if (chosen == null) throw new ArgumentNullException(nameof(chosen));

            if (!_prizes.Contains(chosen))
                throw new VillageRuleException("the prize " + chosen.Position + " is not on the table at " + Name);

            foreach (Prize prize in _prizes)
                prize.LoseTheMainMark();

            chosen.MarkAsMain();
        }

        internal Raffle StartRaffle(IEnumerable<int> prizePositions)
        {
            if (prizePositions == null) throw new ArgumentNullException(nameof(prizePositions));

            if (CurrentRaffle != null && !CurrentRaffle.IsSettled)
                throw new VillageRuleException(
                    "raffle " + CurrentRaffle.Number + " still has prizes to give away");

            List<Prize> inPlay = new List<Prize>();
            foreach (int position in prizePositions)
            {
                Prize prize = PrizeAt(position);

                if (prize.IsGiven)
                    throw new VillageRuleException(prize.What + " was already given away");

                inPlay.Add(prize);
            }

            if (inPlay.Count == 0)
                throw new VillageRuleException("a raffle needs at least one prize in play");

            Raffle raffle = new Raffle(this, _raffles.Count + 1, inPlay);
            _raffles.Add(raffle);
            return raffle;
        }

        internal void HandTo(Merchant merchant, IEnumerable<long> numbers)
        {
            MustStillBeGoing();

            if (merchant == null) throw new ArgumentNullException(nameof(merchant));
            if (numbers == null) throw new ArgumentNullException(nameof(numbers));

            List<Participation> batch = new List<Participation>();
            HashSet<long> seen = new HashSet<long>();

            foreach (long number in numbers)
            {
                if (!seen.Add(number))
                    throw new VillageRuleException(
                        "the number " + number + " comes twice in the same batch");

                Participation participation = ParticipationNumbered(number);

                if (participation.IsSold)
                    throw new VillageRuleException(
                        "the number " + number + " is already sold, it cannot be handed to anybody");

                if (participation.HeldBy != null)
                    throw new VillageRuleException(
                        "the number " + number + " is already with " + participation.HeldBy.Name);

                batch.Add(participation);
            }

            foreach (Participation participation in batch)
                participation.HandTo(merchant);
        }

        internal void TakeBackFrom(Merchant merchant, IEnumerable<long> numbers)
        {
            if (merchant == null) throw new ArgumentNullException(nameof(merchant));
            if (numbers == null) throw new ArgumentNullException(nameof(numbers));

            List<Participation> batch = new List<Participation>();
            HashSet<long> seen = new HashSet<long>();

            foreach (long number in numbers)
            {
                if (!seen.Add(number))
                    throw new VillageRuleException(
                        "the number " + number + " comes twice in the same batch");

                Participation participation = ParticipationNumbered(number);

                if (!ReferenceEquals(participation.HeldBy, merchant))
                    throw new VillageRuleException(
                        merchant.Name + " is not holding the number " + number);

                batch.Add(participation);
            }

            foreach (Participation participation in batch)
                participation.GoBackToTheBook();
        }

        internal void SellWhatIsHeldBy(Merchant merchant, IEnumerable<long> numbers, Villager buyer)
        {
            MustStillBeGoing();

            if (merchant == null) throw new ArgumentNullException(nameof(merchant));
            if (numbers == null) throw new ArgumentNullException(nameof(numbers));
            if (buyer == null) throw new ArgumentNullException(nameof(buyer));

            List<Participation> batch = new List<Participation>();
            HashSet<long> seen = new HashSet<long>();

            foreach (long number in numbers)
            {
                if (!seen.Add(number))
                    throw new VillageRuleException(
                        "the number " + number + " comes twice in the same batch");

                Participation participation = ParticipationNumbered(number);

                if (!ReferenceEquals(participation.HeldBy, merchant))
                    throw new VillageRuleException(
                        merchant.Name + " cannot sell the number " + number + " without holding it");

                batch.Add(participation);
            }

            if (batch.Count == 0)
                throw new VillageRuleException(
                    merchant.Name + " did not say which numbers she sold");

            foreach (Participation participation in batch)
                participation.SellTo(buyer, merchant);
        }

        internal void PassBetweenMerchants(Merchant from, Merchant to, IEnumerable<long> numbers)
        {
            MustStillBeGoing();

            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (numbers == null) throw new ArgumentNullException(nameof(numbers));

            if (ReferenceEquals(from, to))
                throw new VillageRuleException(
                    from.Name + " cannot pass numbers to herself");

            List<Participation> batch = new List<Participation>();
            HashSet<long> seen = new HashSet<long>();

            foreach (long number in numbers)
            {
                if (!seen.Add(number))
                    throw new VillageRuleException(
                        "the number " + number + " comes twice in the same batch");

                Participation participation = ParticipationNumbered(number);

                if (participation.IsSold)
                    throw new VillageRuleException(
                        "the number " + number + " is already sold, it cannot be passed on");

                if (!ReferenceEquals(participation.HeldBy, from))
                    throw new VillageRuleException(
                        from.Name + " is not holding the number " + number
                        + ", so it cannot be passed on");

                batch.Add(participation);
            }

            foreach (Participation participation in batch)
                participation.PassTo(from, to);
        }

        internal void ExchangeSoldFor(Merchant merchant, Participation sold, Participation replacement)
        {
            MustStillBeGoing();

            if (merchant == null) throw new ArgumentNullException(nameof(merchant));

            long soldNumber = Owned(sold).Number;
            long replacementNumber = Owned(replacement).Number;

            if (soldNumber == replacementNumber)
                throw new VillageRuleException(
                    "the number " + soldNumber + " cannot be exchanged for itself");

            if (!sold.IsSold)
                throw new VillageRuleException(
                    "the number " + soldNumber + " was not sold, so there is nothing to exchange");

            if (!ReferenceEquals(replacement.HeldBy, merchant))
                throw new VillageRuleException(
                    merchant.Name + " cannot give away the number " + replacementNumber
                    + " without holding it");

            Villager buyer = sold.Buyer;

            sold.GoBackToTheMerchant(merchant);
            replacement.SellTo(buyer, merchant);
        }

        internal void SellFromTheBook(Participation card, Villager buyer)
        {
            MustStillBeGoing();

            Participation participation = Owned(card);
            long number = card.Number;

            if (participation.HeldBy != null)
                throw new VillageRuleException(
                    "the number " + number + " is out with " + participation.HeldBy.Name
                    + ", so it cannot be sold at the door");

            participation.SellTo(buyer, null);
        }

        internal void PutUpForSale(IEnumerable<long> numbers)
        {
            MustStillBeGoing();

            if (numbers == null) throw new ArgumentNullException(nameof(numbers));

            List<Participation> batch = new List<Participation>();
            HashSet<long> seen = new HashSet<long>();

            foreach (long number in numbers)
            {
                Participation participation = new Participation(number);

                if (!seen.Add(number))
                    throw new VillageRuleException(
                        "the number " + number + " comes twice in the same batch");

                if (_participations.ContainsKey(number))
                    throw new VillageRuleException(
                        "the number " + number + " is already up for sale at " + Name);

                batch.Add(participation);
            }

            foreach (Participation participation in batch)
                _participations.Add(participation.Number, participation);
        }
    }
}
