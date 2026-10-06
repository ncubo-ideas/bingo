using System;

namespace Village
{
    public sealed class Villager
    {
        public Villager(string name, string contact)
            : this(name, contact, string.Empty)
        {
        }

        /// <summary>
        /// El mismo vecino, pero con una marca que lo distingue de otro que se llame igual.
        /// En una parroquia hay dos Ritas, y el nombre no alcanza para saber a cual de las
        /// dos se le vendio un carton. La marca no la pone el pueblo: la trae la persona,
        /// y por eso sigue siendo la misma en su segunda compra de la noche.
        /// Este constructor se AGREGA y el de dos se queda: el diario re-ejecuta lo que ya
        /// escribio, y una funcion vieja tiene que seguir abriendo.
        /// </summary>
        public Villager(string name, string contact, string mark)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("a villager needs a name", nameof(name));
            if (string.IsNullOrWhiteSpace(contact))
                throw new ArgumentException(
                    "a villager needs a way to be reached, otherwise a prize cannot be handed over",
                    nameof(contact));

            Name = name;
            Contact = contact;
            Mark = (mark ?? string.Empty).Trim();
        }

        public string Name { get; }

        public string Contact { get; }

        /// <summary>Vacia en los vecinos de antes de las marcas, y en el que llega sin
        /// telefono: esos se siguen conociendo solo por el nombre.</summary>
        public string Mark { get; }

        public bool IsMarked => Mark.Length > 0;

        public override string ToString() => Name;
    }
}
