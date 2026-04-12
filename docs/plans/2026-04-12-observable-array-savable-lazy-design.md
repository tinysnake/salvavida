# ObservableArraySavableLazy Design

## Overview

Lazy-loaded observable array for `ISavable` elements. Elements are loaded on-demand based on `LazyLoadMode` configuration, with thread-safe access using `ReaderWriterLockSlim`.

## Key Design Decisions

1. **LazyLoadMode.None throws `InvalidOperationException`** - This collection requires explicit lazy loading mode.
2. **Slot-based storage** - Uses `ObservableCollectionSavable<TCol, TElem>.Slot` struct to track Id, Value, IsLoaded, and IsDirty state.
3. **Thread-safe** - Uses `ReaderWriterLockSlim` for concurrent read access and exclusive write access.
4. **Deferred initialization** - `_slots` array is initialized on first load, not during Deserialize.

## Core Structure

```csharp
public sealed class ObservableArraySavableLazy<T> : ObservableArraySavableBase<ObservableArraySavableLazy<T>, T>
    where T : ISavable
{
    private int _count;
    private Slot[] _slots = Array.Empty<Slot>();
    private readonly CollectionOptions _options;
    private readonly ReaderWriterLockSlim _lock = new();
    private int _loadedCount;

    public ObservableArraySavableLazy(string propName, bool saveSeparately, CollectionOptions options)
        : base(propName, saveSeparately)
    {
        if (options.Mode == LazyLoadMode.None)
            throw new InvalidOperationException("LazyLoadMode.None is not supported for lazy-loaded collections.");

        _options = options;
    }
}
```

## Loading Strategy

### LoadAll Mode
- First access to any unloaded element triggers full load of all elements.
- Uses `serializer.ListCollectionIds(ctx)` to get all IDs.

### LoadIndividual Mode
- Access to an unloaded element loads a batch starting from that index.
- Batch size determined by `_options.BatchLoadCount` (default 1).
- Uses `serializer.ListCollectionIdsMinMax(ctx, startId, null)` for range loading.

## Indexer Implementation

```csharp
public override T? this[int index]
{
    get
    {
        ValidateIndex(index);

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_slots[index].IsLoaded)
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
            var oldValue = _slots[index].Value;

            _slots[index] = new Slot(GetId(index), value, true) { IsDirty = true };

            TryUnWatch(oldValue);
            if (value != null)
            {
                value.SvId = GetId(index);
                TryWatch(value);
            }

            OnCollectionChange(CollectionChangeInfo<ObservableArraySavableLazy<T>, T?>.Replace(this, oldValue, value, index));
        }
        finally { _lock.ExitWriteLock(); }
    }
}
```

## LoadSlot Implementation

```csharp
private void LoadSlot(int index)
{
    EnsureSlotsInitialized();

    var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");

    using var locker = serializer.BeginFreshAction(this, out var ctx);

    if (_options.Mode == LazyLoadMode.LoadAll)
    {
        LoadAllInternal(serializer, ctx);
    }
    else // LoadIndividual
    {
        var batchCount = _options.BatchLoadCount > 0 ? _options.BatchLoadCount : 1;
        var endIndex = Math.Min(index + batchCount, _slots.Length);

        var startId = GetPaddedIndex(index, _slots.Length);
        var loaded = 0;

        foreach (var id in serializer.ListCollectionIdsMinMax(ctx, startId, null))
        {
            var slotIndex = index + loaded;
            if (slotIndex >= endIndex)
                break;

            if (!_slots[slotIndex].IsLoaded)
            {
                var item = serializer.Read<T>(ctx, id, PathBuilder.Type.Collection);
                _slots[slotIndex] = new Slot(id, item, true);
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
            _slots[index] = new Slot(id, item, true);
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
            _slots[i] = new Slot(GetPaddedIndex(i, _count), default, false);
        }
    }
}
```

## Deserialize Implementation

```csharp
public override void Deserialize(Serializer serializer, SerializeContext ctx)
{
    if (!SaveSeparately)
        return;

    var meta = serializer.Read<CollectionMetadata>(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
    _count = meta?.Count ?? 0;

    _slots = Array.Empty<Slot>();
    _loadedCount = 0;
    _isDirty = false;
}
```

## Serialize Implementation

```csharp
public override void Serialize(Serializer serializer, SerializeContext ctx)
{
    if (!SaveSeparately)
        return;

    for (int i = 0; i < _slots.Length; i++)
    {
        ref var slot = ref _slots[i];

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

            _slots[i] = slot with { IsDirty = false };
        }
    }

    var meta = new CollectionMetadata
    {
        Count = _count,
    };
    serializer.Save(meta, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
}
```

## GetEnumerator Implementation

```csharp
public override IEnumerator<T?> GetEnumerator()
{
    for (int i = 0; i < _count; i++)
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (!_slots[i].IsLoaded)
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
```

## Contains and IndexOf

Only check loaded elements, do not trigger loading:

```csharp
public override bool Contains(T? item)
{
    _lock.EnterReadLock();
    try
    {
        foreach (ref var slot in _slots.AsSpan())
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
```

## SwapSource Implementation

```csharp
public override void SwapSource(T?[]? array)
{
    _lock.EnterWriteLock();
    try
    {
        if (_slots.Length > 0)
        {
            foreach (ref var slot in _slots.AsSpan())
            {
                if (slot.IsLoaded && slot.Value != null)
                    TryUnWatch(slot.Value);
            }
        }

        if (array != null && array.Length > 0)
        {
            _count = array.Length;
            _slots = new Slot[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                var item = array[i];
                var id = GetPaddedIndex(i, array.Length);
                _slots[i] = new Slot(id, item, true) { IsDirty = true };
                if (item != null)
                {
                    item.SvId = id;
                    TryWatch(item);
                }
            }
            _loadedCount = array.Length;
        }
        else
        {
            _count = 0;
            _slots = Array.Empty<Slot>();
            _loadedCount = 0;
        }

        _isDirty = true;
        OnCollectionChange(CollectionChangeInfo<ObservableArraySavableLazy<T>, T?>.Reset(this));
    }
    finally { _lock.ExitWriteLock(); }
}
```

## IsDirty Property

```csharp
public override bool IsDirty
{
    get
    {
        if (IsSelfDirty)
            return true;

        foreach (ref var slot in _slots.AsSpan())
        {
            if (slot.IsTrueDirty)
                return true;
        }
        return false;
    }
}
```

## Public API Extensions

```csharp
/// <summary>
/// Force load all elements.
/// </summary>
public void LoadAll()
{
    _lock.EnterWriteLock();
    try
    {
        EnsureSlotsInitialized();
        var serializer = this.GetSerializer() ?? throw new NullReferenceException("Serializer not available.");
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

/// <summary>
/// Get count of loaded elements.
/// </summary>
public int LoadedCount => _loadedCount;
```

## Thread Safety Notes

- Use `EnterUpgradeableReadLock` when checking if loaded before potentially loading.
- Use `EnterWriteLock` for any mutation or loading operations.
- Use `EnterReadLock` for read-only operations that don't trigger loading.
