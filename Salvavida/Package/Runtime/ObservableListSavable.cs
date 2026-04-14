using System;
using System.Buffers;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableListSavable<T> : ObservableListSavableBase<ObservableListSavable<T>, T>, ICollectionWrapper<List<T?>>
        where T : ISavable
    {
        public ObservableListSavable(string propName, List<T?>? src, bool saveSeparately)
           : base(propName, saveSeparately)
        {
            SwapSource(src, false);
        }

        private List<T?>? _items;
        private HashSet<string> _idsDeleted = new();
        // Null entries: index → (lexo-rank id, isDirty). Non-null items store their ID in item.SvId.
        private Dictionary<int, (string id, bool isDirty)> _nullSlots = new();
        private bool _needsRebalance;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty)
                    return true;
                if (_items == null)
                    return false;
                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    if (item != null ? item.IsDirty : (_nullSlots.TryGetValue(i, out var s) && s.isDirty))
                        return true;
                }
                return false;
            }
        }

        private string? GetIdAt(int index)
        {
            var item = _items![index];
            if (item != null) return item.SvId;
            return _nullSlots.TryGetValue(index, out var s) ? s.id : null;
        }

        public override T? this[int index]
        {
            get
            {
                if (_items == null) throw new NullReferenceException(nameof(_items));
                return _items[index];
            }
            set
            {
                if (_items == null) throw new NullReferenceException(nameof(_items));
                var oldValue = _items[index];
                if (EqualityComparer<T?>.Default.Equals(oldValue, value))
                    return;
                var oldId = GetIdAt(index);
                var newId = oldId ?? GenerateIdForIndex(index);
                if (!string.IsNullOrEmpty(oldId) && oldId != newId)
                    _idsDeleted.Add(oldId);
                _items[index] = value;
                if (value != null)
                {
                    value.SvId = newId;
                    _nullSlots.Remove(index);
                }
                else
                {
                    _nullSlots[index] = (newId, true);
                }
                TryWatch(value);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Replace(this, oldValue, value, index));
                TryUnWatch(oldValue);
            }
        }

        public override int Count
        {
            get
            {
                if (_items == null) throw new NullReferenceException(nameof(_items));
                return _items.Count;
            }
        }

        public List<T?>? RetrieveSource() => _items;

        public object? RetrieveSourceRaw() => RetrieveSource();

        public Type CollectionType => typeof(List<T?>);

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);
            if (_items == null || !recursive)
                return;
            _isChildrenDirty = dirty;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i]?.SetDirty(dirty, recursive);
                if (_items[i] == null && _nullSlots.TryGetValue(i, out var s))
                    _nullSlots[i] = (s.id, dirty);
            }
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            if (_items != null && _needsRebalance)
            {
                var precisionDigits = Math.Max(DEFAULT_PRECISION_DIGITS, LexoRank.CalculatePrecisionDigits(_items.Count));
                var initRank = LexoRank.GetInitValue(precisionDigits);
                var rank = LexoRank.Rebalance(initRank, _items.Count, out int step, out bool reverseOrder);
                serializer.DeleteAllNoPushPath(ctx);

                // First pass: assign new ranks; null slots written as clean (about to be saved)
                _nullSlots.Clear();
                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    if (item != null)
                        item.SvId = rank;
                    else
                        _nullSlots[i] = (rank, false);
                    rank = LexoRank.Generate(rank, null, precisionDigits, stepSize: step);
                }

                // Second pass: save all items using updated IDs
                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    var id = GetIdAt(i);
                    if (item == null)
                        serializer.Save<T?>(default, ctx, id, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                        item.Serialize(serializer, ctx);
                    }
                }
                _needsRebalance = false;
            }
            else if (_items != null)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    if (item != null ? !item.IsDirty : !(_nullSlots.TryGetValue(i, out var s) && s.isDirty))
                        continue;
                    var id = GetIdAt(i);
                    if (item == null)
                        serializer.Save<T?>(default, ctx, id, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                        item.Serialize(serializer, ctx);
                    }
                }
            }

            if (_items == null)
                serializer.Delete(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
            else
                SaveMetadata(serializer, ctx, _items.Count);

            if (_idsDeleted.Count > 0)
            {
                foreach (var deletedId in _idsDeleted)
                    serializer.Delete(ctx, deletedId, PathBuilder.Type.Collection);
                _idsDeleted.Clear();
            }
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            SwapSource(null, false);

            _items = null;
            using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property))
            {
                if (serializer.HasNoPushPath(ctx))
                {
                    CollectionMetadata metadata = serializer.Read<CollectionMetadata>(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
                    _items = new List<T?>(metadata.Count);
                }
            }

            if (_items == null)
                return;

            var ids = serializer.ListCollectionIds(ctx);
            foreach (var id in ids)
            {
                var item = serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection);
                var index = _items.Count;
                if (item != null)
                    item.SvId = id;
                else
                    _nullSlots[index] = (id, false);  // not dirty after deserialization
                _items.Add(item);
                TryWatch(item);
                OnChildDeserialized(item);
            }

            _idsDeleted.Clear();
            _isDirty = false;
        }

        public override void SwapSource(List<T?>? list)
        {
            SwapSource(list, true);
        }

        private void SwapSource(List<T?>? list, bool notifyChanges)
        {
            if (_items != null)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var id = GetIdAt(i);
                    if (!string.IsNullOrEmpty(id))
                        _idsDeleted.Add(id);
                    TryUnWatch(_items[i]);
                }
                _items.Clear();
            }

            if (notifyChanges)
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));

            _items = list;
            _nullSlots.Clear();
            _isDirty = true;

            if (_items != null)
            {
                var id = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
                for (var i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    if (item != null)
                        item.SvId = id;
                    else
                        _nullSlots[i] = (id, true);
                    id = LexoRank.Generate(id, null, DEFAULT_PRECISION_DIGITS);
                    TryWatch(item);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private CollectionChangeInfo<ObservableListSavableBase<T>, T?> CreateSaveAllEvent()
        {
            return CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, _items!, 0);
        }

        public override void Add(T? item)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            var index = _items.Count;
            var id = GenerateIdForIndex(index);
            if (item != null)
                item.SvId = id;
            else
                _nullSlots[index] = (id, true);
            _items.Add(item);
            TryWatch(item);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
        }

        public void AddRange(IList<T?> collection)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            var index = _items.Count;
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                var id = GenerateIdForIndex(index + i);
                if (item != null)
                    item.SvId = id;
                else
                    _nullSlots[index + i] = (id, true);
                _items.Add(item);
                TryWatch(item);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, collection, index));
        }

        private string GenerateIdForIndex(int index)
        {
            if (_items!.Count == 0)
                return LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);

            string? prevId = index > 0 ? GetIdAt(index - 1) : null;
            string? nextId = index < _items.Count ? GetIdAt(index) : null;
            return GenerateIdBetween(prevId, nextId);
        }

        public override void Clear()
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
            for (int i = 0; i < _items.Count; i++)
            {
                var id = GetIdAt(i);
                if (!string.IsNullOrEmpty(id))
                    _idsDeleted.Add(id);
                TryUnWatch(_items[i]);
            }
            _items.Clear();
            _nullSlots.Clear();
        }

        public override bool Contains(T? item)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            foreach (var i in _items)
            {
                if (EqualityComparer<T?>.Default.Equals(i, item))
                    return true;
            }
            return false;
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            return _items.GetEnumerator();
        }

        public override int IndexOf(T? item)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            return _items.IndexOf(item);
        }

        public override void Insert(int index, T? item)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            // Get neighbor IDs before shifting null tracking
            string? prevId = index > 0 ? GetIdAt(index - 1) : null;
            string? nextId = index < _items.Count ? GetIdAt(index) : null;
            var id = GenerateIdBetween(prevId, nextId);

            if (item != null)
                item.SvId = id;

            if (ShouldRebalance(id))
                _needsRebalance = true;

            ShiftNullTrackingOnInsert(index);

            if (item == null)
                _nullSlots[index] = (id, true);

            _items.Insert(index, item);
            TryWatch(item);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
        }

        public void InsertRange(int index, IList<T?> collection)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            for (var i = 0; i < collection.Count; i++)
            {
                var currentIndex = index + i;
                var item = collection[i];
                // Get neighbor IDs before shifting null tracking for this insertion
                string? prevId = currentIndex > 0 ? GetIdAt(currentIndex - 1) : null;
                string? nextId = currentIndex < _items.Count ? GetIdAt(currentIndex) : null;
                var id = GenerateIdBetween(prevId, nextId);

                if (ShouldRebalance(id))
                    _needsRebalance = true;

                ShiftNullTrackingOnInsert(currentIndex);

                if (item != null)
                    item.SvId = id;
                else
                    _nullSlots[currentIndex] = (id, true);
                _items.Insert(currentIndex, item);
                TryWatch(item);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, collection, index));
        }

        public override bool Remove(T? item)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            var index = IndexOf(item);
            if (index >= 0)
            {
                var id = GetIdAt(index);
                if (!string.IsNullOrEmpty(id))
                    _idsDeleted.Add(id);
                ShiftNullTrackingOnRemove(index);
                _items.RemoveAt(index);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, item, index));
                TryUnWatch(item);
                return true;
            }
            return false;
        }

        public void RemoveRange(int index, int count)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            var arr = new T?[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = _items[index + i];
                var id = GetIdAt(index + i);
                if (!string.IsNullOrEmpty(id))
                    _idsDeleted.Add(id);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, arr, index));
            foreach (var item in arr)
                TryUnWatch(item);
            ShiftNullTrackingOnRemoveRange(index, count);
            _items.RemoveRange(index, count);
        }

        public override void RemoveAt(int index)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            var item = _items[index];
            var id = GetIdAt(index);
            if (!string.IsNullOrEmpty(id))
                _idsDeleted.Add(id);
            ShiftNullTrackingOnRemove(index);
            _items.RemoveAt(index);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, item, index));
            TryUnWatch(item);
        }

        public void Move(int oldIndex, int newIndex)
        {
            if (_items == null) throw new NullReferenceException(nameof(_items));
            var item = _items[oldIndex];
            RemoveAt(oldIndex);
            Insert(newIndex, item);
        }

        #region Null tracking helpers

        private void ShiftNullTrackingOnInsert(int insertedIndex)
        {
            if (_nullSlots == null || _nullSlots.Count == 0) return;
            if (_nullSlots.Count < 1000)
                ShiftNullInsertCore(insertedIndex, 1, stackalloc int[_nullSlots.Count]);
            else
            {
                var arr = ArrayPool<int>.Shared.Rent(_nullSlots.Count);
                try { ShiftNullInsertCore(insertedIndex, 1, arr.AsSpan(0, _nullSlots.Count)); }
                finally { ArrayPool<int>.Shared.Return(arr); }
            }
        }

        // Shifts keys >= insertedIndex up by shift. Processes high-to-low so adjacent entries
        // don't stomp on each other: _nullSlots[id+shift] is set before _nullSlots[id] is removed.
        // Keys in _nullSlots are always in ascending iteration order (maintained by all mutating ops),
        // so buf is already sorted ascending and we can walk it in reverse without an explicit sort.
        private void ShiftNullInsertCore(int insertedIndex, int shift, Span<int> buf)
        {
            int n = 0;
            foreach (var kvp in _nullSlots)
                if (kvp.Key >= insertedIndex) buf[n++] = kvp.Key;
            if (n == 0) return;
            int len = n;
            while (len-- > 0)
            {
                int id = buf[len];
                _nullSlots[id + shift] = _nullSlots[id];
                _nullSlots.Remove(id);
            }
        }

        private void ShiftNullTrackingOnRemove(int removedIndex)
        {
            if (_nullSlots == null || _nullSlots.Count == 0) return;
            _nullSlots.Remove(removedIndex);
            if (_nullSlots.Count < 1000)
                ShiftNullRemoveCore(removedIndex, 1, stackalloc int[_nullSlots.Count]);
            else
            {
                var arr = ArrayPool<int>.Shared.Rent(_nullSlots.Count);
                try { ShiftNullRemoveCore(removedIndex, 1, arr.AsSpan(0, _nullSlots.Count)); }
                finally { ArrayPool<int>.Shared.Return(arr); }
            }
        }

        private void ShiftNullTrackingOnRemoveRange(int startIndex, int count)
        {
            if (_nullSlots == null || _nullSlots.Count == 0) return;
            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++)
                _nullSlots.Remove(i);
            // keys > endIndex-1 (i.e. >= endIndex) shift down by count
            if (_nullSlots.Count < 1000)
                ShiftNullRemoveCore(endIndex - 1, count, stackalloc int[_nullSlots.Count]);
            else
            {
                var arr = ArrayPool<int>.Shared.Rent(_nullSlots.Count);
                try { ShiftNullRemoveCore(endIndex - 1, count, arr.AsSpan(0, _nullSlots.Count)); }
                finally { ArrayPool<int>.Shared.Return(arr); }
            }
        }

        // Shifts keys > startExclusive down by shift. Processes low-to-high so adjacent entries
        // don't stomp on each other: _nullSlots[id-shift] is set before _nullSlots[id] is removed.
        // Keys in _nullSlots are always in ascending iteration order, so buf is already sorted.
        private void ShiftNullRemoveCore(int startExclusive, int shift, Span<int> buf)
        {
            int n = 0;
            foreach (var kvp in _nullSlots)
                if (kvp.Key > startExclusive) buf[n++] = kvp.Key;
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                int id = buf[i];
                _nullSlots[id - shift] = _nullSlots[id];
                _nullSlots.Remove(id);
            }
        }

        #endregion
    }
}
