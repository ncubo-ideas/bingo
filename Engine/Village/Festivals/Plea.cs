using System;

namespace Village
{
    internal enum PleaStanding
    {
        Raised,
        Granted,
        Refused
    }

    /// <summary>
    /// Algo que alguien le plantea al elder para que el resuelva. Hoy solo una cosa:
    /// que una venta se deshaga.
    ///
    /// Existe porque deshacer una venta no lo puede decidir quien la plantea. En la
    /// puerta atiende la que este libre, no siempre la que vendio, y el que responde
    /// por el talonario es el organizador. Asi que se plantea y se espera.
    /// </summary>
    public sealed class Plea
    {
        private PleaStanding _standing;

        internal Plea(int position, long number, string raisedBy)
        {
            Position = position;
            Number = number;
            RaisedBy = raisedBy;
            _standing = PleaStanding.Raised;
        }

        public int Position { get; }

        /// <summary>El numero de la venta que se quiere deshacer.</summary>
        public long Number { get; }

        public string RaisedBy { get; }

        public string HowItStands => _standing.ToString();

        public bool IsWaiting => _standing == PleaStanding.Raised;

        public bool WasGranted => _standing == PleaStanding.Granted;

        public bool WasRefused => _standing == PleaStanding.Refused;

        internal void Grant() => Settle(PleaStanding.Granted);

        internal void Refuse() => Settle(PleaStanding.Refused);

        private void Settle(PleaStanding how)
        {
            if (_standing != PleaStanding.Raised)
                throw new VillageRuleException(
                    "the plea about the number " + Number + " was already "
                    + _standing.ToString().ToLowerInvariant());

            _standing = how;
        }

        public override string ToString() =>
            _standing == PleaStanding.Raised
                ? RaisedBy + " asks to undo the sale of " + Number
                : RaisedBy + " asked to undo the sale of " + Number + ", and it was "
                  + _standing.ToString().ToLowerInvariant();
    }
}
