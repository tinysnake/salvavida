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
- Abstract surface: `this[TKey]`, `Keys`, `Values`, `Count`, `ContainsKey`, `Add`, `Remove`, `TryGetValue`, `Clear`, `GetEnumerator`, `SwapSource`
- Shared explicit implementations of `IDictionary` (object-key overloads), `IReadOnlyDictionary`, `ICollection<KeyValuePair<...>>`, `IEnumerable`

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
private uint _version;
private Serializer? _serializer;
```

`Slot` is the existing struct from `ObservableCollectionSavable` (Id/Value/IsDirty/IsLoaded).

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

Only count is stored. Keys and values are unknown until accessed.

### Serialization

```
Serialize(serializer, ctx):
  write lock
  for each (key, slot) in _loadedSlots:
    if slot.IsTrueDirty:
      if value == null: serializer.Delete(ctx, slot.Id, Collection)
      else: push id → value.Serialize(serializer, ctx)
      mark slot not dirty
  write CollectionMetadata { Count = _count }
```

### Operation Behavior by Mode

| Operation | `LoadAll` mode | `LoadIndividual` mode |
|-----------|---------------|----------------------|
| `Count` | Return `_count` directly | Same |
| `ContainsKey(key)` | Check `_loadedSlots`; if not found → `serializer.Has(ctx, id, Collection)` | Same |
| `Keys` | `serializer.ListCollectionIds(ctx)` → convert all IDs; **never cached** | Same |
| `this[key] get` | Trigger `LoadAll()` → return from `_loadedSlots` | Load single item: `serializer.Read(ctx, id, Collection)` |
| `TryGetValue` | Same as indexer | Same as indexer |
| `Values` | Trigger `LoadAll()` → enumerate `_loadedSlots.Values` | Lazy enumeration via `ListCollectionIdsMinMax` + BatchLoad |
| `GetEnumerator()` | Trigger `LoadAll()` → enumerate `_loadedSlots` | Lazy enumeration via `ListCollectionIdsMinMax` + BatchLoad |
| `Add(key, value)` | Immediate serializer sync; `_count++` | Same |
| `Remove(key)` | Immediate `serializer.Delete`; `_count--` | Same |
| `Clear()` | `serializer.DeleteAllNoPushPath`; clear `_loadedSlots`; `_count = 0` | Same |
| `SwapSource(dict)` | Clear + re-add all; immediate sync | Same |

### `ContainsKey` Detail

```
1. EnterReadLock
2. if _loadedSlots.ContainsKey(key) → return true
3. ExitReadLock
4. var id = _idConverter.ConvertTo(key)
5. serializer.BeginFreshAction → Has(ctx, id, Collection)
```

If element was removed locally (slot exists with IsLoaded=true but removed from dict), `_loadedSlots` will not contain it — no false positives.

### Indexer Load (LoadIndividual)

```
this[key] get:
  EnterUpgradeableReadLock
  if _loadedSlots[key].IsLoaded → return value
  EnterWriteLock
  id = _idConverter.ConvertTo(key)
  item = serializer.Read<TValue>(ctx, id, Collection)
  _loadedSlots[key] = new Slot(id, item, false, true)
  if item != null: OnChildDeserialized(item)
  _loadedCount++
  return item
```

No batch load — key access is typically non-sequential.

### BatchLoad Enumeration (LoadIndividual)

Used by `GetEnumerator()` and `Values`:

```
lastYieldedId = null
loop:
  ids = serializer.ListCollectionIdsMinMax(ctx, lastYieldedId, null)
  loaded = 0
  for each id in ids:
    if loaded >= BatchLoadCount (default 1): break
    key = _idConverter.ConvertFrom(id)
    if _loadedSlots[key].IsLoaded:
      yield kvp from slot
    else:
      item = serializer.Read<TValue>(ctx, id, Collection)
      _loadedSlots[key] = new Slot(id, item, false, true)
      if item != null: OnChildDeserialized(item)
      _loadedCount++
      yield new KeyValuePair(key, item)
    lastYieldedId = id
    loaded++
  if no ids returned or all consumed: break
```

Version check on each iteration (throw `InvalidOperationException` if `_version` changed).

### Immediate Sync Operations

`Add`, `Remove`, `Clear`, `SwapSource`, and the `set` indexer all follow the same pattern as `ObservableListSavableLazy`: acquire write lock → mutate `_loadedSlots` + `_count` → `serializer.BeginFreshAction` → sync → `SaveMetadata` → fire `OnCollectionChange`.

### Public Helpers

```csharp
public bool IsLoaded(TKey key)   // check if key's value is in _loadedSlots and loaded
public void LoadAll()             // force load all entries
public int LoadedCount => _loadedCount;
```

### Thread Safety

All read operations use `EnterReadLock` / `EnterUpgradeableReadLock`.  
All write operations use `EnterWriteLock`.  
`ContainsKey` fallback to `serializer.Has` happens outside the lock (serializer is itself thread-safe).

---

## Files

| File | Action |
|------|--------|
| `Salvavida/Package/Runtime/ObservableDictionarySavableBase.cs` | Create |
| `Salvavida/Package/Runtime/ObservableDictionarySavableLazy.cs` | Create |
| `Salvavida/Package/Runtime/ObservableDictionarySavable.cs` | Modify (base class only) |
