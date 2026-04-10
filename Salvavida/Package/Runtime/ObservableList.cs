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
        private const int DEFAULT_PRECISION_DIGITS = 2;
        private const int REBALANCE_LENGTH_THRESHOLD = 10;

        public ObservableListSavable(string propName, List<T?>? src, bool saveSeparately)
           : base(propName, saveSeparately)
        {
            SwapSource(src, false);
        }

        private List<Slot>? _list;
        private HashSet<string> _idsDeleted = new();
        private bool _needsRebalance;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty)
                    return true;
                if (_list == null)
                    return false;
                foreach (var slot in _list)
                {
                    if(slot.IsDirty || slot.Value != null && slot.Value.IsDirty)
                        return true;
                }
                return false;
            }
        }

        public override T? this[int index]
        {
            get => _list == null ? throw new NullReferenceException(nameof(_list)) : _list[index].Value;
            set
            {
                if (_list == null)
                    throw new NullReferenceException(nameof(_list));
                var slot = _list[index];
                var oldValue = slot.Value;
                if (EqualityComparer<T?>.Default.Equals(oldValue, value))
                    return;
                var oldId = slot.Id;
                    var newId = slot.Id ?? GenerateIdForIndex(index);
                    if (!string.IsNullOrEmpty(oldId) && oldId != newId)
                        _idsDeleted.Add(oldId);
                if (value != null)
                    value.SvId = newId;
                _list[index] = new Slot(newId, value, true);
                OnItemSet(value, index);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Replace(this, oldValue, value, index));
                TryUnWatch(oldValue);
            }
        }

        public override int Count => _list?.Count ?? 0;

        public List<T?>? RetrieveSource()
        {
            if (_list == null)
                return null;
            var result = new List<T?>(_list.Count);
            foreach (var slot in _list)
                result.Add(slot.Value);
            return result;
        }

        public object? RetrieveSourceRaw() => RetrieveSource();

        public Type CollectionType => typeof(List<T?>);

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);
            if (recursive && _list != null)
            {
                _isChildrenDirty = dirty;
                for(var i = 0;i<_list.Count;i++)
                {
                    var slot = _list[i];
                    slot.Value?.SetDirty(dirty, recursive);
                    _list[i] = new Slot(slot.Id, slot.Value, dirty);
                }
            }
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            if (_needsRebalance && _list != null)
            {
                var precisionDigits = Math.Max(DEFAULT_PRECISION_DIGITS, LexoRank.CalculatePrecisionDigits(_list.Count));
                var startRank = LexoRank.GetInitValue(precisionDigits);
                var startRank2 = LexoRank.Rebalance(startRank, _list.Count, false, out int step, out bool reverseOrder);
                var oldIds = SvHelper.idListPool.Get();
                Span<char> rankBuffer = stackalloc char[128];
                try
                {
                    string rank = startRank2;
                    for (int i = 0; i < _list.Count; i++)
                    {
                        var slot = _list[i];
                        if (!string.IsNullOrEmpty(slot.Id))
                            oldIds.Add(slot.Id);
                        slot = new Slot(rank, slot.Value, slot.IsDirty);
                        if (slot.Value != null)
                            slot.Value.SvId = rank;
                        _list[i] = slot;
                        if (i < _list.Count - 1)
                        {
                            int len = LexoRank.GenNext(rank, precisionDigits, rankBuffer, step);
                            rank = new string(rankBuffer.Slice(0, len));
                        }
                    }
                    for (int i = 0; i < _list.Count; i++)
                    {
                        var slot = _list[i];
                        serializer.Save(slot.Value, ctx, PathBuilder.Type.Collection);
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
            else if (_list != null)
            {
                for (int i = 0; i < _list.Count; i++)
                {
                    var slot = _list[i];
                    if (slot.IsDirty || slot.Value != null && slot.Value.IsDirty)
                    {
                        serializer.Save(slot.Value, ctx, PathBuilder.Type.Collection);
                    }
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
            var list = new List<Slot>(ids.Count());
            foreach (var id in ids)
            {
                var item = serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection);
                list.Add(new Slot(id, item, false));
                OnChildDeserialized(item);
            }
            _idsDeleted.Clear();
            SwapSourceFromSlots(list, false);
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
                    foreach (var slot in _list)
                    {
                        if (!string.IsNullOrEmpty(slot.Id))
                            _idsDeleted.Add(slot.Id);
                    }
                    OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
                }
                for (var i = 0; i < _list.Count; i++)
                {
                    TryUnWatch(_list[i].Value);
                }
            }
            _list = null;
            if (list != null)
            {
                _list = new List<Slot>(list.Count);
                var id = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS, chunkId: "V");
                for (var i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    var slot = new Slot(id, item, false);
                    _list.Add(slot);
                    if (item != null)
                        item.SvId = id;
                    id = LexoRank.Generate(id, null, DEFAULT_PRECISION_DIGITS);
                    if (notifyChanges)
                        TryWatch(item);
                    else
                        OnChildDeserialized(item);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private void SwapSourceFromSlots(List<Slot> list, bool notifyChanges)
        {
            if (_list != null)
            {
                if (notifyChanges)
                {
                    foreach (var slot in _list)
                    {
                        if (!string.IsNullOrEmpty(slot.Id))
                            _idsDeleted.Add(slot.Id);
                    }
                    OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
                }
                for (var i = 0; i < _list.Count; i++)
                {
                    TryUnWatch(_list[i].Value);
                }
            }
            _list = list;
            if (_list != null)
            {
                for (var i = 0; i < _list.Count; i++)
                {
                    var slot = _list[i];
                    if (notifyChanges)
                        TryWatch(slot.Value);
                    else
                        OnChildDeserialized(slot.Value);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private CollectionChangeInfo<ObservableListSavableBase<T>, T?> CreateSaveAllEvent()
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var items = new List<T?>(_list.Count);
            foreach (var slot in _list)
                items.Add(slot.Value);
            return CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, items, 0);
        }

        public override void Add(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.Count;
            var id = GenerateIdForIndex(index);
            var slot = new Slot(id, item, item == null);
            if (item != null)
                item.SvId = id;
            _list.Add(slot);
            OnItemSet(item, index);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
        }

        public void AddRange(IList<T?> collection)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = _list.Count;
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                var id = GenerateIdForIndex(index + i);
                var slot = new Slot(id, item, item == null);
                if (item != null)
                    item.SvId = id;
                _list.Add(slot);
                OnItemSet(item, index + i);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, collection, index));
        }

        private void OnItemSet(T? item, int index)
        {
            TryWatch(item);
        }

        private string GenerateIdForIndex(int index)
        {
            if (_list == null || _list.Count == 0)
                return LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS, chunkId: "V");

            string? prevId = index > 0 ? _list[index - 1].Id : null;
            string? nextId = index < _list.Count ? _list[index].Id : null;

            Span<char> rankBuffer = stackalloc char[128];
            int rankLen = LexoRank.Generate(
                prevId.AsSpan(), nextId.AsSpan(),
                DEFAULT_PRECISION_DIGITS, rankBuffer);
            return new string(rankBuffer.Slice(0, rankLen));
        }

        public override void Clear()
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
            foreach (var slot in _list)
            {
                if (!string.IsNullOrEmpty(slot.Id))
                    _idsDeleted.Add(slot.Id);
                TryUnWatch(slot.Value);
            }
            _list.Clear();
        }

        public override bool Contains(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            foreach (var slot in _list)
            {
                if (EqualityComparer<T?>.Default.Equals(slot.Value, item))
                    return true;
            }
            return false;
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            foreach (var slot in _list)
                yield return slot.Value;
        }

        public override int IndexOf(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            for (int i = 0; i < _list.Count; i++)
            {
                if (EqualityComparer<T?>.Default.Equals(_list[i].Value, item))
                    return i;
            }
            return -1;
        }

        public override void Insert(int index, T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));

            string? prevId = index > 0 ? _list[index - 1].Id : null;
            string? nextId = index < _list.Count ? _list[index].Id : null;

            Span<char> rankBuffer = stackalloc char[128];
            int rankLen = LexoRank.Generate(
                prevId.AsSpan(), nextId.AsSpan(),
                DEFAULT_PRECISION_DIGITS, rankBuffer);
            var id = new string(rankBuffer.Slice(0, rankLen));

            if (item != null)
                item.SvId = id;

            var lexoPart = id.AsSpan();
            int sepIdx = lexoPart.IndexOf('~');
            if (sepIdx >= 0 && lexoPart.Length - sepIdx - 1 > REBALANCE_LENGTH_THRESHOLD)
                _needsRebalance = true;

            _list.Insert(index, new Slot(id, item, item == null));
            OnItemSet(item, index);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
        }

        public void InsertRange(int index, IList<T?> collection)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            Span<char> rankBuffer = stackalloc char[128];
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                string? prevId = (index + i) > 0 ? _list[index + i - 1].Id : null;
                string? nextId = (index + i) < _list.Count ? _list[index + i].Id : null;
                int rankLen = LexoRank.Generate(
                    prevId.AsSpan(), nextId.AsSpan(),
                    DEFAULT_PRECISION_DIGITS, rankBuffer);
                var id = new string(rankBuffer.Slice(0, rankLen));
                if (item != null)
                    item.SvId = id;
                _list.Insert(index + i, new Slot(id, item, item == null));
                OnItemSet(item, index + i);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, collection, index));
        }

        public override bool Remove(T? item)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var index = IndexOf(item);
            if (index >= 0)
            {
                var slot = _list[index];
                if (!string.IsNullOrEmpty(slot.Id))
                    _idsDeleted.Add(slot.Id);
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
            for (int i = 0; i < count; i++)
            {
                arr[i] = _list[index + i].Value;
                if (!string.IsNullOrEmpty(_list[index + i].Id))
                    _idsDeleted.Add(_list[index + i].Id);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, arr, index));
            foreach (var item in arr)
            {
                TryUnWatch(item);
            }
            _list.RemoveRange(index, count);
        }

        public override void RemoveAt(int index)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var slot = _list[index];
            if (!string.IsNullOrEmpty(slot.Id))
                _idsDeleted.Add(slot.Id);
            _list.RemoveAt(index);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, slot.Value, index));
            TryUnWatch(slot.Value);
        }

        public void Move(int oldIndex, int newIndex)
        {
            if (_list == null)
                throw new NullReferenceException(nameof(_list));
            var slot = _list[oldIndex];
            RemoveAt(oldIndex);
            Insert(newIndex, slot.Value);
        }
    }
}
