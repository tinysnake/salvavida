using Salvavida.DefaultImpl;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable array for ISavable elements.
    /// Elements are loaded on-demand by page, with optional LRU cache eviction.
    /// Fixed size - no Add/Insert/Remove/Clear operations.
    /// </summary>
    public sealed class ObservableLazyArraySavable<T> : ObservableArraySavableBase<ObservableLazyArraySavable<T>, T>
        where T : ISavable
    {
        #region Configuration

        private readonly int _pageSize;
        private readonly int _maxCachedPages;
        private readonly CacheStrategy _cacheStrategy;

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

        public ObservableLazyArraySavable(
            string propName,
            bool saveSeparately,
            int pageSize,
            int maxCachedPages,
            CacheStrategy cacheStrategy)
            : base(propName, saveSeparately)
        {
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

        #region Page Loading

        private PageData GetOrLoadPage(int pageIndex)
        {
            if (_loadedPages.TryGetValue(pageIndex, out var page))
                return page;

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
                if (page.IsDirty)
                    FlushPage(pageIndex, page);

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

        #region Indexer

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

                    OnCollectionChange(CollectionChangeInfo<ObservableLazyArraySavable<T>, T?>.Replace(this, oldValue, value, index));
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        #endregion

        #region Contains / IndexOf / GetEnumerator

        public override bool Contains(T? item)
        {
            return IndexOf(item) >= 0;
        }

        public override int IndexOf(T? item)
        {
            _rwLock.EnterReadLock();
            try
            {
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

        #endregion

        #region SwapSource

        public override void SwapSource(T?[]? array)
        {
            _rwLock.EnterWriteLock();
            try
            {
                foreach (var page in _loadedPages.Values)
                {
                    foreach (var elem in page.Elements)
                    {
                        TryUnWatch(elem);
                    }
                }
                _loadedPages.Clear();
                _lruList.Clear();

                if (array == null)
                {
                    _allIds = Array.Empty<string>();
                    _totalElementCount = 0;
                    _hasPendingWrites = false;
                }
                else
                {
                    _totalElementCount = array.Length;
                    _allIds = new string[_totalElementCount];

                    for (int i = 0; i < _totalElementCount; i++)
                    {
                        _allIds[i] = i.ToString();
                        var item = array[i];
                        TryWatch(item);
                    }

                    var firstPage = new PageData(_pageSize)
                    {
                        IsDirty = true
                    };
                    int copyCount = Math.Min(_pageSize, _totalElementCount);
                    for (int i = 0; i < copyCount; i++)
                    {
                        firstPage.Elements[i] = array[i];
                    }
                    _loadedPages[0] = firstPage;
                    _lruList.AddFirst(0);

                    _hasPendingWrites = true;
                }

                _isDirty = true;
                OnCollectionChange(CollectionChangeInfo<ObservableLazyArraySavable<T>, T?>.Reset(this));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        #endregion

        #region Serialize / Deserialize

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            _rwLock.EnterWriteLock();
            try
            {
                foreach (var (pageIndex, page) in _loadedPages)
                {
                    if (page.IsDirty)
                        FlushPage(pageIndex, page);
                }

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

                _serializer = serializer;
                _context = ctx;

                _loadedPages.Clear();
                _lruList.Clear();
                _hasPendingWrites = false;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        #endregion

        #region Child Changed

        protected override void OnChildChanged(T obj, string _)
        {
            _isChildrenDirty = true;
            var index = IndexOf(obj);
            if (index >= 0)
                OnCollectionChange(CollectionChangeInfo<ObservableLazyArraySavable<T>, T?>.Replace(this, obj, obj, index));
        }

        #endregion
    }
}
