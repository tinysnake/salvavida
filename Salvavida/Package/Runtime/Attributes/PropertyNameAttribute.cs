using System;

namespace Salvavida
{
    public class PropertyNameAttribute : Attribute
    {
        public PropertyNameAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
