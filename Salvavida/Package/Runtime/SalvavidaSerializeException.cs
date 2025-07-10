using System;

namespace Salvavida
{
    public class SalvavidaSerializeException : Exception
    {
        public SalvavidaSerializeException(string message)
            : base(message)
        {

        }

        public SalvavidaSerializeException(string message, Exception innerException)
            : base(message, innerException)
        {

        }
    }
}
