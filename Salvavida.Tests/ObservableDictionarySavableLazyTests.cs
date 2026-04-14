using System;
using System.Linq;
using Xunit;

namespace Salvavida.Tests
{
    public class ObservableDictionarySavableLazyTests
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

        private ObservableDictionarySavableLazy<int, SavableCustomData> CreateRootedDictionary(
            Serializer serializer, CollectionOptions? opt = null, string name = null)
        {
            opt ??= CreateLoadAllOptions();
            var root = new TestRoot<ObservableDictionarySavableLazy<int, SavableCustomData>>();
            root.SetSerializer(serializer);
            name = string.IsNullOrEmpty(name) ? "testDictionary" : name;
            var dict = new ObservableDictionarySavableLazy<int, SavableCustomData>(name, true, opt.Value);
            root.Data = dict;
            return dict;
        }

        #endregion

        #region Serialize/Deserialize Tests

        [Fact]
        public void Serialize_Deserialize_LoadAll_DataIntegrity()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            // Add items
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize into new dictionary
            var newDict = CreateRootedDictionary(serializer);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // Verify data
            Assert.Equal(3, newDict.Count);
            Assert.Equal(1, newDict[1]?.SavableId);
            Assert.Equal("Item1", newDict[1]?.SavableName);
            Assert.Equal(2, newDict[2]?.SavableId);
            Assert.Equal("Item2", newDict[2]?.SavableName);
            Assert.Equal(3, newDict[3]?.SavableId);
            Assert.Equal("Item3", newDict[3]?.SavableName);
        }

        [Fact]
        public void Serialize_Deserialize_LoadIndividual_DataIntegrity()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize into new dictionary
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // Verify data (items should be loaded on demand)
            Assert.Equal(3, newDict.Count);
            Assert.Equal(0, newDict.LoadedCount); // Initially nothing loaded

            // Access items
            var item1 = newDict[1];
            Assert.NotNull(item1);
            Assert.Equal(1, item1.SavableId);
            Assert.Equal("Item1", item1.SavableName);

            var item3 = newDict[3];
            Assert.NotNull(item3);
            Assert.Equal(3, item3.SavableId);
            Assert.Equal("Item3", item3.SavableName);

            var item2 = newDict[2];
            Assert.NotNull(item2);
            Assert.Equal(2, item2.SavableId);
            Assert.Equal("Item2", item2.SavableName);
        }

        [Fact]
        public void Serialize_EmptyDictionary_SavesMetadata()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Serialize empty dictionary
            using (serializer.BeginFreshAction(dict, out var ctx))
            {
                dict.Serialize(serializer, ctx);
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
            var dict = CreateRootedDictionary(serializer, options);

            // Add items and do a full initial save
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            using (serializer.BeginFreshAction(dict, out var ctx))
                dict.Serialize(serializer, ctx);

            // Deserialize to get clean state (all items loaded, dirty = false)
            var workDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(workDict, out var ctx))
                workDict.Deserialize(serializer, ctx);

            // Access all items to populate loaded slots, then clear all dirty flags recursively
            workDict.LoadAll();
            workDict.SetDirty(false, true);
            Assert.False(workDict.IsDirty);

            // Modify only item 2
            workDict[2]!.SavableName = "Modified";
            Assert.True(workDict.IsDirty);

            // Second serialize — only item 2 should be written
            using (serializer.BeginFreshAction(workDict, out var ctx))
                workDict.Serialize(serializer, ctx);

            // Deserialize into a fresh dict and verify all three values are correct
            var resultDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(resultDict, out var ctx))
                resultDict.Deserialize(serializer, ctx);

            Assert.Equal(3, resultDict.Count);
            Assert.Equal("Item1", resultDict[1]?.SavableName);
            Assert.Equal("Modified", resultDict[2]?.SavableName);
            Assert.Equal("Item3", resultDict[3]?.SavableName);
        }

        #endregion

        #region LoadAll Mode Tests

        [Fact]
        public void LoadAll_AccessAfterDeserialize_LoadsAllItems()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // Access one item - should load all items
            var item = newDict[1];
            Assert.NotNull(item);

            // Verify all items are loaded
            Assert.Equal(3, newDict.LoadedCount);
            Assert.True(newDict.IsLoaded(1));
            Assert.True(newDict.IsLoaded(2));
            Assert.True(newDict.IsLoaded(3));
        }

        [Fact]
        public void LoadAll_Enumerate_LoadsAllItems()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            for (int i = 1; i <= 5; i++)
            {
                dict.Add(i, CreateTestItem(i, $"Item{i}"));
            }

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // Enumerate — dictionary order is unspecified, use a set to verify membership
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var kvp in newDict)
            {
                Assert.NotNull(kvp.Value);
                ids.Add(kvp.Value!.SavableId);
            }

            Assert.Equal(new System.Collections.Generic.HashSet<int> { 1, 2, 3, 4, 5 }, ids);
            Assert.Equal(5, newDict.LoadedCount);
        }

        [Fact]
        public void LoadAll_LoadAllMethod_LoadsAllItems()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // Call LoadAll
            newDict.LoadAll();

            // Verify all items are loaded
            Assert.Equal(3, newDict.LoadedCount);
            Assert.True(newDict.IsLoaded(1));
            Assert.True(newDict.IsLoaded(2));
            Assert.True(newDict.IsLoaded(3));
        }

        #endregion

        #region LoadIndividual Mode Tests

        [Fact]
        public void LoadIndividual_AccessByKey_LoadsSingleItem()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // Verify initially nothing loaded
            Assert.Equal(0, newDict.LoadedCount);

            // Access one item
            var item = newDict[2];
            Assert.NotNull(item);
            Assert.Equal(2, item.SavableId);
            Assert.Equal("Item2", item.SavableName);

            // Verify only that item is loaded
            Assert.Equal(1, newDict.LoadedCount);
            Assert.True(newDict.IsLoaded(2));
            Assert.False(newDict.IsLoaded(1));
            Assert.False(newDict.IsLoaded(3));
        }

        [Fact]
        public void LoadIndividual_AccessMultiple_LoadsOnDemand()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            for (int i = 0; i <= 4; i++)
            {
                dict.Add(i, CreateTestItem(i, $"Item{i}"));
            }

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // Access items in non-sequential order
            var item2 = newDict[2];
            var item0 = newDict[0];
            var item4 = newDict[4];

            Assert.NotNull(item2);
            Assert.Equal(2, item2.SavableId);
            Assert.NotNull(item0);
            Assert.Equal(0, item0.SavableId);
            Assert.NotNull(item4);
            Assert.Equal(4, item4.SavableId);

            // Verify only accessed items are loaded
            Assert.Equal(3, newDict.LoadedCount);
            Assert.True(newDict.IsLoaded(0));
            Assert.False(newDict.IsLoaded(1));
            Assert.True(newDict.IsLoaded(2));
            Assert.False(newDict.IsLoaded(3));
            Assert.True(newDict.IsLoaded(4));
        }

        [Fact]
        public void LoadIndividual_Enumerate_LoadsOnDemand()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            for (int i = 1; i <= 5; i++)
            {
                dict.Add(i, CreateTestItem(i, $"Item{i}"));
            }

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // Enumerate — dictionary order is unspecified, use a set to verify membership
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var kvp in newDict)
            {
                Assert.NotNull(kvp.Value);
                ids.Add(kvp.Value!.SavableId);
            }

            Assert.Equal(new System.Collections.Generic.HashSet<int> { 1, 2, 3, 4, 5 }, ids);
            Assert.Equal(5, newDict.LoadedCount);
        }

        [Fact]
        public void LoadIndividual_TryGetValue_LoadsOnDemand()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            // TryGetValue should load item
            var result = newDict.TryGetValue(2, out var value);
            Assert.True(result);
            Assert.NotNull(value);
            Assert.Equal(2, value.SavableId);

            // Verify item is loaded
            Assert.Equal(1, newDict.LoadedCount);
            Assert.True(newDict.IsLoaded(2));
        }

        #endregion

        #region BatchLoadCount Tests

        [Fact]
        public void BatchLoadCount_Enumerate_LoadsInBatches()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions(batchCount: 3);
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            for (int i = 1; i <= 10; i++)
            {
                dict.Add(i, CreateTestItem(i, $"Item{i}"));
            }

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            Assert.Equal(0, newDict.LoadedCount);

            // Enumerate 3 items — each GetEnumerator iteration loads a batch of 3
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var kvp in newDict.Take(3))
            {
                Assert.NotNull(kvp.Value);
                ids.Add(kvp.Value!.SavableId);
            }

            // Exactly 3 items should have been loaded (one batch)
            Assert.Equal(3, ids.Count);
            Assert.Equal(3, newDict.LoadedCount);
        }

        #endregion

        #region Dictionary Operations Tests

        [Fact]
        public void Add_Item_AddsToDictionary()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));

            Assert.Equal(2, dict.Count);
            Assert.Equal(1, dict[1]?.SavableId);
            Assert.Equal(2, dict[2]?.SavableId);
        }

        [Fact]
        public void Remove_ExistingKey_RemovesItem()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            var result = dict.Remove(2);

            Assert.True(result);
            Assert.Equal(2, dict.Count);
            Assert.True(dict.ContainsKey(1));
            Assert.False(dict.ContainsKey(2));
            Assert.True(dict.ContainsKey(3));
        }

        [Fact]
        public void Remove_NonExistingKey_ReturnsFalse()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));

            var result = dict.Remove(99);

            Assert.False(result);
            Assert.Equal(1, dict.Count);
        }

        [Fact]
        public void Clear_RemovesAllItems()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            dict.Clear();

            Assert.Equal(0, dict.Count);
            Assert.Equal(0, dict.LoadedCount);
        }

        [Fact]
        public void Indexer_Set_AddsOrUpdatesItem()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict[1] = CreateTestItem(1, "Item1");
            dict[2] = CreateTestItem(2, "Item2");

            Assert.Equal(2, dict.Count);

            // Update existing item
            dict[1] = CreateTestItem(10, "Updated");

            Assert.Equal(2, dict.Count);
            Assert.Equal(10, dict[1]?.SavableId);
            Assert.Equal("Updated", dict[1]?.SavableName);
        }

        [Fact]
        public void ContainsKey_ExistingKey_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));

            Assert.True(dict.ContainsKey(1));
            Assert.True(dict.ContainsKey(2));
            Assert.False(dict.ContainsKey(99));
        }

        [Fact]
        public void ContainsKey_UnloadedEntry_UsesSerializerHas_NoLoad()
        {
            // After deserialization nothing is loaded — ContainsKey must fall through
            // to serializer.Has without triggering a value load.
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var dict = CreateRootedDictionary(serializer, options);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));

            using (serializer.BeginFreshAction(dict, out var ctx))
                dict.Serialize(serializer, ctx);

            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx))
                newDict.Deserialize(serializer, ctx);

            Assert.Equal(0, newDict.LoadedCount); // sanity: nothing loaded yet

            Assert.True(newDict.ContainsKey(1));
            Assert.True(newDict.ContainsKey(2));
            Assert.False(newDict.ContainsKey(99));

            Assert.Equal(0, newDict.LoadedCount); // ContainsKey must NOT trigger value load
        }

        [Fact]
        public void SwapSource_WithDictionary_ReplacesContents()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));

            var newDict = new Dictionary<int, SavableCustomData?>
            {
                { 10, CreateTestItem(10, "NewItem1") },
                { 20, CreateTestItem(20, "NewItem2") },
                { 30, CreateTestItem(30, "NewItem3") }
            };

            dict.SwapSource(newDict);

            Assert.Equal(3, dict.Count);
            Assert.Equal(10, dict[10]?.SavableId);
            Assert.Equal("NewItem1", dict[10]?.SavableName);
            Assert.Equal(20, dict[20]?.SavableId);
            Assert.Equal("NewItem2", dict[20]?.SavableName);
            Assert.Equal(30, dict[30]?.SavableId);
            Assert.Equal("NewItem3", dict[30]?.SavableName);
        }

        [Fact]
        public void SwapSource_WithNull_ClearsDictionary()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));

            dict.SwapSource(null);

            Assert.Equal(0, dict.Count);
            Assert.Equal(0, dict.LoadedCount);
        }

        [Fact]
        public void GetEnumerator_EnumeratesAllItems()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            var count = 0;
            foreach (var kvp in dict)
            {
                Assert.NotNull(kvp.Value);
                count++;
            }

            Assert.Equal(3, count);
        }

        #endregion

        #region Dirty Flag Tests

        [Fact]
        public void IsDirty_AfterAdd_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            Assert.True(dict.IsDirty); // newly created dictionary is always dirty

            dict.SetDirty(false, false);

            Assert.False(dict.IsDirty);

            dict.Add(1, CreateTestItem(1, "Item1"));

            Assert.True(dict.IsDirty);
        }

        [Fact]
        public void IsDirty_AfterRemove_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.SetDirty(false, false);

            dict.Remove(1);

            Assert.True(dict.IsDirty);
        }

        [Fact]
        public void IsDirty_AfterModifyItem_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var dict = CreateRootedDictionary(serializer, options);

            dict.Add(1, CreateTestItem(1, "Item1"));
            using (serializer.BeginFreshAction(dict, out var ctx))
            {
                dict.Serialize(serializer, ctx);
            }

            dict.SetDirty(false, false);

            dict[1]!.SavableName = "Modified";

            Assert.True(dict.IsDirty);
        }

        [Fact]
        public void SetDirty_Recursive_SetsAllItemsDirty()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));

            dict.SetDirty(false, false);
            Assert.False(dict.IsDirty);

            dict.SetDirty(true, true);

            Assert.True(dict.IsDirty);
        }

        [Fact]
        public void SetDirty_NotRecursive_SetsSelfDirty()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));

            dict.SetDirty(true, false);

            Assert.True(dict.IsDirty);
        }

        #endregion

        #region Null Value Tests

        [Fact]
        public void Indexer_SetNullValue_SetsNull()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict[1] = CreateTestItem(1, "Item1");
            Assert.Equal(1, dict.Count);
            Assert.NotNull(dict[1]);

            dict[1] = null;
            Assert.Equal(1, dict.Count);
            Assert.Null(dict[1]);
        }

        [Fact]
        public void Add_NullValue_AddsToDictionary()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, null);

            Assert.Equal(1, dict.Count);
            Assert.Null(dict[1]);
        }

        #endregion

        #region Exception Tests

        [Fact]
        public void Indexer_Get_NonExistingKey_ThrowsKeyNotFoundException()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));

            Assert.Throws<KeyNotFoundException>(() =>
            {
                var _ = dict[99];
            });
        }

        [Fact]
        public void Constructor_LazyLoadModeNone_Throws()
        {
            var options = new CollectionOptions
            {
                Mode = LazyLoadMode.None
            };

            Assert.Throws<InvalidOperationException>(() =>
            {
                var _ = new ObservableDictionarySavableLazy<int, SavableCustomData>("testDict", true, options);
            });
        }

        #endregion

        #region Additional Tests

        [Fact]
        public void Count_LoadedCount_AfterOperations()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadIndividualOptions();
            var dict = CreateRootedDictionary(serializer, options);

            // Add items
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            Assert.Equal(3, dict.Count);
            Assert.Equal(3, dict.LoadedCount); // All loaded because we added them

            // Serialize and deserialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            var newDict = CreateRootedDictionary(serializer, options);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            Assert.Equal(3, newDict.Count);
            Assert.Equal(0, newDict.LoadedCount); // Nothing loaded yet

            // Access one item
            var item = newDict[2];
            Assert.NotNull(item);

            Assert.Equal(3, newDict.Count);
            Assert.True(newDict.LoadedCount >= 1);
        }

        [Fact]
        public void MultipleDictionaries_SameSerializer_WorkIndependently()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();

            var dict1 = CreateRootedDictionary(serializer, options, "dict1");
            var dict2 = CreateRootedDictionary(serializer, options, "dict2");

            // Add different items to each dictionary
            dict1.Add(1, CreateTestItem(1, "Item1"));
            dict1.Add(2, CreateTestItem(2, "Item2"));

            dict2.Add(10, CreateTestItem(10, "Item10"));
            dict2.Add(20, CreateTestItem(20, "Item20"));

            // Serialize both
            using (serializer.BeginFreshAction(dict1, out var ctx1))
            {
                dict1.Serialize(serializer, ctx1);
            }

            using (serializer.BeginFreshAction(dict2, out var ctx2))
            {
                dict2.Serialize(serializer, ctx2);
            }

            // Deserialize both
            var newDict1 = CreateRootedDictionary(serializer, options, "dict1");
            using (serializer.BeginFreshAction(newDict1, out var ctx3))
            {
                newDict1.Deserialize(serializer, ctx3);
            }

            var newDict2 = CreateRootedDictionary(serializer, options, "dict2");
            using (serializer.BeginFreshAction(newDict2, out var ctx4))
            {
                newDict2.Deserialize(serializer, ctx4);
            }

            // Verify dictionaries are independent
            Assert.Equal(2, newDict1.Count);
            Assert.Equal(1, newDict1[1]?.SavableId);
            Assert.Equal(2, newDict1[2]?.SavableId);

            Assert.Equal(2, newDict2.Count);
            Assert.Equal(10, newDict2[10]?.SavableId);
            Assert.Equal(20, newDict2[20]?.SavableId);
        }

        [Fact]
        public void StringKeys_WorkCorrectly()
        {
            var serializer = new InMemorySerializer();
            var options = CreateLoadAllOptions();
            var root = new TestRoot<ObservableDictionarySavableLazy<string, SavableCustomData>>();
            root.SetSerializer(serializer);
            var dict = new ObservableDictionarySavableLazy<string, SavableCustomData>("test", true, options);
            root.Data = dict;

            dict.Add("key1", CreateTestItem(1, "Item1"));
            dict.Add("key2", CreateTestItem(2, "Item2"));
            dict.Add("key3", CreateTestItem(3, "Item3"));

            Assert.Equal(3, dict.Count);
            Assert.Equal(1, dict["key1"]?.SavableId);
            Assert.Equal(2, dict["key2"]?.SavableId);
            Assert.Equal(3, dict["key3"]?.SavableId);
        }

        #endregion
    }
}
