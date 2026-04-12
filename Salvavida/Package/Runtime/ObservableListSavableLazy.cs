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

        private const int DEFAULT_PRECISION_DIGITS = 2;
        private const int REBALANCE_LENGTH_THRESHOLD = 10;

        public ObservableListSavableLazy(string propName, bool saveSeparately, CollectionOptions options)
            : base(propName, saveSeparately)
        {
            if (options.Mode == LazyLoadMode.None)
                throw new InvalidOperationException("LazyLoadMode.None is not supported for lazy-loaded collections. Use ObservableListSavable<T> instead.");

            _slots = new List<Slot>();
            _options = options;
        }

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
                    if (_slots == null)
                        return false;

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
                return _slots != null && index < _slots.Count && _slots[index].IsLoaded;
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
                    if (_slots![index].IsLoaded)
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
                    var oldValue = _slots![index].Value;
                    var oldId = _slots[index].Id;

                    _slots[index] = new Slot(oldId, value, true, true);

                    TryUnWatch(oldValue);
                    if (value != null)
                    {
                        value.SvId = oldId;
                        TryWatch(value);
                    }

                    // Immediate sync to serializer
                    var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
                    using var locker = serializer.BeginFreshAction(this, out var ctx);

                    if (value == null)
                        serializer.Save<T?>(default, ctx, oldId, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(oldId, PathBuilder.Type.Collection);
                        value.Serialize(serializer, ctx);
                    }

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
                if (_slots == null)
                    return false;

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
                if (_slots == null)
                    return -1;

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

            _lock.EnterReadLock();
            try
            {
                if (_slots == null || _slots.Count == 0)
                {
                    var meta = new CollectionMetadata { Count = _count };
                    serializer.Save(meta, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
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

                var metaOut = new CollectionMetadata { Count = _count };
                serializer.Save(metaOut, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
            }
            finally { _lock.ExitReadLock(); }
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                _lock.EnterUpgradeableReadLock();
                try
                {
                    if (_slots == null || !_slots[i].IsLoaded)
                    {
                        _lock.EnterWriteLock();
                        try
                        {
                            LoadSlotByIndex(i);
                        }
                        finally { _lock.ExitWriteLock(); }
                    }

                    yield return _slots![i].Value;
                }
                finally { _lock.ExitUpgradeableReadLock(); }
            }
        }

        public override void Add(T? item)
        {
            _lock.EnterWriteLock();
            try
            {
                var index = _slots!.Count;
                var id = GenerateIdForIndex(index);

                if (item != null)
                    item.SvId = id;

                _slots.Add(new Slot(id, item, true, true));
                _count++;
                _loadedCount++;

                TryWatch(item);

                // Immediate sync to serializer
                var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                if (item == null)
                    serializer.Save<T?>(default, ctx, id, PathBuilder.Type.Collection);
                else
                {
                    using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                    item.Serialize(serializer, ctx);
                }

                SaveMetadata(serializer, ctx);

                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                if (_slots != null)
                {
                    // Immediate sync - delete all from serializer
                    var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
                    using var locker = serializer.BeginFreshAction(this, out var ctx);

                    foreach (var slot in _slots)
                    {
                        if (!string.IsNullOrEmpty(slot.Id))
                            serializer.Delete(ctx, slot.Id, PathBuilder.Type.Collection);
                        TryUnWatch(slot.Value);
                    }

                    _slots.Clear();
                }

                _count = 0;
                _loadedCount = 0;
                _isDirty = true;
                _needsRebalance = false;

                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));

                // Save empty meta
                var serializer2 = this.GetSerializer();
                if (serializer2 != null)
                {
                    using var locker = serializer2.BeginFreshAction(this, out var ctx);
                    SaveMetadata(serializer2, ctx);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override void Insert(int index, T? item)
        {
            _lock.EnterWriteLock();
            try
            {
                string? prevId = index > 0 ? _slots![index - 1].Id : null;
                string? nextId = index < _slots!.Count ? _slots[index].Id : null;

                var id = GenerateIdBetween(prevId, nextId);

                if (item != null)
                    item.SvId = id;

                _slots.Insert(index, new Slot(id, item, true, true));
                _count++;
                _loadedCount++;

                TryWatch(item);

                // Check if rebalance needed
                CheckRebalanceNeeded(id);

                // Immediate sync to serializer
                var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                if (item == null)
                    serializer.Save<T?>(default, ctx, id, PathBuilder.Type.Collection);
                else
                {
                    using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                    item.Serialize(serializer, ctx);
                }

                SaveMetadata(serializer, ctx);

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
                var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
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
                    _slots ??= new List<Slot>(list.Count);
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
                    _slots.Clear();
                    _count = 0;
                    _loadedCount = 0;
                }

                _isDirty = true;
                _needsRebalance = false;

                SaveMetadata(serializer, ctx);
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
            _lock.EnterWriteLock();
            try
            {
                if (_count == 0)
                    return;

                var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                for (int i = 0; i < _slots!.Count; i++)
                {
                    if (!_slots[i].IsLoaded)
                        LoadSlotByIndexInternal(i, serializer, ctx);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        #region Private Methods

        private void LoadSlotByIndex(int index)
        {
            var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
            using var locker = serializer.BeginFreshAction(this, out var ctx);
            LoadSlotByIndexInternal(index, serializer, ctx);
        }

        private void LoadSlotByIndexInternal(int index, Serializer serializer, SerializeContext ctx)
        {
            if (_slots![index].IsLoaded)
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

                foreach (var id in ids)
                {
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
            foreach (var id in serializer.ListCollectionIds(ctx))
            {
                // Find slot by ID
                for (int i = 0; i < _slots!.Count; i++)
                {
                    if (_slots[i].Id == id && !_slots[i].IsLoaded)
                    {
                        var item = serializer.Read<T>(ctx, id, PathBuilder.Type.Collection);
                        _slots[i] = new Slot(id, item, false, true);
                        if (item != null)
                            OnChildDeserialized(item);
                        _loadedCount++;
                        break;
                    }
                }
            }
        }

        private string GenerateIdForIndex(int index)
        {
            if (_slots == null || _slots.Count == 0)
                return LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);

            string? prevId = index > 0 ? _slots[index - 1].Id : null;
            string? nextId = index < _slots.Count ? _slots[index].Id : null;

            return GenerateIdBetween(prevId, nextId);
        }

        private string GenerateIdBetween(string? prevId, string? nextId)
        {
            Span<char> rankBuffer = stackalloc char[128];
            int rankLen = LexoRank.Generate(
                prevId.AsSpan(),
                nextId.AsSpan(),
                DEFAULT_PRECISION_DIGITS,
                rankBuffer);
            return new string(rankBuffer.Slice(0, rankLen));
        }

        private void CheckRebalanceNeeded(string id)
        {
            var lexoPart = id.AsSpan();
            int sepIdx = lexoPart.IndexOf('~');
            if (sepIdx >= 0 && lexoPart.Length - sepIdx - 1 > REBALANCE_LENGTH_THRESHOLD)
                _needsRebalance = true;
        }

        private int IndexOfInternal(T? item)
        {
            if (_slots == null)
                return -1;

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsLoaded && EqualityComparer<T?>.Default.Equals(_slots[i].Value, item))
                    return i;
            }
            return -1;
        }

        private void RemoveAtInternal(int index)
        {
            var slot = _slots![index];

            // Immediate sync - delete from serializer
            var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
            using var locker = serializer.BeginFreshAction(this, out var ctx);

            if (!string.IsNullOrEmpty(slot.Id))
                serializer.Delete(ctx, slot.Id, PathBuilder.Type.Collection);

            _slots.RemoveAt(index);
            _count--;
            if (slot.IsLoaded)
                _loadedCount--;

            TryUnWatch(slot.Value);

            SaveMetadata(serializer, ctx);

            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, slot.Value, index));
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range. Count: {_count}");
        }

        private void SaveMetadata(Serializer serializer, SerializeContext ctx)
        {
            var meta = new CollectionMetadata { Count = _count };
            serializer.Save(meta, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
        }

        private void SerializeWithRebalance(Serializer serializer, SerializeContext ctx)
        {
            var precisionDigits = Math.Max(DEFAULT_PRECISION_DIGITS, LexoRank.CalculatePrecisionDigits(_slots!.Count));
            var initRank = LexoRank.GetInitValue(precisionDigits);
            var rank = LexoRank.Rebalance(initRank, _slots.Count, out int step, out bool _);

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var oldId = slot.Id;
                var newId = rank;

                // Delete old ID
                if (!string.IsNullOrEmpty(oldId))
                    serializer.Delete(ctx, oldId, PathBuilder.Type.Collection);

                // Write new ID
                if (slot.IsLoaded && slot.Value != null)
                {
                    slot.Value.SvId = newId;
                    using var _ = ctx.Path.UsePush(newId, PathBuilder.Type.Collection);
                    slot.Value.Serialize(serializer, ctx);
                }
                else
                {
                    serializer.Save<T?>(default, ctx, newId, PathBuilder.Type.Collection);
                }

                _slots[i] = new Slot(newId, slot.Value, false, slot.IsLoaded);
                rank = LexoRank.Generate(rank, null, precisionDigits, stepSize: step);
            }

            _needsRebalance = false;
        }

        private void SerializeDirtyItems(Serializer serializer, SerializeContext ctx)
        {
            for (int i = 0; i < _slots!.Count; i++)
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
