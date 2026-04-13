using System;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable array for ISavable elements.
    /// Elements are loaded on-demand based on LazyLoadMode configuration.
    /// Fixed size - no Add/Insert/Remove/Clear operations.
    /// Thread-safe with ReaderWriterLockSlim.
    /// </summary>
    public sealed class ObservableArraySavableLazy<T> : ObservableArraySavableBase<ObservableArraySavableLazy<T>, T>
        where T : ISavable
    {
        private int _count;
        private Slot[] _slots = Array.Empty<Slot>();
        private readonly CollectionOptions _options;
        private readonly ReaderWriterLockSlim _lock = new();
        private int _loadedCount;
        private Serializer? _serializer;

        public ObservableArraySavableLazy(string propName, bool saveSeparately, CollectionOptions options)
            : base(propName, saveSeparately)
        {
            if (options.Mode == LazyLoadMode.None)
                throw new InvalidOperationException("LazyLoadMode.None is not supported for lazy-loaded collections. Use ObservableArraySavable<T> instead.");

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
                    foreach (ref readonly var slot in _slots.AsSpan())
                    {
                        if (slot.IsTrueDirty)
                            return true;
                    }
                    return false;
                }
                finally { _lock.ExitReadLock(); }
            }
        }

        public override T? this[int index]
        {
            get
            {
                ValidateIndex(index);

                _lock.EnterUpgradeableReadLock();
                try
                {
                    if (_slots.Length > 0 && _slots[index].IsLoaded)
                        return _slots[index].Value;

                    _lock.EnterWriteLock();
                    try
                    {
                        LoadSlot(index);
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
                    EnsureSlotsInitialized();

                    var oldValue = _slots[index].Value;

                    _slots[index] = new Slot(GetId(index), value, true, true);

                    TryUnWatch(oldValue);
                    if (value != null)
                    {
                        value.SvId = GetId(index);
                        TryWatch(value);
                    }

                    OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Replace(this, oldValue, value, index));
                }
                finally { _lock.ExitWriteLock(); }
            }
        }

        public override bool Contains(T? item)
        {
            _lock.EnterReadLock();
            try
            {
                foreach (ref readonly var slot in _slots.AsSpan())
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
                for (int i = 0; i < _slots.Length; i++)
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

            _slots = Array.Empty<Slot>();
            _loadedCount = 0;
            _isDirty = false;
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            _lock.EnterWriteLock();
            try
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    ref readonly var slot = ref _slots[i];

                    if (!slot.IsLoaded)
                        continue;

                    if (slot.IsTrueDirty)
                    {
                        if (slot.Value == null)
                        {
                            serializer.Delete(ctx, slot.Id, PathBuilder.Type.Collection);
                        }
                        else
                        {
                            using var _ = ctx.Path.UsePush(slot.Id, PathBuilder.Type.Collection);
                            slot.Value.Serialize(serializer, ctx);
                        }

                        _slots[i] = new Slot(slot.Id, slot.Value, false, slot.IsLoaded);
                    }
                }
            }
            finally { _lock.ExitWriteLock(); }

            var meta = new CollectionMetadata
            {
                Count = _count,
            };
            serializer.Save(meta, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                _lock.EnterUpgradeableReadLock();
                try
                {
                    if (_slots.Length == 0 || !_slots[i].IsLoaded)
                    {
                        _lock.EnterWriteLock();
                        try
                        {
                            LoadSlot(i);
                        }
                        finally { _lock.ExitWriteLock(); }
                    }

                    yield return _slots[i].Value;
                }
                finally { _lock.ExitUpgradeableReadLock(); }
            }
        }

        public override void SwapSource(T?[]? array)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_slots.Length > 0)
                {
                    foreach (ref readonly var slot in _slots.AsSpan())
                    {
                        if (slot.IsLoaded && slot.Value != null)
                            TryUnWatch(slot.Value);
                    }
                    OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Reset(this));
                }

                if (array != null && array.Length > 0)
                {
                    _count = array.Length;
                    _slots = new Slot[array.Length];
                    for (int i = 0; i < array.Length; i++)
                    {
                        var item = array[i];
                        var id = GetPaddedIndex(i, array.Length);
                        _slots[i] = new Slot(id, item, true, true);
                        if (item != null)
                        {
                            item.SvId = id;
                            TryWatch(item);
                        }
                    }
                    _loadedCount = array.Length;
                    var values = new T?[_slots.Length];
                    for (int i = 0; i < _slots.Length; i++)
                        values[i] = _slots[i].Value;
                    OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Add(this, values, 0));
                }
                else
                {
                    _count = 0;
                    _slots = Array.Empty<Slot>();
                    _loadedCount = 0;
                }

                _isDirty = true;
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
                    for (int i = 0; i < _slots.Length; i++)
                    {
                        ref readonly var slot = ref _slots[i];
                        if (slot.IsLoaded)
                        {
                            if (slot.Value != null)
                                slot.Value.SetDirty(dirty, recursive);
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

                EnsureSlotsInitialized();
                var serializer = GetSerializer();
                using var locker = serializer.BeginFreshAction(this, out var ctx);
                LoadAllInternal(serializer, ctx);
            }
            finally { _lock.ExitWriteLock(); }
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
                return _slots.Length > 0 && _slots[index].IsLoaded;
            }
            finally { _lock.ExitReadLock(); }
        }

        private void LoadSlot(int index)
        {
            EnsureSlotsInitialized();

            var serializer = GetSerializer();

            using var locker = serializer.BeginFreshAction(this, out var ctx);

            if (_options.Mode == LazyLoadMode.LoadAll)
            {
                LoadAllInternal(serializer, ctx);
            }
            else // LoadIndividual
            {
                var batchCount = _options.BatchLoadCount > 0 ? _options.BatchLoadCount : 1;
                var endIndex = Math.Min(index + batchCount, _slots.Length);

                var startId = index == 0 ? null : GetPaddedIndex(index - 1, _slots.Length);
                var loaded = 0;

                foreach (var id in serializer.ListCollectionIdsMinMax(ctx, startId, null))
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
                var index = ParseIndexFromId(id);
                if (index >= 0 && index < _slots.Length && !_slots[index].IsLoaded)
                {
                    var item = serializer.Read<T>(ctx, id, PathBuilder.Type.Collection);
                    _slots[index] = new Slot(id, item, false, true);
                    if (item != null)
                        OnChildDeserialized(item);
                    _loadedCount++;
                }
            }
        }

        private void EnsureSlotsInitialized()
        {
            if (_slots.Length == 0 && _count > 0)
            {
                _slots = new Slot[_count];
                for (int i = 0; i < _count; i++)
                {
                    _slots[i] = new Slot(GetPaddedIndex(i, _count), default, false, false);
                }
            }
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range. Count: {_count}");
        }

        private string GetId(int index)
        {
            return GetPaddedIndex(index, _count);
        }

        private static int ParseIndexFromId(string id)
        {
            return int.TryParse(id, out var index) ? index : -1;
        }
    }
}
