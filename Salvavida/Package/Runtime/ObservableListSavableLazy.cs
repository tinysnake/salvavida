using System;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable list for ISavable elements.
    /// Elements are loaded on-demand based on LazyLoadMode configuration.
    /// Supports dynamic insert/delete with immediate serializer sync.
    /// Thread-safe with ReaderWriterLockSlim.
    /// </summary>
    public sealed class ObservableListSavableLazy<T> : ObservableListSavableBase<ObservableListSavableLazy<T>, T>
        where T : ISavable
    {
        private List<Slot> _slots;
        private readonly CollectionOptions _options;
        private readonly ReaderWriterLockSlim _lock = new();
        private int _count;
        private int _loadedCount;
        private bool _needsRebalance;
        private uint _version;
        private Serializer? _serializer;

        public ObservableListSavableLazy(string propName, bool saveSeparately, CollectionOptions options)
            : base(propName, saveSeparately)
        {
            if (options.Mode == LazyLoadMode.None)
                throw new InvalidOperationException("LazyLoadMode.None is not supported for lazy-loaded collections. Use ObservableListSavable<T> instead.");

            _slots = new List<Slot>();
            _options = options;
        }

        protected override void SetParent(ISavable? parent)
        {
            base.SetParent(parent);
            _serializer = parent?.GetSerializer();
        }

        private Serializer GetSerializer() => _serializer ?? throw new NullReferenceException("Serializer not available.");

        public override int Count => _count;

        public int LoadedCount => _loadedCount;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty)
                    return true;

                _lock.EnterReadLock();
                try
                {
                    foreach (var slot in _slots)
                    {
                        if (slot.IsTrueDirty)
                            return true;
                    }
                    return false;
                }
                finally { _lock.ExitReadLock(); }
            }
        }

        /// <summary>
        /// Check if element at specified index is loaded.
        /// </summary>
        public bool IsLoaded(int index)
        {
            ValidateIndex(index);
            _lock.EnterReadLock();
            try
            {
                return index < _slots.Count && _slots[index].IsLoaded;
            }
            finally { _lock.ExitReadLock(); }
        }

        public override T? this[int index]
        {
            get
            {
                ValidateIndex(index);

                _lock.EnterUpgradeableReadLock();
                try
                {
                    EnsureSlotCapacity(index);

                    if (_slots[index].IsLoaded)
                        return _slots[index].Value;

                    _lock.EnterWriteLock();
                    try
                    {
                        LoadSlotByIndex(index);
                        return _slots[index].Value;
                    }
                    finally { _lock.ExitWriteLock(); }
                }
                finally { _lock.ExitUpgradeableReadLock(); }
            }
            set
            {
                ValidateIndex(index);

                _lock.EnterWriteLock();
                try
                {
                    EnsureSlotCapacity(index);

                    var oldValue = _slots[index].Value;
                    var oldId = _slots[index].Id;

                    _slots[index] = new Slot(oldId, value, true, true);

                    TryUnWatch(oldValue);
                    if (value != null)
                    {
                        value.SvId = oldId;
                        TryWatch(value);
                    }

                    // Immediate sync to serializer
                    var serializer = GetSerializer();
                    using var locker = serializer.BeginFreshAction(this, out var ctx);

                    if (value == null)
                        serializer.Save<T?>(default, ctx, oldId, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(oldId, PathBuilder.Type.Collection);
                        value.Serialize(serializer, ctx);
                    }

                    _version++;
                    OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Replace(this, oldValue, value, index));
                }
                finally { _lock.ExitWriteLock(); }
            }
        }

        public override bool Contains(T? item)
        {
            _lock.EnterReadLock();
            try
            {
                foreach (var slot in _slots)
                {
                    if (slot.IsLoaded && EqualityComparer<T?>.Default.Equals(slot.Value, item))
                        return true;
                }
                return false;
            }
            finally { _lock.ExitReadLock(); }
        }

        public override int IndexOf(T? item)
        {
            _lock.EnterReadLock();
            try
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].IsLoaded && EqualityComparer<T?>.Default.Equals(_slots[i].Value, item))
                        return i;
                }
                return -1;
            }
            finally { _lock.ExitReadLock(); }
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            var meta = serializer.Read<CollectionMetadata?>(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
            _count = meta?.Count ?? 0;

            _slots.Clear();
            _loadedCount = 0;
            _isDirty = false;
            _needsRebalance = false;
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_slots.Count == 0)
                {
                    SaveMetadata(serializer, ctx, _count);
                    return;
                }

                if (_needsRebalance)
                {
                    SerializeWithRebalance(serializer, ctx);
                }
                else
                {
                    SerializeDirtyItems(serializer, ctx);
                }

                SaveMetadata(serializer, ctx, _count);
            }
            finally { _lock.ExitUpgradeableReadLock(); }
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            var version = _version;
            for (int i = 0; i < _count; i++)
            {
                if(version!= _version)
                    throw new InvalidOperationException("collection was modified");
                _lock.EnterUpgradeableReadLock();
                try
                {
                    if (_slots.Count <= i || !_slots[i].IsLoaded)
                    {
                        _lock.EnterWriteLock();
                        try
                        {
                            LoadSlotByIndex(i);
                        }
                        finally { _lock.ExitWriteLock(); }
                    }

                    yield return _slots[i].Value;
                }
                finally { _lock.ExitUpgradeableReadLock(); }
            }
        }

        public override void Add(T? item)
        {
            _lock.EnterWriteLock();
            try
            {
                var index = _slots.Count;
                var id = GenerateIdForIndex(index);

                if (item != null)
                    item.SvId = id;

                _slots.Add(new Slot(id, item, true, true));
                _count++;
                _loadedCount++;
                _version++;

                TryWatch(item);

                // Immediate sync to serializer
                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                if (item == null)
                    serializer.Save<T?>(default, ctx, id, PathBuilder.Type.Collection);
                else
                {
                    using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                    item.Serialize(serializer, ctx);
                }

                SaveMetadata(serializer, ctx, _count);

                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                foreach (var slot in _slots)
                {
                    if (!string.IsNullOrEmpty(slot.Id))
                        serializer.Delete(ctx, slot.Id, PathBuilder.Type.Collection);
                    TryUnWatch(slot.Value);
                }

                _slots.Clear();
                _count = 0;
                _loadedCount = 0;
                _version++;
                _isDirty = true;
                _needsRebalance = false;

                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));

                SaveMetadata(serializer, ctx, _count);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override void Insert(int index, T? item)
        {
            _lock.EnterWriteLock();
            try
            {
                string? prevId = index > 0 ? _slots[index - 1].Id : null;
                string? nextId = index < _slots.Count ? _slots[index].Id : null;

                var id = GenerateIdBetween(prevId, nextId);

                if (item != null)
                    item.SvId = id;

                _slots.Insert(index, new Slot(id, item, true, true));
                _count++;
                _loadedCount++;
                _version++;

                TryWatch(item);

                if (ShouldRebalance(id))
                    _needsRebalance = true;

                // Immediate sync to serializer
                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                if (item == null)
                    serializer.Save<T?>(default, ctx, id, PathBuilder.Type.Collection);
                else
                {
                    using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                    item.Serialize(serializer, ctx);
                }

                SaveMetadata(serializer, ctx, _count);

                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override bool Remove(T? item)
        {
            _lock.EnterWriteLock();
            try
            {
                var index = IndexOfInternal(item);
                if (index < 0)
                    return false;

                RemoveAtInternal(index);
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override void RemoveAt(int index)
        {
            _lock.EnterWriteLock();
            try
            {
                RemoveAtInternal(index);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override void SwapSource(List<T?>? list)
        {
            _lock.EnterWriteLock();
            try
            {
                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                // Clear existing
                foreach (var slot in _slots)
                {
                    if (!string.IsNullOrEmpty(slot.Id))
                        serializer.Delete(ctx, slot.Id, PathBuilder.Type.Collection);
                    TryUnWatch(slot.Value);
                }
                _slots.Clear();

                if (list != null && list.Count > 0)
                {
                    _slots.Capacity = list.Count;
                    var id = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);

                    for (int i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        if (item != null)
                            item.SvId = id;

                        _slots.Add(new Slot(id, item, true, true));

                        if (item == null)
                            serializer.Save<T?>(default, ctx, id, PathBuilder.Type.Collection);
                        else
                        {
                            using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                            item.Serialize(serializer, ctx);
                        }

                        TryWatch(item);
                        id = LexoRank.Generate(id, null, DEFAULT_PRECISION_DIGITS);
                    }

                    _count = list.Count;
                    _loadedCount = list.Count;
                }
                else
                {
                    _count = 0;
                    _loadedCount = 0;
                }

                _isDirty = true;
                _needsRebalance = false;

                SaveMetadata(serializer, ctx, _count);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);

            if (recursive)
            {
                _lock.EnterWriteLock();
                try
                {
                    for (int i = 0; i < _slots.Count; i++)
                    {
                        var slot = _slots[i];
                        if (slot.IsLoaded)
                        {
                            slot.Value?.SetDirty(dirty, recursive);
                            _slots[i] = new Slot(slot.Id, slot.Value, dirty, slot.IsLoaded);
                        }
                    }
                }
                finally { _lock.ExitWriteLock(); }
            }
        }

        /// <summary>
        /// Force load all elements.
        /// </summary>
        public void LoadAll()
        {
            var serializer = GetSerializer();
            using var locker = serializer.BeginFreshAction(this, out var ctx);
            LoadAll(serializer, ctx);
        }

        private void LoadAll(Serializer serializer, SerializeContext ctx)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_count == 0)
                    return;


                EnsureSlotCapacity(_count);

                for (int i = 0; i < _count; i++)
                {
                    if (!_slots[i].IsLoaded)
                        LoadSlotByIndexInternal(i, serializer, ctx);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        #region Private Methods

        private void EnsureSlotCapacity(int index)
        {
            // Ensure _slots has at least index + 1 elements
            // This handles the case after Deserialize where _slots.Count might be less than _count
            while (_slots.Count <= index && _slots.Count < _count)
            {
                _slots.Add(new Slot(string.Empty, default, false, false));
            }
        }

        private void LoadSlotByIndex(int index)
        {
            var serializer = GetSerializer();
            using var locker = serializer.BeginFreshAction(this, out var ctx);
            LoadSlotByIndexInternal(index, serializer, ctx);
        }

        private void LoadSlotByIndexInternal(int index, Serializer serializer, SerializeContext ctx)
        {
            EnsureSlotCapacity(index);

            if (_slots[index].IsLoaded)
                return;

            if (_options.Mode == LazyLoadMode.LoadAll)
            {
                LoadAllInternal(serializer, ctx);
            }
            else // LoadIndividual
            {
                var batchCount = _options.BatchLoadCount > 0 ? _options.BatchLoadCount : 1;
                var endIndex = Math.Min(index + batchCount, _slots.Count);

                // Find previous loaded slot
                int prevIndex = -1;
                string? prevId = null;
                for (int i = index - 1; i >= 0; i--)
                {
                    if (_slots[i].IsLoaded)
                    {
                        prevIndex = i;
                        prevId = _slots[i].Id;
                        break;
                    }
                }

                // Get IDs starting from prevId
                var ids = serializer.ListCollectionIdsMinMax(ctx, prevId, null);
                int skip = index - prevIndex - 1;
                int loaded = 0;

                int idIndex = 0;
                foreach (var id in ids)
                {
                    if (idIndex < skip)
                    {
                        idIndex++;
                        continue;
                    }

                    var slotIndex = index + loaded;
                    if (slotIndex >= endIndex)
                        break;

                    if (!_slots[slotIndex].IsLoaded)
                    {
                        var item = serializer.Read<T>(ctx, id, PathBuilder.Type.Collection);
                        _slots[slotIndex] = new Slot(id, item, false, true);
                        if (item != null)
                            OnChildDeserialized(item);
                        _loadedCount++;
                    }
                    loaded++;
                }
            }
        }

        private void LoadAllInternal(Serializer serializer, SerializeContext ctx)
        {
            _slots.Capacity = _count;
            EnsureSlotCapacity(_count);

            var ids = serializer.ListCollectionIds(ctx);
            int slotIndex = 0;

            foreach (var id in ids)
            {
                // Find the next empty/unloaded slot
                while (slotIndex < _slots.Count && _slots[slotIndex].IsLoaded)
                {
                    slotIndex++;
                }

                if (slotIndex >= _slots.Count)
                    break;

                // Load the item into this slot
                var item = serializer.Read<T>(ctx, id, PathBuilder.Type.Collection);
                _slots[slotIndex] = new Slot(id, item, false, true);
                if (item != null)
                {
                    item.SvId = id;
                    OnChildDeserialized(item);
                }
                _loadedCount++;
                slotIndex++;
            }
        }

        private string GenerateIdForIndex(int index)
        {
            if (_slots.Count == 0)
                return LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);

            string? prevId = index > 0 ? _slots[index - 1].Id : null;
            string? nextId = index < _slots.Count ? _slots[index].Id : null;
            return GenerateIdBetween(prevId, nextId);
        }

        private int IndexOfInternal(T? item)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsLoaded && EqualityComparer<T?>.Default.Equals(_slots[i].Value, item))
                    return i;
            }
            return -1;
        }

        private void RemoveAtInternal(int index)
        {
            var slot = _slots[index];

            // Immediate sync - delete from serializer
            var serializer = GetSerializer();
            using var locker = serializer.BeginFreshAction(this, out var ctx);

            if (!string.IsNullOrEmpty(slot.Id))
                serializer.Delete(ctx, slot.Id, PathBuilder.Type.Collection);

            _slots.RemoveAt(index);
            _count--;
            _version++;
            if (slot.IsLoaded)
                _loadedCount--;

            TryUnWatch(slot.Value);

            SaveMetadata(serializer, ctx, _count);

            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, slot.Value, index));
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range. Count: {_count}");
        }

        private void SerializeWithRebalance(Serializer serializer, SerializeContext ctx)
        {
            var precisionDigits = Math.Max(DEFAULT_PRECISION_DIGITS, LexoRank.CalculatePrecisionDigits(_slots.Count));
            var initRank = LexoRank.GetInitValue(precisionDigits);
            var rank = LexoRank.Rebalance(initRank, _slots.Count, out int step, out bool _);

            LoadAll(serializer, ctx);

            serializer.DeleteAllNoPushPath(ctx);

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var newId = rank;
                // Write new ID
                using (ctx.Path.UsePush(newId, PathBuilder.Type.Collection))
                {
                    if (slot.IsLoaded && slot.Value != null)
                    {
                        slot.Value.SvId = newId;
                        slot.Value.Serialize(serializer, ctx);
                    }
                    else
                    {
                        serializer.SaveNoPushPath<T?>(default, ctx);
                    }
                }

                _slots[i] = new Slot(newId, slot.Value, false, slot.IsLoaded);
                rank = LexoRank.Generate(rank, null, precisionDigits, stepSize: step);
            }
            _version++;

            _needsRebalance = false;
        }

        private void SerializeDirtyItems(Serializer serializer, SerializeContext ctx)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.IsTrueDirty)
                    continue;

                if (slot.Value == null)
                    serializer.Save<T?>(default, ctx, slot.Id, PathBuilder.Type.Collection);
                else
                {
                    using var _ = ctx.Path.UsePush(slot.Id, PathBuilder.Type.Collection);
                    slot.Value.Serialize(serializer, ctx);
                }

                _slots[i] = new Slot(slot.Id, slot.Value, false, slot.IsLoaded);
            }
        }

        #endregion
    }
}
