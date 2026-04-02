# Salvavida 集合懒加载功能设计（重构版）

## 概述

为 `ObservableListSavable<T>` 和 `ObservableArraySavable<T>` 提供分页懒加载能力。当集合数据量超过阈值时，不一次性加载所有元素，而是按需加载，降低内存占用和初始加载时间。

**注意：** 懒加载仅适用于元素类型为 `ISavable` 的集合。非 `ISavable` 元素的集合不支持懒加载。

## 核心设计决策

| 决策点 | 选择 | 说明 |
|--------|------|------|
| 适用范围 | 仅 Savable 集合 | 元素类型必须实现 `ISavable` 接口 |
| 类型决策时机 | 运行时（Deserialize 时） | 根据 metadata 中的 count 决定 |
| 类层级设计 | 抽象基类 + 两个具体实现 | 基类提供接口，子类实现存储模型 |
| 存储格式 | 新格式含页配置 | metadata 包含 pageSize、cacheStrategy 等 |
| Modify 操作 | 强制加载相关页 | 简化实现，后续可优化 |
| 配置方式 | Attribute + 静态配置 | SourceGenerator 生成配置常量 |

## 架构设计

### 类层级结构

**List 系列：**

```
ObservableListSavableBase<T> (abstract, where T : ISavable)
├── ObservableListSavable<T>           # 全量加载，所有元素常驻内存
└── ObservableLazyListSavable<T>       # 分页懒加载，按需加载页
```

**Array 系列：**

```
ObservableArraySavableBase<T> (abstract, where T : ISavable)
├── ObservableArraySavable<T>          # 全量加载
└── ObservableLazyArraySavable<T>      # 分页懒加载
```

### 运行时类型决策

```
┌─────────────────────────────────────────────────────────────┐
│                     Deserialize 流程                         │
├─────────────────────────────────────────────────────────────┤
│  1. 读取 metadata                                           │
│  2. 比较 count 与 threshold                                 │
│  3. count > threshold && isLazyLoaded                       │
│     ├─ true  → 创建 ObservableLazyListSavable               │
│     └─ false → 创建 ObservableListSavable，加载所有元素      │
└─────────────────────────────────────────────────────────────┘
```

### 存储格式

**Metadata 结构：**

```json
{
  "ids": ["0", "1", "2", "..."],
  "count": 1500,
  "pageSize": 100,
  "maxCachedPages": 10,
  "cacheStrategy": 1,
  "isLazyLoaded": true
}
```

字段说明：
- `ids`: 元素 ID 数组（ID 为索引字符串）
- `count`: 总元素数
- `pageSize`: 每页元素数
- `maxCachedPages`: 最大缓存页数
- `cacheStrategy`: 缓存策略（0=None, 1=LRU）
- `isLazyLoaded`: 是否为懒加载格式

**元素存储路径：**
```
集合路径: "items"
元素路径: "items/0", "items/1", ..., "items/1499"
         "items/__ob_metadata__"
```

## 详细设计

### ObservableListSavableBase<T>

抽象基类，定义公共接口：

```csharp
public abstract class ObservableListSavableBase<T> : ObservableCollectionSavable<ObservableListSavableBase<T>, T>, IList<T?>, IReadOnlyList<T?>, IList
    where T : ISavable
{
    protected ObservableListSavableBase(string propName, bool saveSeparately);

    // 抽象方法 - 子类必须实现
    public abstract T? this[int index] { get; set; }
    public abstract int Count { get; }

    public abstract void Add(T? item);
    public abstract void Clear();
    public abstract bool Contains(T? item);
    public abstract int IndexOf(T? item);
    public abstract void Insert(int index, T? item);
    public abstract bool Remove(T? item);
    public abstract void RemoveAt(int index);

    // 数据替换
    public abstract void SwapSource(List<T?>? list);

    // 序列化
    public abstract override void Serialize(Serializer serializer, SerializeContext ctx);
    public abstract override void Deserialize(Serializer serializer, SerializeContext ctx);
    public abstract override bool IsDirty { get; }

    // 类型检查
    public bool IsLazyLoaded => this is ObservableLazyListSavable<T>;
}
```

### ObservableListSavable<T>

全量加载实现，与现有实现基本相同：

```csharp
public sealed class ObservableListSavable<T> : ObservableListSavableBase<T>
    where T : ISavable
{
    private List<T?>? _list;
    private string[]? _idsOnDeserialized;

    public override T? this[int index] { get; set; }
    public override int Count => _list?.Count ?? 0;
    public override bool IsDirty { get; }

    public override void SwapSource(List<T?>? list) { /* 同现有实现 */ }
    public override void Serialize(Serializer serializer, SerializeContext ctx) { /* 同现有实现 */ }
    public override void Deserialize(Serializer serializer, SerializeContext ctx) { /* 加载所有元素 */ }
}
```

### ObservableLazyListSavable<T>

#### 数据结构

```csharp
public sealed class ObservableLazyListSavable<T> : ObservableListSavableBase<T>
    where T : ISavable
{
    // 配置
    private readonly int _pageSize;
    private readonly int _maxCachedPages;
    private readonly CacheStrategy _cacheStrategy;

    // 页存储
    private readonly Dictionary<int, PageData> _loadedPages = new();
    private int _totalElementCount;

    // 所有元素 ID（反序列化时加载）
    private string[] _allIds = Array.Empty<string>();

    // LRU 追踪
    private readonly LinkedList<int> _lruList = new();

    // 脏追踪
    private bool _hasPendingWrites;

    // 并发控制
    private readonly ReaderWriterLockSlim _rwLock = new();

    // Serializer 引用（用于懒加载页）
    private Serializer? _serializer;
    private SerializeContext? _context;

    // 内部结构
    private class PageData
    {
        public T?[] Elements;
        public bool IsDirty;
    }
}

public enum CacheStrategy { None, LRU }
```

#### 核心操作

**索引访问：**

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
        // 类似，使用写锁，标记页脏
    }
}

private PageData GetOrLoadPage(int pageIndex)
{
    if (_loadedPages.TryGetValue(pageIndex, out var page))
        return page;

    // 超出容量时淘汰
    if (_cacheStrategy == CacheStrategy.LRU && _loadedPages.Count >= _maxCachedPages)
        EvictLruPage();

    page = LoadPageFromStorage(pageIndex);
    _loadedPages[pageIndex] = page;
    return page;
}

private PageData LoadPageFromStorage(int pageIndex)
{
    var page = new PageData
    {
        Elements = new T?[_pageSize],
        IsDirty = false
    };

    int startIdx = pageIndex * _pageSize;
    int endIdx = Math.Min(startIdx + _pageSize, _totalElementCount);

    for (int i = startIdx; i < endIdx; i++)
    {
        string id = _allIds[i];
        page.Elements[i - startIdx] = _serializer!.Read<T?>(_context!, id, PathBuilder.Type.Collection);
    }

    return page;
}
```

**LRU 缓存淘汰：**

```csharp
private void TouchPage(int pageIndex)
{
    if (_cacheStrategy != CacheStrategy.LRU)
        return;

    _lruList.Remove(pageIndex);
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
            TryUnWatch(elem);

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
```

**修改操作：**

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

        Array.Resize(ref _allIds, _totalElementCount + 1);
        _allIds[_totalElementCount] = _totalElementCount.ToString();

        _totalElementCount++;

        OnItemSet(item, _totalElementCount - 1);
        OnCollectionChange(CollectionChangeInfo<...>.Add(this, item, _totalElementCount - 1));
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
}

public override void Insert(int index, T? item)
{
    // 强制加载所有受影响的页
    // 向右移动元素
    // 更新 ID 数组
}

public override void RemoveAt(int index)
{
    // 强制加载所有受影响的页
    // 向左移动元素
    // 更新 ID 数组
}

public override void Clear()
{
    // 删除所有元素
    // 清空页缓存
    // 重置状态
}
```

**序列化：**

```csharp
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

public override void SwapSource(List<T?>? list)
{
    // 清空现有页
    // 设置新的元素列表
    // 加载第一页
}

public override void Serialize(Serializer serializer, SerializeContext ctx)
{
    if (!SaveSeparately)
        return;

    _rwLock.EnterWriteLock();
    try
    {
        // 刷新所有脏页
        foreach (var (pageIndex, page) in _loadedPages)
        {
            if (page.IsDirty)
                FlushPage(pageIndex, page);
        }

        // 保存 metadata
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
```

### ObservableArray 系列

Array 系列与 List 类似，但：
- 固定大小，无 `Add`、`Insert`、`Remove` 操作
- `SwapSource` 接受 `T?[]?` 参数
- 实现更简单

```csharp
public abstract class ObservableArraySavableBase<T> : ObservableCollectionSavable<ObservableArraySavableBase<T>, T>, IList<T?>, IReadOnlyList<T?>, IList
    where T : ISavable
{
    public abstract T? this[int index] { get; set; }
    public abstract int Count { get; }
    public abstract void SwapSource(T?[]? array);

    // 不支持的操作
    void ICollection<T?>.Add(T? item) => throw new NotSupportedException();
    void IList<T?>.Insert(int index, T? item) => throw new NotSupportedException();
    bool ICollection<T?>.Remove(T? item) => throw new NotSupportedException();
    void IList<T?>.RemoveAt(int index) => throw new NotSupportedException();
    void ICollection<T?>.Clear() => throw new NotSupportedException();
}
```

## Serializer 工厂方法

```csharp
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
            metadata = ReadNoPushPath<CollectionMetadata>(ctx);
    }

    if (metadata == null)
    {
        sourceList = new List<T?>();
        return new ObservableListSavable<T>(propertyName, sourceList, saveSeparately);
    }

    int threshold = config?.Threshold ?? GlobalConfig.DefaultLazyLoadThreshold;

    if (metadata.Count > threshold && metadata.IsLazyLoaded)
    {
        int pageSize = config?.PageSize ?? metadata.PageSize ?? GlobalConfig.DefaultPageSize;
        int maxCached = config?.MaxCachedPages ?? metadata.MaxCachedPages ?? GlobalConfig.DefaultMaxCachedPages;
        var cacheStrategy = config?.CacheStrategy ?? metadata.CacheStrategy ?? GlobalConfig.DefaultCacheStrategy;

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

## SourceGenerator 变更

### 字段类型生成

```csharp
// 生成前
private ObservableListSavable<ItemData>? _itemsOb;
public ObservableListSavable<ItemData> Items { get { ... } }

// 生成后
private ObservableListSavableBase<ItemData>? _itemsOb;
public ObservableListSavableBase<ItemData> Items { get { ... } }
```

### LazyLoad Attribute

```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class LazyLoadAttribute : Attribute
{
    public int Threshold { get; set; } = 1000;
    public int PageSize { get; set; } = 100;
    public int MaxCachedPages { get; set; } = 10;
    public CacheStrategy CacheStrategy { get; set; } = CacheStrategy.LRU;
}
```

使用示例：

```csharp
[Savable]
partial class PlayerData
{
    [LazyLoad(Threshold = 500, PageSize = 50)]
    public List<ItemData> Items;

    [LazyLoad]
    public ItemData[] History;
}
```

### 静态配置生成

```csharp
partial class PlayerData
{
    private static readonly LazyLoadConfig s_itemsConfig = new LazyLoadConfig
    {
        Threshold = 500,
        PageSize = 50,
        MaxCachedPages = 10,
        CacheStrategy = CacheStrategy.LRU
    };

    private static readonly LazyLoadConfig s_historyConfig = new LazyLoadConfig
    {
        Threshold = 1000,
        PageSize = 100,
        MaxCachedPages = 10,
        CacheStrategy = CacheStrategy.LRU
    };
}
```

### TryInit 方法

```csharp
private void TryInitItems()
{
    if (_itemsOb != null) return;
    _itemsOb = new ObservableListSavable<ItemData>("Items", _items, true);
    WatchCollection<ObservableListSavableBase<ItemData>, ItemData>(_itemsOb);
}
```

### Deserialize 调用

```csharp
void ISavable.AfterDeserialize(Serializer serializer, SerializeContext ctx)
{
    _itemsOb = serializer.LoadCollectionSavable<ItemData>(
        ctx, "Items", true, ref _items, s_itemsConfig);
    OnCollectionDeserialized(_itemsOb);
}
```

## 全局配置

```csharp
public static class GlobalConfig
{
    public static int DefaultLazyLoadThreshold { get; set; } = 1000;
    public static int DefaultPageSize { get; set; } = 100;
    public static int DefaultMaxCachedPages { get; set; } = 10;
    public static CacheStrategy DefaultCacheStrategy { get; set; } = CacheStrategy.LRU;
}
```

## 运行时流程

### 新建对象

```
1. 用户创建对象
2. 首次访问 Items 属性
3. TryInitItems() 创建 ObservableListSavable<T>
4. 用户操作集合
5. Serialize 时根据 count 决定是否写入懒加载格式
```

### 加载已有数据

```
1. Deserialize 被调用
2. 读取 metadata
3. LoadCollectionSavable 比较 count 与 threshold
4. 创建 ObservableListSavable 或 ObservableLazyListSavable
5. 懒加载版本不加载任何元素，仅存储 ID 列表
6. 访问元素时按需加载页
```

## 迁移兼容性

- 现有数据没有 `isLazyLoaded` 字段，视为非懒加载
- 当 count 首次超过 threshold 并保存时，自动切换为懒加载格式
- 从懒加载切换回非懒加载：需手动调用 `SwapSource` 并保存

## 文件结构

```
Salvavida/Package/Runtime/
├── LazyLoading/
│   ├── LazyLoadConfig.cs
│   ├── LazyLoadAttribute.cs
│   ├── CacheStrategy.cs
│   └── CollectionMetadata.cs
├── ObservableCollection.cs
├── ObservableList.cs
│   ├── ObservableListSavableBase.cs
│   ├── ObservableListSavable.cs (已有)
│   └── ObservableLazyListSavable.cs (新增)
├── ObservableArray.cs
│   ├── ObservableArraySavableBase.cs
│   ├── ObservableArraySavable.cs (已有)
│   └── ObservableLazyArraySavable.cs (新增)
└── GlobalConfig.cs

Salvavida.Generator/
├── BasicCodeGenerator.cs (修改)
└── LazyLoadConfigGenerator.cs (修改)
```

## 实现阶段

| 阶段 | 内容 | 依赖 |
|------|------|------|
| 1 | 核心数据结构（LazyLoadConfig、CacheStrategy、CollectionMetadata） | - |
| 2 | ObservableListSavableBase 抽象基类 | 阶段 1 |
| 3 | ObservableListSavable 适配基类 | 阶段 2 |
| 4 | ObservableLazyListSavable 核心逻辑 | 阶段 2 |
| 5 | LRU 缓存淘汰 | 阶段 4 |
| 6 | 修改操作（Insert/Remove） | 阶段 4 |
| 7 | ObservableArray 系列 | 阶段 2-6 |
| 8 | Serializer 工厂方法 | 阶段 3, 4, 7 |
| 9 | SourceGenerator 变更 | 阶段 8 |
| 10 | GlobalConfig 与 Attribute | 阶段 1 |
| 11 | 单元测试与集成测试 | 阶段 1-10 |

## 测试覆盖

- 页面按需加载测试
- LRU 淘汰测试
- 修改操作测试（Add、Insert、Remove、Clear）
- SwapSource 测试
- 类型切换测试（非懒加载 → 懒加载）
- 端到端保存加载测试
- Array 系列测试
- 并发访问测试
