# Salvavida 集合懒加载功能设计

## 概述

为 `ObservableList<T>`、`ObservableArray<T>` 和 `ObservableDictionary<TKey, TValue>` 提供分页懒加载能力。当集合数据量超过阈值时，不一次性加载所有元素，而是按需加载，降低内存占用和初始加载时间。

## 核心设计决策

| 决策点 | 选择 | 说明 |
|--------|------|------|
| 阈值判断 | metadata 存储元素数量 | 在 metadata 中增加 count 字段 |
| List/Array 懒加载结构 | 页面块结构 | 按页面分组管理 |
| Dictionary 懒加载模式 | Auto + Single | Auto：首次访问全加载；Single：单条按需加载 |
| 动态配置存储 | 集合自身 metadata | 运行时可调整配置 |
| Pending 操作结构 | 操作日志 + 快速索引 | 列表保序，索引加速访问 |
| 缓存淘汰策略 | LRU + None | 可配置选择 |
| 类层级设计 | 组合模式 + 策略接口 | IPageLoader 抽象加载行为 |
| 结构性修改 | 标记删除 + 追加新增 | 避免频繁页面重排 |
| Attribute 配置 | CodeGenerator 优化 | 编译时常量注入 |
| 遍历枚举器 | struct 枚举器 | 零堆分配 |
| 并发控制 | ReaderWriterLockSlim | Trim 阻塞读写 |

## 架构设计

### 类层级结构

采用组合模式，通过策略接口隔离加载行为：

```
ObservableList<T> / ObservableArray<T> / ObservableDictionary<TKey, TValue>
├── 继承自现有基类
├── 内部持有 IPageLoader 策略实例
└── 根据配置在运行时选择策略实现

IPageLoader<T> (策略接口)
├── FullPageLoader<T>              # 非懒加载，一次性加载所有元素
└── LazyPageLoader<T>              # 懒加载，按页加载

IDictionaryLoader<TKey, TValue> (字典专用接口)
├── FullDictionaryLoader<TKey, TValue>
└── LazySingleLoader<TKey, TValue> # 单条懒加载
```

### Metadata 扩展

在现有 `__ob_metadata__` 中扩展：

```json
{
  "ids": ["id1", "id2", ...],
  "count": 1500,
  "pageSize": 100,
  "loadMode": "lazy",
  "cacheStrategy": "lru",
  "maxCachedPages": 10,
  "trimThreshold": {
    "fragmentationRatio": 0.3,
    "minDeletedCount": 100
  },
  "appendStartIndex": 1000,
  "appendedCount": 50,
  "dynamicConfig": {
    "threshold": 2000,
    "pageSize": 200,
    "maxCachedPages": 20
  }
}
```

### 动态配置序列化

动态配置存储在集合的 metadata 中，与集合紧密绑定。

**CollectionMetadata 结构：**

```csharp
class CollectionMetadata
{
    // 原有字段
    public int Count;
    public int PageSize;
    public DictionaryLoadMode LoadMode;
    public CacheStrategy CacheStrategy;
    public int MaxCachedPages;
    public TrimThreshold? TrimThreshold;
    public int AppendStartIndex;
    public int AppendedCount;
    
    // 动态配置（可为 null）
    public LazyLoadConfig? DynamicConfig;
}
```

**序列化时机：**

1. **集合保存时**：如果 `_dynamicConfig` 被修改，写入 metadata
2. **动态配置修改时**：标记集合为脏，等待下次保存

**动态配置修改：**

```csharp
class LazyPageLoader<T>
{
    private LazyLoadConfig? _dynamicConfig;
    private bool _dynamicConfigDirty;
    
    public void SetDynamicConfig(LazyLoadConfig config)
    {
        _rwLock.EnterWriteLock();
        try
        {
            _dynamicConfig = config;
            _dynamicConfigDirty = true;
            
            // 应用新配置
            if (config.MaxCachedPages.HasValue)
            {
                _maxCachedPages = config.MaxCachedPages.Value;
            }
            if (config.CacheStrategy.HasValue)
            {
                _cacheStrategy = config.CacheStrategy.Value;
            }
            // ... 其他配置项
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
}
```

**Metadata 保存：**

```csharp
void UpdateMetadata(SerializeContext ctx)
{
    var metadata = new CollectionMetadata
    {
        Count = _totalElementCount + _appendedCount - _deletedCount,
        PageSize = _pageSize,
        LoadMode = _loadMode,
        CacheStrategy = _cacheStrategy,
        MaxCachedPages = _maxCachedPages,
        TrimThreshold = _trimThreshold,
        AppendStartIndex = _appendStartIndex,
        AppendedCount = _appendedCount,
        DynamicConfig = _dynamicConfigDirty ? _dynamicConfig : null,
    };
    
    // 序列化 metadata
    using var scope = ctx.Path.UsePush("__ob_metadata__", PathBuilder.Type.Property);
    serializer.Save(metadata, ctx);
    
    _dynamicConfigDirty = false;
}
```

**反序列化时读取动态配置：**

```csharp
void Deserialize(Serializer serializer, SerializeContext ctx)
{
    var metadata = ReadMetadata(ctx);
    
    // 读取动态配置
    if (metadata.DynamicConfig != null)
    {
        _dynamicConfig = metadata.DynamicConfig;
    }
    
    // 计算有效阈值
    int threshold = GetEffectiveThreshold();
    
    if (metadata.Count > threshold)
    {
        // 创建懒加载策略，应用动态配置
        _pageLoader = new LazyPageLoader<T>(this, _propertyName, _staticConfig, _dynamicConfig);
    }
    else
    {
        _pageLoader = new FullPageLoader<T>();
        _pageLoader.LoadAll(serializer, ctx);
    }
}
```

**配置优先级实现：**

```csharp
int GetEffectiveThreshold()
{
    // 1. 动态配置（metadata 中存储）
    if (_dynamicConfig?.Threshold > 0)
        return _dynamicConfig.Threshold.Value;
    
    // 2. 编译时常量（Attribute）
    if (_staticConfig.Threshold > 0)
        return _staticConfig.Threshold;
    
    // 3. 全局默认
    return GlobalConfig.DefaultLazyLoadThreshold;
}
```

**首次保存 vs 后续保存：**

- **首次保存**：metadata 中不包含 `dynamicConfig` 字段，使用静态配置
- **后续保存**：如果运行时修改了动态配置，metadata 中包含 `dynamicConfig` 字段
- **跨会话持久化**：动态配置在下次加载时从 metadata 恢复

## LazyPageLoader 设计

### 内部结构

```csharp
class LazyPageLoader<T> : IPageLoader<T>
{
    // 页面缓存
    Dictionary<int, PageData<T>> _loadedPages;  // 页号 → 页面数据
    int _totalElementCount;                       // 总元素数
    int _pageSize;                                // 每页元素数
    
    // LRU 缓存管理
    LinkedList<int> _lruList;                     // 页号 LRU 队列
    int _maxCachedPages;
    CacheStrategy _cacheStrategy;                 // LRU | None
    
    // 操作日志（保存顺序）
    List<PendingOperation<T>> _pendingOperations;
    
    // 快速查找索引
    Dictionary<int, T> _pendingWrites;            // 待写入的值
    HashSet<int> _pendingDeletes;                 // 待删除的索引
    
    // 结构性修改元数据
    int _appendStartIndex;                        // 追加区域起始索引
    int _appendedCount;                           // 追加元素数量
    int _deletedCount;                            // 已删除元素数量
    
    // 并发控制
    ReaderWriterLockSlim _rwLock;
    
    // 配置
    LazyLoadConfig _staticConfig;
    LazyLoadConfig? _dynamicConfig;
    TrimThreshold? _trimThreshold;
}
```

### PageData 结构

```csharp
class PageData<T>
{
    T?[] Elements;                  // 页面内元素数组
    HashSet<int> Tombstones;        // 已删除位置的本地索引
    bool HasLocalChanges;           // 是否有待保存的修改
}
```

### PendingOperation 定义

```csharp
enum PendingOperationType
{
    Set,           // 设置元素（包括修改和追加）
    Delete,        // 删除元素
    Clear,         // 清空集合
    Insert,        // 插入元素
}

struct PendingOperation<T>
{
    PendingOperationType Type;
    int Index;
    T? Value;
}
```

### 页面加载流程

```csharp
PageData<T> LoadPage(int pageIndex)
{
    _rwLock.EnterUpgradeableReadLock();
    try
    {
        // 1. 检查是否已加载
        if (_loadedPages.TryGetValue(pageIndex, out var page))
        {
            TouchPage(pageIndex);
            return page;
        }
        
        // 2. 升级为写锁
        _rwLock.EnterWriteLock();
        try
        {
            // 3. 检查缓存容量，必要时淘汰
            if (_cacheStrategy == CacheStrategy.LRU && 
                _loadedPages.Count >= _maxCachedPages)
            {
                EvictLruPages(_loadedPages.Count - _maxCachedPages + 1);
            }
            
            // 4. 从存储加载页面元素
            var page = LoadPageFromStorage(pageIndex);
            
            // 5. 合并 pending 操作
            ApplyPendingOperationsToPage(page, pageIndex);
            
            // 6. 添加到缓存
            _loadedPages[pageIndex] = page;
            UpdateLru(pageIndex);
            
            return page;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
    finally
    {
        _rwLock.ExitUpgradeableReadLock();
    }
}
```

### 合并 Pending 操作到页面

```csharp
void ApplyPendingOperationsToPage(PageData<T> page, int pageIndex)
{
    int startIndex = pageIndex * _pageSize;
    int endIndex = startIndex + _pageSize - 1;
    
    // 应用写入
    foreach (var (index, value) in _pendingWrites)
    {
        if (index >= startIndex && index <= endIndex)
        {
            int localIndex = index - startIndex;
            page.Elements[localIndex] = value;
            page.HasLocalChanges = true;
        }
    }
    
    // 标记删除（tombstone）
    foreach (var index in _pendingDeletes)
    {
        if (index >= startIndex && index <= endIndex)
        {
            int localIndex = index - startIndex;
            page.Tombstones.Add(localIndex);
        }
    }
}
```

### LRU 缓存淘汰

```csharp
void EvictLruPages(int count)
{
    for (int i = 0; i < count && _lruList.Count > 0; i++)
    {
        int pageIndex = _lruList.Last.Value;
        _lruList.RemoveLast();
        
        var page = _loadedPages[pageIndex];
        
        // 如有未保存的本地修改，先持久化
        if (page.HasLocalChanges)
        {
            FlushPageChanges(page, pageIndex);
        }
        
        _loadedPages.Remove(pageIndex);
    }
}

void TouchPage(int pageIndex)
{
    if (_cacheStrategy == CacheStrategy.LRU)
    {
        _lruList.Remove(pageIndex);
        _lruList.AddFirst(pageIndex);
    }
}
```

## LazySingleLoader 设计（Dictionary）

### 内部结构

```csharp
class LazySingleLoader<TKey, TValue> : IDictionaryLoader<TKey, TValue>
{
    // 已加载元素
    Dictionary<TKey, LoadedEntry<TValue>> _loadedEntries;
    HashSet<TKey> _allKeys;                       // 所有已知键
    int _totalCount;                              // 总元素数
    
    // LRU 缓存管理
    LinkedList<TKey> _lruList;
    int _maxCachedEntries;
    CacheStrategy _cacheStrategy;
    
    // 操作日志
    List<PendingOperation<TKey, TValue>> _pendingOperations;
    
    // 快速查找索引
    Dictionary<TKey, TValue> _pendingWrites;
    HashSet<TKey> _pendingDeletes;
    
    // 加载模式
    DictionaryLoadMode _loadMode;
    
    // 并发控制
    ReaderWriterLockSlim _rwLock;
}

struct LoadedEntry<TValue>
{
    TValue Value;
    bool IsLoaded;
}
```

### 字典加载模式

```csharp
enum DictionaryLoadMode
{
    Auto,            // 访问时自动全加载
    Single,          // 单条懒加载
}
```

### 访问逻辑

**Single 模式 - 访问 `dict[key]`：**

```csharp
TValue? GetElement(TKey key)
{
    _rwLock.EnterUpgradeableReadLock();
    try
    {
        // 1. 检查 pending
        if (_pendingDeletes.Contains(key))
            throw new KeyNotFoundException();
        
        if (_pendingWrites.TryGetValue(key, out var pendingValue))
            return pendingValue;
        
        // 2. 检查已加载
        if (_loadedEntries.TryGetValue(key, out var entry) && entry.IsLoaded)
        {
            TouchKey(key);
            return entry.Value;
        }
        
        // 3. 从存储加载单个元素
        _rwLock.EnterWriteLock();
        try
        {
            var value = LoadElementFromStorage(key);
            _loadedEntries[key] = new LoadedEntry<TValue> { Value = value, IsLoaded = true };
            UpdateLru(key);
            return value;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
    finally
    {
        _rwLock.ExitUpgradeableReadLock();
    }
}
```

**Auto 模式 - 首次访问触发全加载：**

```csharp
void EnsureLoaded()
{
    if (_isFullyLoaded) return;
    
    _rwLock.EnterWriteLock();
    try
    {
        if (_isFullyLoaded) return;
        LoadAllInternal();
        _isFullyLoaded = true;
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
}
```

**ContainsKey/Count 不触发加载：**

```csharp
bool ContainsKey(TKey key)
{
    if (_pendingDeletes.Contains(key)) return false;
    if (_pendingWrites.ContainsKey(key)) return true;
    return _allKeys.Contains(key);
}

int Count => _totalCount - _pendingDeletes.Count + NewKeysCount;
```

## 碎片化检测与 Trim

### 碎片化程度衡量

```csharp
float FragmentationRatio => 
    (float)_deletedCount / (_totalElementCount + _appendedCount);

bool ShouldAutoTrim()
{
    if (_trimThreshold == null) return false;
    
    return _deletedCount >= _trimThreshold.MinDeletedCount &&
           FragmentationRatio >= _trimThreshold.FragmentationRatio;
}
```

### Trim 流程

```csharp
void Trim()
{
    _rwLock.EnterWriteLock();
    try
    {
        // 1. 加载所有页面
        LoadAllPages();
        
        // 2. 构建新的连续数据
        var newElements = new List<T>();
        foreach (var page in _loadedPages.OrderBy(p => p.Key))
        {
            for (int i = 0; i < page.Value.Elements.Length; i++)
            {
                if (!page.Value.Tombstones.Contains(i))
                {
                    newElements.Add(page.Value.Elements[i]);
                }
            }
        }
        
        // 3. 清理存储
        ClearAllStorage();
        
        // 4. 重新分页写入
        _totalElementCount = newElements.Count;
        _deletedCount = 0;
        _appendedCount = 0;
        _appendStartIndex = _totalElementCount;
        
        SaveAllPages(newElements);
        
        // 5. 清空 pending 操作
        _pendingOperations.Clear();
        _pendingWrites.Clear();
        _pendingDeletes.Clear();
        
        // 6. 重置内存缓存
        _loadedPages.Clear();
        _lruList.Clear();
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
}
```

## 保存机制

### 保存流程

```csharp
void Save(Serializer serializer, SerializeContext ctx)
{
    _rwLock.EnterWriteLock();
    try
    {
        // 1. 检查是否需要自动 Trim
        if (ShouldAutoTrim())
        {
            Trim();
            return;
        }
        
        // 2. 处理 pending 操作
        foreach (var op in _pendingOperations)
        {
            switch (op.Type)
            {
                case PendingOperationType.Set:
                case PendingOperationType.Append:
                    SaveElement(serializer, ctx, op.Index, op.Value);
                    break;
                case PendingOperationType.Delete:
                    DeleteElement(serializer, ctx, op.Index);
                    break;
                case PendingOperationType.Insert:
                    HandleInsert(serializer, ctx, op.Index, op.Value);
                    break;
            }
        }
        
        // 3. 更新 metadata
        UpdateMetadata(ctx);
        
        // 4. 清除 pending 操作
        _pendingOperations.Clear();
        _pendingWrites.Clear();
        _pendingDeletes.Clear();
        
        // 5. 清除脏标记
        SetDirty(false, false);
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
}
```

### 元素保存路径

所有元素统一按索引存储：

```
集合路径: "items"
元素路径: "items/0", "items/1", ..., "items/1500"
         "items/__ob_metadata__"
```

## 反序列化与加载流程

### 入口判断

```csharp
void Deserialize(Serializer serializer, SerializeContext ctx)
{
    var metadata = ReadMetadata(ctx);
    int threshold = GetEffectiveThreshold();
    
    if (metadata.Count > threshold)
    {
        _pageLoader = new LazyPageLoader<T>(this, _propertyName, _staticConfig);
    }
    else
    {
        _pageLoader = new FullPageLoader<T>();
        _pageLoader.LoadAll(serializer, ctx);
    }
}
```

### 配置优先级

优先级：动态配置 > 编译时常量 > 全局默认

| 配置项 | 动态配置 | 编译时常量 | 全局默认 |
|--------|----------|------------|----------|
| Threshold | `_dynamicConfig.Threshold` | `_staticConfig.Threshold` | `GlobalConfig.DefaultLazyLoadThreshold` |
| PageSize | `_dynamicConfig.PageSize` | `_staticConfig.PageSize` | `GlobalConfig.DefaultPageSize` |
| LoadMode | `_dynamicConfig.LoadMode` | `_staticConfig.LoadMode` | `GlobalConfig.DefaultDictionaryLoadMode` |
| MaxCachedPages | `_dynamicConfig.MaxCachedPages` | `_staticConfig.MaxCachedPages` | `GlobalConfig.DefaultMaxCachedPages` |
| TrimThreshold | `_dynamicConfig.TrimThreshold` | `_staticConfig.TrimThreshold` | `GlobalConfig.DefaultTrimThreshold` |

### SourceGenerator 生成的代码

Generator 只负责生成编译时常量配置，动态配置由集合自身的 metadata 管理：

```csharp
partial class MySavable : ISavable
{
    // 每个集合属性独立的静态配置
    private static readonly LazyLoadConfig s_itemsConfig = new LazyLoadConfig
    {
        Threshold = 1000,
        PageSize = 50,
        MaxCachedPages = 10,
    };
    
    private static readonly LazyLoadConfig s_playersConfig = new LazyLoadConfig
    {
        Threshold = 500,
        PageSize = 100,
    };
    
    private ObservableList<ItemData>? _items;
    private ObservableList<PlayerData>? _players;
    
    public ObservableList<ItemData> Items
    {
        get => _items ??= CreateCollection(nameof(Items), s_itemsConfig);
        set => SetCollectionField(ref _items, value, nameof(Items), s_itemsConfig);
    }
    
    public ObservableList<PlayerData> Players
    {
        get => _players ??= CreateCollection(nameof(Players), s_playersConfig);
        set => SetCollectionField(ref _players, value, nameof(Players), s_playersConfig);
    }
}
```

**动态配置修改通过集合自身 API：**

```csharp
// 用户代码修改动态配置
items.SetDynamicConfig(new LazyLoadConfig
{
    MaxCachedPages = 20,
    CacheStrategy = CacheStrategy.LRU,
});

// 下次保存时，动态配置会写入 metadata
```

## IPageLoader 接口

```csharp
interface IPageLoader<T>
{
    T? GetElement(int index);
    void SetElement(int index, T? value);
    int Count { get; }
    bool Contains(int index);
    
    IndexEnumerator GetIndices();
    ElementEnumerator<T> GetElements();
    
    void Add(T? value);
    void Insert(int index, T? value);
    void RemoveAt(int index);
    void Clear();
    
    void LoadAll(Serializer serializer, SerializeContext ctx);
    void UnloadAll();
    
    void Save(Serializer serializer, SerializeContext ctx);
    void Deserialize(Serializer serializer, SerializeContext ctx);
    
    bool IsDirty { get; }
}

interface IDictionaryLoader<TKey, TValue>
{
    TValue? GetElement(TKey key);
    void SetElement(TKey key, TValue? value);
    int Count { get; }
    bool ContainsKey(TKey key);
    KeyEnumerator<TKey> Keys { get; }
    
    KeyValueEnumerator<TKey, TValue> GetElements();
    
    void Add(TKey key, TValue? value);
    bool Remove(TKey key);
    void Clear();
    
    void LoadAll(Serializer serializer, SerializeContext ctx);
    void LoadKey(TKey key, Serializer serializer, SerializeContext ctx);
    void UnloadKey(TKey key);
    void UnloadAll();
    
    void Save(Serializer serializer, SerializeContext ctx);
    void Deserialize(Serializer serializer, SerializeContext ctx);
    
    bool IsDirty { get; }
}
```

## Struct 枚举器

```csharp
public struct IndexEnumerator
{
    private readonly int _count;
    private int _index;
    
    public IndexEnumerator(int count) { _count = count; _index = -1; }
    public int Current => _index;
    public bool MoveNext() => ++_index < _count;
    public IndexEnumerator GetEnumerator() => this;
}

public struct ElementEnumerator<T>
{
    private readonly IPageLoader<T> _loader;
    private readonly int _count;
    private int _index;
    
    public ElementEnumerator(IPageLoader<T> loader, int count)
    {
        _loader = loader; _count = count; _index = -1;
    }
    
    public T Current => _loader.GetElement(_index);
    public bool MoveNext() => ++_index < _count;
    public ElementEnumerator<T> GetEnumerator() => this;
}

public struct KeyEnumerator<TKey>
{
    private readonly HashSet<TKey> _keys;
    private HashSet<TKey>.Enumerator _enumerator;
    
    public KeyEnumerator(HashSet<TKey> keys)
    {
        _keys = keys;
        _enumerator = _keys.GetEnumerator();
    }
    
    public TKey Current => _enumerator.Current;
    public bool MoveNext() => _enumerator.MoveNext();
    public KeyEnumerator<TKey> GetEnumerator() => this;
}

public struct KeyValueEnumerator<TKey, TValue>
{
    private readonly IDictionaryLoader<TKey, TValue> _loader;
    private KeyEnumerator<TKey> _keyEnumerator;
    
    public KeyValueEnumerator(IDictionaryLoader<TKey, TValue> loader, KeyEnumerator<TKey> keyEnumerator)
    {
        _loader = loader;
        _keyEnumerator = keyEnumerator;
    }
    
    public KeyValuePair<TKey, TValue> Current =>
        new KeyValuePair<TKey, TValue>(_keyEnumerator.Current, _loader.GetElement(_keyEnumerator.Current));
    public bool MoveNext() => _keyEnumerator.MoveNext();
    public KeyValueEnumerator<TKey, TValue> GetEnumerator() => this;
}
```

## 异常类型

```csharp
public class LazyLoadException : Exception
{
    public string CollectionPath { get; }
    public int? PageIndex { get; }
    public string? Key { get; }
}

public class PageLoadException : LazyLoadException { }
public class ElementDeletedException : LazyLoadException { }
public class ConfigMismatchException : LazyLoadException { }
```

## 文件结构

```
Salvavida/Package/Runtime/
├── LazyLoading/
│   ├── IPageLoader.cs
│   ├── IDictionaryLoader.cs
│   ├── FullPageLoader.cs
│   ├── LazyPageLoader.cs
│   ├── LazySingleLoader.cs
│   ├── PageData.cs
│   ├── PendingOperation.cs
│   ├── LazyLoadConfig.cs
│   ├── LazyLoadAttribute.cs
│   ├── Enumerators.cs
│   ├── CollectionMetadata.cs
│   └── Exceptions.cs
├── ObservableCollection.cs
├── ObservableList.cs
├── ObservableArray.cs
└── ObservableDictionary.cs

Salvavida.Generator/
├── LazyLoadConfigGenerator.cs
└── ...existing generators...
```

## 实现阶段

| 阶段 | 内容 | 依赖 |
|------|------|------|
| 1 | 核心数据结构（PageData、PendingOperation、LazyLoadConfig） | - |
| 2 | IPageLoader 接口与 FullPageLoader | 阶段 1 |
| 3 | LazyPageLoader 核心逻辑（加载、访问） | 阶段 1 |
| 4 | LazySingleLoader 核心逻辑 | 阶段 1 |
| 5 | LRU 缓存淘汰 | 阶段 3, 4 |
| 6 | Pending 操作与保存机制 | 阶段 3, 4 |
| 7 | Trim 机制 | 阶段 5, 6 |
| 8 | 集合类集成（委托模式） | 阶段 2, 3, 4 |
| 9 | Metadata 扩展与反序列化 | 阶段 8 |
| 10 | CodeGenerator 配置注入 | 阶段 9 |
| 11 | 并发控制（ReaderWriterLockSlim） | 阶段 3, 4, 7 |
| 12 | 单元测试与集成测试 | 阶段 1-11 |

## 测试覆盖

- 页面按需加载测试
- LRU 淘汰测试
- Pending 操作测试（写入、删除、追加）
- Trim 触发与数据重组测试
- 字典加载模式测试（Auto、Single）
- 策略切换测试
- 端到端保存加载测试
