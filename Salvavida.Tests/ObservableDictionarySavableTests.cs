using System;
using System.Linq;
using Xunit;

namespace Salvavida.Tests
{
    public class ObservableDictionarySavableTests
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

        private ObservableDictionarySavable<int, SavableCustomData> CreateRootedDictionary(Serializer serializer, string name = null)
        {
            var root = new TestRoot<ObservableDictionarySavable<int, SavableCustomData>>();
            root.SetSerializer(serializer);
            name = string.IsNullOrEmpty(name) ? "testDictionary" : name;
            var dict = new ObservableDictionarySavable<int, SavableCustomData>(name, null, true);
            root.Data = dict;
            return dict;
        }

        #endregion

        #region Serialize/Deserialize Tests

        [Fact]
        public void Serialize_Deserialize_DataIntegrity()
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
        public void Serialize_EmptyDictionary_SavesNothing()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            // Serialize empty dictionary
            using (serializer.BeginFreshAction(dict, out var ctx))
            {
                dict.Serialize(serializer, ctx);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            Assert.Equal(0, newDict.Count);
        }

        [Fact]
        public void Serialize_WithDirtyItems_SavesOnlyDirty()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            // Add items and do a full initial save
            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            using (serializer.BeginFreshAction(dict, out var ctx))
                dict.Serialize(serializer, ctx);

            // Deserialize to get clean state, then clear all dirty flags recursively
            var workDict = CreateRootedDictionary(serializer);
            using (serializer.BeginFreshAction(workDict, out var ctx))
                workDict.Deserialize(serializer, ctx);

            workDict.SetDirty(false, true);
            Assert.False(workDict.IsDirty);

            // Modify only item 2
            workDict[2]!.SavableName = "Modified";
            Assert.True(workDict.IsDirty);

            // Second serialize — only item 2 should be written
            using (serializer.BeginFreshAction(workDict, out var ctx))
                workDict.Serialize(serializer, ctx);

            // Deserialize into a fresh dict and verify all three values are correct
            var resultDict = CreateRootedDictionary(serializer);
            using (serializer.BeginFreshAction(resultDict, out var ctx))
                resultDict.Deserialize(serializer, ctx);

            Assert.Equal(3, resultDict.Count);
            Assert.Equal("Item1", resultDict[1]?.SavableName);
            Assert.Equal("Modified", resultDict[2]?.SavableName);
            Assert.Equal("Item3", resultDict[3]?.SavableName);
        }

        [Fact]
        public void Serialize_WithRemovedItems_RemovesFromSerializer()
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

            // Remove an item
            dict.Remove(2);

            // Serialize again
            using (serializer.BeginFreshAction(dict, out var ctx2))
            {
                dict.Serialize(serializer, ctx2);
            }

            // Deserialize and verify
            var newDict = CreateRootedDictionary(serializer);
            using (serializer.BeginFreshAction(newDict, out var ctx3))
            {
                newDict.Deserialize(serializer, ctx3);
            }

            Assert.Equal(2, newDict.Count);
            Assert.True(newDict.ContainsKey(1));
            Assert.False(newDict.ContainsKey(2));
            Assert.True(newDict.ContainsKey(3));
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
        public void Add_ExistingKey_Throws()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));

            Assert.Throws<ArgumentException>(() =>
            {
                dict.Add(1, CreateTestItem(2, "Item2"));
            });
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
        public void TryGetValue_ExistingKey_ReturnsValue()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));

            var result1 = dict.TryGetValue(1, out var value1);
            var result2 = dict.TryGetValue(2, out var value2);
            var result3 = dict.TryGetValue(99, out var value3);

            Assert.True(result1);
            Assert.NotNull(value1);
            Assert.Equal(1, value1.SavableId);

            Assert.True(result2);
            Assert.NotNull(value2);
            Assert.Equal(2, value2.SavableId);

            Assert.False(result3);
            Assert.Null(value3);
        }

        [Fact]
        public void Keys_ReturnsAllKeys()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            var keys = dict.Keys;

            Assert.Equal(3, keys.Count);
            Assert.Contains(1, keys);
            Assert.Contains(2, keys);
            Assert.Contains(3, keys);
        }

        [Fact]
        public void Values_ReturnsAllValues()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, CreateTestItem(2, "Item2"));
            dict.Add(3, CreateTestItem(3, "Item3"));

            var values = dict.Values;

            Assert.Equal(3, values.Count);
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
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            using (serializer.BeginFreshAction(dict, out var ctx))
                dict.Serialize(serializer, ctx);

            // Recursive clear ensures both the collection and items are clean
            dict.SetDirty(false, true);
            Assert.False(dict.IsDirty); // baseline: nothing dirty before modify

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

            dict.SetDirty(false, true);
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

        [Fact]
        public void IsDirty_WithDirtyChild_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            using (serializer.BeginFreshAction(dict, out var ctx))
            {
                dict.Serialize(serializer, ctx);
            }

            dict.SetDirty(false, false);
            Assert.False(dict.IsDirty);

            // Modify child item
            dict[1]!.SavableName = "Modified";

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

        [Fact]
        public void Serialize_WithNullValues_SavesCorrectly()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));
            dict.Add(2, null);
            dict.Add(3, CreateTestItem(3, "Item3"));

            // Serialize
            using (serializer.BeginFreshAction(dict, out var ctx1))
            {
                dict.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newDict = CreateRootedDictionary(serializer);
            using (serializer.BeginFreshAction(newDict, out var ctx2))
            {
                newDict.Deserialize(serializer, ctx2);
            }

            Assert.Equal(3, newDict.Count);
            Assert.NotNull(newDict[1]);
            Assert.Null(newDict[2]);
            Assert.NotNull(newDict[3]);
        }

        #endregion

        #region Exception Tests

        [Fact]
        public void Indexer_Get_NonExistingKey_Throws()
        {
            var serializer = new InMemorySerializer();
            var dict = CreateRootedDictionary(serializer);

            dict.Add(1, CreateTestItem(1, "Item1"));

            Assert.Throws<KeyNotFoundException>(() =>
            {
                var _ = dict[99];
            });
        }

        [Fact]
        public void Constructor_NullSourceDictionary_Works()
        {
            var serializer = new InMemorySerializer();
            var dict = new ObservableDictionarySavable<int, SavableCustomData>("test", null, true);

            Assert.Equal(0, dict.Count);
        }

        #endregion

        #region Additional Tests

        [Fact]
        public void MultipleDictionaries_SameSerializer_WorkIndependently()
        {
            var serializer = new InMemorySerializer();

            var dict1 = CreateRootedDictionary(serializer, "dict1");
            var dict2 = CreateRootedDictionary(serializer, "dict2");

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
            var newDict1 = CreateRootedDictionary(serializer, "dict1");
            using (serializer.BeginFreshAction(newDict1, out var ctx3))
            {
                newDict1.Deserialize(serializer, ctx3);
            }

            var newDict2 = CreateRootedDictionary(serializer, "dict2");
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
            var root = new TestRoot<ObservableDictionarySavable<string, SavableCustomData>>();
            root.SetSerializer(serializer);
            var dict = new ObservableDictionarySavable<string, SavableCustomData>("test", null, true);
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
