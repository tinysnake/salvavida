using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableDictionary<TKey, TValue> : ObservableCollection<ObservableDictionary<TKey, TValue>, TValue>, IDictionary<TKey, TValue?>, IReadOnlyDictionary<TKey, TValue?>, IDictionary, ICollectionWrapper<Dictionary<TKey, TValue?>>
        where TKey : notnull
    {
        public ObservableDictionary(string propName, Dictionary<TKey, TValue?>? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _idConverter = SvIdConverter.GetConverter<TKey>() ?? throw new NotSupportedException($"不支持的字典Key类型:{typeof(TKey)}，请先在SvIdConverter中注册此种类型的Converter");
            SwapSource(src, false);
        }

        private Dictionary<TKey, TValue?>? _dict;
        private readonly ISvIdConverter<TKey> _idConverter;

        public TValue? this[TKey key]
        {
            get => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict[key];
            set
            {
                if (_dict == null)
                    throw new NullReferenceException(nameof(_dict));
                var add = !_dict.TryGetValue(key, out var oldValue);
                _dict[key] = value;
                OnItemSet(value, key);
                if (add)
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Add(value, -1));
                else
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Replace(oldValue, value, -1));
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

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            serializer.SaveObject(_dict, ctx, SvId, PathBuilder.Type.Property);
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            var list = serializer.ReadObject<Dictionary<TKey, TValue?>>(ctx);
            SwapSource(list);
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
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Reset());
                foreach (var (_, item) in _dict)
                {
                    TryUnWatch(item);
                }
            }
            _dict = dict;
            if (_dict != null)
            {
                foreach (var (key, item) in _dict)
                {
                    OnItemSet(item, key);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        protected override void OnChildChanged(TValue obj, string _)
        {
            if (obj is not ISavable)
                throw new InvalidOperationException();
            OnCollectionChange(CollectionChangeInfo<TValue?>.Replace(obj, obj, -1));
        }

        protected override CollectionChangeInfo<TValue?> CreateSaveAllEvent()
        {
            var arr = new TValue?[_dict.Count];
            var i = 0;
            foreach (var (_, val) in _dict)
            {
                arr[i++] = val;
            }
            return CollectionChangeInfo<TValue?>.Add(arr, -1);
        }

        //protected override void TrySaveSeparatelyByEvent(Serializer serializer, SerializeContext? ctx, CollectionChangeInfo<TValue?> e)
        //{
        //    if (!SaveSeparately)
        //        throw new NotSupportedException();

        //    if (string.IsNullOrEmpty(SvId))
        //        throw new NullReferenceException(nameof(SvId));
        //    if (ctx == null)
        //        serializer.FreshAction(this, path => serializer.SaveDict(_dict, path), null);
        //    else
        //        serializer.SaveDict(_dict, ctx);
        //}

        private void OnItemSet(TValue? item, TKey key)
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
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
            OnCollectionChange(CollectionChangeInfo<TValue?>.Add(value, -1));
        }

        public void Add(KeyValuePair<TKey, TValue?> item) => Add(item.Key, item.Value);

        void IDictionary.Add(object key, object value) => Add((TKey)key, (TValue?)value);

        public void Clear()
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            OnCollectionChange(CollectionChangeInfo<TValue?>.Reset());
            foreach (var value in _dict.Values)
            {
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
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Remove(item, -1));
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
                OnCollectionChange(CollectionChangeInfo<TValue?>.Remove(item.Value, -1));
                TryUnWatch(item.Value);
                return true;
            }
            return false;
        }

        void IDictionary.Remove(object key) => Remove((TKey)key);

        public bool TryGetValue(TKey key, out TValue? value) => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.TryGetValue(key, out value);

    }

    public sealed class ObservableDictionarySavable<TKey, TValue> : ObservableCollectionSavable<ObservableDictionarySavable<TKey, TValue>, TValue>, IDictionary<TKey, TValue?>, IReadOnlyDictionary<TKey, TValue?>, IDictionary, ICollectionWrapper<Dictionary<TKey, TValue?>>
        where TKey : notnull
        where TValue : ISavable?
    {
        public ObservableDictionarySavable(string propName, Dictionary<TKey, TValue?>? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _idConverter = SvIdConverter.GetConverter<TKey>() ?? throw new NotSupportedException($"不支持的字典Key类型:{typeof(TKey)}，请先在SvIdConverter中注册此种类型的Converter");
            SwapSource(src, false);
        }

        private Dictionary<TKey, TValue?>? _dict;
        private readonly ISvIdConverter<TKey> _idConverter;
        private string[]? _keysOnDeserialized;

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
                _dict[key] = value;
                OnItemSet(value, key);
                if (add)
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Add(value, -1));
                else
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Replace(oldValue, value, -1));
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

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            var tempIds = SvHelper.idListPool.Get();
            try
            {
                if (_dict != null)
                {
                    foreach (var (key, value) in _dict)
                    {
                        var id = _idConverter.ConvertTo(key);
                        serializer.SaveObject(value, ctx, id, PathBuilder.Type.Collection);
                        tempIds.Add(id);
                    }
                    serializer.SaveObject(tempIds, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
                }
                else
                {
                    serializer.DeleteObject(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
                }
                if (_keysOnDeserialized != null)
                {
                    foreach (var oldId in _keysOnDeserialized)
                    {
                        if (tempIds.IndexOf(oldId) < 0)
                        {
                            serializer.DeleteObject(ctx, oldId, PathBuilder.Type.Collection);
                        }
                    }
                }
            }
            finally
            {
                SvHelper.idListPool.Return(tempIds);
            }

            _keysOnDeserialized = null;
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            var tempIds = serializer.ReadObject<string[]?>(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection);
            Dictionary<TKey, TValue?>? dict;
            if (tempIds == null || tempIds.Length == 0)
                dict = null;
            else
            {
                _keysOnDeserialized = tempIds;
                dict = new Dictionary<TKey, TValue?>();
                foreach (var id in _keysOnDeserialized)
                {
                    dict[_idConverter.ConvertFrom(id)] = serializer.ReadObject<TValue?>(ctx, id, PathBuilder.Type.Collection);
                }
            }
            SwapSource(dict);
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
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Reset());
                foreach (var (_, item) in _dict)
                {
                    TryUnWatch(item);
                }
            }
            _dict = dict;
            if (_dict != null)
            {
                foreach (var (key, item) in _dict)
                {
                    OnItemSet(item, key);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        protected override void OnChildChanged(TValue obj, string _)
        {
            if (obj is not ISavable)
                throw new InvalidOperationException();
            OnCollectionChange(CollectionChangeInfo<TValue?>.Replace(obj, obj, -1));
        }

        protected override CollectionChangeInfo<TValue?> CreateSaveAllEvent()
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            var arr = new TValue?[_dict.Count];
            var i = 0;
            foreach (var (_, val) in _dict)
            {
                arr[i++] = val;
            }
            return CollectionChangeInfo<TValue?>.Add(arr, -1);
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
            OnCollectionChange(CollectionChangeInfo<TValue?>.Add(value, -1));
        }

        public void Add(KeyValuePair<TKey, TValue?> item) => Add(item.Key, item.Value);

        void IDictionary.Add(object key, object value) => Add((TKey)key, (TValue?)value);

        public void Clear()
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            OnCollectionChange(CollectionChangeInfo<TValue?>.Reset());
            foreach (var value in _dict.Values)
            {
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
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Remove(item, -1));
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
                OnCollectionChange(CollectionChangeInfo<TValue?>.Remove(item.Value, -1));
                TryUnWatch(item.Value);
                return true;
            }
            return false;
        }

        void IDictionary.Remove(object key) => Remove((TKey)key);

        public bool TryGetValue(TKey key, out TValue? value) => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.TryGetValue(key, out value);


        //protected override void TrySaveItems(Serializer serializer, SerializeContext? ctx, CollectionChangeInfo<TValue?> e)
        //{
        //    switch (e.Action)
        //    {
        //        case CollectionChangedAction.Add:
        //        case CollectionChangedAction.Replace:
        //            if (e.IsSingleItem)
        //                KeyCollectionSave(serializer, ctx, e.NewItem!, isRemove: false);
        //            else
        //                KeyCollectionSave(serializer, ctx, e.NewItems!, isRemove: false);
        //            break;
        //        case CollectionChangedAction.Remove:
        //            if (e.IsSingleItem)
        //                KeyCollectionSave(serializer, ctx, e.OldItem!, isRemove: true);
        //            else
        //                KeyCollectionSave(serializer, ctx, e.OldItems!, isRemove: true);
        //            break;
        //        case CollectionChangedAction.Reset:
        //            ClearCollection(serializer, ctx);
        //            break;
        //        default:
        //            throw new NotSupportedException();
        //    }
        //}

        //private void KeyCollectionSave(Serializer serializer, SerializeContext? ctx, TValue value, bool isRemove)
        //{
        //    if (isRemove)
        //    {
        //        if (ctx == null)
        //            serializer.FreshDelete(value);
        //        else
        //            serializer.Delete(value, ctx, PathBuilder.Type.Collection);
        //    }
        //    else
        //    {
        //        if (ctx == null)
        //            serializer.FreshSave(value);
        //        else
        //            serializer.Save(value, ctx, PathBuilder.Type.Collection);
        //    }
        //}

        //private void KeyCollectionSave(Serializer serializer, SerializeContext? ctx, IList<TValue?> values, bool isRemove)
        //{
        //    if (values == null)
        //        throw new ArgumentNullException(nameof(values));
        //    if (ctx == null)
        //        serializer.FreshAction(this, x => KeyCollectionSaveAction(serializer, x, values, isRemove), null);
        //    else
        //        KeyCollectionSaveAction(serializer, ctx, values, isRemove);
        //}

        //private void KeyCollectionSaveAction(Serializer serializer, SerializeContext ctx, IList<TValue?> values, bool isRemove)
        //{
        //    for (var i = 0; i < values.Count; i++)
        //    {
        //        var val = values[i];
        //        if (val == null)
        //            continue;
        //        if (isRemove)
        //            serializer.Delete(val, ctx, PathBuilder.Type.Collection);
        //        else
        //            serializer.Save(val, ctx, PathBuilder.Type.Collection);
        //    }
        //}
    }
}
