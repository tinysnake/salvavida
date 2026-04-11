using System;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable array for ISavable elements.
    /// Elements are loaded on-demand by page, with optional LRU cache eviction.
    /// Fixed size - no Add/Insert/Remove/Clear operations.
    /// </summary>
    public sealed class ObservableArraySavableLazy<T> : ObservableArraySavableBase<ObservableArraySavableLazy<T>, T>
        where T : ISavable
    {
        #region Configuration

        private readonly int _pageSize;
        private readonly int _maxCachedPages;
        private readonly CacheStrategy _cacheStrategy;

        #endregion

        #region Page Storage

        private int _totalElementCount;

        private readonly Dictionary<int, PageData> _loadedPages = new();

        #endregion

        #region LRU Tracking

        private readonly LinkedList<int> _lruList = new();

        #endregion

        #region State Tracking

        private bool _hasPendingWrites;
        private readonly HashSet<int> _idxDeleted = new();

        #endregion

        #region Concurrency

        private readonly ReaderWriterLockSlim _rwLock = new();

        #endregion

        private readonly ISvIdConverter<int> _idConverter = SvIdConverter.GetConverter<int>() ?? throw new NullReferenceException("cannot find idconverter for int.");

        #region Constructor

        public ObservableArraySavableLazy(
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
                    foreach (var item in page.Elements)
                    {
                        if(item == null)
                            continue;
                        if (item.IsDirty) return true;
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
            while (startIdx >= _totalElementCount)
            {
                _totalElementCount += _pageSize;
            }
            int endIdx = Math.Min(startIdx + _pageSize, _totalElementCount);

            var serializer = this.GetSerializer() ?? throw new InvalidOperationException("Serializer is not available.");
            var prefix = GetPaddedIndex(startIdx, _totalElementCount);
            using var scope = serializer.BeginFreshAction(this, out var ctx);
             
            var ids = serializer.ListCollectionIdsPrefix(ctx, prefix, 0, _pageSize);
            var i = 0;
            foreach(var id in ids)
            {
                if(i >= endIdx)
                    break;
                var item = serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection);
                page.Elements[_idConverter.ConvertFrom(id)] = item;
                if (item != null)
                {
                    item.SvId = id;
                    OnChildDeserialized(item);
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

            var serializer = this.GetSerializer() ?? throw new InvalidOperationException("Serializer is not available.");
            using var scope = serializer.BeginFreshAction(out var ctx);
            for (int i = startIdx; i < endIdx; i++)
            {
                var elem = page.Elements[i - startIdx];

                if(elem == null)
                    continue;
                var id = string.IsNullOrEmpty(elem.SvId) ? GetPaddedIndex(i, _totalElementCount) : elem.SvId;
                {
                    using var _ = ctx.Path.UsePush(id, PathBuilder.Type.Collection);
                    elem.Serialize(serializer, ctx);
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

                    if(oldValue!=null)
                        _idxDeleted.Add(index);

                    var svId = index.ToString();
                    if (value != null)
                        value.SvId = svId;

                    TryUnWatch(oldValue);
                    page.Elements[localIndex] = value;
                    page.IsDirty = true;
                    _hasPendingWrites = true;
                    TryWatch(value);

                    OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Replace(this, oldValue, value, index));
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
            throw new NotSupportedException();
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
            DangerouslyDeleteOldElements();
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
                    _totalElementCount = 0;
                    _hasPendingWrites = false;
                }
                else
                {
                    _totalElementCount = array.Length;

                    for (int i = 0; i < _totalElementCount; i++)
                    {
                        var item = array[i];
                        if (item != null)
                        {
                            item.SvId = i.ToString();
                            item.SetDirty(true, true);
                        }
                        TryWatch(item);
                        var page = GetOrLoadPage(i / _pageSize);
                        page.Elements[i % _pageSize] = item;
                        page.IsDirty = true;
                    }

                    _hasPendingWrites = true;
                }

                _isDirty = true;
                OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Reset(this));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        #endregion

        #region Serialize / Deserialize

        private void DangerouslyDeleteOldElements()
        {
            // This method will only take effect if the collection is parented to a ISavable object that has a working serializer.
            // Hope this method will not accidentally delete the whole collection, otherwise this method is so so dumb.

            var serializer = this.GetSerializer();
            if (serializer == null)
                return;

            serializer.FreshDeleteAll(this);
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately && !_hasPendingWrites)
                return;

            _rwLock.EnterWriteLock();
            try
            {
                foreach (var (pageIndex, page) in _loadedPages)
                {
                    if (page.IsDirty)
                        FlushPage(pageIndex, page);
                }

                var metadata = serializer.Read<CollectionMetadata>(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
                metadata.Count = _totalElementCount;
                serializer.Save(metadata, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);

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
                _loadedPages.Clear();
                _lruList.Clear();
                _hasPendingWrites = false;
                var metadata = serializer.Read<CollectionMetadata>(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
                _totalElementCount = metadata.Count;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        #endregion
    }
}
