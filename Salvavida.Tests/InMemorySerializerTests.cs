using System;
using Xunit;

namespace Salvavida.Tests
{
    public class InMemorySerializerTests
    {
        #region Basic Save/Read Tests

        [Fact]
        public void SaveAndRead_SimpleObject_Works()
        {
            var serializer = new InMemorySerializer();
            var testObj = new TestData { Name = "Test", Value = 42 };

            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("test", PathBuilder.Type.Property);
            serializer.SaveNoPushPath(testObj, ctx);

            Assert.True(serializer.ContainsPath("test"));
            Assert.Equal(1, serializer.Count);
        }

        [Fact]
        public void Read_NonExistentPath_ReturnsDefault()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("nonexistent", PathBuilder.Type.Property);
            var result = serializer.ReadNoPushPath<string>(ctx);

            Assert.Null(result);
        }

        [Fact]
        public void Has_ExistingPath_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("test", PathBuilder.Type.Property);
            serializer.SaveNoPushPath("value", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("test", PathBuilder.Type.Property);
            Assert.True(serializer.HasNoPushPath(ctx));
        }

        [Fact]
        public void Has_NonExistentPath_ReturnsFalse()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("nonexistent", PathBuilder.Type.Property);
            Assert.False(serializer.HasNoPushPath(ctx));
        }

        #endregion

        #region Delete Tests

        [Fact]
        public void Delete_ExistingPath_RemovesIt()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("test", PathBuilder.Type.Property);
            serializer.SaveNoPushPath("value", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("test", PathBuilder.Type.Property);
            serializer.DeleteNoPushPath(ctx);

            Assert.Equal(0, serializer.Count);
            Assert.False(serializer.ContainsPath("test"));
        }

        [Fact]
        public void DeleteAll_WithPrefix_RemovesAllMatching()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            // Save multiple items with common prefix
            ctx.Path.Push("parent", PathBuilder.Type.Property);
            ctx.Path.Push("child1", PathBuilder.Type.Property);
            serializer.SaveNoPushPath("value1", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("parent", PathBuilder.Type.Property);
            ctx.Path.Push("child2", PathBuilder.Type.Property);
            serializer.SaveNoPushPath("value2", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("other", PathBuilder.Type.Property);
            serializer.SaveNoPushPath("value3", ctx);

            Assert.Equal(3, serializer.Count);

            // Delete all under "parent"
            ctx.Path.Clear();
            ctx.Path.Push("parent", PathBuilder.Type.Property);
            serializer.DeleteAllNoPushPath(ctx);

            Assert.Equal(1, serializer.Count);
            Assert.False(serializer.ContainsPath("parent.child1"));
            Assert.False(serializer.ContainsPath("parent.child2"));
            Assert.True(serializer.ContainsPath("other"));
        }

        #endregion

        #region ListCollectionIds Tests

        [Fact]
        public void ListCollectionIds_ReturnsOrderedIds()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            // Save collection items with parent path
            ctx.Path.Push("user", PathBuilder.Type.Property);
            ctx.Path.Push("items", PathBuilder.Type.Property);
            ctx.Path.Push("item3", PathBuilder.Type.Collection);
            serializer.SaveNoPushPath("value3", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("user", PathBuilder.Type.Property);
            ctx.Path.Push("items", PathBuilder.Type.Property);
            ctx.Path.Push("item1", PathBuilder.Type.Collection);
            serializer.SaveNoPushPath("value1", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("user", PathBuilder.Type.Property);
            ctx.Path.Push("items", PathBuilder.Type.Property);
            ctx.Path.Push("item2", PathBuilder.Type.Collection);
            serializer.SaveNoPushPath("value2", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("user", PathBuilder.Type.Property);
            var ids = serializer.ListCollectionIds(ctx, "items");

            var idList = new System.Collections.Generic.List<string>(ids);
            Assert.Equal(3, idList.Count);
            Assert.Equal("item1", idList[0]);
            Assert.Equal("item2", idList[1]);
            Assert.Equal("item3", idList[2]);
        }

        [Fact]
        public void ListCollectionIds_LexoRank_ReturnsOrderedIds()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            // Save collection items with parent path
            ctx.Path.Push("user", PathBuilder.Type.Property);
            ctx.Path.Push("items", PathBuilder.Type.Property);
            ctx.Path.Push("0~V3", PathBuilder.Type.Collection);
            serializer.SaveNoPushPath("value3", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("user", PathBuilder.Type.Property);
            ctx.Path.Push("items", PathBuilder.Type.Property);
            ctx.Path.Push("0~V1", PathBuilder.Type.Collection);
            serializer.SaveNoPushPath("value1", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("user", PathBuilder.Type.Property);
            ctx.Path.Push("items", PathBuilder.Type.Property);
            ctx.Path.Push("1~V4", PathBuilder.Type.Collection);
            serializer.SaveNoPushPath("value4", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("user", PathBuilder.Type.Property);
            ctx.Path.Push("items", PathBuilder.Type.Property);
            ctx.Path.Push("0~V2", PathBuilder.Type.Collection);
            serializer.SaveNoPushPath("value2", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("user", PathBuilder.Type.Property);
            var ids = serializer.ListCollectionIds(ctx, "items", 0, 10);

            var idList = new System.Collections.Generic.List<string>(ids);
            Assert.Equal(4, idList.Count);
            Assert.Equal("0~V1", idList[0]);
            Assert.Equal("0~V2", idList[1]);
            Assert.Equal("0~V3", idList[2]);
            Assert.Equal("1~V4", idList[3]);
        }

        [Fact]
        public void ListCollectionIds_EmptyCollection_ReturnsEmpty()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("items", PathBuilder.Type.Property);
            var ids = serializer.ListCollectionIds(ctx, "items");

            Assert.Empty(ids);
        }

        #endregion

        #region Helper Tests

        [Fact]
        public void Clear_RemovesAllItems()
        {
            var serializer = new InMemorySerializer();

            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("test1", PathBuilder.Type.Property);
            serializer.SaveNoPushPath("value1", ctx);

            ctx.Path.Clear();
            ctx.Path.Push("test2", PathBuilder.Type.Property);
            serializer.SaveNoPushPath("value2", ctx);

            Assert.Equal(2, serializer.Count);

            serializer.Clear();

            Assert.Equal(0, serializer.Count);
        }

        [Fact]
        public void Storage_IsReadOnly()
        {
            var serializer = new InMemorySerializer();
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(serializer.Storage);
        }

        #endregion

        #region Test Data

        private class TestData
        {
            public string Name { get; set; } = "";
            public int Value { get; set; }
        }

        #endregion
    }
}
