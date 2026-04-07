using Salvavida.DefaultImpl;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    /// <summary>
    /// Lazy-loaded observable list for ISavable elements.
    /// Elements are loaded on-demand by page, with optional LRU cache eviction.
    /// </summary>
    public sealed class ObservableLazyListSavable<T> : ObservableListSavableBase<ObservableLazyListSavable<T>, T>
        where T : ISavable
    {
        #region Configuration

        private const int REBALANCE_LENGTH_THRESHOLD = 10;

        private readonly int _pageSize;
        private readonly int _maxCachedPages;
        private readonly CacheStrategy _cacheStrategy;

        #endregion

        #region Page Storage

        private readonly Dictionary<int, PageData> _loadedPages = new();
        private int _totalElementCount;
        private string[] _allIds = Array.Empty<string>();
        private int[] _bucketCumulativeIndex = Array.Empty<int>();
        private BucketMeta[] _bucketMetas = Array.Empty<BucketMeta>();

        #endregion

        #region LRU Tracking

        private readonly LinkedList<int> _lruList = new();

        #endregion

        #region State Tracking

        private bool _hasPendingWrites;
        private HashSet<string> _idsDeleted = new();
        private Serializer? _serializer;
        private SerializeContext? _context;
        private bool _needsRebalance;
        private bool _needsBucketSplit;
        private bool _needsBucketMerge;
        private int _bucketMultiplier = 3;

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
            _pageSize = pageSize > 0 ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));
            _maxCachedPages = maxCachedPages > 0 ? maxCachedPages : throw new ArgumentOutOfRangeException(nameof(maxCachedPages));
            _cacheStrategy = cacheStrategy;
        }

        #endregion

        #region Nested Types

        private class PageData
        {
            public List<T?> Elements;
            public bool IsDirty;

            public PageData(int pageSize)
            {
                Elements = new List<T?>(pageSize);
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
            var ids = GetPageIds(pageIndex);

            for (int i = 0; i < ids.Length; i++)
            {
                page.Elements.Add(_serializer!.Read<T?>(_context!, ids[i], PathBuilder.Type.Collection));
                var elem = page.Elements[i];
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

            for (int i = 0; i < page.Elements.Count; i++)
            {
                var elem = page.Elements[i];
                if (elem == null)
                {
                    _serializer!.Delete(_context!, elem.SvId ?? _allIds[startIdx + i], PathBuilder.Type.Collection);
                }
                else
                {
                    using (_context!.Path.UsePush(elem.SvId!, PathBuilder.Type.Collection))
                    {
                        elem.Serialize(_serializer!, _context!);
                    }
                }
            }

            page.IsDirty = false;
        }

        private (string? bucketId, int skipCount) FindBucketForIndex(int elementIndex)
        {
            if (_bucketCumulativeIndex.Length == 0)
            {
                // No buckets yet — all elements in default bucket "A"
                return ("0", elementIndex);
            }

            // Binary search for the bucket
            int bucketIdx = Array.BinarySearch(_bucketCumulativeIndex, elementIndex + 1);
            if (bucketIdx < 0) bucketIdx = ~bucketIdx; // first bucket with cumulative > elementIndex

            int countBefore = bucketIdx > 0 ? _bucketCumulativeIndex[bucketIdx - 1] : 0;
            int skipCount = elementIndex - countBefore;

            return (_bucketMetas[bucketIdx].BucketId, skipCount);
        }

        private string[] GetPageIds(int pageIndex)
        {
            var (bucketId, skipCount) = FindBucketForIndex(pageIndex * _pageSize);
            return _serializer!.ListCollectionIds(_context!, _svid!, bucketId, skipCount, _pageSize);
        }

        private int GetPrecisionDigits()
        {
            return LexoRank.CalculatePrecisionDigits(_pageSize * _bucketMultiplier);
        }

        private string? GetElementRank(int index)
        {
            if (index < 0 || index >= _totalElementCount) return null;
            var page = GetOrLoadPage(index / _pageSize);
            var localIdx = index % _pageSize;
            return localIdx < page.Elements.Count ? page.Elements[localIdx]?.SvId : null;
        }

        private void UpdateBucketCountForIndex(int elementIndex, int delta)
        {
            if (_bucketCumulativeIndex.Length == 0)
            {
                // No buckets yet — elements are in default bucket
                return;
            }

            // Binary search for the bucket
            int bucketIdx = Array.BinarySearch(_bucketCumulativeIndex, elementIndex + 1);
            if (bucketIdx < 0) bucketIdx = ~bucketIdx;

            _bucketMetas[bucketIdx].Count += delta;
            _bucketCumulativeIndex[bucketIdx] += delta;

            // Check if bucket split is needed
            int bucketSize = _bucketMultiplier * _pageSize;
            if (_bucketMetas[bucketIdx].Count > bucketSize)
                _needsBucketSplit = true;
        }

        private int GetPageCount()
        {
            return (_totalElementCount + _pageSize - 1) / _pageSize;
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

                    OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Replace(this, oldValue, value, index));
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        #endregion

        #region Add / Clear

        public override void Add(T? item)
        {
            _rwLock.EnterWriteLock();
            try
            {
                var pageIndex = _totalElementCount / _pageSize;
                var page = GetOrLoadPage(pageIndex);

                string? prevId = GetElementRank(_totalElementCount - 1);
                int precision = GetPrecisionDigits();
                item.SvId = LexoRank.Generate(prevId, null, precision);

                page.Elements.Add(item);
                page.IsDirty = true;
                _hasPendingWrites = true;

                UpdateBucketCountForIndex(_totalElementCount, 1);
                _totalElementCount++;

                OnItemSet(item, _totalElementCount - 1);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, _totalElementCount - 1));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public override void Clear()
        {
            _rwLock.EnterWriteLock();
            try
            {
                if (_serializer != null && _context != null)
                {
                    foreach (var id in _allIds)
                    {
                        _serializer.Delete(_context, id, PathBuilder.Type.Collection);
                    }
                }

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

                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        private void OnItemSet(T? item, int index)
        {
            if (item is ISavable sv)
                sv.SvId ??= DefaultIdGenerator.Default.GetId();
            TryWatch(item);
        }

        #endregion

        #region Insert / Remove

        public override void Insert(int index, T? item)
        {
            if (index < 0 || index > _totalElementCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            _rwLock.EnterWriteLock();
            try
            {
                string? prevId = GetElementRank(index - 1);
                string? nextId = GetElementRank(index);
                int precision = GetPrecisionDigits();
                item.SvId = LexoRank.Generate(prevId, nextId, precision);

                int targetPageIdx = index / _pageSize;
                var page = GetOrLoadPage(targetPageIdx);

                int localIdx = index % _pageSize;
                page.Elements.Insert(localIdx, item);

                while (page.Elements.Count > _pageSize)
                {
                    var nextPage = GetOrLoadPage(targetPageIdx + 1);
                    var moved = page.Elements[page.Elements.Count - 1];
                    page.Elements.RemoveAt(page.Elements.Count - 1);
                    nextPage.Elements.Insert(0, moved);
                    nextPage.IsDirty = true;
                    page.IsDirty = true;
                }

                UpdateBucketCountForIndex(index, 1);

                _totalElementCount++;
                _hasPendingWrites = true;

                var rankParts = item.SvId.Split('~');
                if (rankParts.Length == 3 && rankParts[2].Length > REBALANCE_LENGTH_THRESHOLD)
                    _needsRebalance = true;

                OnItemSet(item, index);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public override void RemoveAt(int index)
        {
            if (index < 0 || index >= _totalElementCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            _rwLock.EnterWriteLock();
            try
            {
                int targetPageIdx = index / _pageSize;
                var page = GetOrLoadPage(targetPageIdx);
                int localIdx = index % _pageSize;

                var oldItem = page.Elements[localIdx];
                var itemRank = oldItem?.SvId;
                if (!string.IsNullOrEmpty(itemRank))
                    _idsDeleted.Add(itemRank!);

                page.Elements.RemoveAt(localIdx);
                page.IsDirty = true;

                if (page.Elements.Count < _pageSize / 2 && targetPageIdx + 1 < GetPageCount())
                {
                    var nextPage = GetOrLoadPage(targetPageIdx + 1);
                    if (nextPage.Elements.Count > 0)
                    {
                        page.Elements.Add(nextPage.Elements[0]);
                        nextPage.Elements.RemoveAt(0);
                        nextPage.IsDirty = true;
                        page.IsDirty = true;
                    }
                }

                UpdateBucketCountForIndex(index, -1);
                _totalElementCount--;
                _hasPendingWrites = true;

                TryUnWatch(oldItem);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, oldItem, index));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public override bool Remove(T? item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
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

        public override void SwapSource(List<T?>? list)
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
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        #endregion

        #region Serialize / Deserialize

        private string GetNextBucketPrefix()
        {
            if (_bucketMetas.Length == 0)
                return "B";

            string lastPrefix = _bucketMetas[_bucketMetas.Length - 1].BucketId;
            return IncrementBase62(lastPrefix);
        }

        private string IncrementBase62(string value)
        {
            throw new System.NotImplementedException();
        }

        private int[] RecomputeCumulativeIndex()
        {
            var index = new int[_bucketMetas.Length];
            int cumulative = 0;
            for (int i = 0; i < _bucketMetas.Length; i++)
            {
                cumulative += _bucketMetas[i].Count;
                index[i] = cumulative;
            }
            return index;
        }

        private void PerformBucketSplit(Serializer serializer, SerializeContext ctx)
        {
            for (int i = 0; i < _bucketMetas.Length; i++)
            {
                int bucketSize = _bucketMultiplier * _pageSize;
                if (_bucketMetas[i].Count > bucketSize)
                {
                    int startIdx = i > 0 ? _bucketCumulativeIndex[i - 1] : 0;
                    int endIdx = _bucketCumulativeIndex[i];
                    int midIdx = startIdx + (endIdx - startIdx) / 2;
                    string newPrefix = GetNextBucketPrefix();

                    var firstHalfRanks = FakeLexoRank.Rebalance(midIdx - startIdx);
                    for (int j = startIdx; j < midIdx; j++)
                    {
                        var page = GetOrLoadPage(j / _pageSize);
                        var elem = page.Elements[j % _pageSize];
                        if (elem != null)
                        {
                            elem.SvId = _bucketMetas[i].BucketId + "~" + firstHalfRanks[j - startIdx];
                            serializer.Save(elem, ctx, PathBuilder.Type.Collection);
                        }
                    }

                    var secondHalfRanks = FakeLexoRank.Rebalance(endIdx - midIdx);
                    for (int j = midIdx; j < endIdx; j++)
                    {
                        var page = GetOrLoadPage(j / _pageSize);
                        var elem = page.Elements[j % _pageSize];
                        if (elem != null)
                        {
                            elem.SvId = newPrefix + "~" + secondHalfRanks[j - midIdx];
                            serializer.Save(elem, ctx, PathBuilder.Type.Collection);
                        }
                    }

                    var newMetas = new BucketMeta[_bucketMetas.Length + 1];
                    int ni = 0;
                    for (int mi = 0; mi < _bucketMetas.Length; mi++)
                    {
                        if (mi == i)
                        {
                            newMetas[ni++] = new BucketMeta { BucketId = _bucketMetas[mi].BucketId, Count = midIdx - startIdx };
                            newMetas[ni++] = new BucketMeta { BucketId = newPrefix, Count = endIdx - midIdx };
                        }
                        else
                        {
                            newMetas[ni++] = _bucketMetas[mi];
                        }
                    }
                    _bucketMetas = newMetas;
                    _bucketCumulativeIndex = RecomputeCumulativeIndex();
                    break;
                }
            }
            _needsBucketSplit = false;
        }

        private void PerformBucketMerge(Serializer serializer, SerializeContext ctx)
        {
            for (int i = 0; i < _bucketMetas.Length - 1; i++)
            {
                int bucketSize = _bucketMultiplier * _pageSize;
                int threshold = bucketSize / 4;
                if (_bucketMetas[i].Count < threshold || _bucketMetas[i + 1].Count < threshold)
                {
                    int startIdx = i > 0 ? _bucketCumulativeIndex[i - 1] : 0;
                    int endIdx = _bucketCumulativeIndex[i + 1];
                    string mergePrefix = _bucketMetas[i].BucketId;

                    var newRanks = FakeLexoRank.Rebalance(endIdx - startIdx);
                    for (int j = startIdx; j < endIdx; j++)
                    {
                        var page = GetOrLoadPage(j / _pageSize);
                        var elem = page.Elements[j % _pageSize];
                        if (elem != null)
                        {
                            elem.SvId = mergePrefix + "~" + newRanks[j - startIdx];
                            serializer.Save(elem, ctx, PathBuilder.Type.Collection);
                        }
                    }

                    var newMetas = new BucketMeta[_bucketMetas.Length - 1];
                    int ni = 0;
                    for (int mi = 0; mi < _bucketMetas.Length; mi++)
                    {
                        if (mi == i)
                        {
                            newMetas[ni++] = new BucketMeta { BucketId = mergePrefix, Count = endIdx - startIdx };
                        }
                        else if (mi != i + 1)
                        {
                            newMetas[ni++] = _bucketMetas[mi];
                        }
                    }
                    _bucketMetas = newMetas;
                    _bucketCumulativeIndex = RecomputeCumulativeIndex();
                    break;
                }
            }
            _needsBucketMerge = false;
        }

        private void PerformRebalance(Serializer serializer, SerializeContext ctx)
        {
            if (_bucketMetas.Length == 0)
            {
                var newRanks = FakeLexoRank.Rebalance(_totalElementCount);
                for (int i = 0; i < _totalElementCount; i++)
                {
                    var page = GetOrLoadPage(i / _pageSize);
                    var elem = page.Elements[i % _pageSize];
                    if (elem != null)
                    {
                        elem.SvId = "0~" + newRanks[i];
                        serializer.Save(elem, ctx, PathBuilder.Type.Collection);
                    }
                }
            }
            else
            {
                for (int i = 0; i < _bucketMetas.Length; i++)
                {
                    int startIdx = i > 0 ? _bucketCumulativeIndex[i - 1] : 0;
                    int count = _bucketMetas[i].Count;
                    var newRanks = FakeLexoRank.Rebalance(count);
                    for (int j = startIdx; j < startIdx + count; j++)
                    {
                        var page = GetOrLoadPage(j / _pageSize);
                        var elem = page.Elements[j % _pageSize];
                        if (elem != null)
                        {
                            elem.SvId = _bucketMetas[i].BucketId + "~" + newRanks[j - startIdx];
                            serializer.Save(elem, ctx, PathBuilder.Type.Collection);
                        }
                    }
                }
            }
            _needsRebalance = false;
        }

        private void FlushDirtyPages(Serializer serializer, SerializeContext ctx)
        {
            foreach (var (pageIndex, page) in _loadedPages)
            {
                if (page.IsDirty)
                    FlushPage(pageIndex, page);
            }
        }

        private void SaveCollectionMetadata(Serializer serializer, SerializeContext ctx)
        {
            var metadata = new CollectionMetadata
            {
                Count = _totalElementCount,
                PageSize = _pageSize,
                MaxCachedPages = _maxCachedPages,
                CacheStrategy = _cacheStrategy,
                IsLazyLoaded = true,
                BucketMetas = _bucketMetas.Length > 0 ? _bucketMetas : null,
                BucketMultiplier = _bucketMultiplier
            };
            serializer.Save(metadata, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection);
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            _rwLock.EnterWriteLock();
            try
            {
                if (_needsBucketSplit)
                {
                    PerformBucketSplit(serializer, ctx);
                }

                if (_needsBucketMerge)
                {
                    PerformBucketMerge(serializer, ctx);
                }

                if (_needsRebalance)
                {
                    PerformRebalance(serializer, ctx);
                }

                FlushDirtyPages(serializer, ctx);

                foreach (var deletedId in _idsDeleted)
                {
                    serializer.Delete(ctx, deletedId, PathBuilder.Type.Collection);
                }
                _idsDeleted.Clear();

                SaveCollectionMetadata(serializer, ctx);

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

                _totalElementCount = metadata.Count;
                _allIds = metadata.Ids ?? Array.Empty<string>();
                _idsDeleted = new HashSet<string>();

                // Build cumulative bucket index
                if (metadata.BucketMetas != null && metadata.BucketMetas.Length > 0)
                {
                    _bucketMetas = metadata.BucketMetas;
                    _bucketCumulativeIndex = new int[metadata.BucketMetas.Length];
                    int cumulative = 0;
                    for (int i = 0; i < metadata.BucketMetas.Length; i++)
                    {
                        cumulative += metadata.BucketMetas[i].Count;
                        _bucketCumulativeIndex[i] = cumulative;
                    }
                }
                else
                {
                    _bucketMetas = Array.Empty<BucketMeta>();
                    _bucketCumulativeIndex = Array.Empty<int>();
                }

                _bucketMultiplier = metadata.BucketMultiplier > 0 ? metadata.BucketMultiplier : 3;

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
    }
}
