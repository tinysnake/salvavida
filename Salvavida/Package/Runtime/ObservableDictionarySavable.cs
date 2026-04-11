using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableDictionarySavable<TKey, TValue> : ObservableCollectionSavable<ObservableDictionarySavable<TKey, TValue>, TValue>, IDictionary<TKey, TValue?>, IReadOnlyDictionary<TKey, TValue?>, IDictionary, ICollectionWrapper<Dictionary<TKey, TValue?>>
        where TKey : notnull
        where TValue : ISavable?
    {
        public ObservableDictionarySavable(string propName, Dictionary<TKey, TValue?>? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _idConverter = SvIdConverter.GetConverter<TKey>() ?? throw new NotSupportedException($"不支持的字典Key类型:{typeof(TKey)}，请先在SvIdConverter中注册此种类型的Converter");
            _idsDeleted = new();
            SwapSource(src, false);
        }

        private Dictionary<TKey, TValue?>? _dict;
        private readonly ISvIdConverter<TKey> _idConverter;
        private readonly HashSet<TKey> _idsDeleted;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty)
                    return true;
                if (_dict == null)
                    return false;
                foreach (var (_, val) in _dict)
                {
                    if (val == null)
                        continue;
                    if (val.IsDirty)
                        return true;
                }

                return false;
            }
        }

        public TValue? this[TKey key]
        {
            get => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict[key];
            set
            {
                if (_dict == null)
                    throw new NullReferenceException(nameof(_dict));
                var add = !_dict.TryGetValue(key, out var oldValue);
                if (EqualityComparer<TValue?>.Default.Equals(oldValue, value))
                    return;
                _dict[key] = value;
                if (value != null)
                    value.SvId = _idConverter.ConvertTo(key);
                _idsDeleted.Remove(key);
                OnItemSet(value, key);
                if (add)
                    OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Add(this, value, -1));
                else
                    OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Replace(this, oldValue, value, -1));
                TryUnWatch(oldValue);
            }
        }

        object? IDictionary.this[object key] { get => this[(TKey)key]; set => this[(TKey)key] = (TValue?)value; }

        public ICollection<TKey> Keys => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.Keys;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue?>.Keys => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.Keys;

        ICollection IDictionary.Keys => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.Keys;

        public ICollection<TValue?> Values => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.Values;

        IEnumerable<TValue?> IReadOnlyDictionary<TKey, TValue?>.Values => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.Values;

        ICollection IDictionary.Values => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.Values;

        public int Count => _dict?.Count ?? 0;

        bool IDictionary.IsFixedSize => ((IDictionary?)_dict)?.IsFixedSize ?? false;

        bool ICollection.IsSynchronized => ((IDictionary?)_dict)?.IsSynchronized ?? false;

        object? ICollection.SyncRoot => ((ICollection?)_dict)?.SyncRoot ?? null;

        bool ICollection<KeyValuePair<TKey, TValue?>>.IsReadOnly => false;

        bool IDictionary.IsReadOnly => false;

        public Dictionary<TKey, TValue?>? RetrieveSource() => _dict;

        public object? RetrieveSourceRaw() => _dict;

        public Type CollectionType => typeof(Dictionary<TKey, TValue?>);

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);
            if (recursive && _dict != null)
            {
                _isChildrenDirty = dirty;
                foreach (var (_, val) in _dict)
                {
                    val?.SetDirty(dirty, recursive);
                }
            }
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            if (_dict != null)
            {
                foreach (var (key, value) in _dict)
                {
                    var id = _idConverter.ConvertTo(key);
                    using (ctx.Path.UsePush(id, PathBuilder.Type.Collection))
                    {
                        if (value == null)
                            serializer.SaveNoPushPath<TValue>(default, ctx);
                        else if (value.IsDirty)
                        {
                            if (value.IsDirty)
                                value.Serialize(serializer, ctx);
                        }
                    }
                }
            }
            foreach(var id in _idsDeleted)
            {
                serializer.Delete(ctx, _idConverter.ConvertTo(id), PathBuilder.Type.Collection);
            }
            _idsDeleted.Clear();
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            var ids = serializer.ListCollectionIds(ctx);
            var dict = new Dictionary<TKey, TValue?>();
            foreach (var id in ids)
            {
                dict[_idConverter.ConvertFrom(id)] = serializer.Read<TValue?>(ctx, id, PathBuilder.Type.Collection);
            }
            _idsDeleted.Clear();
            SwapSource(dict, false);
        }

        public void SwapSource(Dictionary<TKey, TValue?>? dict)
        {
            _isDirty = true;
            SwapSource(dict, true);
        }

        private void SwapSource(Dictionary<TKey, TValue?>? dict, bool notifyChanges)
        {
            if (_dict != null)
            {
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Reset(this));
                foreach (var (_, item) in _dict)
                {
                    TryUnWatch(item);
                }
            }
            _dict = dict;
            if (_dict != null)
            {
                var idConverter = SvIdConverter.GetConverter<TKey>() ?? throw new NullReferenceException("cannot find ID converter for key type " + typeof(TKey));
                foreach (var (key, item) in _dict)
                {
                    if (item != null && string.IsNullOrEmpty(item.SvId))
                        item.SvId = idConverter.ConvertTo(key);
                    if (notifyChanges)
                        TryWatch(item);
                    else
                        OnChildDeserialized(item);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        protected override void OnChildChanged(TValue obj, string _)
        {
            _isChildrenDirty = true;
            if (obj is not ISavable)
                throw new InvalidOperationException();
            OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Replace(this, obj, obj, -1));
        }

        private CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?> CreateSaveAllEvent()
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            var arr = new TValue?[_dict.Count];
            var i = 0;
            foreach (var (_, val) in _dict)
            {
                arr[i++] = val;
            }
            return CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Add(this, arr, -1);
        }

        private void OnItemSet(TValue? item, TKey key)
        {
            if (item is ISavable sv)
                sv.SvId = _idConverter.ConvertTo(key);
            TryWatch(item);
        }

        public void Add(TKey key, TValue? value)
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            _dict.Add(key, value);
            OnItemSet(value, key);
            _idsDeleted.Remove(key);
            OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Add(this, value, -1));
        }

        public void Add(KeyValuePair<TKey, TValue?> item) => Add(item.Key, item.Value);

        void IDictionary.Add(object key, object value) => Add((TKey)key, (TValue?)value);

        public void Clear()
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Reset(this));
            foreach (var (key, value) in _dict)
            {
                _idsDeleted.Add(key); 
                TryUnWatch(value);
            }
            _dict.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue?> item) => _dict == null ? throw new NullReferenceException(nameof(_dict)) : ((ICollection<KeyValuePair<TKey, TValue?>>)_dict).Contains(item);

        bool IDictionary.Contains(object key) => _dict == null ? throw new NullReferenceException(nameof(_dict)) : ((IDictionary)_dict).Contains(key);

        public bool ContainsKey(TKey key) => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.ContainsKey(key);

        public void CopyTo(KeyValuePair<TKey, TValue?>[] array, int arrayIndex) => ((IDictionary<TKey, TValue?>?)_dict)?.CopyTo(array, arrayIndex);

        void ICollection.CopyTo(Array array, int index) => ((ICollection?)_dict)?.CopyTo(array, index);

        public Dictionary<TKey, TValue?>.Enumerator GetEnumerator() => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.GetEnumerator();

        IEnumerator<KeyValuePair<TKey, TValue?>> IEnumerable<KeyValuePair<TKey, TValue?>>.GetEnumerator() => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.GetEnumerator();

        IDictionaryEnumerator IDictionary.GetEnumerator() => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.GetEnumerator();

        public bool Remove(TKey key)
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            if (_dict.TryGetValue(key, out var item))
            {
                if (_dict.Remove(key))
                {
                    _idsDeleted.Add(key);
                    OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Remove(this, item, -1));
                    TryUnWatch(item);
                    return true;
                }
            }
            return false;
        }

        public bool Remove(KeyValuePair<TKey, TValue?> item)
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            if (((ICollection<KeyValuePair<TKey, TValue?>>)_dict).Remove(item))
            {
                OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavable<TKey, TValue>, TValue?>.Remove(this, item.Value, -1));
                TryUnWatch(item.Value);
                return true;
            }
            return false;
        }

        void IDictionary.Remove(object key) => Remove((TKey)key);

        public bool TryGetValue(TKey key, out TValue? value) => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.TryGetValue(key, out value);
    }
}
