using Salvavida.DefaultImpl;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable list for ISavable elements.
    /// Elements are loaded on-demand by page, with optional LRU cache eviction.
    /// </summary>
    public sealed class ObservableListSavableLazy<T> : ObservableListSavableBase<ObservableListSavableLazy<T>, T>
        where T : ISavable
    {
        public ObservableListSavableLazy(string propName, List<T> src, bool saveSeparately) : base(propName, saveSeparately)
        {
        }

        public override T? this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override int Count => throw new NotImplementedException();

        public override void Add(T? item)
        {
            throw new NotImplementedException();
        }

        public override void Clear()
        {
            throw new NotImplementedException();
        }

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

        public override void Insert(int index, T? item)
        {
            throw new NotImplementedException();
        }

        public override bool Remove(T? item)
        {
            throw new NotImplementedException();
        }

        public override void RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            throw new NotImplementedException();
        }

        public override void SwapSource(List<T?>? list)
        {
            throw new NotImplementedException();
        }
    }
}
