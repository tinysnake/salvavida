using System;
using System.Collections.Generic;

namespace Salvavida
{
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
        private readonly HashSet<string> _idsDeleted = new();
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

            if(_list == null)
                return;

            if (_needsRebalance)
            {
                var precisionDigits = Math.Max(DEFAULT_PRECISION_DIGITS, LexoRank.CalculatePrecisionDigits(_list.Count));
                var initRank = LexoRank.GetInitValue(precisionDigits);
                var rank = LexoRank.Rebalance(initRank, _list.Count, out int step, out bool reverseOrder);
                var oldIds = SvHelper.idListPool.Get();
                Span<char> rankBuffer = stackalloc char[128];
                try
                {
                    for (int i = 0; i < _list.Count; i++)
                    {
                        var slot = _list[i];
                        if (!string.IsNullOrEmpty(slot.Id))
                            oldIds.Add(slot.Id);
                        slot = new Slot(rank, slot.Value, slot.IsDirty);
                        if (slot.Value != null)
                            slot.Value.SvId = rank;
                        _list[i] = slot;
                            rank = LexoRank.Generate(rank, null, precisionDigits, stepSize: step);
                    }
                    for (int i = 0; i < _list.Count; i++)
                    {
                        var slot = _list[i];
                        if(slot.Value == null)
                            serializer.Save<T?>(default, ctx, slot.Id, PathBuilder.Type.Collection);
                        else
                        {
                            using var _ = ctx.Path.UsePush(slot.Id, PathBuilder.Type.Collection); 
                            slot.Value.Serialize(serializer, ctx);
                        }
                        _list[i] = new Slot(slot.Id, slot.Value, false);
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
            else
            {
                for (int i = 0; i < _list.Count; i++)
                {
                    var slot = _list[i];
                    if(!slot.IsTrueDirty)
                        continue;
                    if(slot.Value == null)
                        serializer.Save<T?>(default, ctx, slot.Id, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(slot.Id, PathBuilder.Type.Collection); 
                        slot.Value.Serialize(serializer, ctx);
                    }
                }
            }

            var meta = new CollectionMetadata
            {
                Count = _list?.Count??0,
            };

            serializer.Save(meta, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);

            if (_idsDeleted.Count > 0)
            {
                foreach (var deletedId in _idsDeleted)
                {
                    serializer.Delete(ctx, deletedId, PathBuilder.Type.Collection);
                }
                _idsDeleted.Clear();
            }
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            var list = new List<Slot>();
            var ids = serializer.ListCollectionIds(ctx, _svid!);
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
                var id = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
                for (var i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    var slot = new Slot(id, item, item == null);
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
                return LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);

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
