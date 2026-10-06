using System;

namespace Village
{
    public abstract class VillageException : Exception
    {
        protected VillageException(string message) : base(message)
        {
        }
    }
}
