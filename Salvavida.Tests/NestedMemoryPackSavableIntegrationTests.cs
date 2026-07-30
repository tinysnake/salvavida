using Xunit;

namespace Salvavida.Tests;

public class NestedMemoryPackSavableIntegrationTests
{
    [Fact]
    public void InlineMemoryPackChild_RestoresItsSeparatelySavedValue()
    {
        var serializer = new MemoryPackInMemorySerializer();
        var root = new NestedMemoryPackRoot { SvId = "root" };
        var child = new NestedMemoryPackChild();
        child.SetSeparateValue("survives-reload");
        root.SetChild(child);

        using (serializer.BeginFreshAction(out var context))
        {
            context.Path.Push(root.SvId, PathBuilder.Type.Property);
            root.Serialize(serializer, context);
        }

        var loadedRoot = Assert.IsType<NestedMemoryPackRoot>(serializer.FreshRead<NestedMemoryPackRoot>("root"));
        var loadedChild = Assert.IsType<NestedMemoryPackChild>(loadedRoot.Child);
        Assert.Equal("survives-reload", loadedChild.SeparateValue);
    }

    [Fact]
    public void InlineChildUnderSeparatelySavedDictionary_DoesNotBecomeADictionaryKey()
    {
        var serializer = new InMemorySerializer();
        var root = new NestedCollectionRoot { SvId = "root" };
        var child = new NestedCollectionChild();
        child.SetSeparateValue("nested-value");
        var entry = new NestedCollectionEntry();
        entry.SetChild(child);
        root.SetEntries(new Dictionary<int, NestedCollectionEntry> { [7] = entry });

        using (serializer.BeginFreshAction(out var context))
        {
            context.Path.Push(root.SvId, PathBuilder.Type.Property);
            root.Serialize(serializer, context);
        }

        var loadedRoot = Assert.IsType<NestedCollectionRoot>(serializer.FreshRead<NestedCollectionRoot>("root"));
        var loadedEntry = Assert.IsType<NestedCollectionEntry>(loadedRoot.Entries![7]);
        var loadedChild = Assert.IsType<NestedCollectionChild>(loadedEntry.Child);
        Assert.Equal("nested-value", loadedChild.SeparateValue);
    }
}
