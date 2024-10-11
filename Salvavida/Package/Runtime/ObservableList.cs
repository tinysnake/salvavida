using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableList<T> : ObservableCollection<ObservableList<T>, T>, IEnumerable<T?>, IEnumerable, IList<T?>, IReadOnlyList<T?>, IList, ICollectionWrapper<List<T?>>
    {
        public ObservableList(string propName, List<T?> src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _list = default!;
            _isSavable = SvHelper.CheckIsSavable<T>();
            _orderMatters = SvHelper.CheckIsSaveWithOrder<T>();
            SwapSource(src, false);
        }

        private List<T?> _list;
        private readonly bool _isSavable;
        private readonly bool _orderMatters;

        public override bool IsSavableCollection => _isSavable;

        public T? this[int index]
        {
            get => _list[index];
            set
            {
                var oldValue = _list[index];
                TryUnWatch(oldValue);
                _list[index] = value;
                if (_orderMatters && value is ISaveWithOrder swo)
                    swo.SvOrder = index;
                TryWatch(value);
                OnCollectionChange(CollectionChangeInfo<T?>.Replace(oldValue, value, index));
            }
        }

        object? IList.this[int index] { get => _list[index]; set => this[index] = (T?)value; }

        public int Count => _list.Count;

        bool IList.IsFixedSize => false;

        int ICollection.Count => _list.Count;

        bool ICollection<T?>.IsReadOnly => false;

        bool IList.IsReadOnly => false;

        bool ICollection.IsSynchronized => ((ICollection)_list).IsSynchronized;

        object ICollection.SyncRoot => ((ICollection)_list).SyncRoot;

        public List<T?> RetrieveSource() => _list;

        public void SwapSource(List<T?> list)
        {
            _isDirty = true;
            SwapSource(list, true);
        }

        private void SwapSource(List<T?> list, bool notifyChanges)
        {
            if (_list != null)
            {
                for (var i = 0; i < _list.Count; i++)
                {
                    TryUnWatch(_list[i]);
                }
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<T?>.Reset());
            }
            _list = list;
            if (_list != null)
            {
                for (var i = 0; i < _list.Count; i++)
                {
                    if (_orderMatters && _list[i] is ISaveWithOrder swo)
                        swo.SvOrder = i;
                    TryWatch(_list[i]);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        protected override void OnChildChanged(T obj, string _)
        {
            var index = IndexOf(obj);
            if (index >= 0)
                OnCollectionChange(CollectionChangeInfo<T?>.Replace(obj, obj, index));
        }

        protected override CollectionChangeInfo<T?> CreateSaveAllEvent()
        {
            return CollectionChangeInfo<T?>.Add(_list, 0);
        }

        protected override void TrySaveItems(Serializer serializer, PathBuilder path, CollectionChangeInfo<T?> e)
        {
            base.TrySaveItems(serializer, path, e);
            if (!_orderMatters)
                return;
            if (e.Action == CollectionChangedAction.Add && e.NewStartingIndex >= 0)
            {
                var count = e.IsSingleItem ? 1 : e.NewItems!.Count;
                var index = e.NewStartingIndex + count;
                TryUpdateOrder(serializer, path, index);
            }
            else if (e.Action == CollectionChangedAction.Remove && e.OldStartingIndex >= 0)
            {
                var index = e.OldStartingIndex;
                TryUpdateOrder(serializer, path, index);
            }
        }

        protected override void TrySaveSource(Serializer serializer, PathBuilder pathBuilder)
        {
            if (string.IsNullOrEmpty(SvId))
                throw new NullReferenceException(nameof(SvId));
            serializer.SaveList(_list, pathBuilder);
        }

        private void TryUpdateOrder(Serializer serializer, PathBuilder pathBuilder, int index)
        {
            var count = _list.Count - index;
            if (count <= 0)
                return;
            var list = new List<T?>();
            for (var i = index; i < _list.Count; i++)
            {
                var item = _list[i];
                if (item is ISaveWithOrder swo)
                    swo.SvOrder = i;
                list.Add(_list[i]);
            }
            CollectionUpdateOrder(serializer, pathBuilder, list);
        }

        public void Add(T? item)
        {
            var index = _list.Count;
            _list.Add(item);
            if (_orderMatters && item is ISaveWithOrder swo)
                swo.SvOrder = index;
            TryWatch(item);
            OnCollectionChange(CollectionChangeInfo<T?>.Add(item, index));
        }

        public void AddRange(IList<T?> collection)
        {
            var index = _list.Count;
            _list.AddRange(collection);
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                if (item is ISaveWithOrder swo)
                    swo.SvOrder = index + i;
                TryWatch(item);
            }
            OnCollectionChange(CollectionChangeInfo<T?>.Add(collection, index));
        }

        public void Clear()
        {
            foreach (var item in _list)
            {
                TryUnWatch(item);
            }
            _list.Clear();
            OnCollectionChange(CollectionChangeInfo<T?>.Reset());
        }

        public bool Contains(T? item) => _list.Contains(item);

        public void CopyTo(T?[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

        public List<T?>.Enumerator GetEnumerator() => _list.GetEnumerator();

        IEnumerator<T?> IEnumerable<T?>.GetEnumerator() => GetEnumerator();

        public int IndexOf(T? item) => _list.IndexOf(item);

        public void Insert(int index, T? item)
        {
            _list.Insert(index, item);
            if (item is ISaveWithOrder swo)
                swo.SvOrder = index;
            TryWatch(item);
            OnCollectionChange(CollectionChangeInfo<T?>.Add(item, index));
        }

        public void InsertRange(int index, IList<T?> collection)
        {
            _list.InsertRange(index, collection);
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                if (item is ISaveWithOrder swo)
                    swo.SvOrder = index + i;
                TryWatch(item);
            }
            OnCollectionChange(CollectionChangeInfo<T?>.Add(collection, index));
        }

        public bool Remove(T? item)
        {
            var index = _list.IndexOf(item);
            if (index >= 0)
            {
                _list.RemoveAt(index);
                TryUnWatch(item);
                OnCollectionChange(CollectionChangeInfo<T?>.Remove(item, index));
                return true;
            }
            return false;
        }

        public void RemoveRange(int index, int count)
        {
            var arr = new T?[count];
            _list.CopyTo(index, arr, 0, count);
            foreach (var item in arr)
            {
                TryUnWatch(item);
            }
            _list.RemoveRange(index, count);
            OnCollectionChange(CollectionChangeInfo<T?>.Remove(arr, index));
        }

        public void RemoveAt(int index)
        {
            var item = _list[index];
            TryUnWatch(item);
            _list.RemoveAt(index);
            OnCollectionChange(CollectionChangeInfo<T?>.Remove(item, index));
        }

        public void Move(int oldIndex, int newIndex)
        {
            var item = _list[oldIndex];
            RemoveAt(oldIndex);
            Insert(newIndex, item);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        int IList.Add(object value)
        {
            Add((T?)value);
            return Count - 1;
        }
        void IList.Clear() => Clear();

        bool IList.Contains(object value) => Contains((T?)value);

        int IList.IndexOf(object value) => IndexOf((T?)value);

        void IList.Insert(int index, object value) => Insert(index, (T?)value);

        void IList.Remove(object value) => Remove((T?)value);

        void IList.RemoveAt(int index) => RemoveAt(index);

        void ICollection.CopyTo(Array array, int index)
        {
            ((ICollection)_list).CopyTo(array, index);
        }
    }
}
