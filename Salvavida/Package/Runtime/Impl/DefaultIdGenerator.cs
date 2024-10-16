using System;

namespace Salvavida.DefaultImpl
{
    public class DefaultIdGenerator : IIdGenerator
    {
        private static uint baseValue = (uint)DateTimeOffset.UtcNow.Ticks;
        public string GetId()
        {
            return ((uint)(16777259L * ++baseValue)).ToString("x8");
        }
    }
}
