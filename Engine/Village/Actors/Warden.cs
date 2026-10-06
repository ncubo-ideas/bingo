using System;

namespace Village
{
    public sealed class Warden
    {
        public Warden(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("a warden needs a name", nameof(name));

            Name = name;
        }

        public string Name { get; }

        public override string ToString() => Name;
    }
}
