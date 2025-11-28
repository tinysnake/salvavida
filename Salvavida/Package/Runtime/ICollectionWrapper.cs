using System;

namespace Salvavida
{
    public interface ICollectionWrapper
    {
        Type CollectionType { get; }
        object? RetrieveSourceRaw();
    }

    public interface ICollectionWrapper<TCollection> : ICollectionWrapper
    {
        TCollection? RetrieveSource();
        void SwapSource(TCollection? source);
    }
}
