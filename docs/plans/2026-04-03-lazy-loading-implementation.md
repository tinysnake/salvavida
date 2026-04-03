# Lazy Loading Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement page-based lazy loading for ObservableListSavable and ObservableArraySavable collections, reducing memory usage and initial load time for large collections.

**Architecture:** Abstract base class pattern with runtime type decision. ObservableListSavableBase provides interface, ObservableListSavable (full load) and ObservableLazyListSavable (lazy load) implement storage strategies. Serializer factory method decides which concrete type to instantiate based on metadata count at deserialize time.

**Tech Stack:** C#, .NET, Unity-compatible, SourceGenerator (Roslyn)

---

## Phase 1: Core Data Structures

### Task 1: Create CacheStrategy enum

**Files:**
- Create: `Salvavida/Package/Runtime/LazyLoading/CacheStrategy.cs`

**Step 1: Create CacheStrategy enum**

```csharp
namespace Salvavida
{
    /// <summary>
    /// Cache strategy for lazy-loaded collection pages.
    /// </summary>
    public enum CacheStrategy
    {
        /// <summary>
        /// No cache eviction. Pages stay loaded once accessed.
        /// </summary>
        None = 0,

        /// <summary>
        /// Least Recently Used eviction. Evicts least recently accessed pages when cache is full.
        /// </summary>
        LRU = 1
    }
}
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/LazyLoading/CacheStrategy.cs
git commit -m "feat: add CacheStrategy enum for lazy loading"
```

---

### Task 2: Create LazyLoadConfig class

**Files:**
- Create: `Salvavida/Package/Runtime/LazyLoading/LazyLoadConfig.cs`

**Step 1: Create LazyLoadConfig class**

```csharp
namespace Salvavida
{
    /// <summary>
    /// Configuration for lazy-loaded collections.
    /// </summary>
    public class LazyLoadConfig
    {
        /// <summary>
        /// Element count threshold to trigger lazy loading.
        /// Collections with count > threshold will use lazy loading.
        /// Default: 1000
        /// </summary>
        public int Threshold { get; set; } = 1000;

        /// <summary>
        /// Number of elements per page.
        /// Default: 100
        /// </summary>
        public int PageSize { get; set; } = 100;

        /// <summary>
        /// Maximum number of pages to keep in memory cache.
        /// Only applies when CacheStrategy is LRU.
        /// Default: 10
        /// </summary>
        public int MaxCachedPages { get; set; } = 10;

        /// <summary>
        /// Cache eviction strategy.
        /// Default: LRU
        /// </summary>
        public CacheStrategy CacheStrategy { get; set; } = CacheStrategy.LRU;
    }
}
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/LazyLoading/LazyLoadConfig.cs
git commit -m "feat: add LazyLoadConfig for lazy loading configuration"
```

---

### Task 3: Create CollectionMetadata class

**Files:**
- Create: `Salvavida/Package/Runtime/LazyLoading/CollectionMetadata.cs`

**Step 1: Create CollectionMetadata class**

```csharp
using System;

namespace Salvavida
{
    /// <summary>
    /// Metadata for lazy-loaded collections, stored in __ob_metadata__.
    /// </summary>
    [Serializable]
    public class CollectionMetadata
    {
        /// <summary>
        /// Array of element IDs (indices as strings).
        /// </summary>
        public string[]? Ids { get; set; }

        /// <summary>
        /// Total element count.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Number of elements per page.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Maximum number of cached pages.
        /// </summary>
        public int MaxCachedPages { get; set; }

        /// <summary>
        /// Cache eviction strategy.
        /// </summary>
        public CacheStrategy CacheStrategy { get; set; }

        /// <summary>
        /// Whether this collection uses lazy loading format.
        /// </summary>
        public bool IsLazyLoaded { get; set; }
    }
}
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/LazyLoading/CollectionMetadata.cs
git commit -m "feat: add CollectionMetadata for lazy loading storage"
```

---

### Task 4: Create LazyLoadAttribute

**Files:**
- Create: `Salvavida/Package/Runtime/LazyLoading/LazyLoadAttribute.cs`

**Step 1: Create LazyLoadAttribute class**

```csharp
using System;

namespace Salvavida
{
    /// <summary>
    /// Attribute to configure lazy loading for collection properties.
    /// Place on fields or properties that are List or Array types.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class LazyLoadAttribute : Attribute
    {
        /// <summary>
        /// Element count threshold to trigger lazy loading.
        /// Collections with count > threshold will use lazy loading.
        /// Default: 1000
        /// </summary>
        public int Threshold { get; set; } = 1000;

        /// <summary>
        /// Number of elements per page.
        /// Default: 100
        /// </summary>
        public int PageSize { get; set; } = 100;

        /// <summary>
        /// Maximum number of pages to keep in memory cache.
        /// Only applies when CacheStrategy is LRU.
        /// Default: 10
        /// </summary>
        public int MaxCachedPages { get; set; } = 10;

        /// <summary>
        /// Cache eviction strategy.
        /// Default: LRU
        /// </summary>
        public CacheStrategy CacheStrategy { get; set; } = CacheStrategy.LRU;

        /// <summary>
        /// Convert attribute to LazyLoadConfig.
        /// </summary>
        public LazyLoadConfig ToConfig()
        {
            return new LazyLoadConfig
            {
                Threshold = Threshold,
                PageSize = PageSize,
                MaxCachedPages = MaxCachedPages,
                CacheStrategy = CacheStrategy
            };
        }
    }
}
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/LazyLoading/LazyLoadAttribute.cs
git commit -m "feat: add LazyLoadAttribute for compile-time configuration"
```

---

### Task 5: Create GlobalConfig

**Files:**
- Create: `Salvavida/Package/Runtime/LazyLoading/GlobalConfig.cs`

**Step 1: Create GlobalConfig static class**

```csharp
namespace Salvavida
{
    /// <summary>
    /// Global configuration defaults for lazy loading.
    /// </summary>
    public static class GlobalConfig
    {
        /// <summary>
        /// Default threshold for lazy loading.
        /// Collections with count > threshold will use lazy loading.
        /// </summary>
        public static int DefaultLazyLoadThreshold { get; set; } = 1000;

        /// <summary>
        /// Default page size for lazy loading.
        /// </summary>
        public static int DefaultPageSize { get; set; } = 100;

        /// <summary>
        /// Default maximum cached pages for LRU strategy.
        /// </summary>
        public static int DefaultMaxCachedPages { get; set; } = 10;

        /// <summary>
        /// Default cache eviction strategy.
        /// </summary>
        public static CacheStrategy DefaultCacheStrategy { get; set; } = CacheStrategy.LRU;
    }
}
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/LazyLoading/GlobalConfig.cs
git commit -m "feat: add GlobalConfig for lazy loading defaults"
```

---

## Phase 2: ObservableListSavableBase Abstract Base Class

### Task 6: Create ObservableListSavableBase

**Files:**
- Create: `Salvavida/Package/Runtime/ObservableListSavableBase.cs`

**Step 1: Create ObservableListSavableBase abstract class**

Read the existing `ObservableList.cs` first to understand the current structure, then create:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    /// <summary>
    /// Abstract base class for ObservableListSavable collections.
    /// Provides common interface for both full-load and lazy-load implementations.
    /// </summary>
    /// <typeparam name="T">Element type, must implement ISavable</typeparam>
    public abstract class ObservableListSavableBase<T> : ObservableCollectionSavable<ObservableListSavableBase<T>, T>, IList<T?>, IReadOnlyList<T?>, IList
        where T : ISavable
    {
        protected ObservableListSavableBase(string propName, bool saveSeparately)
            : base(propName, saveSeparately)
        {
        }

        #region Abstract Methods

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        public abstract T? this[int index] { get; set; }

        /// <summary>
        /// Gets the number of elements in the collection.
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// Adds an item to the collection.
        /// </summary>
        public abstract void Add(T? item);

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public abstract void Clear();

        /// <summary>
        /// Determines whether the collection contains a specific value.
        /// </summary>
        public abstract bool Contains(T? item);

        /// <summary>
        /// Determines the index of a specific item.
        /// </summary>
        public abstract int IndexOf(T? item);

        /// <summary>
        /// Inserts an item at the specified index.
        /// </summary>
        public abstract void Insert(int index, T? item);

        /// <summary>
        /// Removes the first occurrence of a specific object.
        /// </summary>
        public abstract bool Remove(T? item);

        /// <summary>
        /// Removes the item at the specified index.
        /// </summary>
        public abstract void RemoveAt(int index);

        /// <summary>
        /// Replaces the entire collection with a new list.
        /// </summary>
        public abstract void SwapSource(List<T?>? list);

        #endregion

        #region IList<T?> Explicit Implementation

        bool ICollection<T?>.IsReadOnly => false;
        bool IList.IsReadOnly => false;
        bool IList.IsFixedSize => false;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (T?)value;
        }

        int IList.Add(object? value)
        {
            Add((T?)value);
            return Count - 1;
        }

        void IList.Clear() => Clear();

        bool IList.Contains(object? value) => Contains((T?)value);

        int IList.IndexOf(object? value) => IndexOf((T?)value);

        void IList.Insert(int index, object? value) => Insert(index, (T?)value);

        void IList.Remove(object? value) => Remove((T?)value);

        void IList.RemoveAt(int index) => RemoveAt(index);

        public void CopyTo(T?[] array, int arrayIndex)
        {
            for (int i = 0; i < Count; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            for (int i = 0; i < Count; i++)
            {
                array.SetValue(this[i], index + i);
            }
        }

        public abstract IEnumerator<T?> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableListSavableBase.cs
git commit -m "feat: add ObservableListSavableBase abstract base class"
```

---

## Phase 3: Refactor ObservableListSavable

### Task 7: Update ObservableListSavable to extend base class

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableList.cs`

**Step 1: Change base class and mark methods as override**

Modify `ObservableListSavable<T>` class:
1. Change base class from `ObservableCollectionSavable<ObservableListSavable<T>, T>` to `ObservableListSavableBase<T>`
2. Add `override` to all abstract methods
3. Keep existing implementation logic unchanged

Key changes needed:
- `public sealed class ObservableListSavable<T> : ObservableListSavableBase<T>`
- Add `override` to: `this[int index]`, `Count`, `Add`, `Clear`, `Contains`, `IndexOf`, `Insert`, `Remove`, `RemoveAt`, `SwapSource`, `Serialize`, `Deserialize`, `IsDirty`
- Add `public override IEnumerator<T?> GetEnumerator()` implementation

**Step 2: Build to verify**

```bash
dotnet build Salvavida/Salvavida.csproj --verbosity quiet
```

Expected: Build successful with no new errors

**Step 3: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableList.cs
git commit -m "refactor: ObservableListSavable extends ObservableListSavableBase"
```

---

## Phase 4: ObservableLazyListSavable Implementation

### Task 8: Create ObservableLazyListSavable skeleton

**Files:**
- Create: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

**Step 1: Create basic structure with PageData**

```csharp
using Salvavida.DefaultImpl;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable list for ISavable elements.
    /// Elements are loaded on-demand by page, with optional LRU cache eviction.
    /// </summary>
    public sealed partial class ObservableLazyListSavable<T> : ObservableListSavableBase<T>
        where T : ISavable
    {
        #region Configuration

        private readonly int _pageSize;
        private readonly int _maxCachedPages;
        private readonly CacheStrategy _cacheStrategy;
        private readonly string _propertyName;

        #endregion

        #region Page Storage

        private readonly Dictionary<int, PageData> _loadedPages = new();
        private int _totalElementCount;
        private string[] _allIds = Array.Empty<string>();

        #endregion

        #region LRU Tracking

        private readonly LinkedList<int> _lruList = new();

        #endregion

        #region State Tracking

        private bool _hasPendingWrites;
        private Serializer? _serializer;
        private SerializeContext? _context;

        #endregion

        #region Concurrency

        private readonly ReaderWriterLockSlim _rwLock = new();

        #endregion

        #region Constructor

        public ObservableLazyListSavable(
            string propName,
            bool saveSeparately,
            int pageSize,
            int maxCachedPages,
            CacheStrategy cacheStrategy)
            : base(propName, saveSeparately)
        {
            _propertyName = propName;
            _pageSize = pageSize > 0 ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));
            _maxCachedPages = maxCachedPages > 0 ? maxCachedPages : throw new ArgumentOutOfRangeException(nameof(maxCachedPages));
            _cacheStrategy = cacheStrategy;
        }

        #endregion

        #region Nested Types

        private class PageData
        {
            public T?[] Elements;
            public bool IsDirty;

            public PageData(int pageSize)
            {
                Elements = new T?[pageSize];
                IsDirty = false;
            }
        }

        #endregion

        #region Properties

        public override int Count => _totalElementCount;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty) return true;

                foreach (var page in _loadedPages.Values)
                {
                    if (page.IsDirty) return true;
                    foreach (var elem in page.Elements)
                    {
                        if (elem?.IsDirty == true) return true;
                    }
                }

                return false;
            }
        }

        #endregion

        // Implementation methods will be added in subsequent tasks

        public override T? this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override void Add(T? item) => throw new NotImplementedException();
        public override void Clear() => throw new NotImplementedException();
        public override bool Contains(T? item) => throw new NotImplementedException();
        public override int IndexOf(T? item) => throw new NotImplementedException();
        public override void Insert(int index, T? item) => throw new NotImplementedException();
        public override bool Remove(T? item) => throw new NotImplementedException();
        public override void RemoveAt(int index) => throw new NotImplementedException();
        public override void SwapSource(List<T?>? list) => throw new NotImplementedException();
        public override void Serialize(Serializer serializer, SerializeContext ctx) => throw new NotImplementedException();
        public override void Deserialize(Serializer serializer, SerializeContext ctx) => throw new NotImplementedException();
        public override IEnumerator<T?> GetEnumerator() => throw new NotImplementedException();
    }
}
```

**Step 2: Build to verify skeleton compiles**

```bash
dotnet build Salvavida/Salvavida.csproj --verbosity quiet
```

Expected: Build successful

**Step 3: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: add ObservableLazyListSavable skeleton"
```

---

### Task 9: Implement page loading methods

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

**Step 1: Add page loading helper methods**

Add these methods to the partial class:

```csharp
        #region Page Loading

        private PageData GetOrLoadPage(int pageIndex)
        {
            if (_loadedPages.TryGetValue(pageIndex, out var page))
                return page;

            // Evict if at capacity
            if (_cacheStrategy == CacheStrategy.LRU && _loadedPages.Count >= _maxCachedPages)
                EvictLruPage();

            page = LoadPageFromStorage(pageIndex);
            _loadedPages[pageIndex] = page;
            UpdateLru(pageIndex);

            return page;
        }

        private PageData LoadPageFromStorage(int pageIndex)
        {
            var page = new PageData(_pageSize);

            int startIdx = pageIndex * _pageSize;
            int endIdx = Math.Min(startIdx + _pageSize, _totalElementCount);

            for (int i = startIdx; i < endIdx; i++)
            {
                string id = _allIds[i];
                page.Elements[i - startIdx] = _serializer!.Read<T?>(_context!, id, PathBuilder.Type.Collection);
                var elem = page.Elements[i - startIdx];
                if (elem != null)
                {
                    OnChildDeserialized(elem);
                }
            }

            return page;
        }

        private void TouchPage(int pageIndex)
        {
            if (_cacheStrategy != CacheStrategy.LRU)
                return;

            _lruList.Remove(pageIndex);
            _lruList.AddFirst(pageIndex);
        }

        private void UpdateLru(int pageIndex)
        {
            if (_cacheStrategy != CacheStrategy.LRU)
                return;

            _lruList.AddFirst(pageIndex);
        }

        private void EvictLruPage()
        {
            if (_lruList.Count == 0)
                return;

            int pageIndex = _lruList.Last.Value;
            _lruList.RemoveLast();

            if (_loadedPages.TryGetValue(pageIndex, out var page))
            {
                // Flush dirty page before eviction
                if (page.IsDirty)
                    FlushPage(pageIndex, page);

                // Unwatch elements before evicting
                foreach (var elem in page.Elements)
                {
                    TryUnWatch(elem);
                }

                _loadedPages.Remove(pageIndex);
            }
        }

        private void FlushPage(int pageIndex, PageData page)
        {
            int startIdx = pageIndex * _pageSize;
            int endIdx = Math.Min(startIdx + _pageSize, _totalElementCount);

            for (int i = startIdx; i < endIdx; i++)
            {
                string id = _allIds[i];
                var elem = page.Elements[i - startIdx];

                if (elem == null)
                {
                    _serializer!.Delete(_context!, id, PathBuilder.Type.Collection);
                }
                else
                {
                    using (_context!.Path.UsePush(id, PathBuilder.Type.Collection))
                    {
                        elem.Serialize(_serializer!, _context!);
                    }
                }
            }

            page.IsDirty = false;
        }

        #endregion
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: implement page loading and LRU eviction"
```

---

### Task 10: Implement indexer (get/set)

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

**Step 1: Replace indexer implementation**

```csharp
        public override T? this[int index]
        {
            get
            {
                if (index < 0 || index >= _totalElementCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                _rwLock.EnterReadLock();
                try
                {
                    var pageIndex = index / _pageSize;
                    var localIndex = index % _pageSize;
                    var page = GetOrLoadPage(pageIndex);
                    TouchPage(pageIndex);
                    return page.Elements[localIndex];
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                if (index < 0 || index >= _totalElementCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                _rwLock.EnterWriteLock();
                try
                {
                    var pageIndex = index / _pageSize;
                    var localIndex = index % _pageSize;
                    var page = GetOrLoadPage(pageIndex);

                    var oldValue = page.Elements[localIndex];
                    if (EqualityComparer<T?>.Default.Equals(oldValue, value))
                        return;

                    TryUnWatch(oldValue);
                    page.Elements[localIndex] = value;
                    page.IsDirty = true;
                    _hasPendingWrites = true;
                    TryWatch(value);

                    OnCollectionChange(CollectionChangeInfo<ObservableLazyListSavable<T>, T?>.Replace(this, oldValue, value, index));
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: implement indexer for ObservableLazyListSavable"
```

---

### Task 11: Implement Add and Clear methods

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

**Step 1: Replace Add implementation**

```csharp
        public override void Add(T? item)
        {
            _rwLock.EnterWriteLock();
            try
            {
                var pageIndex = _totalElementCount / _pageSize;
                var localIndex = _totalElementCount % _pageSize;

                var page = GetOrLoadPage(pageIndex);
                page.Elements[localIndex] = item;
                page.IsDirty = true;
                _hasPendingWrites = true;

                // Update ID array
                Array.Resize(ref _allIds, _totalElementCount + 1);
                _allIds[_totalElementCount] = _totalElementCount.ToString();

                _totalElementCount++;

                OnItemSet(item, _totalElementCount - 1);
                OnCollectionChange(CollectionChangeInfo<ObservableLazyListSavable<T>, T?>.Add(this, item, _totalElementCount - 1));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
```

**Step 2: Replace Clear implementation**

```csharp
        public override void Clear()
        {
            _rwLock.EnterWriteLock();
            try
            {
                // Delete all elements from storage
                if (_serializer != null && _context != null)
                {
                    foreach (var id in _allIds)
                    {
                        _serializer.Delete(_context, id, PathBuilder.Type.Collection);
                    }
                }

                // Unwatch all loaded elements
                foreach (var page in _loadedPages.Values)
                {
                    foreach (var elem in page.Elements)
                    {
                        TryUnWatch(elem);
                    }
                }

                _loadedPages.Clear();
                _lruList.Clear();
                _allIds = Array.Empty<string>();
                _totalElementCount = 0;
                _hasPendingWrites = false;

                OnCollectionChange(CollectionChangeInfo<ObservableLazyListSavable<T>, T?>.Reset(this));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
```

**Step 3: Add OnItemSet helper**

```csharp
        private void OnItemSet(T? item, int index)
        {
            if (item is ISavable sv)
                sv.SvId ??= DefaultIdGenerator.Default.GetId();
            TryWatch(item);
        }
```

**Step 4: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: implement Add and Clear for ObservableLazyListSavable"
```

---

### Task 12: Implement Insert and RemoveAt methods

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

**Step 1: Replace Insert implementation**

```csharp
        public override void Insert(int index, T? item)
        {
            if (index < 0 || index > _totalElementCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            _rwLock.EnterWriteLock();
            try
            {
                // Force load all pages from index onward
                for (int i = index; i < _totalElementCount; i++)
                {
                    GetOrLoadPage(i / _pageSize);
                }

                // Shift elements right
                for (int i = _totalElementCount - 1; i >= index; i--)
                {
                    var srcPageIdx = i / _pageSize;
                    var srcLocalIdx = i % _pageSize;
                    var dstPageIdx = (i + 1) / _pageSize;
                    var dstLocalIdx = (i + 1) % _pageSize;

                    var srcPage = _loadedPages[srcPageIdx];
                    var dstPage = GetOrLoadPage(dstPageIdx);

                    dstPage.Elements[dstLocalIdx] = srcPage.Elements[srcLocalIdx];
                    dstPage.IsDirty = true;
                }

                // Insert new element
                var insertPage = GetOrLoadPage(index / _pageSize);
                insertPage.Elements[index % _pageSize] = item;
                insertPage.IsDirty = true;

                // Update ID array
                Array.Resize(ref _allIds, _totalElementCount + 1);
                Array.Copy(_allIds, index, _allIds, index + 1, _totalElementCount - index);
                _allIds[index] = index.ToString();

                _totalElementCount++;
                _hasPendingWrites = true;

                OnItemSet(item, index);
                OnCollectionChange(CollectionChangeInfo<ObservableLazyListSavable<T>, T?>.Add(this, item, index));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
```

**Step 2: Replace RemoveAt implementation**

```csharp
        public override void RemoveAt(int index)
        {
            if (index < 0 || index >= _totalElementCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            _rwLock.EnterWriteLock();
            try
            {
                // Force load all pages from index onward
                for (int i = index; i < _totalElementCount; i++)
                {
                    GetOrLoadPage(i / _pageSize);
                }

                var oldItem = _loadedPages[index / _pageSize].Elements[index % _pageSize];
                TryUnWatch(oldItem);

                // Shift elements left
                for (int i = index; i < _totalElementCount - 1; i++)
                {
                    var srcPageIdx = (i + 1) / _pageSize;
                    var srcLocalIdx = (i + 1) % _pageSize;
                    var dstPageIdx = i / _pageSize;
                    var dstLocalIdx = i % _pageSize;

                    var srcPage = _loadedPages[srcPageIdx];
                    var dstPage = _loadedPages[dstPageIdx];

                    dstPage.Elements[dstLocalIdx] = srcPage.Elements[srcLocalIdx];
                    dstPage.IsDirty = true;
                }

                // Clear last position
                var lastPage = _loadedPages[(_totalElementCount - 1) / _pageSize];
                lastPage.Elements[(_totalElementCount - 1) % _pageSize] = default;
                lastPage.IsDirty = true;

                // Update ID array
                Array.Copy(_allIds, index + 1, _allIds, index, _totalElementCount - index - 1);
                Array.Resize(ref _allIds, _totalElementCount - 1);

                _totalElementCount--;
                _hasPendingWrites = true;

                OnCollectionChange(CollectionChangeInfo<ObservableLazyListSavable<T>, T?>.Remove(this, oldItem, index));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
```

**Step 3: Replace Remove implementation**

```csharp
        public override bool Remove(T? item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }
```

**Step 4: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: implement Insert and RemoveAt for ObservableLazyListSavable"
```

---

### Task 13: Implement Contains, IndexOf, GetEnumerator

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

**Step 1: Replace implementations**

```csharp
        public override bool Contains(T? item)
        {
            return IndexOf(item) >= 0;
        }

        public override int IndexOf(T? item)
        {
            _rwLock.EnterReadLock();
            try
            {
                // Check loaded pages first
                foreach (var (pageIndex, page) in _loadedPages)
                {
                    int startIdx = pageIndex * _pageSize;
                    int count = Math.Min(_pageSize, _totalElementCount - startIdx);

                    for (int i = 0; i < count; i++)
                    {
                        if (EqualityComparer<T?>.Default.Equals(page.Elements[i], item))
                            return startIdx + i;
                    }
                }

                // Check unloaded elements by loading pages
                for (int i = 0; i < _totalElementCount; i++)
                {
                    var pageIndex = i / _pageSize;
                    if (!_loadedPages.ContainsKey(pageIndex))
                    {
                        var page = GetOrLoadPage(pageIndex);
                        var localIndex = i % _pageSize;
                        if (EqualityComparer<T?>.Default.Equals(page.Elements[localIndex], item))
                            return i;
                    }
                }

                return -1;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            for (int i = 0; i < _totalElementCount; i++)
            {
                yield return this[i];
            }
        }
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: implement Contains, IndexOf, GetEnumerator for ObservableLazyListSavable"
```

---

### Task 14: Implement SwapSource

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

**Step 1: Replace SwapSource implementation**

```csharp
        public override void SwapSource(List<T?>? list)
        {
            _rwLock.EnterWriteLock();
            try
            {
                // Clear existing
                foreach (var page in _loadedPages.Values)
                {
                    foreach (var elem in page.Elements)
                    {
                        TryUnWatch(elem);
                    }
                }
                _loadedPages.Clear();
                _lruList.Clear();

                if (list == null)
                {
                    _allIds = Array.Empty<string>();
                    _totalElementCount = 0;
                    _hasPendingWrites = false;
                }
                else
                {
                    _totalElementCount = list.Count;
                    _allIds = new string[_totalElementCount];

                    for (int i = 0; i < _totalElementCount; i++)
                    {
                        _allIds[i] = i.ToString();
                        var item = list[i];
                        TryWatch(item);
                    }

                    // Load first page immediately
                    var firstPage = new PageData(_pageSize)
                    {
                        IsDirty = true
                    };
                    int copyCount = Math.Min(_pageSize, _totalElementCount);
                    for (int i = 0; i < copyCount; i++)
                    {
                        firstPage.Elements[i] = list[i];
                    }
                    _loadedPages[0] = firstPage;
                    _lruList.AddFirst(0);

                    _hasPendingWrites = true;
                }

                _isDirty = true;
                OnCollectionChange(CollectionChangeInfo<ObservableLazyListSavable<T>, T?>.Reset(this));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: implement SwapSource for ObservableLazyListSavable"
```

---

### Task 15: Implement Serialize and Deserialize

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

**Step 1: Replace Serialize implementation**

```csharp
        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            _rwLock.EnterWriteLock();
            try
            {
                // Flush all dirty pages
                foreach (var (pageIndex, page) in _loadedPages)
                {
                    if (page.IsDirty)
                        FlushPage(pageIndex, page);
                }

                // Save metadata
                var metadata = new CollectionMetadata
                {
                    Ids = _allIds,
                    Count = _totalElementCount,
                    PageSize = _pageSize,
                    MaxCachedPages = _maxCachedPages,
                    CacheStrategy = _cacheStrategy,
                    IsLazyLoaded = true
                };
                serializer.Save(metadata, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection);

                _hasPendingWrites = false;
                _isDirty = false;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
```

**Step 2: Replace Deserialize implementation**

```csharp
        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            _rwLock.EnterWriteLock();
            try
            {
                CollectionMetadata metadata;
                using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection))
                {
                    metadata = serializer.ReadNoPushPath<CollectionMetadata>(ctx);
                }

                _allIds = metadata.Ids ?? Array.Empty<string>();
                _totalElementCount = metadata.Count;

                // Store serializer/context for lazy loading
                _serializer = serializer;
                _context = ctx;

                // Don't load any pages yet - that's the lazy part
                _loadedPages.Clear();
                _lruList.Clear();
                _hasPendingWrites = false;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
```

**Step 3: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: implement Serialize and Deserialize for ObservableLazyListSavable"
```

---

## Phase 5: Serializer Factory Method

### Task 16: Add LoadCollectionSavable factory method

**Files:**
- Modify: `Salvavida/Package/Runtime/Serializer.cs` (or appropriate location)

**Step 1: Find Serializer class and add factory method**

First, locate the Serializer class and add:

```csharp
        /// <summary>
        /// Loads a collection, deciding between lazy and full load based on metadata.
        /// </summary>
        public ObservableListSavableBase<T> LoadCollectionSavable<T>(
            SerializeContext ctx,
            string propertyName,
            bool saveSeparately,
            ref List<T?>? sourceList,
            LazyLoadConfig? config = null)
            where T : ISavable
        {
            CollectionMetadata? metadata = null;
            using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection))
            {
                if (HasNoPushPath(ctx))
                    metadata = ReadNoPushPath<CollectionMetadata?>(ctx);
            }

            if (metadata == null)
            {
                sourceList = new List<T?>();
                return new ObservableListSavable<T>(propertyName, sourceList, saveSeparately);
            }

            int threshold = config?.Threshold ?? GlobalConfig.DefaultLazyLoadThreshold;

            if (metadata.Count > threshold && metadata.IsLazyLoaded)
            {
                int pageSize = config?.PageSize ?? metadata.PageSize > 0 ? metadata.PageSize : GlobalConfig.DefaultPageSize;
                int maxCached = config?.MaxCachedPages ?? metadata.MaxCachedPages > 0 ? metadata.MaxCachedPages : GlobalConfig.DefaultMaxCachedPages;
                var cacheStrategy = config?.CacheStrategy ?? metadata.CacheStrategy;

                sourceList = null;
                var lazyCol = new ObservableLazyListSavable<T>(propertyName, saveSeparately,
                    pageSize, maxCached, cacheStrategy);
                lazyCol.Deserialize(this, ctx);
                return lazyCol;
            }
            else
            {
                var ids = metadata.Ids ?? Array.Empty<string>();
                sourceList = new List<T?>(ids.Length);
                foreach (var id in ids)
                {
                    sourceList.Add(Read<T?>(ctx, id, PathBuilder.Type.Collection));
                }
                var fullCol = new ObservableListSavable<T>(propertyName, sourceList, saveSeparately);
                fullCol.Deserialize(this, ctx);
                return fullCol;
            }
        }
```

**Step 2: Build to verify**

```bash
dotnet build Salvavida/Salvavida.csproj --verbosity quiet
```

**Step 3: Commit**

```bash
git add Salvavida/Package/Runtime/Serializer.cs
git commit -m "feat: add LoadCollectionSavable factory method"
```

---

## Phase 6: ObservableArray Series

### Task 17: Create ObservableArraySavableBase

**Files:**
- Create: `Salvavida/Package/Runtime/ObservableArraySavableBase.cs`

**Step 1: Create abstract base class**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public abstract class ObservableArraySavableBase<T> : ObservableCollectionSavable<ObservableArraySavableBase<T>, T>, IList<T?>, IReadOnlyList<T?>, IList
        where T : ISavable
    {
        protected ObservableArraySavableBase(string propName, bool saveSeparately)
            : base(propName, saveSeparately)
        {
        }

        #region Abstract Methods

        public abstract T? this[int index] { get; set; }
        public abstract int Count { get; }
        public abstract bool Contains(T? item);
        public abstract int IndexOf(T? item);
        public abstract void SwapSource(T?[]? array);


        #endregion

        #region Not Supported (Fixed Size)

        void ICollection<T?>.Add(T? item) => throw new NotSupportedException();
        int IList.Add(object? value) => throw new NotSupportedException();
        void ICollection<T?>.Clear() => throw new NotSupportedException();
        void IList.Clear() => throw new NotSupportedException();
        void IList<T?>.Insert(int index, T? item) => throw new NotSupportedException();
        void IList.Insert(int index, object? value) => throw new NotSupportedException();
        bool ICollection<T?>.Remove(T? item) => throw new NotSupportedException();
        void IList.Remove(object? value) => throw new NotSupportedException();
        void IList<T?>.RemoveAt(int index) => throw new NotSupportedException();
        void IList.RemoveAt(int index) => throw new NotSupportedException();

        #endregion

        #region IList Explicit Implementation

        bool ICollection<T?>.IsReadOnly => false;
        bool IList.IsReadOnly => false;
        bool IList.IsFixedSize => true;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (T?)value;
        }

        bool IList.Contains(object? value) => Contains((T?)value);
        int IList.IndexOf(object? value) => IndexOf((T?)value);

        public void CopyTo(T?[] array, int arrayIndex)
        {
            for (int i = 0; i < Count; i++)
                array[arrayIndex + i] = this[i];
        }

        void ICollection.CopyTo(Array array, int index)
        {
            for (int i = 0; i < Count; i++)
                array.SetValue(this[i], index + i);
        }

        public abstract IEnumerator<T?> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableArraySavableBase.cs
git commit -m "feat: add ObservableArraySavableBase abstract base class"
```

---

### Task 18: Update ObservableArraySavable to extend base

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableArray.cs`

**Step 1: Change base class and add override**

Similar to Task 7, update `ObservableArraySavable<T>`:
1. Change base class to `ObservableArraySavableBase<T>`
2. Add `override` to methods
3. Keep implementation unchanged

**Step 2: Build and verify**

```bash
dotnet build Salvavida/Salvavida.csproj --verbosity quiet
```

**Step 3: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableArray.cs
git commit -m "refactor: ObservableArraySavable extends ObservableArraySavableBase"
```

---

### Task 19: Create ObservableLazyArraySavable

**Files:**
- Create: `Salvavida/Package/Runtime/ObservableLazyArraySavable.cs`

**Step 1: Create lazy array implementation**

This is similar to `ObservableLazyListSavable` but:
- No `Add`, `Insert`, `Remove`, `RemoveAt` methods
- `SwapSource` takes `T?[]?` instead of `List<T?>?`
- Fixed size operations

Copy the structure from `ObservableLazyListSavable` and adapt:
- Remove modify operations that change size
- Implement only indexer, Contains, IndexOf, GetEnumerator
- Implement fixed-size `SwapSource`

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyArraySavable.cs
git commit -m "feat: add ObservableLazyArraySavable for lazy-loaded arrays"
```

---

### Task 20: Add LoadArraySavable factory method

**Files:**
- Modify: `Salvavida/Package/Runtime/Serializer.cs`

**Step 1: Add array factory method**

```csharp
        public ObservableArraySavableBase<T> LoadArraySavable<T>(
            SerializeContext ctx,
            string propertyName,
            bool saveSeparately,
            ref T?[]? sourceArray,
            LazyLoadConfig? config = null)
            where T : ISavable
        {
            // Similar to LoadCollectionSavable but for arrays
            // ...
        }
```

**Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/Serializer.cs
git commit -m "feat: add LoadArraySavable factory method"
```

---

## Phase 7: SourceGenerator Updates

### Task 21: Update BasicCodeGenerator to use base types

**Files:**
- Modify: `Salvavida.Generator/BasicCodeGenerator.cs`

**Step 1: Update GetCollectionTypeString method**

Modify to return base class types when `isSavable` is true:

```csharp
        protected string GetCollectionTypeString(CollectionType colType, ImmutableArray<ITypeSymbol> typeSymbols)
        {
            var format = SymbolDisplayFormat.FullyQualifiedFormat;
            var typeIndex = colType switch
            {
                CollectionType.Array or CollectionType.List => 0,
                CollectionType.Dictionary => 1,
                _ => throw new NotSupportedException(),
            };
            var isSavable = IsTypeISavable(typeSymbols[typeIndex]);

            return colType switch
            {
                CollectionType.Array => isSavable
                    ? $"ObservableArraySavableBase<{typeSymbols[0].ToDisplayString(format)}>"
                    : $"ObservableArray<{typeSymbols[0].ToDisplayString(format)}>",
                CollectionType.List => isSavable
                    ? $"ObservableListSavableBase<{typeSymbols[0].ToDisplayString(format)}>"
                    : $"ObservableList<{typeSymbols[0].ToDisplayString(format)}>",
                CollectionType.Dictionary => $"ObservableDictionarySavable<{typeSymbols[0].ToDisplayString(format)}, {typeSymbols[1].ToDisplayString(format)}>",
                _ => throw new NotSupportedException()
            };
        }
```

**Step 2: Commit**

```bash
git add Salvavida.Generator/BasicCodeGenerator.cs
git commit -m "feat: generator uses base class types for savable collections"
```

---

### Task 22: Add LazyLoad attribute handling to generator

**Files:**
- Modify: `Salvavida.Generator/BasicCodeGenerator.cs`

**Step 1: Add attribute detection in HandleFieldOrProp**

Check for `[LazyLoad]` attribute and generate static config:

```csharp
        // In HandleFieldOrProp, after detecting saveSeparatelyAttr
        AttributeData? lazyLoadAttr = null;
        foreach (var attrData in attrs)
        {
            var attrSymbol = attrData.AttributeClass!;
            var attrName = attrSymbol.ToDisplayString();
            if (attrName == "Salvavida.LazyLoadAttribute")
                lazyLoadAttr = attrData;
        }
```

**Step 2: Generate static config field**

When `lazyLoadAttr != null` and collection is savable:

```csharp
        // Generate static config
        sb.WriteLine($"private static readonly LazyLoadConfig s_{fieldName}Config = new LazyLoadConfig");
        sb.WriteLine("{");
        sb.WriteLine($"    Threshold = {threshold},");
        sb.WriteLine($"    PageSize = {pageSize},");
        sb.WriteLine($"    MaxCachedPages = {maxCachedPages},");
        sb.WriteLine($"    CacheStrategy = CacheStrategy.{cacheStrategy}");
        sb.WriteLine("};");
```

**Step 3: Pass config to TryInit/Deserialize**

Update generated code to pass config to factory methods.

**Step 4: Commit**

```bash
git add Salvavida.Generator/BasicCodeGenerator.cs
git commit -m "feat: generator handles LazyLoad attribute"
```

---

## Phase 8: Testing and Finalization

### Task 23: Build all projects

**Step 1: Build main project**

```bash
dotnet build Salvavida/Salvavida.csproj
```

Expected: Build successful

**Step 2: Build generator project**

```bash
dotnet build Salvavida.Generator/Salvavida.Generator.csproj
```

Expected: Build successful

**Step 3: Commit if any fixes needed**

```bash
git add -A
git commit -m "fix: resolve build issues"
```

---

### Task 24: Update design document with final notes

**Files:**
- Modify: `docs/superpowers/plans/2026-04-03-lazy-loading-design-refactored.md`

**Step 1: Add implementation notes**

Document any deviations from original design, lessons learned.

**Step 2: Commit**

```bash
git add docs/superpowers/plans/2026-04-03-lazy-loading-design-refactored.md
git commit -m "docs: update lazy loading design with implementation notes"
```

---

### Task 25: Final commit and summary

**Step 1: Review all changes**

```bash
git log --oneline feature/lazy-loading-v2 ^v0.3-dev
```

**Step 2: Push branch**

```bash
git push origin feature/lazy-loading-v2
```

---

## Summary

This implementation plan covers:

- **Phase 1**: Core data structures (CacheStrategy, LazyLoadConfig, CollectionMetadata, LazyLoadAttribute, GlobalConfig)
- **Phase 2**: ObservableListSavableBase abstract base class
- **Phase 3**: Refactor ObservableListSavable to extend base
- **Phase 4**: ObservableLazyListSavable implementation (13 tasks)
- **Phase 5**: Serializer factory method
- **Phase 6**: ObservableArray series (base class, refactor, lazy implementation)
- **Phase 7**: SourceGenerator updates
- **Phase 8**: Testing and finalization

Total: 25 bite-sized tasks, each 2-5 minutes of focused work.
