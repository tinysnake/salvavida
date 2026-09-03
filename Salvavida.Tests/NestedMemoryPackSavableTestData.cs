using MemoryPack;
using Salvavida;

namespace Salvavida.Tests;

[Savable]
[MemoryPackable]
public partial class NestedMemoryPackRoot
{
    [MemoryPackInclude]
    private NestedMemoryPackChild? _child;

    public void SetChild(NestedMemoryPackChild child) => Child = child;
}

[Savable]
[MemoryPackable]
public partial class NestedMemoryPackChild
{
    [SaveSeparately]
    [MemoryPackIgnore]
    private string? _separateValue;

    public void SetSeparateValue(string value) => SeparateValue = value;
}

[Savable]
[UseSystemTextJson]
public partial class NestedCollectionRoot
{
    [SaveSeparately]
    [System.Text.Json.Serialization.JsonIgnore]
    private Dictionary<int, NestedCollectionEntry>? _entries;
}

[Savable]
[UseSystemTextJson]
public partial class NestedCollectionEntry
{
    // This field is serialized inline with the dictionary element.
    public NestedCollectionChild? child;

    public void SetChild(NestedCollectionChild value) => Child = value;
}

[Savable]
[UseSystemTextJson]
public partial class NestedCollectionChild
{
    [SaveSeparately]
    [System.Text.Json.Serialization.JsonIgnore]
    private string? _separateValue;

    public void SetSeparateValue(string value) => SeparateValue = value;
}

[Savable]
[MemoryPackable]
public partial class InlineCollectionMemoryPackRoot
{
    [MemoryPackInclude]
    private List<string>? _items;
}

