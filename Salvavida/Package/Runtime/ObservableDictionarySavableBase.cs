using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    /// <summary>
    /// Abstract base class for observable savable dictionary collections.
    /// Provides shared IDictionary/IReadOnlyDictionary explicit implementations and the _idConverter field.
    /// </summary>
    public abstract class ObservableDictionarySavableBase<TKey, TValue>
        : ObservableCollectionSavable<ObservableDictionarySavableBase<TKey, TValue>, TValue>
        , IDictionary<TKey, TValue?>, IReadOnlyDictionary<TKey, TValue?>, IDictionary
        where TKey : notnull
        where TValue : ISavable?
    {
        protected readonly ISvIdConverter<TKey> _idConverter;

        protected ObservableDictionarySavableBase(string propName, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _idConverter = SvIdConverter.GetConverter<TKey>()
                ?? throw new NotSupportedException($"不支持的字典Key类型:{typeof(TKey)}，请先在SvIdConverter中注册此种类型的Converter");
        }

        // ── Abstract surface ──────────────────────────────────────────────

        public abstract TValue? this[TKey key] { get; set; }
        public abstract ICollection<TKey> Keys { get; }
        public abstract ICollection<TValue?> Values { get; }
        public abstract int Count { get; }
        public abstract bool ContainsKey(TKey key);
        public abstract void Add(TKey key, TValue? value);
        public abstract bool Remove(TKey key);
        public abstract bool TryGetValue(TKey key, out TValue? value);
        public abstract void Clear();
        public abstract void SwapSource(Dictionary<TKey, TValue?>? dict);

        /// <summary>Core enumerator used by all IEnumerable/IDictionary explicit implementations.</summary>
        protected abstract IEnumerator<KeyValuePair<TKey, TValue?>> GetEnumeratorCore();

        // ── Shared concrete implementations ──────────────────────────────

        public void Add(KeyValuePair<TKey, TValue?> item) => Add(item.Key, item.Value);

        public bool Contains(KeyValuePair<TKey, TValue?> item)
            => TryGetValue(item.Key, out var v) && EqualityComparer<TValue?>.Default.Equals(v, item.Value);

        public bool Remove(KeyValuePair<TKey, TValue?> item)
        {
            if (!TryGetValue(item.Key, out var v))
                return false;
            if (!EqualityComparer<TValue?>.Default.Equals(v, item.Value))
                return false;
            return Remove(item.Key);
        }

        public void CopyTo(KeyValuePair<TKey, TValue?>[] array, int arrayIndex)
        {
            int i = arrayIndex;
            foreach (var kvp in (IEnumerable<KeyValuePair<TKey, TValue?>>)this)
                array[i++] = kvp;
        }

        protected static void SaveMetadata(Serializer serializer, SerializeContext ctx, int count)
        {
            var meta = new CollectionMetadata { Count = count };
            serializer.Save(meta, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
        }

        // ── Explicit IDictionary implementations ─────────────────────────

        object? IDictionary.this[object key] { get => this[(TKey)key]; set => this[(TKey)key] = (TValue?)value; }

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue?>.Keys => Keys;
        IEnumerable<TValue?> IReadOnlyDictionary<TKey, TValue?>.Values => Values;
        ICollection IDictionary.Keys => (ICollection)Keys;
        ICollection IDictionary.Values => (ICollection)Values;

        bool IDictionary.IsFixedSize => false;
        bool ICollection.IsSynchronized => false;
        object? ICollection.SyncRoot => null;
        bool ICollection<KeyValuePair<TKey, TValue?>>.IsReadOnly => false;
        bool IDictionary.IsReadOnly => false;

        bool IDictionary.Contains(object key) => ContainsKey((TKey)key);
        void IDictionary.Add(object key, object? value) => Add((TKey)key, (TValue?)value);
        void IDictionary.Remove(object key) => Remove((TKey)key);

        void ICollection.CopyTo(Array array, int index)
        {
            int i = index;
            foreach (var kvp in this)
                array.SetValue(kvp, i++);
        }

        IEnumerator<KeyValuePair<TKey, TValue?>> IEnumerable<KeyValuePair<TKey, TValue?>>.GetEnumerator() => GetEnumeratorCore();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumeratorCore();
        IDictionaryEnumerator IDictionary.GetEnumerator() => new DictionaryEnumeratorAdapter(GetEnumeratorCore());

        bool IReadOnlyDictionary<TKey, TValue?>.ContainsKey(TKey key) => ContainsKey(key);
        bool IReadOnlyDictionary<TKey, TValue?>.TryGetValue(TKey key, out TValue? value) => TryGetValue(key, out value);
        TValue? IReadOnlyDictionary<TKey, TValue?>.this[TKey key] => this[key];

        // ── Private helpers ───────────────────────────────────────────────

        private sealed class DictionaryEnumeratorAdapter : IDictionaryEnumerator
        {
            private readonly IEnumerator<KeyValuePair<TKey, TValue?>> _inner;

            public DictionaryEnumeratorAdapter(IEnumerator<KeyValuePair<TKey, TValue?>> inner)
                => _inner = inner;

            public DictionaryEntry Entry => new DictionaryEntry(_inner.Current.Key, _inner.Current.Value);
            public object Key => _inner.Current.Key;
            public object? Value => _inner.Current.Value;
            public object Current => Entry;
            public bool MoveNext() => _inner.MoveNext();
            public void Reset() => _inner.Reset();
            public void Dispose() => _inner.Dispose();
        }
    }

    /// <summary>
    /// CRTP intermediate layer providing precise CollectionChangeInfo type for OnChildChanged.
    /// </summary>
    public abstract class ObservableDictionarySavableBase<TSelf, TKey, TValue>
        : ObservableDictionarySavableBase<TKey, TValue>
        where TSelf : ObservableDictionarySavableBase<TSelf, TKey, TValue>
        where TKey : notnull
        where TValue : ISavable?
    {
        protected ObservableDictionarySavableBase(string propName, bool saveSeparately)
            : base(propName, saveSeparately)
        {
        }

        protected override void OnChildChanged(TValue obj, string _)
        {
            _isChildrenDirty = true;
            OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>
                .Replace((ObservableDictionarySavableBase<TKey, TValue>)(object)this, obj, obj, -1));
        }
    }
}
