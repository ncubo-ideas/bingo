using System;

namespace BingoEngine
{
    public abstract class BingoException : Exception
    {
        protected BingoException(string message) : base(message)
        {
        }
    }
}
