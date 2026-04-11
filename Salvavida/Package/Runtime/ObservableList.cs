using Salvavida.DefaultImpl;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Salvavida
{
    public sealed class ObservableList<T> : ObservableCollection<ObservableList<T>, T>, IList<T?>, IReadOnlyList<T?>, IList, ICollectionWrapper<List<T?>>
    {
        public ObservableList(string propName, List<T?>? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            SwapSource(src, false);
        }

        private List<T?>? _list;

        public T? this[int index]
        {
            get => _list == null ? throw new NullReferenceException(nameof(_list)) : _list[index];
            set
            {
                if (_list == null)
                    throw new NullReferenceException(nameof(_list));
                var oldValue = _list[index];
                if (EqualityComparer<T?>.Default.Equals(oldValue, value))
                    return;
                _list[index] = value;
                OnItemSet(value, index);
                OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Replace(this, oldValue, value, index));
                TryUnWatch(oldValue);
            }
        }

        object? IList.this[int index] { get => this[index]; set => this[index] = (T?)value; }

        public int Count => _list?.Count ?? 0;

        int ICollection.Count => _list?.Count ?? 0;

        bool IList.IsFixedSize => false;

        bool ICollection<T?>.IsReadOnly => false;

        bool IList.IsReadOnly => false;

        bool ICollection.IsSynchronized => ((ICollection?)_list)?.IsSynchronized ?? false;

        object? ICollection.SyncRoot => ((ICollection?)_list)?.SyncRoot ?? null;

        public List<T?>? RetrieveSource() => _list;

        public object? RetrieveSourceRaw() => _list;

        public Type CollectionType => typeof(List<T?>);

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            serializer.SaveNoPushPath(_list, ctx);
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            var list = serializer.ReadNoPushPath<List<T?>>(ctx);
            SwapSource(list, false);
        }

        public void SwapSource(List<T?>? list)
        {
            _isDirty = true;
            SwapSource(list, true);
        }

        private void SwapSource(List<T?>? list, bool notifyChanges)
        {
            if (_list != null)
            {
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Reset(this));
                for (var i = 0; i < _list.Count; i++)
                {
                    TryUnWatch(_list[i]);
                }
            }
            _list = list;
            if (_list != null)
            {
                for (var i = 0; i < _list.Count; i++)
                {
                    if (notifyChanges)
                        TryWatch(_list[i]);
                    else
                        OnChildDeserialized(_list[i]);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        protected override void OnChildChanged(T obj, string _)
        {
            _isDirty = true;
            var index = IndexOf(obj);
            if (index >= 0)
                OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Replace(this, obj, obj, index));
        }

        private CollectionChangeInfo<ObservableList<T>, T?> CreateSaveAllEvent()
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            return CollectionChangeInfo<ObservableList<T>, T?>.Add(this, _list, 0);
        }

        public void Add(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.Count;
            _list.Add(item);
            OnItemSet(item, index);
            OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Add(this, item, index));
        }

        int IList.Add(object value)
        {
            Add((T?)value);
            return Count - 1;
        }

        public void AddRange(IList<T?> collection)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.Count;
            _list.AddRange(collection);
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                OnItemSet(item, index);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Add(this, collection, index));
        }

        private void OnItemSet(T? item, int index)
        {
            //if (_orderMatters && item is ISaveWithOrder swo)
            //    swo.SvOrder = index;
            if (item is ISavable sv)
                sv.SvId ??= DefaultIdGenerator.Default.GetId();
            TryWatch(item);
        }

        public void Clear()
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Reset(this));
            foreach (var item in _list)
            {
                TryUnWatch(item);
            }
            _list.Clear();
        }

        void IList.Clear() => Clear();

        public bool Contains(T? item) => _list == null ? throw new NullReferenceException(nameof(_list)) : _list.Contains(item);

        bool IList.Contains(object value) => Contains((T?)value);

        public void CopyTo(T?[] array, int arrayIndex) => _list?.CopyTo(array, arrayIndex);

        void ICollection.CopyTo(Array array, int index) => ((ICollection?)_list)?.CopyTo(array, index);

        public List<T?>.Enumerator GetEnumerator() => _list == null ? throw new NullReferenceException(nameof(_list)) : _list.GetEnumerator();

        IEnumerator<T?> IEnumerable<T?>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int IndexOf(T? item) => _list == null ? throw new NullReferenceException(nameof(_list)) : _list.IndexOf(item);

        int IList.IndexOf(object value) => IndexOf((T?)value);

        public void Insert(int index, T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            _list.Insert(index, item);
            OnItemSet(item, index);
            OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Add(this, item, index));
        }

        void IList.Insert(int index, object value) => Insert(index, (T?)value);

        public void InsertRange(int index, IList<T?> collection)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            _list.InsertRange(index, collection);
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                OnItemSet(item, i + index);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Add(this, collection, index));
        }

        public bool Remove(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.IndexOf(item);
            if (index >= 0)
            {
                _list.RemoveAt(index);
                OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Remove(this, item, index));
                TryUnWatch(item);
                return true;
            }
            return false;
        }

        void IList.Remove(object value) => Remove((T?)value);

        public void RemoveRange(int index, int count)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var arr = new T?[count];
            _list.CopyTo(index, arr, 0, count);
            OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Remove(this, arr, index));
            foreach (var item in arr)
            {
                TryUnWatch(item);
            }
            _list.RemoveRange(index, count);
        }

        public void RemoveAt(int index)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var item = _list[index];
            _list.RemoveAt(index);
            OnCollectionChange(CollectionChangeInfo<ObservableList<T>, T?>.Remove(this, item, index));
            TryUnWatch(item);
        }

        void IList.RemoveAt(int index) => RemoveAt(index);

        public void Move(int oldIndex, int newIndex)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var item = _list[oldIndex];
            RemoveAt(oldIndex);
            Insert(newIndex, item);
        }
    }
}
