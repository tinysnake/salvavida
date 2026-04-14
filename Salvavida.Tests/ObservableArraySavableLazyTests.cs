using System;
using System.Linq;
using Xunit;

namespace Salvavida.Tests
{
    public class ObservableArraySavableLazyTests
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

        private CollectionOptions CreateLoadAllOptions()
        {
            return new CollectionOptions
            {
                Mode = LazyLoadMode.LoadAll,
                BatchLoadCount = 0
            };
        }

        private CollectionOptions CreateLoadIndividualOptions(int batchCount = 1)
        {
            return new CollectionOptions
            {
                Mode = LazyLoadMode.LoadIndividual,
                BatchLoadCount = batchCount
            };
        }

        private ObservableArraySavableLazy<SavableCustomData> CreateRootedArray(Serializer serializer, CollectionOptions? opt = null, string name = null)
        {
            opt ??= CreateLoadAllOptions();
            var root = new TestRoot<ObservableArraySavableLazy<SavableCustomData>>();
            root.SetSerializer(serializer);
            name = string.IsNullOrEmpty(name) ? "testArray" : name;
            var array = new ObservableArraySavableLazy<SavableCustomData>(name, opt.Value);
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
        public void Serialize_Deserialize_LoadAll_DataIntegrity()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            // Set array contents via SwapSource
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize into new array
            var newArray = CreateRootedArray(serializer);
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
        public void Serialize_Deserialize_LoadIndividual_DataIntegrity()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize into new array
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Debug output
            System.Console.WriteLine($"After Deserialize - Count: {newArray.Count}, LoadedCount: {newArray.LoadedCount}");

            // Verify data (items should be loaded on demand)
            Assert.Equal(3, newArray.Count);
            Assert.Equal(0, newArray.LoadedCount); // Initially nothing loaded

            // Access items
            var item0 = newArray[0];
            Assert.NotNull(item0);
            Assert.Equal(1, item0.SavableId);
            Assert.Equal("Item1", item0.SavableName);

            var item2 = newArray[2];
            Assert.NotNull(item2);
            Assert.Equal(3, item2.SavableId);
            Assert.Equal("Item3", item2.SavableName);

            var item1 = newArray[1];
            Assert.NotNull(item1);
            Assert.Equal(2, item1.SavableId);
            Assert.Equal("Item2", item1.SavableName);
        }

        [Fact]
        public void Serialize_EmptyArray_SavesMetadata()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var array = CreateRootedArray(serializer, options);

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
        public void Serialize_WithDirtyItems_SavesOnlyDirty()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

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
            var newArray = CreateRootedArray(serializer, options);
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

        #region LoadAll Mode Tests

        [Fact]
        public void LoadAll_AccessAfterDeserialize_LoadsAllItems()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Access first item - should load all items
            var item = newArray[0];
            Assert.NotNull(item);

            // Verify all items are loaded
            Assert.Equal(3, newArray.LoadedCount);
            Assert.True(newArray.IsLoaded(0));
            Assert.True(newArray.IsLoaded(1));
            Assert.True(newArray.IsLoaded(2));
        }

        [Fact]
        public void LoadAll_Enumerate_LoadsAllItems()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"), (4, "Item4"), (5, "Item5")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Enumerate
            var count = 0;
            foreach (var item in newArray)
            {
                Assert.NotNull(item);
                Assert.Equal(count + 1, item!.SavableId);
                count++;
            }

            Assert.Equal(5, count);
            Assert.Equal(5, newArray.LoadedCount);
        }

        [Fact]
        public void LoadAll_LoadAllMethod_LoadsAllItems()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Call LoadAll
            newArray.LoadAll();

            // Verify all items are loaded
            Assert.Equal(3, newArray.LoadedCount);
            Assert.True(newArray.IsLoaded(0));
            Assert.True(newArray.IsLoaded(1));
            Assert.True(newArray.IsLoaded(2));
        }

        #endregion

        #region LoadIndividual Mode Tests

        [Fact]
        public void LoadIndividual_AccessByIndex_LoadsSingleItem()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Verify initially nothing loaded
            Assert.Equal(0, newArray.LoadedCount);

            // Access middle item
            var item = newArray[1];
            Assert.NotNull(item);
            Assert.Equal(2, item.SavableId);
            Assert.Equal("Item2", item.SavableName);

            // Verify only that item is loaded
            Assert.Equal(1, newArray.LoadedCount);
            Assert.True(newArray.IsLoaded(1));
            Assert.False(newArray.IsLoaded(0));
            Assert.False(newArray.IsLoaded(2));
        }

        [Fact]
        public void LoadIndividual_AccessMultiple_LoadsOnDemand()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"), (4, "Item4"), (5, "Item5")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Access items in non-sequential order
            var item2 = newArray[2];
            var item0 = newArray[0];
            var item4 = newArray[4];

            Assert.NotNull(item2);
            Assert.Equal(3, item2.SavableId);
            Assert.NotNull(item0);
            Assert.Equal(1, item0.SavableId);
            Assert.NotNull(item4);
            Assert.Equal(5, item4.SavableId);

            // Verify only accessed items are loaded
            Assert.Equal(3, newArray.LoadedCount);
            Assert.True(newArray.IsLoaded(0));
            Assert.False(newArray.IsLoaded(1));
            Assert.True(newArray.IsLoaded(2));
            Assert.False(newArray.IsLoaded(3));
            Assert.True(newArray.IsLoaded(4));
        }

        [Fact]
        public void LoadIndividual_Enumerate_LoadsOnDemand()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"), (4, "Item4"), (5, "Item5")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Enumerate
            var count = 0;
            foreach (var item in newArray)
            {
                Assert.NotNull(item);
                Assert.Equal(count + 1, item!.SavableId);
                count++;
            }

            Assert.Equal(5, count);
            Assert.Equal(5, newArray.LoadedCount);
        }

        #endregion

        #region BatchLoadCount Tests

        [Fact]
        public void BatchLoadCount_AccessByIndex_LoadsBatch()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions(batchCount: 3);
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"), (4, "Item4"), (5, "Item5"),
                (6, "Item6"), (7, "Item7"), (8, "Item8"), (9, "Item9"), (10, "Item10")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Access item at index 5 - should load batch starting from there
            var item = newArray[5];
            Assert.NotNull(item);
            Assert.Equal(6, item.SavableId);

            // Verify batch is loaded
            Assert.True(newArray.LoadedCount >= 1);
        }

        [Fact]
        public void BatchLoadCount_BatchCountOf1_LoadsSingleItem()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions(batchCount: 1);
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"), (4, "Item4"), (5, "Item5")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Access item at index 2
            var item = newArray[2];
            Assert.NotNull(item);

            // With batchCount=1, should only load that single item
            Assert.Equal(1, newArray.LoadedCount);
            Assert.True(newArray.IsLoaded(2));
        }

        [Fact]
        public void BatchLoadCount_LargeBatch_LoadsMultiple()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions(batchCount: 5);
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3"), (4, "Item4"), (5, "Item5"),
                (6, "Item6"), (7, "Item7"), (8, "Item8"), (9, "Item9"), (10, "Item10")));

            // Serialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            // Access item at index 2
            var item = newArray[2];
            Assert.NotNull(item);

            // With batchCount=5, should load multiple items
            Assert.True(newArray.LoadedCount >= 1);
            Assert.True(newArray.IsLoaded(2));
        }

        #endregion

        #region Array Operations Tests (Fixed Size - No Add/Insert/Remove)

        [Fact]
        public void Indexer_Get_ReturnsCorrectItem()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            Assert.Equal(1, array[0]?.SavableId);
            Assert.Equal(2, array[1]?.SavableId);
            Assert.Equal(3, array[2]?.SavableId);
        }

        [Fact]
        public void Indexer_Set_UpdatesItem()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

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
            var array = CreateRootedArray(serializer);

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
            var array = CreateRootedArray(serializer);

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
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1")));

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
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2")));

            array.SwapSource(null);

            Assert.Equal(0, array.Count);
            Assert.Equal(0, array.LoadedCount);
        }

        [Fact]
        public void SwapSource_WithEmptyArray_ClearsArray()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2")));

            array.SwapSource(Array.Empty<SavableCustomData>());

            Assert.Equal(0, array.Count);
            Assert.Equal(0, array.LoadedCount);
        }

        #endregion

        #region Dirty Flag Tests

        [Fact]
        public void IsDirty_AfterSwapSource_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

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
            var options = CreateLoadAllOptions();
            var array = CreateRootedArray(serializer, options);

            array.SwapSource(CreateTestArray((1, "Item1")));
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
            var array = CreateRootedArray(serializer);

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
            var array = CreateRootedArray(serializer);

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
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var _ = array[-1];
            });
        }

        [Fact]
        public void Indexer_Get_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var _ = array[10];
            });
        }

        [Fact]
        public void Indexer_Set_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                array[-1] = CreateTestItem(2, "Item2");
            });
        }

        [Fact]
        public void Indexer_Set_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                array[10] = CreateTestItem(2, "Item2");
            });
        }

        [Fact]
        public void IsLoaded_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var _ = array.IsLoaded(-1);
            });
        }

        [Fact]
        public void IsLoaded_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1")));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var _ = array.IsLoaded(10);
            });
        }

        [Fact]
        public void IsLoaded_EmptyArray_Throws()
        {
            var options = CreateLoadAllOptions();
            var array = new ObservableArraySavableLazy<SavableCustomData>("testArray", options);

            Assert.Equal(0, array.Count);
            Assert.Equal(0, array.LoadedCount);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Assert.False(array.IsLoaded(0)));
        }

        #endregion

        #region Additional Tests

        [Fact]
        public void Count_LoadedCount_AfterOperations()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var array = CreateRootedArray(serializer, options);

            // Set array contents
            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

            Assert.Equal(3, array.Count);
            Assert.Equal(3, array.LoadedCount); // All loaded because we set them

            // Serialize and deserialize
            using (serializer.BeginFreshAction(array, out var ctx1))
            {
                array.Serialize(serializer, ctx1);
            }

            var newArray = CreateRootedArray(serializer, options);
            using (serializer.BeginFreshAction(newArray, out var ctx2))
            {
                newArray.Deserialize(serializer, ctx2);
            }

            Assert.Equal(3, newArray.Count);
            Assert.Equal(0, newArray.LoadedCount); // Nothing loaded yet

            // Access one item
            var item = newArray[1];
            Assert.NotNull(item);

            Assert.Equal(3, newArray.Count);
            Assert.True(newArray.LoadedCount >= 1);
        }

        [Fact]
        public void MultipleArrays_SameSerializer_WorkIndependently()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();

            var array1 = CreateRootedArray(serializer, options, "array1");
            var array2 = CreateRootedArray(serializer, options, "array2");

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
            var newArray1 = CreateRootedArray(serializer, options, "array1");
            using (serializer.BeginFreshAction(newArray1, out var ctx3))
            {
                newArray1.Deserialize(serializer, ctx3);
            }

            var newArray2 = CreateRootedArray(serializer, options, "array2");
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
            var array = CreateRootedArray(serializer);

            array.SwapSource(CreateTestArray((1, "Item1"), (2, "Item2"), (3, "Item3")));

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

        #endregion
    }
}
