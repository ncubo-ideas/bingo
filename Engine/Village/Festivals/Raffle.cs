using System;
using System.Collections.Generic;

namespace Village
{
    public sealed class Raffle
    {
        private readonly Festival _festival;
        private readonly List<Prize> _inPlay;

        internal Raffle(Festival festival, int number, List<Prize> inPlay)
        {
            _festival = festival;
            _inPlay = inPlay;
            Number = number;
        }

        public int Number { get; }

        public int PrizeCount => _inPlay.Count;

        public bool IsSettled
        {
            get
            {
                foreach (Prize prize in _inPlay)
                    if (!prize.IsGiven) return false;

                return true;
            }
        }

        public Prize PrizeAt(int position)
        {
            if (position < 1 || position > _inPlay.Count)
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "this raffle has " + _inPlay.Count + " prizes in play");

            return _inPlay[position - 1];
        }

        public void AssignPrizeTo(int prizePosition, long number)
        {
            Prize prize = InPlayAt(prizePosition);

            if (!_festival.WasBought(number))
                throw new VillageRuleException(
                    "the number " + number + " was not bought by anybody, so it cannot take "
                    + prize.What);

            prize.GiveTo(number);
        }

        private Prize InPlayAt(int prizePosition)
        {
            foreach (Prize prize in _inPlay)
                if (prize.Position == prizePosition) return prize;

            throw new VillageRuleException(
                "the prize at position " + prizePosition + " is not in play in raffle " + Number);
        }
    }
}
