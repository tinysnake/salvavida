# Salvavida 序列化框架分析

## 项目概述

Salvavida 是一个 Unity 序列化库，使用 Roslyn 源生成器进行代码生成，支持路径属性遍历和可观察集合。框架设计为纯同步操作，所有序列化操作都是同步执行的。

## 项目结构

```
salvavida/
├── Salvavida/Package/Runtime/
│   ├── Serializer.cs              # 核心序列化引擎（抽象类）
│   ├── ISavable.cs                # 可序列化对象接口
│   ├── ISerializeRoot.cs          # 根对象接口
│   ├── ISalvavida.cs              # 高层 API 接口
│   ├── SerializeContext.cs        # 序列化上下文
│   ├── PathBuilder.cs             # 路径构建器
│   ├── SvHelper.cs                # 辅助工具类
│   ├── IIdGenerator.cs            # ID 生成器接口
│   ├── IObjectPool.cs             # 对象池接口
│   ├── ISvPropertyChanged.cs      # 属性变更事件
│   ├── ISvCollectionChanged.cs    # 集合变更事件
│   ├── ICollectionWrapper.cs      # 集合包装器接口
│   ├── SalvavidaSerializeException.cs
│   ├── ObservableArray.cs         # 可观察数组
│   ├── ObservableList.cs          # 可观察列表
│   ├── ObservableDictionary.cs    # 可观察字典
│   ├── ObservableCollection.cs    # 可观察集合基类
│   ├── Attributes/
│   │   ├── SavableAttribute.cs
│   │   ├── SaveSeparatelyAttribute.cs
│   │   ├── PropertyNameAttribute.cs
│   │   └── IgnoreAttribute.cs
│   ├── IdConverters/
│   │   ├── SvIdConverter.cs       # ID 转换器接口和注册表
│   │   └── SvIdConverters.cs      # 内置转换器
│   └── Impl/
│       ├── Salvavida.cs           # 高层 API 实现
│       ├── DefaultIdGenerator.cs
│       └── DefaultObjectPool.cs
├── Salvavida.Generator/           # Roslyn 源生成器
│   ├── SalvavidaGenerator.cs
│   ├── ICodeGenerator.cs
│   ├── BasicCodeGenerator.cs      # 默认生成器
│   ├── MemoryPackCodeGenerator.cs # MemoryPack 支持
│   ├── UnityJsonCodeGenerator.cs  # Unity Json 支持
│   ├── CodeGenHelper.cs
│   ├── CodeGenInfoStore.cs
│   ├── ScriptBuilder.cs
│   └── CollectionType.cs
└── Salvavida.Generator.Debug/     # 调试项目
```

## 核心组件

### 1. ISavable 接口

所有可序列化对象的标记接口，定义了核心属性和生命周期方法：

```csharp
public interface ISavable
{
    ISavable? SvParent { get; }       // 父对象引用
    string? SvId { get; set; }        // 唯一标识符
    bool IsDirty { get; }             // 脏标记（包含子对象）
    bool IsSelfDirty { get; }         // 自身脏标记（不包含子对象）

    void SetParent(ISavable? parent);
    void SetDirty(bool dirty, bool recursively);

    // 生命周期回调
    void Serialize(Serializer serializer, SerializeContext ctx);
    void AfterDeserialize(Serializer serializer, SerializeContext ctx);
}

// 泛型版本，结合属性变更通知
public interface ISavable<T> : ISavable, ISvPropertyChanged<T>
{
}
```

**关键属性说明：**

| 属性 | 说明 |
|------|------|
| `IsDirty` | 递归脏标记，包含自身和所有子对象的脏状态 |
| `IsSelfDirty` | 仅自身的脏标记，用于精确追踪变更 |

### 2. ISerializeRoot 接口

标记根对象，持有 Serializer 引用：

```csharp
public interface ISerializeRoot
{
    Serializer? Serializer { get; }
    void SetSerializer(Serializer? serializer);
    void Save();
}
```

### 3. ISalvavida 接口（高层 API）

提供简单的保存/加载接口：

```csharp
public interface ISalvavida : IDisposable
{
    string Id { get; }
    void Save();
    void Load();
    Serializer Serializer { get; }
}

public interface ISalvavida<T> : ISalvavida where T : ISavable, ISerializeRoot
{
    T? Data { get; }
}
```

### 4. Serializer 核心类

`Serializer` 是核心抽象类，负责协调所有序列化操作。

#### 关键属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IdGenerator` | `IIdGenerator` | ID 生成器 |

#### 注意

框架现在完全是同步的，不提供异步 API。所有 `FreshSaveAsync`、`FreshReadAsync` 等方法已被移除。

---

## 序列化流程（Save）

### 入口方法

```
┌─────────────────────────────────────────────────────────────────┐
│                      序列化入口方法                              │
├─────────────────────────────────────────────────────────────────┤
│ FreshSave<T>(T data)                            → 独立保存      │
│ FreshSave<T>(parent, data, propName, pathType)  → 保存子属性    │
│ Save<T>(savable, ctx, type)                     → 在上下文中保存│
│ Save<T>(data, ctx, propName, pathType)          → 保存子属性    │
│ SaveNoPushPath<T>(obj, ctx)                     → 不修改路径保存│
└─────────────────────────────────────────────────────────────────┘
```

### FreshSave 流程

```
FreshSave(data)
       │
       ▼
┌─────────────────────────┐
│ BeginFreshAction(out ctx)│  ← 获取上下文
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ data.GetSavePathAsSpan  │  ← 构建保存路径
│     (ctx.Path)          │
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ TryThrowOnSvIdEmpty     │  ← 验证 SvId
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ DoSaveObject(data, ctx) │  ← 执行保存
└───────────┬─────────────┘
            │
            ▼
   [using 语句释放 locker]   ← 自动清理上下文
```

### DoSaveObject 核心逻辑

```
DoSaveObject(obj, ctx)
       │
       ▼
┌─────────────────────────┐
│ obj.Serialize(this, ctx)│  ← 调用 ISavable.Serialize
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ obj.SetDirty(false)     │  ← 清除脏标记
└─────────────────────────┘
```

---

## 反序列化流程（Read）

### 入口方法

```
┌─────────────────────────────────────────────────────────────────┐
│                     反序列化入口方法                             │
├─────────────────────────────────────────────────────────────────┤
│ FreshRead<T>(svid)              → 独立读取                       │
│ Read<T>(ctx, propName, type)    → 在上下文中读取                 │
│ ReadNoPushPath<T>(ctx)          → 不修改路径读取                 │
└─────────────────────────────────────────────────────────────────┘
```

### FreshRead 流程

```
FreshRead<T>(svid)
       │
       ▼
┌─────────────────────────────────┐
│ BeginFreshAction(out ctx)       │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ ctx.Path.UsePush(svid, Property)│  ← 压入路径段
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ DoRead<T>(ctx)                  │  ← 执行读取
└───────────────┬─────────────────┘
                │
                ▼
         [返回结果]
```

### DoRead 核心逻辑

```
DoRead<T>(ctx)
       │
       ▼
┌─────────────────────────────────┐
│ DoReadImpl<T>(ctx)              │  ← 抽象方法（子类实现）
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ 设置 ISavable 属性:              │
│ - result.SvId = 路径最后一段     │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ result.AfterDeserialize(...)    │  ← ISavable 回调
│ result.SetDirty(false, false)   │  ← 清除脏标记
└───────────────┬─────────────────┘
                │
                ▼
         [返回结果]
```

---

## 辅助操作

### 删除操作

```csharp
// 删除单个对象
FreshDelete<T>(T data)
Delete<T>(T data, SerializeContext ctx, PathBuilder.Type type)
Delete(ctx, propName, pathType)
DeleteNoPushPath(ctx)

// 删除对象及其所有子项
FreshDeleteAll<T>(T savable)
DeleteAll<T>(T savable, SerializeContext ctx, PathBuilder.Type type)
DeleteAll(ctx, propName, pathType)
DeleteAllNoPushPath(ctx)
```

### 存在性检查

```csharp
FreshHas<T>(T data)           // 检查对象是否存在
Has<T>(T data, ctx)           // 在上下文中检查
Has(ctx, propName, pathType)  // 检查指定路径是否存在
HasNoPushPath(ctx)            // 不修改路径检查
```

### 集合加载辅助

```csharp
// 加载 ISavable 元素的集合
LoadCollectionSavable<T>(serializer, ctx, propName, pathType, saveSeparately)
// 返回 ObservableArraySavable<T> 或 ObservableListSavable<T> 或 ObservableDictionarySavable<TKey, TValue>

// 加载普通元素的集合
LoadCollection<T>(serializer, ctx, propName, pathType, saveSeparately)
// 返回 ObservableArray<T> 或 ObservableList<T> 或 ObservableDictionary<TKey, TValue>
```

---

## 上下文管理

### FreshActionLocker 结构

用于获取和释放上下文：

```csharp
public readonly struct FreshActionLocker : IDisposable
{
    // 构造时：获取 SerializeContext
    // Dispose 时：归还上下文
}
```

### 使用方式

```csharp
using var locker = BeginFreshAction(out var ctx);
// 使用 ctx 进行操作
// 自动清理上下文
```

---

## 路径构建（PathBuilder）

### 路径结构

```
示例路径: "user.profile.settings"

路径段类型:
- Property: 用 '.' 分隔
- Collection: 用 '/' 分隔

示例: "users/john.items/sword"
→ users (Collection)
→ john (Collection item)
→ items (Property)
→ sword (Property)
```

### PathBuilder 类

```csharp
public class PathBuilder : IEqualityComparer<PathBuilder>
{
    public enum Type { Property, Collection }

    public struct PushScope : IDisposable { ... }  // 用于 using 语句

    public static int defaultMaxLength = 1024;
    public static char propertySaperator = '.';

    public bool IsEmpty { get; }
    public int SegmentCount { get; }
    public int MaxLength { get; }

    // 路径操作
    public PushScope UsePush(ReadOnlySpan<char> segment, Type type);  // 推荐
    public PathBuilder Push(ReadOnlySpan<char> segment, Type type);
    public PathBuilder Pop();
    public ReadOnlySpan<char> PopAsSpan();
    public void Clear();

    // 路径获取
    public ReadOnlySpan<char> AsSpan();
    public override string ToString();
    public ReadOnlySpan<char> GetSegmentSpan(Index segmentIndex);
    public string GetSegmentString(Index segmentIndex);

    // 复制
    public void CopyTo(PathBuilder target);
}
```

### 路径操作示例

```csharp
// 使用 using 自动弹栈（推荐）
using var __s = ctx.Path.UsePush("propertyName", PathBuilder.Type.Property);

// 手动操作
ctx.Path.Push("segment", type);  // 压入
ctx.Path.Pop();                   // 弹出

// 获取信息
var span = ctx.Path.AsSpan();                    // 完整路径
var segment = ctx.Path.GetSegmentSpan(^1);       // 最后一段
```

---

## 集合类型支持

### 可观察集合类型

```
ObservableCollection (抽象基类)
├── ObservableCollection<TCol, TElem> (泛型基类)
│   ├── ObservableArray<T>              → 可观察数组
│   ├── ObservableList<T>               → 可观察列表
│   └── ObservableDictionary<TKey, TValue> → 可观察字典
│
└── ObservableCollectionSavable<TCol, TElem> (ISavable 元素基类)
    ├── ObservableArraySavable<T>       → ISavable 元素数组
    ├── ObservableListSavable<T>        → ISavable 元素列表
    └── ObservableDictionarySavable<TKey, TValue> → ISavable 元素字典
```

### ObservableCollection 基类

```csharp
public abstract class ObservableCollection : ISavable
{
    public ISavable? SvParent { get; protected set; }
    public virtual bool IsDirty { get; }
    public bool IsSelfDirty { get; }
    public string? SvId { get; set; }
    public bool SaveSeparately { get; protected set; }

    public abstract void Serialize(Serializer serializer, SerializeContext ctx);
    public abstract void Deserialize(Serializer serializer, SerializeContext ctx);
    public virtual void SetDirty(bool dirty, bool recursive);

    // 生命周期钩子
    public virtual void BeforeSerialize(Serializer serializer, SerializeContext ctx);
    public virtual void AfterSerialize(Serializer serializer, SerializeContext ctx);
    public virtual void AfterDeserialize(Serializer serializer, SerializeContext ctx);
}
```

### SaveSeparately 模式

当 `SaveSeparately = true` 时，集合中的每个 `ISavable` 元素会作为独立文件存储：

```
集合路径: "users"
元素路径: "users/user_001", "users/user_002", ...

元数据文件: "users/__ob_metadata__"
存储元素 ID 和顺序信息
```

### 集合变更事件

```csharp
public enum CollectionChangedAction { Add, Replace, Remove, Reset }

public readonly ref struct CollectionChangeInfo<TCol, T>
{
    public TCol SourceCollection { get; }
    public CollectionChangedAction Action { get; }
    public bool IsSingleItem { get; }
    public T? NewItem { get; }
    public T? OldItem { get; }
    public IList<T>? NewItems { get; }
    public IList<T>? OldItems { get; }
    public int NewStartingIndex { get; }
    public int OldStartingIndex { get; }
}

public delegate void CollectionChanged<TCol, TElem>(CollectionChangeInfo<TCol, TElem?> changeInfo);
```

---

## ID 转换器系统

### ISvIdConverter 接口

用于将字典键转换为字符串路径：

```csharp
public interface ISvIdConverter<T>
{
    string ConvertTo(T value);
    T ConvertFrom(string str);
}
```

### 注册和使用

```csharp
// 注册自定义转换器
SvIdConverter.RegisterConverter(new MyKeyConverter());

// 获取转换器
var converter = SvIdConverter.GetConverter<int>();

// 内置转换器
SvIdConverterString  // string ↔ string
SvIdConverterInt     // int ↔ string
```

---

## 辅助工具类（SvHelper）

```csharp
public static class SvHelper
{
    public const string PROPNAME_COLLECTION_METADATA = "__ob_metadata__";

    public static bool CheckIsSavable<T>();
    public static void ChildDeserialized<T>(this ISavable parent, T child);
    public static void SetChild<T>(this ISavable parent, T child);
    public static ReadOnlySpan<char> GetParentPathAsSpan(this ISavable? savable, PathBuilder pathBuilder);
    public static ReadOnlySpan<char> GetSavePathAsSpan(this ISavable? savable, PathBuilder pathBuilder);
    public static Serializer? GetSerializer(this ISavable? savable);
    public static void TrySerialize<T>(this T savable, Serializer serializer, SerializeContext ctx, ...);
    public static void TryThrowOnSvIdEmpty<T>(T? sv);
    public static int GetHashCodeFromSpan<T>(ReadOnlySpan<T> span);
}
```

---

## 生命周期钩子

### ISavable 生命周期

```
序列化 (Save):
  Serialize → SetDirty(false)

反序列化 (Read):
  DoReadImpl → 设置 SvId → AfterDeserialize → SetDirty(false)
```

### ObservableCollection 生命周期

```
序列化:
  BeforeSerialize → Serialize → AfterSerialize → SetDirty(false)

反序列化:
  Deserialize → AfterDeserialize → SetDirty(false)
```

---

## 抽象方法（子类需实现）

| 方法 | 说明 |
|------|------|
| `DoSaveObjectImpl<T>(T obj, SerializeContext ctx)` | 保存对象的实际实现 |
| `DoSaveObjectImpl<T>(T obj, Type type, SerializeContext ctx)` | 带类型的保存实现 |
| `DoReadImpl<T>(SerializeContext ctx)` | 读取对象的实际实现 |
| `DoDelete(SerializeContext ctx)` | 删除当前路径对象 |
| `DoDeleteAll(SerializeContext ctx)` | 删除当前路径及所有子路径 |
| `DoHas(SerializeContext ctx)` | 检查路径是否存在 |

---

## 属性标记

### SavableAttribute

```csharp
[Savable(SerializeWithOrder = true, IsRootObject = true)]
public partial class MyClass : ISavable
{
    // SerializeWithOrder: 序列化时记录元素顺序
    // IsRootObject: 标记为根对象，生成 ISerializeRoot 实现
}
```

### 其他属性

```csharp
[SaveSeparately]           // 标记集合元素独立存储
[PropertyName("name")]     // 指定序列化属性名
[Ignore]                   // 忽略该属性
```

---

## 源生成器

框架提供三种代码生成器：

### 1. BasicCodeGenerator（默认）

为标记 `[Savable]` 的类生成 `ISavable` 实现。

### 2. MemoryPackCodeGenerator

为同时标记 `[MemoryPackable]` 和 `[Savable]` 的类生成代码，集成 MemoryPack 序列化。

### 3. UnityJsonCodeGenerator

为同时标记 `[Serializable]` 和 `[Savable]` 的类生成代码，使用 Unity JsonUtility。

### 生成内容

- `ISavable<T>` 实现
- `ISerializeRoot` 实现（如果 `IsRootObject = true`）
- 属性包装器，自动追踪脏状态
- 集合属性包装器（`ObservableArray`、`ObservableList`、`ObservableDictionary`）
- 子对象监听/取消监听
- 序列化/反序列化方法

---

## 使用示例

### 创建数据模型

```csharp
[Savable(IsRootObject = true)]
public partial class GameData : ISavable
{
    // 源生成器自动生成 ISavable 实现
    public string PlayerName { get; set; }
    public int Level { get; set; }
}
```

### 使用 Serializer

```csharp
// 创建 Serializer 实例（具体实现由子类提供）
var serializer = new MySerializer();

// 保存
serializer.FreshSave(gameData);

// 读取
var data = serializer.FreshRead<GameData>("game_data");

// 检查是否存在
if (serializer.FreshHas(gameData))
{
    // ...
}

// 删除
serializer.FreshDelete(gameData);
```

### 使用 Salvavida 高层 API

```csharp
var salvavida = new Salvavida<GameData>("main", serializer);
salvavida.Load();
// 使用 salvavida.Data
salvavida.Save();
```

### 使用可观察集合

```csharp
[Savable]
public partial class PlayerData : ISavable
{
    // 源生成器会生成 ObservableList<PlayerItem> 类型的属性
    public ObservableList<PlayerItem> Items { get; set; }
}

// 监听集合变更
items.CollectionChanged += (info) =>
{
    if (info.Action == CollectionChangedAction.Add)
    {
        Console.WriteLine($"Added item at index {info.NewStartingIndex}");
    }
};
```

---

## 关键设计模式

1. **模板方法模式**：`DoSaveObject`/`DoRead` 定义骨架，子类实现 `DoSaveObjectImpl`/`DoReadImpl`
2. **观察者模式**：`ObservableCollection` 提供集合变更通知
3. **生成器模式**：Roslyn Source Generator 自动生成 `ISavable` 实现
4. **策略模式**：多种代码生成器可选（Basic、MemoryPack、UnityJson）

---

## 脏追踪机制

### 双层脏标记

```
IsSelfDirty     → 仅对象自身属性变更
IsDirty         → 自身或任意子对象变更
```

### 脏传播

```
子对象.SetDirty(true) → 递归向上通知父对象
                    → 更新父对象的 _svIsChildrenDirty 标记
```

### 清除脏标记

```
SetDirty(false, recursively: true)  → 清除自身及所有子对象
SetDirty(false, recursively: false) → 仅清除自身
```
