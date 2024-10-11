using System;
using System.Collections.Generic;

namespace Salvavida
{
    public interface ISvIdConverter<T>
    {
        string ConvertTo(T value);
        T ConvertFrom(string str);
    }

    public static class SvIdConverter
    {
        static SvIdConverter()
        {
            RegisterConverter(new SvIdConverterString());
            RegisterConverter(new SvIdConverterInt());
        }

        private static readonly Dictionary<Type, object> _converters = new();

        public static void RegisterConverter<T>(ISvIdConverter<T> converter)
        {
            _converters[typeof(T)] = converter;
        }

        public static void UnregisterConverter<T>()
        {
            _converters.Remove(typeof(T));
        }

        public static ISvIdConverter<T>? GetConverter<T>()
        {
            if (_converters.TryGetValue(typeof(T), out var converter))
                return (ISvIdConverter<T>)converter;
            return null;
        }
    }
}
