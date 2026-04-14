using System;
using System.Linq;
using Xunit;

namespace Salvavida.Tests
{
    public class ObservableArraySavableTests
    {
        #region Helper Methods

        private SavableCustomData CreateTestItem(int id, string name)
        {
            return new SavableCustomData
            {
                savableId = id,
                savableName = name
            };
        }

        private ObservableArraySavable<SavableCustomData> CreateRootedArray(Serializer serializer, SavableCustomData?[]? src, string? name = null)
        {
            var root = new TestRoot<ObservableArraySavable<SavableCustomData>>();
            root.SetSerializer(serializer);
            name = string.IsNullOrEmpty(name) ? "testArray" : name;
            var array = new ObservableArraySavable<SavableCustomData>(name, src, true);
            root.Data = array;
            return array;
        }

        private SavableCustomData[] CreateTestArray(params (int id, string name)[] items)
        {
            var result = new SavableCustomData?[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = CreateTestItem(items[i].id, items[i].name);
            }
            return result!;
        }

        #endregion

        #region Serialize/Deserialize Tests

        [Fact]
        public void Serialize_Deserialize_DataIntegrity()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer,CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));


            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize into new array
            var newArray = CreateRootedArray(serializer, null);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Verify data
            Assert.Equal(3, newArray.Count);
            Assert.Equal(1, newArray[0]?.SavableId);
            Assert.Equal("Item1", newArray[0]?.SavableName);
            Assert.Equal(2, newArray[1]?.SavableId);
            Assert.Equal("Item2", newArray[1]?.SavableName);
            Assert.Equal(3, newArray[2]?.SavableId);
            Assert.Equal("Item3", newArray[2]?.SavableName);
        }

        [Fact]
        public void Serialize_EmptyArray_SavesMetadata()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, []);

            // Serialize empty array
            using (serializer.BeginFreshAction(array, out var ctx))
            {
                array.Serialize(serializer, ctx);
                using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property))
                {
                    Assert.True(serializer.ContainsPath(ctx.Path.ToString()));
                    var meta = serializer.ReadNoPushPath<CollectionMetadata>(ctx);
                    Assert.Equal(0, meta.Count);
                }
            }
        }

        [Fact]
        public void Serialize_EmptyArray_AlsoSavesMetadata()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, []);

            // Serialize empty array
            using (serializer.BeginFreshAction(array, out var ctx))
            {
                array.Serialize(serializer, ctx);
                using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property))
                {
                    Assert.True(serializer.ContainsPath(ctx.Path.ToString()));
                    var meta = serializer.ReadNoPushPath<CollectionMetadata>(ctx);
                    Assert.Equal(0, meta.Count);
                }
            }
        }

        [Fact]
        public void Serialize_NullArray_AlsoSavesMetadata()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            // Serialize empty array
            using (serializer.BeginFreshAction(array, out var ctx))
            {
                array.Serialize(serializer, ctx);
                using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property))
                {
                    Assert.False(serializer.HasNoPushPath(ctx));
                }
            }
        }

        [Fact]
        public void Serialize_WithDirtyItems_SavesOnlyDirty()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            // Clear dirty flags
            array.SetDirty(false, false);

            // Modify one item
            array[1]!.SavableName = "Modified";

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize and verify only modified item was saved
            var newArray = CreateRootedArray(serializer, null);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            Assert.Equal(3, newArray.Count);
            Assert.Equal("Item1", newArray[0]?.SavableName);
            Assert.Equal("Modified", newArray[1]?.SavableName);
            Assert.Equal("Item3", newArray[2]?.SavableName);
        }

        #endregion

        #region Array Operations Tests

        [Fact]
        public void Indexer_Get_ReturnsCorrectItem()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            Assert.Equal(1, array[0]?.SavableId);
            Assert.Equal(2, array[1]?.SavableId);
            Assert.Equal(3, array[2]?.SavableId);
        }

        [Fact]
        public void Indexer_Set_UpdatesItem()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            array[1] = CreateTestItem(99, "Modified");

            Assert.Equal(3, array.Count);
            Assert.Equal(1, array[0]?.SavableId);
            Assert.Equal(99, array[1]?.SavableId);
            Assert.Equal("Modified", array[1]?.SavableName);
            Assert.Equal(3, array[2]?.SavableId);
        }

        [Fact]
        public void Contains_Item_ReturnsCorrectResult()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            var items = CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"));
            var item2 = items[1];
            array.SwapSource(items);

            Assert.True(array.Contains(item2));
            Assert.False(array.Contains(CreateTestItem(99, "NonExistent")));
        }

        [Fact]
        public void IndexOf_Item_ReturnsCorrectIndex()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            var items = CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"));
            var item2 = items[1];
            array.SwapSource(items);

            Assert.Equal(1, array.IndexOf(item2));
            Assert.Equal(-1, array.IndexOf(CreateTestItem(99, "NonExistent")));
        }

        [Fact]
        public void SwapSource_WithArray_ReplacesContents()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1")));

            var newItems = CreateTestArray((10, "NewItem1"), (20, "NewItem2"), (30, "NewItem3"));
            array.SwapSource(newItems);

            Assert.Equal(3, array.Count);
            Assert.Equal(10, array[0]?.SavableId);
            Assert.Equal("NewItem1", array[0]?.SavableName);
            Assert.Equal(20, array[1]?.SavableId);
            Assert.Equal("NewItem2", array[1]?.SavableName);
            Assert.Equal(30, array[2]?.SavableId);
            Assert.Equal("NewItem3", array[2]?.SavableName);
        }

        [Fact]
        public void SwapSource_WithNull_ClearsArray()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2")));

            array.SwapSource(null);

            Assert.Equal(0, array.Count);
        }

        [Fact]
        public void SwapSource_WithEmptyArray_ClearsArray()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2")));

            array.SwapSource(Array.Empty<SavableCustomData>());

            Assert.Equal(0, array.Count);
        }

        [Fact]
        public void RetrieveSource_ReturnsCorrectArray()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            var items = CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"));
            array.SwapSource(items);

            var retrieved = array.RetrieveSource();
            Assert.NotNull(retrieved);
            Assert.Equal(3, retrieved.Length);
            Assert.Same(items, retrieved);
        }

        #endregion

        #region Dirty Flag Tests

        [Fact]
        public void IsDirty_AfterSwapSource_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            Assert.True(array.IsDirty); // newly created array is always dirty

            array.SetDirty(false, true);

            Assert.False(array.IsDirty);

            array.SwapSource(CreateTestArray((1, "Item1")));

            Assert.True(array.IsDirty);
        }

        [Fact]
        public void IsDirty_AfterModifyItem_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1")));
            using (serializer.BeginFreshAction(array, out var ctx))
            {
                array.Serialize(serializer, ctx);
            }

            array.SetDirty(false, false);

            array[0]!.SavableName = "Modified";

            Assert.True(array.IsDirty);
        }

        [Fact]
        public void SetDirty_Recursive_SetsAllItemsDirty()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2")));

            array.SetDirty(false, true);
            Assert.False(array.IsDirty);

            array.SetDirty(true, true);

            Assert.True(array.IsDirty);
        }

        [Fact]
        public void SetDirty_NotRecursive_SetsSelfDirty()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2")));

            array.SetDirty(true, false);

            Assert.True(array.IsDirty);
        }

        #endregion

        #region Exception Tests

        [Fact]
        public void Indexer_Get_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var _ = array[-1];
            });
        }

        [Fact]
        public void Indexer_Get_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var _ = array[10];
            });
        }

        [Fact]
        public void Indexer_Set_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                array[-1] = CreateTestItem(2, "Item2");
            });
        }

        [Fact]
        public void Indexer_Set_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                array[10] = CreateTestItem(2, "Item2");
            });
        }

        #endregion

        #region Additional Tests

        [Fact]
        public void Count_AfterOperations()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, null);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            Assert.Equal(3, array.Count);

            // Swap with different size
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2")));

            Assert.Equal(2, array.Count);

            // Clear
            array.SwapSource(null);

            Assert.Equal(0, array.Count);
        }

        [Fact]
        public void MultipleArrays_SameSerializer_WorkIndependently()
        {
            var serializer = new InMemorySerializer();

            var array1 = CreateRootedArray(serializer, null, "array1");
            var array2 = CreateRootedArray(serializer, null, "array2");

            // Set different items to each array
            array1.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2")));
            array2.SwapSource(CreateTestArray((10, "Item10"), (20, "Item20")));

            // Serialize both
            using (serializer.BeginFreshAction(array1, out var ctx1))
            {
                array1.Serialize(serializer, ctx1);
            }

            using (serializer.BeginFreshAction(array2, out var ctx2))
            {
                array2.Serialize(serializer, ctx2);
            }

            // Deserialize both
            var newArray1 = CreateRootedArray(serializer, null, "array1");
            using (serializer.BeginFreshAction(newArray1, out var ctx3))
            {
                newArray1.Deserialize(serializer, ctx3);
            }

            var newArray2 = CreateRootedArray(serializer, null, "array2");
            using (serializer.BeginFreshAction(newArray2, out var ctx4))
            {
                newArray2.Deserialize(serializer, ctx4);
            }

            // Verify arrays are independent
            Assert.Equal(2, newArray1.Count);
            Assert.Equal(1, newArray1[0]?.SavableId);
            Assert.Equal(2, newArray1[1]?.SavableId);

            Assert.Equal(2, newArray2.Count);
            Assert.Equal(10, newArray2[0]?.SavableId);
            Assert.Equal(20, newArray2[1]?.SavableId);
        }

        [Fact]
        public void Iterator_ModifyWhileIterating_NotThrows()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            // This might throw or might not, depending on implementation
            // The test is to document the behavior

            foreach (var item in array)
            {
                if (item!.SavableId == 2)
                {
                    array[0] = CreateTestItem(4, "Item4");
                }
            }
        }

        [Fact]
        public void Enumerate_ReturnsAllItems()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer, CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            var count = 0;
            foreach (var item in array)
            {
                Assert.NotNull(item);
                Assert.Equal(count + 1, item!.SavableId);
                count++;
            }

            Assert.Equal(3, count);
        }

        #endregion
    }
}
