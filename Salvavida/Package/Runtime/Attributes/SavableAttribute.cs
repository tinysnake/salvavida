using System;

namespace Salvavida
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public class SavableAttribute : Attribute
    {
        public bool SerializeWithOrder { get; set; }
        public bool IsRootObject { get; set; }
    }
}
