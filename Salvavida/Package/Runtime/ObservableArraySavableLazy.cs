using System;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable array for ISavable elements.
    /// Elements are loaded on-demand by page, with optional LRU cache eviction.
    /// Fixed size - no Add/Insert/Remove/Clear operations.
    /// </summary>
    public sealed class ObservableArraySavableLazy<T> : ObservableArraySavableBase<ObservableArraySavableLazy<T>, T>
        where T : ISavable
    {
        public ObservableArraySavableLazy(string propName, bool saveSeparately) : base(propName, saveSeparately)
        {
        }

        public override T? this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override int Count => throw new NotImplementedException();

        public override bool Contains(T? item)
        {
            throw new NotImplementedException();
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            throw new NotImplementedException();
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public override int IndexOf(T? item)
        {
            throw new NotImplementedException();
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            throw new NotImplementedException();
        }

        public override void SwapSource(T?[]? array)
        {
            throw new NotImplementedException();
        }
    }
}
