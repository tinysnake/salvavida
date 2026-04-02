# Salvavida 懒加载功能设计

## 概述

为 `ObservableList<T>`、`ObservableArray<T>` 和 `ObservableDictionary<TKey, TValue>` 提供分页懒加载能力。当集合数据量超过阈值时，不一次性加载所有元素，而是按需加载，降低内存占用和初始加载时间。

## 核心机制

### 运行时决策

懒加载在反序列化时根据实际数据量动态决定：

- 元素数量超过阈值 → 创建懒加载代理
- 元素数量未超过阈值 → 使用原始集合类型

### 透明代理

懒加载代理和非懒加载集合都继承自对应的 Base 抽象类（如 `ObservableListBase<T>`），对外暴露相同的接口和行为，用户代码只需依赖 Base 类，无需关心具体实现。

### 分页大小优先级

1. 属性标记：`[LazyLoadCollection(PageSize = 100)]`
2. 动态调整：根据设备内存、集合元素大小等因素计算
3. 全局固定值：默认值（如 50）

## Attribute 与枚举定义

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class LazyLoadCollectionAttribute : Attribute
{
    public int PageSize { get; set; } = 0;  // 0 表示使用动态调整或全局默认
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class LazyLoadDictionaryAttribute : Attribute
{
    public DictionaryLoadMode LoadMode { get; set; } = DictionaryLoadMode.Full;
}

public enum DictionaryLoadMode
{
    Full,   // 访问时全加载
    Item    // 单条数据懒加载
}

public enum CollectionCacheStrategy
{
    LRU,        // 保留最近访问的 N 页
    FullCache   // 一旦加载就保持在内存中
}

// 集合类（List/Array）配置
public struct LazyLoadCollectionConfig
{
    public int PageSize { get; set; }
    public CollectionCacheStrategy CacheStrategy { get; set; }
    public int MaxCachedPages { get; set; }  // LRU 模式下生效

    public static LazyLoadCollectionConfig Default => new()
    {
        PageSize = 50,
        CacheStrategy = CollectionCacheStrategy.LRU,
        MaxCachedPages = 5
    };
}

// 字典类配置
public struct LazyLoadDictionaryConfig
{
    public DictionaryLoadMode LoadMode { get; set; }

    public static LazyLoadDictionaryConfig Default => new()
    {
        LoadMode = DictionaryLoadMode.Full
    };
}
```

## ISavable 接口扩展

当前 `ISavable` 接口定义：

```csharp
public interface ISavable
{
    ISavable? SvParent { get; }
    string? SvId { get; set; }
    bool IsDirty { get; }
    bool IsSelfDirty { get; }

    void SetParent(ISavable? parent);
    void SetDirty(bool dirty, bool recursively);

    void Serialize(Serializer serializer, SerializeContext ctx);
    void AfterDeserialize(Serializer serializer, SerializeContext ctx);
}
```

扩展懒加载配置方法：

```csharp
public interface ISavable
{
    // 现有成员...

    // 集合类（List/Array）懒加载配置
    void SetCollectionLazyLoadConfig(string propertyName, LazyLoadCollectionConfig config);
    LazyLoadCollectionConfig GetCollectionLazyLoadConfig(string propertyName);

    // 字典类懒加载配置
    void SetDictionaryLazyLoadConfig(string propertyName, LazyLoadDictionaryConfig config);
    LazyLoadDictionaryConfig GetDictionaryLazyLoadConfig(string propertyName);
}
```

## 缓存策略（集合类）

### 两种缓存模式

- **LRU 缓存**：保留最近访问的 N 页，当缓存满时淘汰最久未使用的页。适合浏览场景。
- **全量缓存**：一旦加载就保持在内存中。适合频繁随机访问场景。

### 运行时切换 API

```csharp
// 使用示例
gameData.SetCollectionLazyLoadConfig("Items", new LazyLoadCollectionConfig
{
    PageSize = 100,
    CacheStrategy = CollectionCacheStrategy.LRU,
    MaxCachedPages = 5
});

gameData.SetDictionaryLazyLoadConfig("Configs", new LazyLoadDictionaryConfig
{
    LoadMode = DictionaryLoadMode.Full
});
```

### 实现方式

- Savable 内部维护两个字典存储各属性的配置
- 懒加载代理在执行缓存操作时，通过 `SvParent` 获取配置
- 代理首次访问时读取默认配置（优先级：属性标记 > 动态调整 > 全局默认）

## 内存标记机制

### 数据结构

```csharp
private Dictionary<int, T> _pendingWrites = new();  // 待写入的修改
private HashSet<int> _pendingDeletes = new();       // 待删除的索引
```

### 操作示例

```csharp
list[401] = newItem;      // 第 5 页未加载 → _pendingWrites[401] = newItem
list[401] = newerItem;    // 再次修改 → _pendingWrites[401] = newerItem（覆盖）
var x = list[401];        // 读取 → 返回 _pendingWrites[401]
list.RemoveAt(500);       // 删除 → _pendingDeletes.Add(500)
```

### 加载时合并

当某页被加载到内存时：

1. 从存储读取该页所有元素
2. 应用该页范围内的 `_pendingWrites` 到内存数据
3. 移除 `_pendingDeletes` 中该页范围内的索引
4. 清除已处理的标记

## 保存机制

### 保存流程

1. 遍历 `_pendingWrites`，将所有待写入修改提交到存储
2. 遍历 `_pendingDeletes`，从存储删除对应元素
3. 清除所有标记
4. 保持已加载的内存数据不变

### 脏标记

- 任何写入 `_pendingWrites` 或 `_pendingDeletes` 的操作都设置 `_isDirty = true`
- 保存成功后通过 `SetDirty(false, false)` 清除脏标记
- 切换缓存策略导致页面卸载时，如有未提交标记，先保存再卸载

## 类继承关系

### 重构方案

需要创建新的 Base 抽象类作为透明代理的统一接口：

```
ObservableListBase<T>        (新增，abstract class)
       ↑                     - 对用户暴露的统一接口
       │                     - 实现 IList<T?>, ISavable, ICollectionWrapper
       │
  ┌────┴────┐
  │         │
ObservableList<T>      LazyLoadListProxy<T>
- 完整 List 实现        - 分页懒加载实现
```

```
ObservableArrayBase<T>       (新增，abstract class)
       ↑
       │
  ┌────┴────┐
  │         │
ObservableArray<T>    LazyLoadArrayProxy<T>
```

```
ObservableDictionaryBase<TKey, TValue>  (新增，abstract class)
       ↑
       │
  ┌────┴────────┐
  │             │
ObservableDictionary<TKey, TValue>   LazyLoadDictionaryProxy<TKey, TValue>
```

### 用户视角

用户代码只需依赖 Base 类：

```csharp
// 用户定义属性时使用 Base 类型
public class GameData : ISavable
{
    // 用户可以使用具体类型
    public ObservableList<ItemData> Items { get; set; }

    // 或者使用 Base 类型接受任意实现
    public ObservableListBase<ItemData> GetItems() => Items;
}

// 反序列化时，Serializer 根据数据量决定返回哪种实现
ObservableListBase<ItemData> items = serializer.ReadList<ItemData>(ctx);
// items 可能是 ObservableList<T> 或 LazyLoadListProxy<T>，用户无需关心
```

### Base 类职责

**ObservableListBase<T>**：
- 实现 `ISavable` 接口
- 实现 `IList<T?>`, `IReadOnlyList<T?>`, `IList` 接口
- 实现 `ICollectionWrapper<List<T?>>` 接口
- 包含 `CollectionChanged` 和 `PropertyChanged` 事件
- 提供懒加载配置方法
- 抽象方法：`OnChildChanged`、`Serialize`、`Deserialize`

**ObservableArrayBase<T>**：
- 与 List 类似，但 `Add`/`Remove`/`Insert` 抛出 `NotSupportedException`
- 实现 `ICollectionWrapper<T?[]>` 接口

**ObservableDictionaryBase<TKey, TValue>**：
- 实现 `IDictionary<TKey, TValue?>`, `IReadOnlyDictionary<TKey, TValue?>` 接口
- 实现 `ICollectionWrapper<Dictionary<TKey, TValue?>>` 接口

### 实现策略（策略 B）

Base 类继承 CRTP，统一继承链：

```
ObservableCollection (abstract, non-generic)
       ↑
       │
ObservableCollection<TCol, TElem> (abstract, generic, CRTP pattern)
       ↑
       │
ObservableListBase<T> (abstract, public)
       ↑                     - 继承 CRTP，实现序列化等基础功能
       │                     - 提供 IList<T?> 抽象成员（索引器、Count、Add 等）
       │
  ┌────┴────┐
  │         │
ObservableList<T>      LazyLoadListProxy<T>
- 实现完整 IList       - 实现分页懒加载索引器
  CRUD 操作              和内存标记机制
```

### 职责划分

**ObservableListBase<T>**（继承 `ObservableCollection<ObservableListBase<T>, T>`）：

| 职责 | 说明 |
|------|------|
| ISavable 实现 | 继承自 CRTP 基类 |
| 事件机制 | `CollectionChanged`、`PropertyChanged` 继承自 CRTP 基类 |
| 子元素监听 | `TryWatch`、`TryUnWatch`、`OnChildDeserialized` 继承自 CRTP 基类 |
| 懒加载配置 | `SetCollectionLazyLoadConfig`、`GetCollectionLazyLoadConfig` |
| 序列化 | 提供 `Serialize` 和 `Deserialize` 的基础实现（可 override） |
| 抽象成员 | `this[int index]`、`Count`、`Add`、`Remove` 等 IList 成员 |

**ObservableList<T>**（继承 `ObservableListBase<T>`）：

| 职责 | 说明 |
|------|------|
| 数据存储 | 内部 `List<T?>` 字段 |
| 索引器 | 直接访问内部 List |
| CRUD 操作 | Add、Remove、Insert、Clear 等完整实现 |
| SwapSource | 支持切换内部数据源 |
| ICollectionWrapper | `RetrieveSource`、`SwapSource` 实现 |

**LazyLoadListProxy<T>**（继承 `ObservableListBase<T>`）：

| 职责 | 说明 |
|------|------|
| 分页缓存 | `Dictionary<int, T?[]> _pages` |
| 内存标记 | `_pendingWrites`、`_pendingDeletes` |
| LRU 缓存 | `_lruList`、`_lruNodes` |
| 索引器 | 按需加载页面，合并内存标记 |
| Count | 返回固定总数 |
| CRUD 操作 | 大部分抛出 NotSupportedException（固定大小） |
| 序列化 | Override 基类方法，实现分页保存 |

## ObservableDictionary 特殊处理

### 两种懒加载粒度

| 粒度 | LoadMode | 说明 |
|------|----------|------|
| 访问时全加载 | `Full` | Dictionary 整体作为懒加载单元，首次访问时一次性加载所有 |
| 单条数据懒加载 | `Item` | 每个 Key-Value 对独立，访问某 Key 时只加载该 Value |

### 存储结构

| LoadMode | 存储方式 |
|----------|----------|
| Full | 整体序列化为一个文件 |
| Item | 每个 Key 独立一个文件，使用 `ISvIdConverter<TKey>` 转换 Key 为存储 ID |

## 文件结构

### 新增文件

```
Salvavida/Package/Runtime/
├── LazyLoad/
│   ├── LazyLoadCollectionAttribute.cs
│   ├── LazyLoadDictionaryAttribute.cs
│   ├── LazyLoadCollectionConfig.cs
│   ├── LazyLoadDictionaryConfig.cs
│   ├── DictionaryLoadMode.cs
│   ├── CollectionCacheStrategy.cs
│   ├── ObservableListBase.cs
│   ├── ObservableArrayBase.cs
│   ├── ObservableDictionaryBase.cs
│   ├── LazyLoadListProxy.cs
│   ├── LazyLoadArrayProxy.cs
│   └── LazyLoadDictionaryProxy.cs
```

### 修改现有文件

| 文件 | 修改内容 |
|------|----------|
| `ISavable.cs` | 新增集合/字典懒加载配置方法 |
| `ObservableList.cs` | 继承 `ObservableListBase<T>` 或实现其抽象方法 |
| `ObservableArray.cs` | 继承 `ObservableArrayBase<T>` 或实现其抽象方法 |
| `ObservableDictionary.cs` | 继承 `ObservableDictionaryBase<TKey, TValue>` 或实现其抽象方法 |
| `Serializer.cs` | 新增懒加载代理创建逻辑，元数据读写支持 |

## 使用示例

```csharp
public class GameData : ISavable
{
    [LazyLoadCollection(PageSize = 100)]
    public ObservableList<ItemData> Items { get; set; }

    [LazyLoadDictionary(LoadMode = DictionaryLoadMode.Full)]
    public ObservableDictionary<string, Config> Configs { get; set; }

    [LazyLoadDictionary(LoadMode = DictionaryLoadMode.Item)]
    public ObservableDictionary<string, BigData> Cache { get; set; }
}

// 运行时切换配置
gameData.SetCollectionLazyLoadConfig("Items", new LazyLoadCollectionConfig
{
    PageSize = 100,
    CacheStrategy = CollectionCacheStrategy.LRU,
    MaxCachedPages = 5
});

gameData.SetDictionaryLazyLoadConfig("Cache", new LazyLoadDictionaryConfig
{
    LoadMode = DictionaryLoadMode.Item
});

// 用户代码使用 Base 类型
public void ProcessItems(ObservableListBase<ItemData> items)
{
    // 无论 items 是 ObservableList 还是 LazyLoadListProxy，代码都一样
    for (int i = 0; i < items.Count; i++)
    {
        var item = items[i];
        // ...
    }
}
```
