using System;

namespace Salvavida.DefaultImpl
{
    public class DefaultIdGenerator : IIdGenerator
    {
        private static uint baseValue = (uint)DateTimeOffset.UtcNow.Ticks;
        public static DefaultIdGenerator Default { get; } = new DefaultIdGenerator();

        public string GetId()
        {
            return ((uint)(16777259L * ++baseValue)).ToString("x8");
        }
    }
}
