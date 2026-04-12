# ObservableListSavableLazy 设计文档

## 概述

`ObservableListSavableLazy` 是一个延迟加载的可观察列表，用于存储 `ISavable` 元素。与 `ObservableArraySavableLazy` 不同，它支持动态插入和删除操作，并使用 LexoRank 生成排序 ID。

## 关键特性

1. **Lazy Loading**: 元素按需加载，支持 LoadAll 和 LoadIndividual 两种模式
2. **LexoRank ID**: ID 根据前后元素动态生成，保证正确排序
3. **立即同步**: Insert/Delete 操作立即同步到 Serializer
4. **线程安全**: 使用 `ReaderWriterLockSlim` 保证线程安全

## 数据结构

```csharp
public sealed class ObservableListSavableLazy<T> : ObservableListSavableBase<ObservableListSavableLazy<T>, T>
    where T : ISavable
{
    private List<Slot>? _slots;       // 可能有 null gap
    private readonly CollectionOptions _options;
    private readonly ReaderWriterLockSlim _lock = new();
    private int _count;               // 元素总数 (来自 meta)
    private int _loadedCount;         // 已加载数量
    private bool _needsRebalance;     // 是否需要重新排序 ID

    private const int DEFAULT_PRECISION_DIGITS = 2;
    private const int REBALANCE_LENGTH_THRESHOLD = 10;
}
```

## Lazy Loading 算法

### Deserialize

只读取元数据，不初始化 _slots：

```csharp
public override void Deserialize(Serializer serializer, SerializeContext ctx)
{
    var meta = serializer.Read<CollectionMetadata?>(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
    _count = meta?.Count ?? 0;
    _slots = null;
    _loadedCount = 0;
    _isDirty = false;
}
```

### EnsureSlotsInitialized

首次访问时初始化 _slots，加载所有 ID（不带数据）：

```csharp
private void EnsureSlotsInitialized()
{
    if (_slots != null) return;
    var serializer = GetSerializer();
    using var locker = serializer.BeginFreshAction(this, out var ctx);
    _slots = new List<Slot>(_count);
    foreach (var id in serializer.ListCollectionIds(ctx))
        _slots.Add(new Slot(id, default, false, false));
}
```

### 按需加载 (LoadSlotByIndex)

访问 `[index]` 时，找到目标 ID 并加载：

```
算法:
1. 如果 _slots[index].IsLoaded → 直接返回
2. 找到最近已加载的前一个元素 (prevIndex, prevId)
3. 调用 ListCollectionIdsMinMax(ctx, prevId, null)
4. skip = targetIndex - prevIndex - 1
5. 取第 skip 个 ID 作为目标 ID
6. 加载该元素
```

## Insert/Delete 立即同步

### Insert

```csharp
public override void Insert(int index, T? item)
{
    _lock.EnterWriteLock();
    try
    {
        // 1. 生成 LexoRank ID
        // 2. 添加到内存
        // 3. 立即同步到 Serializer
        // 4. 更新 meta
        // 5. 触发事件
    }
    finally { _lock.ExitWriteLock(); }
}
```

### RemoveAt

```csharp
public override void RemoveAt(int index)
{
    _lock.EnterWriteLock();
    try
    {
        // 1. 立即从 Serializer 删除
        // 2. 从内存移除
        // 3. 更新 meta
        // 4. 触发事件
    }
    finally { _lock.ExitWriteLock(); }
}
```

## Serialize 与 Rebalance

### Rebalance 触发条件

当插入生成的 LexoRank 超过长度阈值时标记 `_needsRebalance = true`。

### Serialize 流程

1. 如果 `_needsRebalance`，重新分配所有 ID
2. 否则，只同步 dirty 元素
3. 保存 meta

## 公开 API

```csharp
public int LoadedCount { get; }
public bool IsLoaded(int index);
public void LoadAll();
```

## 与 ObservableArraySavableLazy 的区别

| 特性 | ArrayLazy | ListLazy |
|------|-----------|----------|
| ID 生成 | GetPaddedIndex | LexoRank |
| 动态插入/删除 | 不支持 | 支持 |
| 同步策略 | 延迟同步 | 立即同步 |
| Null 处理 | 无特殊处理 | 保存 null 到 ID 位置 |
