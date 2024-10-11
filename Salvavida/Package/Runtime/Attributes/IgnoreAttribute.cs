using System;

namespace Salvavida
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class IgnoreAttribute : Attribute
    {

    }
}
