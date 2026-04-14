# ObservableDictionarySavableLazy Design Spec

**Date:** 2026-04-14  
**Branch:** v0.3-dev

---

## Goal

Implement `ObservableDictionarySavableLazy<TKey, TValue>` following the same patterns as `ObservableListSavableLazy<T>` and `ObservableArraySavableLazy<T>`. Elements are loaded on-demand from the serializer rather than all at once during deserialization.

---

## Class Hierarchy

Three new files, one modified file:

```
ObservableCollectionSavable<TCol, TElem>
  └── ObservableDictionarySavableBase<TKey, TValue>               (new, abstract)
        └── ObservableDictionarySavableBase<TSelf, TKey, TValue>  (new, abstract CRTP)
              ├── ObservableDictionarySavable<TKey, TValue>        (modified: extend CRTP base)
              └── ObservableDictionarySavableLazy<TKey, TValue>    (new, sealed)
```

`ICollectionWrapper<Dictionary<TKey, TValue?>>` is **not** placed on the base class — only `ObservableDictionarySavable` implements it, since `ObservableDictionarySavableLazy` has no complete backing dictionary.

---

## `ObservableDictionarySavableBase<TKey, TValue>`

Extends `ObservableCollectionSavable<ObservableDictionarySavableBase<TKey, TValue>, TValue>`.  
Implements `IDictionary<TKey, TValue?>`, `IReadOnlyDictionary<TKey, TValue?>`, `IDictionary`.

**Contains:**
- `protected readonly ISvIdConverter<TKey> _idConverter` — shared by both subclasses
- Abstract surface: `this[TKey]`, `Keys`, `Values`, `Count`, `ContainsKey`, `Add`, `Remove(TKey)`, `TryGetValue`, `Clear`, `GetEnumerator`, `SwapSource`
- Shared explicit implementations of `IDictionary` (object-key overloads), `IReadOnlyDictionary`, `IEnumerable`
- Shared implementation of `Remove(KeyValuePair<TKey, TValue?> item)`: calls `TryGetValue` first (triggering a load if needed), compares value, then delegates to `Remove(TKey key)` if matched

---

## `ObservableDictionarySavableBase<TSelf, TKey, TValue>` (CRTP layer)

Extends `ObservableDictionarySavableBase<TKey, TValue>`.

**Contains:**
- `OnChildChanged` override — fires `CollectionChangeInfo<ObservableDictionarySavableBase<TKey, TValue>, TValue?>.Replace` with `index = -1`

---

## `ObservableDictionarySavable<TKey, TValue>` (refactor)

Extends `ObservableDictionarySavableBase<ObservableDictionarySavable<TKey, TValue>, TKey, TValue>`.  
Also implements `ICollectionWrapper<Dictionary<TKey, TValue?>>`.

**No logic changes** — only the base class declaration changes. All existing fields, methods, and behavior remain identical.

---

## `ObservableDictionarySavableLazy<TKey, TValue>`

Extends `ObservableDictionarySavableBase<ObservableDictionarySavableLazy<TKey, TValue>, TKey, TValue>`.

### Constructor

```csharp
public ObservableDictionarySavableLazy(string propName, bool saveSeparately, CollectionOptions options)
```

Throws `InvalidOperationException` if `options.Mode == LazyLoadMode.None`.

### Internal State

```csharp
private Dictionary<TKey, Slot> _loadedSlots;
private readonly CollectionOptions _options;
private readonly ReaderWriterLockSlim _lock;
private int _count;         // total count from metadata, includes unloaded entries
private int _loadedCount;
private uint _version;      // incremented only by Add, Remove, Clear, SwapSource, indexer set
private Serializer? _serializer;
```

`Slot` is the existing struct from `ObservableCollectionSavable` (Id/Value/IsDirty/IsLoaded).

`_version` is **not** incremented by read operations (indexer get, TryGetValue). Only mutating operations increment it.

### `SetParent` override

Caches `parent?.GetSerializer()` into `_serializer`, same as List/Array lazy.

### Deserialization

```
Deserialize(serializer, ctx):
  read CollectionMetadata → _count
  clear _loadedSlots
  _loadedCount = 0
  _isDirty = false
```

Only count is stored. Keys and values are unknown until accessed. Note: `_count` reflects the last persisted metadata — any in-flight mutations not yet serialized are not reflected after a re-deserialize.

### Serialization

```
Serialize(serializer, ctx):
  EnterUpgradeableReadLock
  for each (key, slot) in _loadedSlots:
    if slot.IsTrueDirty:
      EnterWriteLock
      if value == null: serializer.Delete(ctx, slot.Id, Collection)
      else: push id → value.Serialize(serializer, ctx)
      mark slot not dirty (_loadedSlots[key] = new Slot(..., false, ...))
      ExitWriteLock
  ExitUpgradeableReadLock
  write CollectionMetadata { Count = _count }
```

Uses `EnterUpgradeableReadLock` (not write lock) to match `ObservableListSavableLazy.Serialize`, allowing concurrent reads on unvisited slots during serialization.

Entries that were removed via `Remove()` are not in `_loadedSlots` — no action needed since `Remove` calls `serializer.Delete` immediately at mutation time.

### `IsDirty` Override

```csharp
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
```

Only loaded slots are checked — unloaded slots have no in-memory state to be dirty.

### `SetDirty` Override

```csharp
public override void SetDirty(bool dirty, bool recursive)
{
    base.SetDirty(dirty, recursive);
    if (recursive)
    {
        _lock.EnterWriteLock();
        try
        {
            // Iterate entries directly — write lock is held exclusively,
            // no concurrent mutation possible, no defensive copy needed.
            foreach (var key in _loadedSlots.Keys)
            {
                var slot = _loadedSlots[key];
                if (slot.IsLoaded)
                {
                    slot.Value?.SetDirty(dirty, recursive);
                    _loadedSlots[key] = new Slot(slot.Id, slot.Value, dirty, slot.IsLoaded);
                }
            }
        }
        finally { _lock.ExitWriteLock(); }
    }
}
```

Note: iterating `_loadedSlots.Keys` directly (no `.ToList()` copy) is safe under `EnterWriteLock` since no other writer can mutate the dictionary concurrently. The Slot replacement inside the loop does not affect the Keys enumerator because only the value is changed, not the key set.

### Operation Behavior by Mode

| Operation | `LoadAll` mode | `LoadIndividual` mode |
|-----------|---------------|----------------------|
| `Count` | Return `_count` directly | Same |
| `ContainsKey(key)` | See detail below (no LoadAll trigger) | Same |
| `Keys` | See detail below (never triggers LoadAll) | Same |
| `this[key] get` | Trigger `LoadAll()` → return from `_loadedSlots` | Load single item: `serializer.Read(ctx, id, Collection)` |
| `TryGetValue` | Same as indexer | Same as indexer |
| `Values` | Trigger `LoadAll()` → enumerate `_loadedSlots.Values` | Lazy enumeration via `ListCollectionIdsMinMax` + BatchLoad |
| `GetEnumerator()` | Trigger `LoadAll()` → enumerate `_loadedSlots` | Lazy enumeration via `ListCollectionIdsMinMax` + BatchLoad |
| `Add(key, value)` | Immediate serializer sync; `_count++`; `_version++` | Same |
| `Remove(key)` | Immediate `serializer.Delete`; `_loadedSlots.Remove(key)`; `_count--`; `_version++` | Same |
| `Clear()` | `serializer.DeleteAllNoPushPath`; clear `_loadedSlots`; `_count = 0`; `_version++` | Same |
| `SwapSource(dict)` | TryUnWatch all loaded slots → clear → re-add all; immediate sync; `_version++` | Same |

### `ContainsKey` Detail

```
EnterReadLock
if _loadedSlots.TryGetValue(key, out slot) && slot.IsLoaded → ExitReadLock; return true
ExitReadLock
// key not in loaded slots — check serializer directly
var id = _idConverter.ConvertTo(key)
serializer.BeginFreshAction(this, out ctx)
return serializer.Has(ctx, id, Collection)
```

The result is best-effort consistent: a concurrent `Add`/`Remove` between the read-lock release and the `Has` call may cause the result to reflect the post-mutation state. This is the same eventual-consistency trade-off accepted by `ObservableListSavableLazy`. Callers must not rely on `ContainsKey` being atomic with respect to concurrent mutations.

### `Keys` Property Detail

```
serializer.BeginFreshAction(this, out ctx)   // no collection lock held — read-path fallback
return serializer.ListCollectionIds(ctx)
    .Select(id => _idConverter.ConvertFrom(id))
    .ToList()
```

- Always reads from the serializer; **never cached**.
- Does not trigger `LoadAll()` in either mode.
- Only ID metadata is read — no values are loaded.
- Returns a **snapshot** `List<TKey>` (not a live view). Unlike `ObservableDictionarySavable.Keys` which returns a live `Dictionary.KeyCollection`, mutations after the call are not reflected in the returned list.

### Indexer Load (LoadIndividual)

```
this[key] get:
  EnterUpgradeableReadLock
  if _loadedSlots.TryGetValue(key, out slot) && slot.IsLoaded → ExitUpgradeable; return slot.Value
  EnterWriteLock
  id = _idConverter.ConvertTo(key)
  item = serializer.Read<TValue>(ctx, id, Collection)
  _loadedSlots[key] = new Slot(id, item, false, true)
  if item != null: OnChildDeserialized(item)
  _loadedCount++
  ExitWriteLock
  ExitUpgradeableReadLock
  return item
```

No batch load — key access is typically non-sequential.

### BatchLoad Enumeration (LoadIndividual)

Used by `GetEnumerator()` and `Values`. Both hold `EnterUpgradeableReadLock` per iteration, escalating to `EnterWriteLock` when a slot needs loading (same pattern as `ObservableListSavableLazy.GetEnumerator`).

```
lastYieldedId = null
version = _version

loop:
  if version != _version: throw InvalidOperationException("collection was modified")
  serializer.BeginFreshAction(this, out ctx)
  ids = serializer.ListCollectionIdsMinMax(ctx, lastYieldedId, null)
  loaded = 0
  for each id in ids:
    if loaded >= BatchLoadCount (default 1): break
    key = _idConverter.ConvertFrom(id)
    EnterUpgradeableReadLock
    if _loadedSlots.TryGetValue(key, out slot) && slot.IsLoaded:
      yield KeyValuePair(key, slot.Value)
    else:
      EnterWriteLock
      item = serializer.Read<TValue>(ctx, id, Collection)
      _loadedSlots[key] = new Slot(id, item, false, true)
      if item != null: OnChildDeserialized(item)
      _loadedCount++
      ExitWriteLock
      yield KeyValuePair(key, item)
    ExitUpgradeableReadLock
    lastYieldedId = id
    loaded++
  if no ids returned: break
```

### `SwapSource` Detail

```
EnterWriteLock
serializer.BeginFreshAction(this, out ctx)

// 1. Unwatch and delete all existing loaded slots
foreach (key, slot) in _loadedSlots:
  TryUnWatch(slot.Value)
  if !string.IsNullOrEmpty(slot.Id):
    serializer.Delete(ctx, slot.Id, Collection)
_loadedSlots.Clear()
_count = 0
_loadedCount = 0

// 2. Add new items
if dict != null:
  foreach (key, value) in dict:
    id = _idConverter.ConvertTo(key)
    if value != null: value.SvId = id
    _loadedSlots[key] = new Slot(id, value, true, true)
    if value == null: serializer.Save<TValue?>(default, ctx, id, Collection)
    else: push id → value.Serialize(serializer, ctx)
    TryWatch(value)
  _count = dict.Count
  _loadedCount = dict.Count

_isDirty = true
_version++
SaveMetadata(serializer, ctx, _count)
OnCollectionChange(Reset)
ExitWriteLock
```

### Immediate Sync Operations

`Add`, `Remove`, and indexer `set` follow the same pattern as `ObservableListSavableLazy`: acquire write lock → mutate `_loadedSlots` + `_count` + `_version` → `serializer.BeginFreshAction` → sync → `SaveMetadata` → fire `OnCollectionChange`.

`Remove` must explicitly call `_loadedSlots.Remove(key)` in addition to `serializer.Delete`, regardless of whether the slot was loaded.

### `LoadAll()` Detail

```
LoadAll():
  EnterWriteLock               // acquire collection lock first
  if _count == 0: ExitWriteLock; return
  serializer.BeginFreshAction(this, out ctx)   // then acquire serializer path lock
  foreach id in serializer.ListCollectionIds(ctx):
    key = _idConverter.ConvertFrom(id)
    if !_loadedSlots.TryGetValue(key, out slot) || !slot.IsLoaded:
      item = serializer.Read<TValue>(ctx, id, Collection)
      _loadedSlots[key] = new Slot(id, item, false, true)
      if item != null: OnChildDeserialized(item)
      _loadedCount++
  ExitWriteLock
```

Lock-acquisition order: write lock first, then `BeginFreshAction`. All mutating operations follow this same order.

### Public Helpers

```csharp
public bool IsLoaded(TKey key)   // check if key's value is in _loadedSlots and IsLoaded=true
public void LoadAll()             // force load all entries via serializer.ListCollectionIds
public int LoadedCount => _loadedCount;
```

### Thread Safety

- Read operations use `EnterReadLock` or `EnterUpgradeableReadLock`.
- Write operations (Add, Remove, Clear, SwapSource, indexer set, LoadAll, SetDirty) use `EnterWriteLock`.
- **Lock-order rule for mutating operations**: always acquire the collection write lock first, then call `serializer.BeginFreshAction`. Never call `BeginFreshAction` before acquiring the write lock in a mutating path, or a deadlock with a concurrent serializer operation is possible.
- **Lock-order rule for read-path fallbacks** (`ContainsKey`, `Keys`): these do NOT hold the collection lock when calling the serializer. The result is best-effort consistent under concurrent mutations — this is an explicit trade-off, same as `ObservableListSavableLazy`.
- `Serialize` and immediate-sync operations must not be called concurrently — the serializer's own internal lock (`BeginFreshAction`) enforces this with an `InvalidOperationException` rather than a deadlock.
- **Re-entrancy warning**: `GetEnumerator` and `Values` (BatchLoad path) hold `EnterUpgradeableReadLock` across `yield return`. Callers must not call back into any collection operation from within a `foreach` loop over `GetEnumerator` or `Values`, as `ReaderWriterLockSlim` does not support recursive acquisition and will deadlock. This matches the behavior of `ObservableListSavableLazy.GetEnumerator`.

---

## Files

| File | Action |
|------|--------|
| `Salvavida/Package/Runtime/ObservableDictionarySavableBase.cs` | Create |
| `Salvavida/Package/Runtime/ObservableDictionarySavableLazy.cs` | Create |
| `Salvavida/Package/Runtime/ObservableDictionarySavable.cs` | Modify (base class declaration only) |
