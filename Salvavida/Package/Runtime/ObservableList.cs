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
            //_orderMatters = SvHelper.CheckIsSaveWithOrder<T>();
            SwapSource(src, false);
        }

        private List<T?>? _list;
        //private readonly bool _orderMatters;

        public T? this[int index]
        {
            get => _list == null ? throw new NullReferenceException(nameof(_list)) : _list[index];
            set
            {
                if (_list == null)
                    throw new NullReferenceException(nameof(_list));
                var oldValue = _list[index];
                _list[index] = value;
                OnItemSet(value, index);
                OnCollectionChange(CollectionChangeInfo<T?>.Replace(oldValue, value, index));
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
            serializer.SaveObject(_list, ctx, SvId, PathBuilder.Type.Property);
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            var list = serializer.ReadObject<List<T?>>(ctx);
            SwapSource(list);
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
                    OnCollectionChange(CollectionChangeInfo<T?>.Reset());
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
                    OnItemSet(_list[i], i);
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
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            return CollectionChangeInfo<T?>.Add(_list, 0);
        }

        //protected override void TrySaveSeparatelyByEvent(Serializer serializer, SerializeContext? ctx, CollectionChangeInfo<T?> e)
        //{
        //    if (!SaveSeparately)
        //        throw new NotSupportedException();
        //    if (string.IsNullOrEmpty(SvId))
        //        throw new NullReferenceException(nameof(SvId));
        //    if (ctx == null)
        //        serializer.FreshAction(this, path => serializer.SaveList(_list, path), null);
        //    else
        //        serializer.SaveList(_list, ctx);
        //}

        public void Add(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.Count;
            _list.Add(item);
            OnItemSet(item, index);
            OnCollectionChange(CollectionChangeInfo<T?>.Add(item, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Add(collection, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Reset());
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
            OnCollectionChange(CollectionChangeInfo<T?>.Add(item, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Add(collection, index));
        }

        public bool Remove(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.IndexOf(item);
            if (index >= 0)
            {
                _list.RemoveAt(index);
                OnCollectionChange(CollectionChangeInfo<T?>.Remove(item, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Remove(arr, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Remove(item, index));
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

    public sealed class ObservableListSavable<T> : ObservableCollectionSavable<ObservableListSavable<T>, T>, IList<T?>, IReadOnlyList<T?>, IList, ICollectionWrapper<List<T?>>
        where T : ISavable
    {
        public ObservableListSavable(string propName, List<T?>? src, bool saveSeparately)
           : base(propName, saveSeparately)
        {
            //_orderMatters = SvHelper.CheckIsSaveWithOrder<T>();
            SwapSource(src, false);
        }

        private List<T?>? _list;
        private string[]? _idsOnDeserialized;
        //private readonly bool _orderMatters;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty)
                    return true;
                if (_list == null)
                    return false;
                foreach (var item in _list)
                {
                    if (item == null)
                        continue;
                    if (item.IsDirty)
                        return true;
                }

                return false;
            }
        }

        public T? this[int index]
        {
            get => _list == null ? throw new NullReferenceException(nameof(_list)) : _list[index];
            set
            {
                if (_list == null)
                    throw new NullReferenceException(nameof(_list));
                var oldValue = _list[index];
                _list[index] = value;
                OnItemSet(value, index);
                OnCollectionChange(CollectionChangeInfo<T?>.Replace(oldValue, value, index));
                TryUnWatch(oldValue);
            }
        }

        object? IList.this[int index] { get => this[index]; set => this[index] = (T?)value; }

        public int Count => _list?.Count ?? 0;

        bool IList.IsFixedSize => false;

        int ICollection.Count => _list?.Count ?? 0;

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

            var tempIds = SvHelper.idListPool.Get();
            try
            {
                if (_list != null)
                {
                    foreach (var elem in _list)
                    {
                        if (elem == null)
                            continue;
                        if (string.IsNullOrEmpty(elem.SvId))
                            throw new ArgumentNullException("elem.SvId");
                        serializer.SaveObject(elem, ctx, elem.SvId, PathBuilder.Type.Collection);
                        tempIds.Add(elem.SvId);
                    }
                    serializer.SaveObject(tempIds, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
                }
                else
                {
                    serializer.DeleteObject(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
                }
                if (_idsOnDeserialized != null)
                {
                    foreach (var oldId in _idsOnDeserialized)
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

            _idsOnDeserialized = null;
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            string[]? tempIds = null;
            using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection))
            {
                if(serializer.Has(ctx))
                    tempIds = serializer.ReadObject<string[]?>(ctx);
            }
            List<T?>? list;
            if (tempIds == null || tempIds.Length == 0)
                list = null;
            else
            {
                _idsOnDeserialized = tempIds.ToArray();
                list = new List<T?>();
                foreach (var id in _idsOnDeserialized)
                {
                    list.Add(serializer.ReadObject<T?>(ctx, id, PathBuilder.Type.Collection));
                }
            }
            SwapSource(list);
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
                    OnCollectionChange(CollectionChangeInfo<T?>.Reset());
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
                    OnItemSet(_list[i], i);
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
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            return CollectionChangeInfo<T?>.Add(_list, 0);
        }

        public void Add(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.Count;
            _list.Add(item);
            OnItemSet(item, index);
            OnCollectionChange(CollectionChangeInfo<T?>.Add(item, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Add(collection, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Reset());
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
            OnCollectionChange(CollectionChangeInfo<T?>.Add(item, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Add(collection, index));
        }

        public bool Remove(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.IndexOf(item);
            if (index >= 0)
            {
                _list.RemoveAt(index);
                OnCollectionChange(CollectionChangeInfo<T?>.Remove(item, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Remove(arr, index));
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
            OnCollectionChange(CollectionChangeInfo<T?>.Remove(item, index));
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

        //protected override void TrySaveItems(Serializer serializer, SerializeContext? ctx, CollectionChangeInfo<T?> e)
        //{
        //    base.TrySaveItems(serializer, ctx, e);
        //    if (!_orderMatters)
        //        return;
        //    if (e.Action == CollectionChangedAction.Add && e.NewStartingIndex >= 0)
        //    {
        //        var count = e.IsSingleItem ? 1 : e.NewItems!.Count;
        //        var index = e.NewStartingIndex + count;
        //        TryUpdateOrder(serializer, ctx, index);
        //    }
        //    else if (e.Action == CollectionChangedAction.Remove && e.OldStartingIndex >= 0)
        //    {
        //        var index = e.OldStartingIndex;
        //        TryUpdateOrder(serializer, ctx, index);
        //    }
        //}

        //private void TryUpdateOrder(Serializer serializer, SerializeContext? ctx, int index)
        //{
        //    var count = _list.Count - index;
        //    if (count <= 0)
        //        return;
        //    var list = new List<T?>();
        //    for (var i = index; i < _list.Count; i++)
        //    {
        //        var item = _list[i];
        //        if (item == null)
        //            continue;
        //        if (item is ISaveWithOrder swo)
        //            swo.SvOrder = i;
        //        item.SvId = i.ToString();
        //        list.Add(_list[i]);
        //    }
        //    CollectionUpdateOrder(serializer, ctx, list);
        //}
    }
}
