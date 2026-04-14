using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableDictionarySavable<TKey, TValue>
        : ObservableDictionarySavableBase<ObservableDictionarySavable<TKey, TValue>, TKey, TValue>
        , ICollectionWrapper<Dictionary<TKey, TValue?>>
        where TKey : notnull
        where TValue : ISavable?
    {
        public ObservableDictionarySavable(string propName, Dictionary<TKey, TValue?>? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _idsDeleted = new HashSet<TKey>();
            SwapSource(src, false);
        }

        private Dictionary<TKey, TValue?>? _dict;
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

        public override TValue? this[TKey key]
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
                    OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Add((ObservableDictionarySavableBase<TKey, TValue>)(object)this, value, -1));
                else
                    OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Replace((ObservableDictionarySavableBase<TKey, TValue>)(object)this, oldValue, value, -1));
                TryUnWatch(oldValue);
            }
        }

        public override ICollection<TKey> Keys => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.Keys;
        public override ICollection<TValue?> Values => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.Values;
        public override int Count => _dict?.Count ?? 0;

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
                            value.Serialize(serializer, ctx);
                    }
                }
            }
            foreach (var id in _idsDeleted)
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

        public override void SwapSource(Dictionary<TKey, TValue?>? dict)
        {
            _isDirty = true;
            SwapSource(dict, true);
        }

        public override bool ContainsKey(TKey key) => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.ContainsKey(key);

        public override void Add(TKey key, TValue? value)
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            _dict.Add(key, value);
            OnItemSet(value, key);
            _idsDeleted.Remove(key);
            OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Add((ObservableDictionarySavableBase<TKey, TValue>)(object)this, value, -1));
        }

        public override bool Remove(TKey key)
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            if (_dict.TryGetValue(key, out var item))
            {
                if (_dict.Remove(key))
                {
                    _idsDeleted.Add(key);
                    OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Remove((ObservableDictionarySavableBase<TKey, TValue>)(object)this, item, -1));
                    TryUnWatch(item);
                    return true;
                }
            }
            return false;
        }

        public override bool TryGetValue(TKey key, out TValue? value)
            => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.TryGetValue(key, out value);

        public override void Clear()
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Reset((ObservableDictionarySavableBase<TKey, TValue>)(object)this));
            foreach (var (key, value) in _dict)
            {
                _idsDeleted.Add(key);
                TryUnWatch(value);
            }
            _dict.Clear();
        }

        public Dictionary<TKey, TValue?>.Enumerator GetEnumerator()
            => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.GetEnumerator();

        protected override IEnumerator<KeyValuePair<TKey, TValue?>> GetEnumeratorCore()
            => _dict == null ? throw new NullReferenceException(nameof(_dict)) : _dict.GetEnumerator();

        private void SwapSource(Dictionary<TKey, TValue?>? dict, bool notifyChanges)
        {
            if (_dict != null)
            {
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Reset((ObservableDictionarySavableBase<TKey, TValue>)(object)this));
                foreach (var (_, item) in _dict)
                {
                    TryUnWatch(item);
                }
            }
            _dict = dict ?? new Dictionary<TKey, TValue?>();
            if (_dict != null)
            {
                foreach (var (key, item) in _dict)
                {
                    if (item != null && string.IsNullOrEmpty(item.SvId))
                        item.SvId = _idConverter.ConvertTo(key);
                    if (notifyChanges)
                        TryWatch(item);
                    else
                        OnChildDeserialized(item);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?> CreateSaveAllEvent()
        {
            if (_dict == null)
                throw new NullReferenceException(nameof(_dict));
            var arr = new TValue?[_dict.Count];
            var i = 0;
            foreach (var (_, val) in _dict)
            {
                arr[i++] = val;
            }
            return CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Add((ObservableDictionarySavableBase<TKey, TValue>)(object)this, arr, -1);
        }

        private void OnItemSet(TValue? item, TKey key)
        {
            if (item is ISavable sv)
                sv.SvId = _idConverter.ConvertTo(key);
            TryWatch(item);
        }
    }
}
