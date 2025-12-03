using System;

namespace Salvavida
{
    public interface ISalvavida : IDisposable
    {
        string Id { get; }

        void Save();
        void Load();

        Serializer Serializer { get; }
    }

    public interface ISalvavida<T> : ISalvavida where T : ISavable, ISerializeRoot
    {
        T? Data { get; }
    }
}