using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableDictionary<TKey, TValue> : ObservableCollection<ObservableDictionary<TKey, TValue>, TValue>, IDictionary<TKey, TValue?>, IReadOnlyDictionary<TKey, TValue?>, IDictionary, ICollectionWrapper<Dictionary<TKey, TValue?>>
        where TKey : notnull
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public ObservableDictionary(string propName, Dictionary<TKey, TValue?> src, bool saveSeparately)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
            : base(propName, saveSeparately)
        {
            _isSavable = SvHelper.CheckIsSavable<TValue?>();
            _idConverter = SvIdConverter.GetConverter<TKey>() ?? throw new NotSupportedException($"不支持的字典Key类型:{typeof(TKey)}，请先在SvIdConverter中注册此种类型的Converter");
            SwapSource(src, false);
        }

        private Dictionary<TKey, TValue?> _dict;
        private readonly ISvIdConverter<TKey> _idConverter;
        private readonly bool _isSavable;

        public override bool IsSavableCollection => _isSavable;

        public TValue? this[TKey key]
        {
            get => _dict[key];
            set
            {
                var add = !_dict.TryGetValue(key, out var oldValue);
                TryUnWatch(oldValue);
                _dict[key] = value;
                OnItemSet(value, key);
                if (add)
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Add(value, -1));
                else
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Replace(oldValue, value, -1));
            }
        }

        object? IDictionary.this[object key] { get => _dict[(TKey)key]; set => this[(TKey)key] = (TValue?)value; }

        public ICollection<TKey> Keys => _dict.Keys;

        public ICollection<TValue?> Values => _dict.Values;

        public int Count => _dict.Count;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue?>.Keys => _dict.Keys;

        ICollection IDictionary.Keys => _dict.Keys;

        IEnumerable<TValue?> IReadOnlyDictionary<TKey, TValue?>.Values => _dict.Values;

        ICollection IDictionary.Values => _dict.Values;

        bool IDictionary.IsFixedSize => ((IDictionary)_dict).IsFixedSize;

        bool ICollection.IsSynchronized => ((IDictionary)_dict).IsSynchronized;

        object ICollection.SyncRoot => ((ICollection)_dict).SyncRoot;

        bool ICollection<KeyValuePair<TKey, TValue?>>.IsReadOnly => false;

        bool IDictionary.IsReadOnly => false;

        public Dictionary<TKey, TValue?> RetrieveSource() => _dict;

        public void SwapSource(Dictionary<TKey, TValue?> dict)
        {
            _isDirty = true;
            SwapSource(dict, true);
        }

        private void SwapSource(Dictionary<TKey, TValue?> dict, bool notifyChanges)
        {
            if (_dict != null)
            {
                foreach (var (_, item) in _dict)
                {
                    TryUnWatch(item);
                }
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Reset());
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


        protected override void TrySaveItems(Serializer serializer, PathBuilder? pathBuilder, CollectionChangeInfo<TValue?> e)
        {
            switch (e.Action)
            {
                case CollectionChangedAction.Add:
                case CollectionChangedAction.Replace:
                    if (e.IsSingleItem)
                        KeyCollectionSave(serializer, pathBuilder, e.NewItem!, isRemove: false);
                    else
                        KeyCollectionSave(serializer, pathBuilder, e.NewItems!, isRemove: false);
                    break;
                case CollectionChangedAction.Remove:
                    if (e.IsSingleItem)
                        KeyCollectionSave(serializer, pathBuilder, e.OldItem!, isRemove: true);
                    else
                        KeyCollectionSave(serializer, pathBuilder, e.OldItems!, isRemove: true);
                    break;
                case CollectionChangedAction.Reset:
                    ClearCollection(serializer, pathBuilder);
                    break;
                default:
                    throw new NotSupportedException();
            }
        }
        private void KeyCollectionSave(Serializer serializer, PathBuilder? pathBuilder, TValue value, bool isRemove)
        {
            if (value is not ISavable sv)
                throw new ArgumentException("values have to be implementations of ISavable");
            if (isRemove)
            {
                if (pathBuilder == null)
                    serializer.FreshDeleteByPolicy(sv);
                else
                    serializer.Delete(sv, pathBuilder, PathBuilder.Type.Collection);
            }
            else
            {
                if (pathBuilder == null)
                    serializer.FreshSaveByPolicy(sv);
                else
                    serializer.Save(sv, pathBuilder, PathBuilder.Type.Collection);
            }
        }
        private void KeyCollectionSave(Serializer serializer, PathBuilder? pathBuilder, IList<TValue?> values, bool isRemove)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (pathBuilder == null)
                serializer.FreshActionByPolicy(this, path => KeyCollectionSaveAction(serializer, path, values, isRemove));
            else
                KeyCollectionSaveAction(serializer, pathBuilder, values, isRemove);
        }

        private void KeyCollectionSaveAction(Serializer serializer, PathBuilder pathBuilder, IList<TValue?> values, bool isRemove)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] is not ISavable value)
                    throw new ArgumentException("values have to be implementations of ISavable");
                if (isRemove)
                    serializer.Delete(value, pathBuilder, PathBuilder.Type.Collection);
                else
                    serializer.Save(value, pathBuilder, PathBuilder.Type.Collection);
            }
        }

        protected override void TrySaveSource(Serializer serializer, PathBuilder? pathBuilder)
        {
            if (string.IsNullOrEmpty(SvId))
                throw new NullReferenceException(nameof(SvId));
            if (pathBuilder == null)
                serializer.FreshActionByPolicy(this, path => serializer.SaveDict(_dict, path));
            else
                serializer.SaveDict(_dict, pathBuilder);
        }

        public void Add(TKey key, TValue? value)
        {
            _dict.Add(key, value);
            OnItemSet(value, key);
            OnCollectionChange(CollectionChangeInfo<TValue?>.Add(value, -1));
        }

        public void Add(KeyValuePair<TKey, TValue?> item) => Add(item.Key, item.Value);

        private void OnItemSet(TValue? item, TKey key)
        {
            if (item is ISavable sv)
                sv.SvId = _idConverter.ConvertTo(key);
            TryWatch(item);
        }

        public void Clear()
        {
            foreach (var value in _dict.Values)
            {
                TryUnWatch(value);
            }
            _dict.Clear();
            OnCollectionChange(CollectionChangeInfo<TValue?>.Reset());
        }

        public bool Contains(KeyValuePair<TKey, TValue?> item) => ((ICollection<KeyValuePair<TKey, TValue?>>)_dict).Contains(item);

        public bool ContainsKey(TKey key) => _dict.ContainsKey(key);

        public void CopyTo(KeyValuePair<TKey, TValue?>[] array, int arrayIndex) => ((IDictionary<TKey, TValue?>)_dict).CopyTo(array, arrayIndex);

        public Dictionary<TKey, TValue?>.Enumerator GetEnumerator() => _dict.GetEnumerator();

        IEnumerator<KeyValuePair<TKey, TValue?>> IEnumerable<KeyValuePair<TKey, TValue?>>.GetEnumerator() => _dict.GetEnumerator();

        public bool Remove(TKey key)
        {
            if (_dict.TryGetValue(key, out var item))
            {
                if (_dict.Remove(key))
                {
                    TryUnWatch(item);
                    OnCollectionChange(CollectionChangeInfo<TValue?>.Remove(item, -1));
                    return true;
                }
            }
            return false;
        }

        public bool Remove(KeyValuePair<TKey, TValue?> item)
        {
            if (((ICollection<KeyValuePair<TKey, TValue?>>)_dict).Remove(item))
            {
                TryUnWatch(item.Value);
                OnCollectionChange(CollectionChangeInfo<TValue?>.Remove(item.Value, -1));
                return true;
            }
            return false;
        }

        public bool TryGetValue(TKey key, out TValue? value) => _dict.TryGetValue(key, out value);

        void IDictionary.Add(object key, object value) => Add((TKey)key, (TValue?)value);

        bool IDictionary.Contains(object key) => ((IDictionary)_dict).Contains(key);

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_dict).CopyTo(array, index);

        IEnumerator IEnumerable.GetEnumerator() => _dict.GetEnumerator();

        IDictionaryEnumerator IDictionary.GetEnumerator() => _dict.GetEnumerator();

        void IDictionary.Remove(object key) => Remove((TKey)key);
    }
}
