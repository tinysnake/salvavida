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

    [Fact]
    public void InlineCollection_ModifyingObservableList_MarksParentSelfDirtyAndPersistsChanges()
    {
        var serializer = new MemoryPackInMemorySerializer();
        var root = new InlineCollectionMemoryPackRoot { SvId = "root" };
        root.SetItems(["initial"]);

        using (serializer.BeginFreshAction(out var context))
        {
            context.Path.Push(root.SvId, PathBuilder.Type.Property);
            root.Serialize(serializer, context);
        }

        Assert.False(root.IsSelfDirty);
        Assert.False(root.IsDirty);

        root.Items.Add("added-item");

        Assert.True(root.IsSelfDirty);
        Assert.True(root.IsDirty);

        using (serializer.BeginFreshAction(out var context))
        {
            context.Path.Push(root.SvId, PathBuilder.Type.Property);
            root.Serialize(serializer, context);
        }

        var loadedRoot = Assert.IsType<InlineCollectionMemoryPackRoot>(serializer.FreshRead<InlineCollectionMemoryPackRoot>("root"));
        Assert.NotNull(loadedRoot.Items);
        Assert.Equal(new[] { "initial", "added-item" }, loadedRoot.Items);

        root.Items.Remove("initial");
        Assert.True(root.IsSelfDirty);

        using (serializer.BeginFreshAction(out var context))
        {
            context.Path.Push(root.SvId, PathBuilder.Type.Property);
            root.Serialize(serializer, context);
        }

        var reloadedRoot = Assert.IsType<InlineCollectionMemoryPackRoot>(serializer.FreshRead<InlineCollectionMemoryPackRoot>("root"));
        Assert.NotNull(reloadedRoot.Items);
        Assert.Equal(new[] { "added-item" }, reloadedRoot.Items);
    }
}
