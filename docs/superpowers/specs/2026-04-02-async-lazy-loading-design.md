# Salvavida Async Lazy Loading 设计

## 概述

在现有懒加载设计基础上，为页面/元素加载操作提供异步接口，避免磁盘 IO 阻塞主线程。核心改动集中在 `IPageLoader<T>` / `IDictionaryLoader<TKey, TValue>` 层，集合层通过预取 API 和事件通知暴露异步能力。

## 问题背景

当前 `LazyPageLoader.LoadPage()` 中的 `LoadPageFromStorage()` 是同步磁盘 IO，在 Unity 主线程调用时会导致帧率下降甚至卡顿。需要将磁盘 IO 卸载到线程池，主线程通过 await 获取结果。

## 核心设计决策

| 决策点 | 选择 | 说明 |
|--------|------|------|
| 异步模型 | 双轨制 ValueTask + UniTask | `#if USE_UNITASK` 切换，匹配项目现有 `.asmdef` 预埋 |
| 返回类型 | `ValueTask<T>` / `UniTask<T>` | cache hit 时同步返回零分配，只在触发 IO 时分配 |
| API 粒度 | Loader 层异步 + 集合层预取 | 同步 getter 保持不变，不引入 async 传染 |
| 同步语义 | cache miss 返回 default，不阻塞 | 非阻塞保证，通过事件通知数据就绪 |
| 预取方式 | 手动预取 | 用户显式调用 `PrefetchAsync` |
| 取消支持 | `CancellationToken` | 标准 .NET 模式，切换场景时可取消加载 |
| 进度反馈 | `IProgress<float>` | 仅 `LoadAllAsync` 支持，全加载可能很大 |
| LoadAll | 提供 async 版本 | Auto 模式字典首次访问需全加载 |

## 异步接口定义

### IPageLoader\<T\> 扩展

```csharp
#if USE_UNITASK
using Awaitable = Cysharp.Threading.Tasks.UniTask;
using Awaitable<T> = Cysharp.Threading.Tasks.UniTask<T>;
#else
using Awaitable = System.Threading.Tasks.ValueTask;
using Awaitable<T> = System.Threading.Tasks.ValueTask<T>;
#endif

interface IPageLoaderAsync<T>
{
    /// <summary>
    /// 异步获取指定页。cache hit 同步返回，cache miss 在线程池加载。
    /// </summary>
    Awaitable<PageData<T>> GetPageAsync(int pageIndex, CancellationToken ct = default);
    
    /// <summary>
    /// 异步获取指定索引的元素。内部获取所在页后取元素。
    /// </summary>
    Awaitable<T?> GetElementAsync(int index, CancellationToken ct = default);
    
    /// <summary>
    /// 预取指定页，不阻塞。加载完成后触发 PageLoaded 事件。
    /// </summary>
    Awaitable PrefetchAsync(int pageIndex, CancellationToken ct = default);
    
    /// <summary>
    /// 异步全加载。支持进度反馈和取消。
    /// </summary>
    Awaitable LoadAllAsync(Serializer serializer, SerializeContext ctx,
                           CancellationToken ct = default, IProgress<float> progress = null);
}
```

### IDictionaryLoader\<TKey, TValue\> 扩展

```csharp
interface IDictionaryLoaderAsync<TKey, TValue>
{
    /// <summary>
    /// 异步获取指定 key 的值。cache hit 同步返回。
    /// </summary>
    Awaitable<TValue?> GetElementAsync(TKey key, CancellationToken ct = default);
    
    /// <summary>
    /// 预取指定 key，不阻塞。加载完成后触发 EntryLoaded 事件。
    /// </summary>
    Awaitable PrefetchKeyAsync(TKey key, CancellationToken ct = default);
    
    /// <summary>
    /// 异步全加载（Auto 模式首次访问触发）。
    /// </summary>
    Awaitable LoadAllAsync(Serializer serializer, SerializeContext ctx,
                           CancellationToken ct = default, IProgress<float> progress = null);
}
```

## 同步方法语义

同步方法保持不变，增加非阻塞语义约定：

```csharp
// IPageLoader<T> (现有，不变)
T? GetElement(int index);

// 语义：
// - page 已加载 → 返回元素值
// - page 未加载 → 返回 default(T)，不触发磁盘 IO，不阻塞
// - 用户通过 IsPageLoaded 判断状态，决定是否调用 async 方法
```

```csharp
// 集合层增加状态查询
bool IsPageLoaded(int pageIndex);
int PageCount { get; }
```

## 集合层预取 API

### ObservableList\<T\>

```csharp
partial class ObservableList<T> : INotifyPageLoaded
{
#if USE_UNITASK
    public UniTask PrefetchPageAsync(int pageIndex, CancellationToken ct = default)
        => _pageLoader.PrefetchAsync(pageIndex, ct);
    
    public UniTask<T?> GetElementAsync(int index, CancellationToken ct = default)
        => _pageLoader.GetElementAsync(index, ct);
#else
    public ValueTask PrefetchPageAsync(int pageIndex, CancellationToken ct = default)
        => _pageLoader.PrefetchAsync(pageIndex, ct);
    
    public ValueTask<T?> GetElementAsync(int index, CancellationToken ct = default)
        => _pageLoader.GetElementAsync(index, ct);
#endif

    public bool IsPageLoaded(int pageIndex)
        => _pageLoader.IsPageLoaded(pageIndex);
    
    public int PageCount => _pageLoader.PageCount;
    
    public event PageLoadedHandler<T> PageLoaded;
}
```

### ObservableDictionary\<TKey, TValue\>

```csharp
partial class ObservableDictionary<TKey, TValue> : INotifyEntryLoaded<TKey, TValue>
{
#if USE_UNITASK
    public UniTask<TValue?> GetValueAsync(TKey key, CancellationToken ct = default)
        => _dictLoader.GetElementAsync(key, ct);
    
    public UniTask PrefetchKeyAsync(TKey key, CancellationToken ct = default)
        => _dictLoader.PrefetchKeyAsync(key, ct);
#else
    public ValueTask<TValue?> GetValueAsync(TKey key, CancellationToken ct = default)
        => _dictLoader.GetElementAsync(key, ct);
    
    public ValueTask PrefetchKeyAsync(TKey key, CancellationToken ct = default)
        => _dictLoader.PrefetchKeyAsync(key, ct);
#endif

    public event EntryLoadedHandler<TKey, TValue> EntryLoaded;
}
```

## 事件定义

```csharp
// List/Array 页面加载完成
public delegate void PageLoadedHandler<T>(object sender, PageLoadedEventArgs<T> e);

public class PageLoadedEventArgs<T> : EventArgs
{
    public int PageIndex { get; }
    public PageData<T> Page { get; }
}

// Dictionary 条目加载完成
public delegate void EntryLoadedHandler<TKey, TValue>(object sender, EntryLoadedEventArgs<TKey, TValue> e);

public class EntryLoadedEventArgs<TKey, TValue> : EventArgs
{
    public TKey Key { get; }
    public TValue Value { get; }
}

// 接口
public interface INotifyPageLoaded<T>
{
    event PageLoadedHandler<T> PageLoaded;
}

public interface INotifyEntryLoaded<TKey, TValue>
{
    event EntryLoadedHandler<TKey, TValue> EntryLoaded;
}
```

## LazyPageLoader 内部实现

### Pending Load 去重

```csharp
class LazyPageLoader<T> : IPageLoader<T>, IPageLoaderAsync<T>
{
    // 新增：进行中的异步加载任务，防止重复 IO
    ConcurrentDictionary<int, Awaitable<PageData<T>>> _pendingLoads = new();
    
    // 新增：IO 调度函数（可注入，便于测试）
    internal Func<int, CancellationToken, Awaitable<PageData<T>>> _loadFromStorageAsync;
}
```

### GetPageAsync 实现

```csharp
async Awaitable<PageData<T>> GetPageAsync(int pageIndex, CancellationToken ct = default)
{
    // 1. cache hit → 同步返回（零分配）
    if (_loadedPages.TryGetValue(pageIndex, out var page))
    {
        TouchPage(pageIndex);
        return page;
    }
    
    // 2. 有正在进行的加载 → 复用
    if (_pendingLoads.TryGetValue(pageIndex, out var pending))
    {
        return await pending;
    }
    
    // 3. 创建新加载任务
    var loadTask = LoadPageInternalAsync(pageIndex, ct);
    _pendingLoads.TryAdd(pageIndex, loadTask);
    
    try
    {
        var loaded = await loadTask;
        return loaded;
    }
    finally
    {
        _pendingLoads.TryRemove(pageIndex, out _);
    }
}
```

### LoadPageInternalAsync 实现

```csharp
async Awaitable<PageData<T>> LoadPageInternalAsync(int pageIndex, CancellationToken ct)
{
#if USE_UNITASK
    await UniTask.SwitchToThreadPool();
    ct.ThrowIfCancellationRequested();
    
    var page = LoadPageFromStorage(pageIndex);
    ApplyPendingOperationsToPage(page, pageIndex);
    
    await UniTask.SwitchToMainThread();
#else
    var page = await Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var loaded = LoadPageFromStorage(pageIndex);
        ApplyPendingOperationsToPage(loaded, pageIndex);
        return loaded;
    }, ct);
#endif
    
    // 写入缓存（已在主线程或持有写锁）
    _rwLock.EnterWriteLock();
    try
    {
        // 淘汰检查
        if (_cacheStrategy == CacheStrategy.LRU &&
            _loadedPages.Count >= _maxCachedPages)
        {
            EvictLruPages(_loadedPages.Count - _maxCachedPages + 1);
        }
        
        _loadedPages[pageIndex] = page;
        UpdateLru(pageIndex);
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
    
    // 触发事件
    OnPageLoaded?.Invoke(pageIndex, page);
    
    return page;
}
```

### PrefetchAsync 实现

```csharp
async Awaitable PrefetchAsync(int pageIndex, CancellationToken ct = default)
{
    if (IsPageLoaded(pageIndex)) return;
    await GetPageAsync(pageIndex, ct);
    // 不需要返回值，结果已缓存，事件已触发
}
```

### LoadAllAsync 实现

```csharp
async Awaitable LoadAllAsync(Serializer serializer, SerializeContext ctx,
                             CancellationToken ct = default, IProgress<float> progress = null)
{
    int totalPages = (_totalElementCount + _pageSize - 1) / _pageSize;
    int loaded = 0;
    
    for (int i = 0; i < totalPages; i++)
    {
        ct.ThrowIfCancellationRequested();
        
        await GetPageAsync(i, ct);
        
        loaded++;
        progress?.Report((float)loaded / totalPages);
    }
    
    _isFullyLoaded = true;
}
```

## LazySingleLoader (Dictionary) 异步实现

### GetElementAsync

```csharp
async Awaitable<TValue?> GetElementAsync(TKey key, CancellationToken ct = default)
{
    // 1. pending 检查
    if (_pendingDeletes.Contains(key))
        throw new KeyNotFoundException();
    
    if (_pendingWrites.TryGetValue(key, out var pendingValue))
        return pendingValue;
    
    // 2. 已加载
    if (_loadedEntries.TryGetValue(key, out var entry) && entry.IsLoaded)
    {
        TouchKey(key);
        return entry.Value;
    }
    
    // 3. 异步加载单个元素
    return await LoadElementInternalAsync(key, ct);
}

async Awaitable<TValue?> LoadElementInternalAsync(TKey key, CancellationToken ct)
{
#if USE_UNITASK
    await UniTask.SwitchToThreadPool();
    ct.ThrowIfCancellationRequested();
    var value = LoadElementFromStorage(key);
    await UniTask.SwitchToMainThread();
#else
    var value = await Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        return LoadElementFromStorage(key);
    }, ct);
#endif
    
    _rwLock.EnterWriteLock();
    try
    {
        _loadedEntries[key] = new LoadedEntry<TValue> { Value = value, IsLoaded = true };
        UpdateLru(key);
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
    
    OnEntryLoaded?.Invoke(key, value);
    return value;
}
```

### EnsureLoadedAsync (Auto 模式)

```csharp
async Awaitable EnsureLoadedAsync(CancellationToken ct = default)
{
    if (_isFullyLoaded) return;
    
    await LoadAllAsync(_serializer, _ctx, ct);
    _isFullyLoaded = true;
}
```

## IO 线程调度策略

```csharp
// 线程池调度层，可注入便于测试
interface IThreadDispatcher
{
#if USE_UNITASK
    UniTask<T> RunOnThreadPool<T>(Func<T> func, CancellationToken ct);
#else
    ValueTask<T> RunOnThreadPool<T>(Func<T> func, CancellationToken ct);
#endif
}

// 默认实现
class DefaultThreadDispatcher : IThreadDispatcher
{
#if USE_UNITASK
    async UniTask<T> RunOnThreadPool<T>(Func<T> func, CancellationToken ct)
    {
        await UniTask.SwitchToThreadPool();
        ct.ThrowIfCancellationRequested();
        var result = func();
        await UniTask.SwitchToMainThread();
        return result;
    }
#else
    ValueTask<T> RunOnThreadPool<T>(Func<T> func, CancellationToken ct)
    {
        return new ValueTask<T>(Task.Run(func, ct));
    }
#endif
}
```

## 使用示例

```csharp
// 方式 1: 手动预取 + 监听事件
items.PageLoaded += (sender, e) =>
{
    Debug.Log($"Page {e.PageIndex} loaded, {e.Page.Elements.Length} elements");
    // 刷新 UI
};
await items.PrefetchPageAsync(5);  // 不阻塞主线程

// 方式 2: 直接 await 单个元素
var item = await items.GetElementAsync(500);  // 磁盘 IO 在线程池执行

// 方式 3: 同步访问（仅限已加载页）
if (items.IsPageLoaded(5))
{
    var item = items[500];  // 纯内存访问，安全
}

// 方式 4: 带进度的全加载
var progress = new Progress<float>(p => Debug.Log($"Loading: {p:P0}"));
await items.PrefetchPageAsync(0, ct);  // 预取第一页
// 或
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await dictLoader.LoadAllAsync(serializer, ctx, cts.Token, progress);

// 方式 5: 取消加载
var cts = new CancellationTokenSource();
var loadTask = items.GetElementAsync(500, cts.Token);
// 场景切换时
cts.Cancel();  // 抛 OperationCanceledException
```

## 线程安全

| 操作 | 锁 | 说明 |
|------|-----|------|
| GetElementAsync (cache hit) | UpgradeableReadLock | 读缓存 |
| LoadPageInternalAsync (加载) | 写入时 WriteLock | IO 在 ThreadPool，仅写缓存时持锁 |
| PrefetchAsync | 复用 GetPageAsync | 无额外锁 |
| LoadAllAsync | 复用 GetPageAsync 循环 | 无额外锁 |
| _pendingLoads 去重 | ConcurrentDictionary | 无锁 |

关键约束：
- 磁盘 IO 在 ThreadPool 执行，不持有任何锁
- 仅在写入 `_loadedPages` / `_loadedEntries` 缓存时短暂持有 WriteLock
- `_pendingLoads` 使用 `ConcurrentDictionary` 无锁去重

## 实现阶段

| 阶段 | 内容 | 依赖 |
|------|------|------|
| 1 | `IPageLoaderAsync<T>` / `IDictionaryLoaderAsync<TKey, TValue>` 接口定义 | - |
| 2 | 事件类型（`PageLoadedEventArgs`、`EntryLoadedEventArgs`） | - |
| 3 | `IThreadDispatcher` + 默认实现 | - |
| 4 | `PendingLoadTracker`（`ConcurrentDictionary` 去重） | 阶段 1 |
| 5 | `LazyPageLoader` async 方法实现 | 阶段 1, 3, 4 |
| 6 | `LazySingleLoader` async 方法实现 | 阶段 1, 3, 4 |
| 7 | `LoadAllAsync` 实现（含进度反馈） | 阶段 5, 6 |
| 8 | 集合层集成（`PrefetchAsync`、事件、`IsPageLoaded`） | 阶段 5, 6 |
| 9 | 单元测试 | 阶段 1-8 |

## 测试覆盖

- cache hit 同步返回零分配测试
- cache miss 异步加载测试
- pending load 去重测试
- 并发请求同一页测试
- CancellationToken 取消测试
- LoadAllAsync 进度回调测试
- Auto 模式 EnsureLoadedAsync 测试
- PrefetchAsync 预取 + 事件触发测试
- 线程安全：多线程同时 GetElementAsync 测试
- 回退测试：USE_UNITASK 未定义时 ValueTask 正常工作
