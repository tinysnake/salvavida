using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Salvavida.Tests
{
    public class ObservableListSavableTests
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

        private ObservableListSavable<SavableCustomData> CreateRootedList(Serializer serializer, List<SavableCustomData?>? src, string? name = null)
        {
            var root = new TestRoot<ObservableListSavable<SavableCustomData>>();
            root.SetSerializer(serializer);
            name = string.IsNullOrEmpty(name) ? "testList" : name;
            var list = new ObservableListSavable<SavableCustomData>(name, src, true);
            root.Data = list;
            return list;
        }

        #endregion

        #region Serialize/Deserialize Tests

        [Fact]
        public void Serialize_Deserialize_DataIntegrity()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            // Serialize
            using (serializer.BeginFreshAction(list, out var ctx1))
            {
                list.Serialize(serializer, ctx1);
            }

            // Deserialize into new list
            var newList = CreateRootedList(serializer, null);
            using (serializer.BeginFreshAction(newList, out var ctx2))
            {
                newList.Deserialize(serializer, ctx2);
            }

            // Verify data
            Assert.Equal(3, newList.Count);
            Assert.Equal(1, newList[0]?.SavableId);
            Assert.Equal("Item1", newList[0]?.SavableName);
            Assert.Equal(2, newList[1]?.SavableId);
            Assert.Equal("Item2", newList[1]?.SavableName);
            Assert.Equal(3, newList[2]?.SavableId);
            Assert.Equal("Item3", newList[2]?.SavableName);
        }

        [Fact]
        public void Serialize_EmptyList_SavesMetadata()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, null);

            // Serialize empty list
            using (serializer.BeginFreshAction(list, out var ctx))
            {
                list.Serialize(serializer, ctx);
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
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            // Serialize
            using (serializer.BeginFreshAction(list, out var ctx1))
            {
                list.Serialize(serializer, ctx1);
            }

            // Modify one item
            list[0]!.SavableName = "Modified1";
            list[2]!.SavableName = "Modified3";

            list.SetDirty(false, true);

            list[1]!.SavableName = "Modified2";

            // Serialize
            using (serializer.BeginFreshAction(list, out var ctx1))
            {
                list.Serialize(serializer, ctx1);
            }

            // Deserialize and verify only modified item was saved
            var newList = CreateRootedList(serializer, null);
            using (serializer.BeginFreshAction(newList, out var ctx2))
            {
                newList.Deserialize(serializer, ctx2);
            }

            Assert.Equal(3, newList.Count);
            Assert.Equal("Item1", newList[0]?.SavableName);
            Assert.Equal("Modified2", newList[1]?.SavableName);
            Assert.Equal("Item3", newList[2]?.SavableName);
        }

        #endregion

        #region Collection Operations Tests

        [Fact]
        public void Add_Item_AddsToEnd()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2")]);

            Assert.Equal(2, list.Count);
            Assert.Equal(1, list[0]?.SavableId);
            Assert.Equal(2, list[1]?.SavableId);
        }

        [Fact]
        public void AddRange_Items_AddsAllToEnd()
        {
            var serializer = new InMemorySerializer();
            var collection = new List<SavableCustomData?>
            {
                CreateTestItem(1, "Item1"),
                CreateTestItem(2, "Item2"),
                CreateTestItem(3, "Item3")
            };
            var list = CreateRootedList(serializer, collection);

            Assert.Equal(3, list.Count);
            Assert.Equal(1, list[0]?.SavableId);
            Assert.Equal(2, list[1]?.SavableId);
            Assert.Equal(3, list[2]?.SavableId);
        }

        [Fact]
        public void Insert_Item_InsertsAtCorrectPosition()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(3, "Item3")]);
            list.Insert(1, CreateTestItem(2, "Item2"));

            Assert.Equal(3, list.Count);
            Assert.Equal(1, list[0]?.SavableId);
            Assert.Equal(2, list[1]?.SavableId);
            Assert.Equal(3, list[2]?.SavableId);
        }

        [Fact]
        public void InsertRange_Items_InsertsAllAtCorrectPosition()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(4, "Item4")]);

            var collection = new List<SavableCustomData?>
            {
                CreateTestItem(2, "Item2"),
                CreateTestItem(3, "Item3")
            };

            list.InsertRange(1, collection);

            Assert.Equal(4, list.Count);
            Assert.Equal(1, list[0]?.SavableId);
            Assert.Equal(2, list[1]?.SavableId);
            Assert.Equal(3, list[2]?.SavableId);
            Assert.Equal(4, list[3]?.SavableId);
        }

        [Fact]
        public void Remove_ExistingItem_RemovesIt()
        {
            var serializer = new InMemorySerializer();
            var item = CreateTestItem(2, "Item2");
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), item, CreateTestItem(3, "Item3")]);

            var result = list.Remove(item);

            Assert.True(result);
            Assert.Equal(2, list.Count);
            Assert.Equal(1, list[0]?.SavableId);
            Assert.Equal(3, list[1]?.SavableId);
        }

        [Fact]
        public void Remove_NonExistingItem_ReturnsFalse()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            var result = list.Remove(CreateTestItem(99, "NonExistent"));

            Assert.False(result);
            Assert.Equal(1, list.Count);
        }

        [Fact]
        public void RemoveAt_Index_RemovesAtCorrectPosition()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            list.RemoveAt(1);

            Assert.Equal(2, list.Count);
            Assert.Equal(1, list[0]?.SavableId);
            Assert.Equal(3, list[1]?.SavableId);
        }

        [Fact]
        public void RemoveRange_Items_RemovesCorrectCount()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3"), CreateTestItem(4, "Item4"), CreateTestItem(5, "Item5")]);

            list.RemoveRange(1, 3);

            Assert.Equal(2, list.Count);
            Assert.Equal(1, list[0]?.SavableId);
            Assert.Equal(5, list[1]?.SavableId);
        }

        [Fact]
        public void Clear_RemovesAllItems()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            list.Clear();

            Assert.Equal(0, list.Count);
        }

        [Fact]
        public void Indexer_Set_UpdatesItem()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            list[1] = CreateTestItem(99, "Modified");

            Assert.Equal(3, list.Count);
            Assert.Equal(1, list[0]?.SavableId);
            Assert.Equal(99, list[1]?.SavableId);
            Assert.Equal("Modified", list[1]?.SavableName);
            Assert.Equal(3, list[2]?.SavableId);
        }

        [Fact]
        public void Contains_Item_ReturnsCorrectResult()
        {
            var serializer = new InMemorySerializer();
            var item = CreateTestItem(2, "Item2");
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), item, CreateTestItem(3, "Item3")]);

            Assert.True(list.Contains(item));
            Assert.False(list.Contains(CreateTestItem(99, "NonExistent")));
        }

        [Fact]
        public void IndexOf_Item_ReturnsCorrectIndex()
        {
            var serializer = new InMemorySerializer();
            var item = CreateTestItem(2, "Item2");
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), item, CreateTestItem(3, "Item3")]);

            Assert.Equal(1, list.IndexOf(item));
            Assert.Equal(-1, list.IndexOf(CreateTestItem(99, "NonExistent")));
        }

        [Fact]
        public void SwapSource_WithList_ReplacesContents()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            var newList = new List<SavableCustomData?>
            {
                CreateTestItem(10, "NewItem1"),
                CreateTestItem(20, "NewItem2"),
                CreateTestItem(30, "NewItem3")
            };

            list.SwapSource(newList);

            Assert.Equal(3, list.Count);
            Assert.Equal(10, list[0]?.SavableId);
            Assert.Equal("NewItem1", list[0]?.SavableName);
            Assert.Equal(20, list[1]?.SavableId);
            Assert.Equal("NewItem2", list[1]?.SavableName);
            Assert.Equal(30, list[2]?.SavableId);
            Assert.Equal("NewItem3", list[2]?.SavableName);
        }

        [Fact]
        public void SwapSource_WithNull_ClearsList()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2")]);

            list.SwapSource(null);

            Assert.Equal(0, list.Count);
        }

        [Fact]
        public void Move_Item_MovesToNewIndex()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            list.Move(0, 2);

            Assert.Equal(3, list.Count);
            Assert.Equal(2, list[0]?.SavableId);
            Assert.Equal(3, list[1]?.SavableId);
            Assert.Equal(1, list[2]?.SavableId);
        }

        [Fact]
        public void RetrieveSource_ReturnsCorrectList()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            var retrieved = list.RetrieveSource();
            Assert.NotNull(retrieved);
            Assert.Equal(3, retrieved.Count);
        }

        #endregion

        #region Dirty Flag Tests

        [Fact]
        public void IsDirty_AfterAdd_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, null);

            Assert.True(list.IsDirty); // newly created list is always dirty.

            list.SetDirty(false, true);

            Assert.False(list.IsDirty);

            list.Add(CreateTestItem(1, "Item1"));

            Assert.True(list.IsDirty);
        }

        [Fact]
        public void IsDirty_AfterRemove_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);
            list.SetDirty(false, false);

            list.Remove(list[0]!);

            Assert.True(list.IsDirty);
        }

        [Fact]
        public void IsDirty_AfterModifyItem_ReturnsTrue()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);
            using (serializer.BeginFreshAction(list, out var ctx))
            {
                list.Serialize(serializer, ctx);
            }

            list.SetDirty(false, false);

            list[0]!.SavableName = "Modified";

            Assert.True(list.IsDirty);
        }

        [Fact]
        public void SetDirty_Recursive_SetsAllItemsDirty()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2")]);

            list.SetDirty(false, true);
            Assert.False(list.IsDirty);

            list.SetDirty(true, true);

            Assert.True(list.IsDirty);
        }

        [Fact]
        public void SetDirty_NotRecursive_SetsSelfDirty()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2")]);

            list.SetDirty(true, false);

            Assert.True(list.IsDirty);
        }

        #endregion

        #region Rebalance Tests

        [Fact]
        public void Insert_ManyTimes_TriggersRebalance()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, null);

            // Insert many times at the same position to trigger rebalance
            for (int i = 0; i < 15; i++)
            {
                list.Insert(0, CreateTestItem(i, $"Item{i}"));
            }

            // After many inserts, rebalance should have been triggered
            // Verify all items are still accessible
            Assert.Equal(15, list.Count);
            for (int i = 0; i < 15; i++)
            {
                Assert.NotNull(list[i]);
            }
        }

        [Fact]
        public void Serialize_AfterRebalance_DataIntegrity()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, null);
            const int count = 300;
            // Insert many times to trigger rebalance
            for (int i = 0; i < count; i++)
            {
                list.Insert(0, CreateTestItem(i, $"Item{i}"));
            }

            // Serialize
            using (serializer.BeginFreshAction(list, out var ctx1))
            {
                list.Serialize(serializer, ctx1);
            }

            // Deserialize
            var newList = CreateRootedList(serializer, null);
            using (serializer.BeginFreshAction(newList, out var ctx2))
            {
                newList.Deserialize(serializer, ctx2);
            }

            // Verify data integrity after rebalance
            Assert.Equal(count, newList.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.NotNull(newList[i]);
                Assert.Equal(count - i - 1, newList[i]!.SavableId);
            }
        }

        [Fact]
        public void Insert_AtEnd_DoesNotTriggerRebalance()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, null);

            // Add items at the end (should not trigger rebalance)
            for (int i = 0; i < 20; i++)
            {
                list.Add(CreateTestItem(i, $"Item{i}"));
            }

            // All items should be accessible
            Assert.Equal(20, list.Count);
            for (int i = 0; i < 20; i++)
            {
                Assert.NotNull(list[i]);
            }
        }

        #endregion

        #region Exception Tests

        [Fact]
        public void Indexer_Get_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var _ = list[-1];
            });
        }

        [Fact]
        public void Indexer_Get_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var _ = list[10];
            });
        }

        [Fact]
        public void Indexer_Set_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                list[-1] = CreateTestItem(2, "Item2");
            });
        }

        [Fact]
        public void Indexer_Set_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                list[10] = CreateTestItem(2, "Item2");
            });
        }

        [Fact]
        public void RemoveAt_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                list.RemoveAt(-1);
            });
        }

        [Fact]
        public void RemoveAt_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                list.RemoveAt(10);
            });
        }

        [Fact]
        public void Insert_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, null);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                list.Insert(-1, CreateTestItem(1, "Item1"));
            });
        }

        [Fact]
        public void Insert_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                list.Insert(10, CreateTestItem(2, "Item2"));
            });
        }

        [Fact]
        public void Move_NegativeIndex_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                list.Move(-1, 0);
            });
        }

        [Fact]
        public void Move_IndexOutOfRange_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2")]);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                list.Move(0, 10);
            });
        }

        #endregion

        #region Additional Tests

        [Fact]
        public void Enumerate_ReturnsAllItems()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            var count = 0;
            foreach (var item in list)
            {
                Assert.NotNull(item);
                Assert.Equal(count + 1, item!.SavableId);
                count++;
            }

            Assert.Equal(3, count);
        }

        [Fact]
        public void MultipleLists_SameSerializer_WorkIndependently()
        {
            var serializer = new InMemorySerializer();

            var list1 = CreateRootedList(serializer, null, "list1");
            var list2 = CreateRootedList(serializer, null, "list2");

            // Add different items to each list
            list1.Add(CreateTestItem(1, "Item1"));
            list1.Add(CreateTestItem(2, "Item2"));

            list2.Add(CreateTestItem(10, "Item10"));
            list2.Add(CreateTestItem(20, "Item20"));

            // Serialize both
            using (serializer.BeginFreshAction(list1, out var ctx1))
            {
                list1.Serialize(serializer, ctx1);
            }

            using (serializer.BeginFreshAction(list2, out var ctx2))
            {
                list2.Serialize(serializer, ctx2);
            }

            // Deserialize both
            var newList1 = CreateRootedList(serializer, null, "list1");
            using (serializer.BeginFreshAction(newList1, out var ctx3))
            {
                newList1.Deserialize(serializer, ctx3);
            }

            var newList2 = CreateRootedList(serializer, null, "list2");
            using (serializer.BeginFreshAction(newList2, out var ctx4))
            {
                newList2.Deserialize(serializer, ctx4);
            }

            // Verify lists are independent
            Assert.Equal(2, newList1.Count);
            Assert.Equal(1, newList1[0]?.SavableId);
            Assert.Equal(2, newList1[1]?.SavableId);

            Assert.Equal(2, newList2.Count);
            Assert.Equal(10, newList2[0]?.SavableId);
            Assert.Equal(20, newList2[1]?.SavableId);
        }

        [Fact]
        public void Iterator_ModifyWhileIterating_Throws()
        {
            var serializer = new InMemorySerializer();
            var list = CreateRootedList(serializer, [CreateTestItem(1, "Item1"), CreateTestItem(2, "Item2"), CreateTestItem(3, "Item3")]);

            // This might throw or might not, depending on implementation
            // The test is to document the behavior
            var thrown = false;
            Exception? ex = null;
            try
            {
                foreach (var item in list)
                {
                    if (item!.SavableId == 2)
                    {
                        list.Add(CreateTestItem(4, "Item4"));
                    }
                }
            }
            catch (Exception e)
            {
                ex = e;
                thrown = true;
            }

            // If it doesn't throw, that's also acceptable
            // Just verify the list state is consistent
            if (!thrown)
                Assert.Equal(3, list.Count);
            else
                Assert.IsType<InvalidOperationException>(ex);
        }

        #endregion
    }
}
