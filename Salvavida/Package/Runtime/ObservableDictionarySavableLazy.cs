using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable dictionary for ISavable values.
    /// Values are loaded on-demand based on LazyLoadMode configuration.
    /// Supports dynamic add/remove with immediate serializer sync.
    /// Thread-safe with ReaderWriterLockSlim.
    /// </summary>
    public sealed class ObservableDictionarySavableLazy<TKey, TValue>
        : ObservableDictionarySavableBase<ObservableDictionarySavableLazy<TKey, TValue>, TKey, TValue>
        where TKey : notnull
        where TValue : ISavable?
    {
        private readonly Dictionary<TKey, Slot> _loadedSlots;
        private readonly CollectionOptions _options;
        private readonly ReaderWriterLockSlim _lock = new();
        private int _count;
        private uint _version;   // incremented only by mutating operations
        private Serializer? _serializer;

        public ObservableDictionarySavableLazy(string propName, CollectionOptions options)
            : base(propName, true)
        {
            if (options.Mode == LazyLoadMode.None)
                throw new InvalidOperationException("LazyLoadMode.None is not supported for lazy-loaded collections. Use ObservableDictionarySavable<TKey, TValue> instead.");

            _options = options;
            _loadedSlots = new Dictionary<TKey, Slot>();
        }

        protected override void SetParent(ISavable? parent)
        {
            base.SetParent(parent);
            _serializer = parent?.GetSerializer();
        }

        private Serializer GetSerializer() => _serializer ?? throw new NullReferenceException("Serializer not available.");

        // ── State ──────────────────────────────────────────────────────────

        public override int Count => _count;

        /// <summary>Number of currently loaded values.</summary>
        public int LoadedCount => _loadedSlots.Count;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty) return true;
                _lock.EnterReadLock();
                try
                {
                    foreach (var slot in _loadedSlots.Values)
                        if (slot.IsTrueDirty) return true;
                    return false;
                }
                finally { _lock.ExitReadLock(); }
            }
        }

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);
            if (recursive)
            {
                _lock.EnterWriteLock();
                try
                {
                    // Iterating KVPs and updating values for existing keys is safe:
                    // Dictionary._version is only incremented on add/remove, not value updates.
                    foreach (var kvp in _loadedSlots)
                    {
                        var slot = kvp.Value;
                        if (slot.IsLoaded)
                        {
                            slot.Value?.SetDirty(dirty, recursive);
                            _loadedSlots[kvp.Key] = new Slot(slot.Id, slot.Value, dirty, slot.IsLoaded);
                        }
                    }
                }
                finally { _lock.ExitWriteLock(); }
            }
        }

        // ── Serialize / Deserialize ────────────────────────────────────────

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            var meta = serializer.Read<CollectionMetadata?>(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
            _count = meta?.Count ?? 0;

            _loadedSlots.Clear();
            _isDirty = false;
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            _lock.EnterWriteLock();
            try
            {
                foreach (var kvp in _loadedSlots)
                {
                    var slot = kvp.Value;
                    if (!slot.IsTrueDirty)
                        continue;

                    if (slot.Value == null)
                        serializer.Delete(ctx, slot.Id, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(slot.Id, PathBuilder.Type.Collection);
                        slot.Value.Serialize(serializer, ctx);
                    }

                    // Updating an existing key's value — does NOT increment Dictionary._version,
                    // so iterating _loadedSlots remains valid.
                    _loadedSlots[kvp.Key] = new Slot(slot.Id, slot.Value, false, slot.IsLoaded);
                }
            }
            finally { _lock.ExitWriteLock(); }

            SaveMetadata(serializer, ctx, _count);
        }

        // ── Indexer ────────────────────────────────────────────────────────

        public override TValue? this[TKey key]
        {
            get
            {
                _lock.EnterUpgradeableReadLock();
                try
                {
                    if (_loadedSlots.TryGetValue(key, out var slot) && slot.IsLoaded)
                        return slot.Value;

                    _lock.EnterWriteLock();
                    try
                    {
                        // Re-check after acquiring write lock (another thread may have loaded it)
                        if (_loadedSlots.TryGetValue(key, out slot) && slot.IsLoaded)
                            return slot.Value;

                        if (_options.Mode == LazyLoadMode.LoadAll)
                        {
                            LoadAllInternal();
                            if (_loadedSlots.TryGetValue(key, out slot) && slot.IsLoaded)
                                return slot.Value;
                            throw new KeyNotFoundException($"Key not found after LoadAll: {key}");
                        }
                        else
                        {
                            var id = _idConverter.ConvertTo(key);
                            var serializer = GetSerializer();
                            using var locker = serializer.BeginFreshAction(this, out var ctx);
                            return LoadSlotInternal(serializer, ctx, key, id);
                        }
                    }
                    finally { _lock.ExitWriteLock(); }
                }
                finally { _lock.ExitUpgradeableReadLock(); }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    var id = _idConverter.ConvertTo(key);
                    var existed = _loadedSlots.TryGetValue(key, out var oldSlot);
                    var oldValue = existed ? oldSlot.Value : default;

                    _loadedSlots[key] = new Slot(id, value, true, true);

                    TryUnWatch(oldValue);
                    if (value != null)
                    {
                        value.SvId = id;
                        TryWatch(value);
                    }

                    var serializer = GetSerializer();
                    using var locker = serializer.BeginFreshAction(this, out var ctx);

                    if (value == null)
                        serializer.Save<TValue?>(default, ctx, id, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                        value.Serialize(serializer, ctx);
                    }

                    if (!existed)
                    {
                        _count++;
                        SaveMetadata(serializer, ctx, _count);
                        OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Add((ObservableDictionarySavableBase<TKey, TValue>)(object)this, value, -1));
                    }
                    else
                    {
                        OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Replace((ObservableDictionarySavableBase<TKey, TValue>)(object)this, oldValue, value, -1));
                    }

                    _version++;
                }
                finally { _lock.ExitWriteLock(); }
            }
        }

        // ── Read operations ────────────────────────────────────────────────

        public override bool ContainsKey(TKey key)
        {
            _lock.EnterReadLock();
            try
            {
                if (_loadedSlots.TryGetValue(key, out var slot) && slot.IsLoaded)
                    return true;
            }
            finally { _lock.ExitReadLock(); }

            // Not in loaded slots — check serializer (best-effort, no collection lock held)
            var id = _idConverter.ConvertTo(key);
            var serializer = GetSerializer();
            using var locker = serializer.BeginFreshAction(this, out var ctx);
            return serializer.Has(ctx, id, PathBuilder.Type.Collection);
        }

        public override bool TryGetValue(TKey key, out TValue? value)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_loadedSlots.TryGetValue(key, out var slot) && slot.IsLoaded)
                {
                    value = slot.Value;
                    return true;
                }

                var id = _idConverter.ConvertTo(key);
                _lock.EnterWriteLock();
                try
                {
                    // Re-check after write lock
                    if (_loadedSlots.TryGetValue(key, out slot) && slot.IsLoaded)
                    {
                        value = slot.Value;
                        return true;
                    }

                    if (_options.Mode == LazyLoadMode.LoadAll)
                    {
                        LoadAllInternal();
                        if (_loadedSlots.TryGetValue(key, out slot) && slot.IsLoaded)
                        {
                            value = slot.Value;
                            return true;
                        }
                        value = default;
                        return false;
                    }
                    else
                    {
                        var serializer = GetSerializer();
                        using var locker = serializer.BeginFreshAction(this, out var ctx);
                        if (!serializer.Has(ctx, id, PathBuilder.Type.Collection))
                        {
                            value = default;
                            return false;
                        }
                        value = LoadSlotInternal(serializer, ctx, key, id);
                        return true;
                    }
                }
                finally { _lock.ExitWriteLock(); }
            }
            finally { _lock.ExitUpgradeableReadLock(); }
        }

        public override ICollection<TKey> Keys => new LazyKeyCollection(this);

        public override ICollection<TValue?> Values => new LazyValueCollection(this);

        // ── Mutation operations ────────────────────────────────────────────

        public override void Add(TKey key, TValue? value)
        {
            _lock.EnterWriteLock();
            try
            {
                var id = _idConverter.ConvertTo(key);
                if (value != null)
                    value.SvId = id;

                _loadedSlots[key] = new Slot(id, value, true, true);
                _count++;
                _version++;

                TryWatch(value);

                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                if (value == null)
                    serializer.Save<TValue?>(default, ctx, id, PathBuilder.Type.Collection);
                else
                {
                    using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                    value.Serialize(serializer, ctx);
                }

                SaveMetadata(serializer, ctx, _count);
                OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Add((ObservableDictionarySavableBase<TKey, TValue>)(object)this, value, -1));
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override bool Remove(TKey key)
        {
            _lock.EnterWriteLock();
            try
            {
                var id = _idConverter.ConvertTo(key);
                _loadedSlots.TryGetValue(key, out var slot);
                var oldValue = slot.Value;

                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                // Verify existence: loaded slot OR present in serializer
                if (!_loadedSlots.ContainsKey(key) && !serializer.Has(ctx, id, PathBuilder.Type.Collection))
                    return false;

                _loadedSlots.Remove(key);
                _count--;
                _version++;

                TryUnWatch(oldValue);
                serializer.Delete(ctx, id, PathBuilder.Type.Collection);
                SaveMetadata(serializer, ctx, _count);
                OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Remove((ObservableDictionarySavableBase<TKey, TValue>)(object)this, oldValue, -1));
                return true;
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

                foreach (var slot in _loadedSlots.Values)
                    TryUnWatch(slot.Value);

                serializer.DeleteAllNoPushPath(ctx);
                _loadedSlots.Clear();
                _count = 0;
                _version++;
                _isDirty = true;

                SaveMetadata(serializer, ctx, _count);
                OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Reset((ObservableDictionarySavableBase<TKey, TValue>)(object)this));
            }
            finally { _lock.ExitWriteLock(); }
        }

        public override void SwapSource(Dictionary<TKey, TValue?>? dict)
        {
            _lock.EnterWriteLock();
            try
            {
                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);

                foreach (var slot in _loadedSlots.Values)
                {
                    TryUnWatch(slot.Value);
                    if (!string.IsNullOrEmpty(slot.Id))
                        serializer.Delete(ctx, slot.Id, PathBuilder.Type.Collection);
                }
                _loadedSlots.Clear();
                _count = 0;

                if (dict != null)
                {
                    foreach (var (k, v) in dict)
                    {
                        var id = _idConverter.ConvertTo(k);
                        if (v != null) v.SvId = id;
                        _loadedSlots[k] = new Slot(id, v, true, true);

                        if (v == null)
                            serializer.Save<TValue?>(default, ctx, id, PathBuilder.Type.Collection);
                        else
                        {
                            using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                            v.Serialize(serializer, ctx);
                        }

                        TryWatch(v);
                    }
                    _count = dict.Count;
                }

                _isDirty = true;
                _version++;
                SaveMetadata(serializer, ctx, _count);
                OnCollectionChange(CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Reset((ObservableDictionarySavableBase<TKey, TValue>)(object)this));
            }
            finally { _lock.ExitWriteLock(); }
        }

        // ── Enumeration ────────────────────────────────────────────────────

        protected override IEnumerator<KeyValuePair<TKey, TValue?>> GetEnumeratorCore()
        {
            if (_count == 0)
                yield break;

            if (_options.Mode == LazyLoadMode.LoadAll)
            {
                LoadAll();
                _lock.EnterReadLock();
                try
                {
                    foreach (var kvp in _loadedSlots)
                        yield return new KeyValuePair<TKey, TValue?>(kvp.Key, kvp.Value.Value);
                }
                finally { _lock.ExitReadLock(); }
                yield break;
            }

            // LoadIndividual: batch load via ListCollectionIdsMinMax
            var version = _version;
            var batchCount = _options.BatchLoadCount > 0 ? _options.BatchLoadCount : 1;
            string? lastYieldedId = null;

            while (true)
            {
                if (version != _version)
                    throw new InvalidOperationException("collection was modified");

                // Materialize a batch of IDs; release serializer lock before yielding
                var batch = new List<string>(batchCount);
                {
                    var serializer = GetSerializer();
                    using var locker = serializer.BeginFreshAction(this, out var ctx);
                    foreach (var id in serializer.ListCollectionIdsMinMax(ctx, lastYieldedId, null))
                    {
                        if (lastYieldedId != null && id == lastYieldedId)
                            continue;   // skip the inclusive lower-bound itself
                        batch.Add(id);
                        if (batch.Count >= batchCount)
                            break;
                    }
                }

                if (batch.Count == 0)
                    yield break;

                foreach (var id in batch)
                {
                    var key = _idConverter.ConvertFrom(id);

                    // Hold upgradeable read lock across yield (same pattern as ObservableListSavableLazy).
                    // WARNING: callers must not call back into collection operations during foreach,
                    // as ReaderWriterLockSlim does not support recursive acquisition.
                    _lock.EnterUpgradeableReadLock();
                    try
                    {
                        if (!_loadedSlots.TryGetValue(key, out var slot) || !slot.IsLoaded)
                        {
                            _lock.EnterWriteLock();
                            try
                            {
                                var ser = GetSerializer();
                                using var lk = ser.BeginFreshAction(this, out var ctx2);
                                LoadSlotInternal(ser, ctx2, key, id);
                            }
                            finally { _lock.ExitWriteLock(); }

                            _loadedSlots.TryGetValue(key, out slot);
                        }

                        lastYieldedId = id;
                        yield return new KeyValuePair<TKey, TValue?>(key, slot.Value);
                    }
                    finally { _lock.ExitUpgradeableReadLock(); }
                }
            }
        }

        // ── Public helpers ─────────────────────────────────────────────────

        /// <summary>Check if the value for a key is currently loaded in memory.</summary>
        public bool IsLoaded(TKey key)
        {
            _lock.EnterReadLock();
            try { return _loadedSlots.TryGetValue(key, out var slot) && slot.IsLoaded; }
            finally { _lock.ExitReadLock(); }
        }

        /// <summary>Force-load all entries from the serializer.</summary>
        public void LoadAll()
        {
            _lock.EnterWriteLock();
            try
            {
                if (_count == 0) return;
                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);
                LoadAllInternal(serializer, ctx);
            }
            finally { _lock.ExitWriteLock(); }
        }

        // ── Private helpers ────────────────────────────────────────────────

        /// <summary>Load all entries. Caller must hold write lock.</summary>
        private void LoadAllInternal()
        {
            var serializer = GetSerializer();
            using var locker = serializer.BeginFreshAction(this, out var ctx);
            LoadAllInternal(serializer, ctx);
        }

        private void LoadAllInternal(Serializer serializer, SerializeContext ctx)
        {
            foreach (var id in serializer.ListCollectionIds(ctx))
            {
                var key = _idConverter.ConvertFrom(id);
                if (!_loadedSlots.TryGetValue(key, out var slot) || !slot.IsLoaded)
                    LoadSlotInternal(serializer, ctx, key, id);
            }
        }

        /// <summary>
        /// Load a single slot from serializer. Caller must hold write lock and a valid ctx.
        /// </summary>
        private TValue? LoadSlotInternal(Serializer serializer, SerializeContext ctx, TKey key, string id)
        {
            var item = serializer.Read<TValue>(ctx, id, PathBuilder.Type.Collection);
            _loadedSlots[key] = new Slot(id, item, false, true);
            if (item != null)
                OnChildDeserialized(item);
            return item;
        }

        // ── Inner collection types ─────────────────────────────────────────

        private sealed class LazyKeyCollection : ICollection<TKey>
        {
            private readonly ObservableDictionarySavableLazy<TKey, TValue> _owner;
            public LazyKeyCollection(ObservableDictionarySavableLazy<TKey, TValue> owner) => _owner = owner;

            public int Count => _owner._count;
            public bool IsReadOnly => true;
            public bool Contains(TKey item) => _owner.ContainsKey(item);

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                int i = arrayIndex;
                foreach (var k in this) array[i++] = k;
            }

            public IEnumerator<TKey> GetEnumerator()
            {
                // Materialize IDs first so the serializer lock is not held across yields.
                string[] ids;
                {
                    var serializer = _owner.GetSerializer();
                    using var locker = serializer.BeginFreshAction(_owner, out var ctx);
                    ids = serializer.ListCollectionIds(ctx).ToArray();
                }
                foreach (var id in ids)
                    yield return _owner._idConverter.ConvertFrom(id);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            void ICollection<TKey>.Add(TKey item) => throw new NotSupportedException();
            void ICollection<TKey>.Clear() => throw new NotSupportedException();
            bool ICollection<TKey>.Remove(TKey item) => throw new NotSupportedException();
        }

        private sealed class LazyValueCollection : ICollection<TValue?>
        {
            private readonly ObservableDictionarySavableLazy<TKey, TValue> _owner;
            public LazyValueCollection(ObservableDictionarySavableLazy<TKey, TValue> owner) => _owner = owner;

            public int Count => _owner._count;
            public bool IsReadOnly => true;

            public bool Contains(TValue? item)
            {
                foreach (var v in this)
                    if (EqualityComparer<TValue?>.Default.Equals(v, item)) return true;
                return false;
            }

            public void CopyTo(TValue?[] array, int arrayIndex)
            {
                int i = arrayIndex;
                foreach (var v in this) array[i++] = v;
            }

            public IEnumerator<TValue?> GetEnumerator()
            {
                foreach (var kvp in _owner)
                    yield return kvp.Value;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            void ICollection<TValue?>.Add(TValue? item) => throw new NotSupportedException();
            void ICollection<TValue?>.Clear() => throw new NotSupportedException();
            bool ICollection<TValue?>.Remove(TValue? item) => throw new NotSupportedException();
        }
    }
}
