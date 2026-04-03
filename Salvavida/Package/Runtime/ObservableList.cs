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

    public sealed class ObservableListSavable<T> : ObservableListSavableBase<ObservableListSavable<T>, T>, ICollectionWrapper<List<T?>>
        where T : ISavable
    {
        public ObservableListSavable(string propName, List<T?>? src, bool saveSeparately)
           : base(propName, saveSeparately)
        {
            //_orderMatters = SvHelper.CheckIsSaveWithOrder<T>();
            SwapSource(src, false);
        }

        private List<T?>? _list;
        private HashSet<string> _idsDeleted = new();
        private bool _needsRebalance;
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

        public override T? this[int index]
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
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Replace(this, oldValue, value, index));
                TryUnWatch(oldValue);
            }
        }

        public override int Count => _list?.Count ?? 0;

        public List<T?>? RetrieveSource() => _list;

        public object? RetrieveSourceRaw() => _list;

        public Type CollectionType => typeof(List<T?>);

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);
            if (recursive && _list != null)
            {
                _isChildrenDirty = dirty;
                foreach (var item in _list)
                {
                    item?.SetDirty(dirty, recursive);
                }
            }
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            if (_needsRebalance && _list != null)
            {
                var newRanks = LexoRank.Rebalance(_list.Count);
                var oldIds = SvHelper.idListPool.Get();
                try
                {
                    for (int i = 0; i < _list.Count; i++)
                    {
                        if (_list[i] != null && !string.IsNullOrEmpty(_list[i].SvId))
                            oldIds.Add(_list[i].SvId);
                        _list[i].SvId = LexoRank.DEFAULT_PREFIX + "~" + newRanks[i];
                    }
                    for (int i = 0; i < _list.Count; i++)
                    {
                        serializer.Save(_list[i], ctx, PathBuilder.Type.Collection);
                    }
                    foreach (var oldId in oldIds)
                    {
                        serializer.Delete(ctx, oldId, PathBuilder.Type.Collection);
                    }
                }
                finally
                {
                    SvHelper.idListPool.Return(oldIds);
                }
                _needsRebalance = false;
            }

            for (int i = 0; i < _list.Count; i++)
            {
                var item = _list[i];
                if (item != null && item.IsDirty)
                {
                    serializer.Save(item, ctx, PathBuilder.Type.Collection);
                }
            }

            if (_idsDeleted.Count > 0)
            {
                foreach (var deletedId in _idsDeleted)
                {
                    serializer.Delete(ctx, deletedId, PathBuilder.Type.Collection);
                }
                _idsDeleted.Clear();
            }

            var metadata = new CollectionMetadata
            {
                Count = _list?.Count ?? 0,
                IsLazyLoaded = false
            };
            serializer.Save(metadata, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection);
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            using var listScope = ctx.Path.UsePush(_svid!, PathBuilder.Type.Property);
            var ids = serializer.ListCollectionIds(ctx, _svid!);
            var list = new List<T?>();
            foreach (var id in ids)
            {
                var item = serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection);
                list.Add(item);
                OnChildDeserialized(item);
            }
            _idsDeleted.Clear();
            SwapSource(list, false);
        }

        public override void SwapSource(List<T?>? list)
        {
            _isDirty = true;
            SwapSource(list, true);
        }

        private void SwapSource(List<T?>? list, bool notifyChanges)
        {
            if (_list != null)
            {
                if (notifyChanges)
                {
                    foreach (var item in _list)
                    {
                        if (item != null && !string.IsNullOrEmpty(item.SvId))
                            _idsDeleted.Add(item.SvId);
                    }
                    OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
                }
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

        private CollectionChangeInfo<ObservableListSavableBase<T>, T?> CreateSaveAllEvent()
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            return CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, _list, 0);
        }

        public override void Add(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.Count;
            _list.Add(item);
            OnItemSet(item, index);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
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
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, collection, index));
        }

        private void OnItemSet(T? item, int index)
        {
            //if (_orderMatters && item is ISaveWithOrder swo)
            //    swo.SvOrder = index;
            if (item is ISavable sv)
                sv.SvId ??= DefaultIdGenerator.Default.GetId();
            TryWatch(item);
        }

        public override void Clear()
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
            foreach (var item in _list)
            {
                if (item != null && !string.IsNullOrEmpty(item.SvId))
                    _idsDeleted.Add(item.SvId);
                TryUnWatch(item);
            }
            _list.Clear();
        }

        public override bool Contains(T? item) => _list == null ? throw new NullReferenceException(nameof(_list)) : _list.Contains(item);

        public List<T?>.Enumerator GetEnumeratorStruct() => _list == null ? throw new NullReferenceException(nameof(_list)) : _list.GetEnumerator();

        public override IEnumerator<T?> GetEnumerator() => GetEnumeratorStruct();

        public override int IndexOf(T? item) => _list == null ? throw new NullReferenceException(nameof(_list)) : _list.IndexOf(item);

        public override void Insert(int index, T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));

            string? prevId = index > 0 ? _list[index - 1]?.SvId : null;
            string? nextId = index < _list.Count ? _list[index]?.SvId : null;

            item.SvId = LexoRank.Between(prevId, nextId);

            var rankParts = item.SvId.Split('~');
            if (rankParts.Length == 2 && rankParts[1].Length > LexoRank.REBALANCE_LENGTH_THRESHOLD)
                _needsRebalance = true;

            _list.Insert(index, item);
            OnItemSet(item, index);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
        }

        public void InsertRange(int index, IList<T?> collection)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            _list.InsertRange(index, collection);
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                string? prevId = (index + i) > 0 ? _list[index + i - 1]?.SvId : null;
                string? nextId = (index + i + 1) < _list.Count ? _list[index + i + 1]?.SvId : null;
                item.SvId = LexoRank.Between(prevId, nextId);
                OnItemSet(item, i + index);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, collection, index));
        }

        public override bool Remove(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.IndexOf(item);
            if (index >= 0)
            {
                if (item != null && !string.IsNullOrEmpty(item.SvId))
                    _idsDeleted.Add(item.SvId);
                _list.RemoveAt(index);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, item, index));
                TryUnWatch(item);
                return true;
            }
            return false;
        }

        public void RemoveRange(int index, int count)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var arr = new T?[count];
            _list.CopyTo(index, arr, 0, count);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, arr, index));
            foreach (var item in arr)
            {
                if (item != null && !string.IsNullOrEmpty(item.SvId))
                    _idsDeleted.Add(item.SvId);
                TryUnWatch(item);
            }
            _list.RemoveRange(index, count);
        }

        public override void RemoveAt(int index)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var item = _list[index];
            if (item != null && !string.IsNullOrEmpty(item.SvId))
                _idsDeleted.Add(item.SvId);
            _list.RemoveAt(index);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, item, index));
            TryUnWatch(item);
        }

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
