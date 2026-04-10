using System;
using System.Collections.Generic;
using Xunit;
using Salvavida;
using System.Text.Json;

namespace Salvavida.Tests
{
    public class ComplexSavableIntegrationTests
    {
        [Fact]
        public void SerializeAndVerify_BasicFields()
        {
            var serializer = new InMemorySerializer();
            var testObj = new ComplexSavableTestClass();
            
            testObj.IntValue = 42;
            testObj.FloatValue = 3.14f;
            testObj.BoolValue = true;
            testObj.StringValue = "test";
            
            using (serializer.BeginFreshAction(out var ctx))
            {
                ctx.Path.Push("testObj", PathBuilder.Type.Property);
                testObj.Serialize(serializer, ctx);
            }

            Assert.True(serializer.Count > 0);

            using (serializer.BeginFreshAction(out var ctx))
            {
                ctx.Path.Push("testObj", PathBuilder.Type.Property);
                var result = serializer.ReadNoPushPath<ComplexSavableTestClass>(ctx);
                Assert.NotNull(result);
                Assert.Equal(42, result.IntValue);
                Assert.True(Math.Abs(3.14f - result.FloatValue) < 0.001f);
                Assert.True(result.BoolValue);
                Assert.Equal("test", result.StringValue);
            }
        }
        
        [Fact]
        public void SerializeAndVerify_NormalCollections()
        {
            var serializer = new InMemorySerializer();
            var testObj = new ComplexSavableTestClass();
            
            testObj.SetIntList([1, 2, 3]);
            testObj.SetFloatList([1.1f, 2.2f, 3.3f]);
            testObj.SetBoolList([true, false, true]);
            testObj.SetStringList(["a", "b", "c"]);
            testObj.SetIntDict(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
            testObj.SetFloatDict(new Dictionary<string, float> { ["x"] = 1.1f, ["y"] = 2.2f });
            testObj.SetBoolDict(new Dictionary<string, bool> { ["k1"] = true, ["k2"] = false });
            testObj.SetStringDict(new Dictionary<string, string> { ["key1"] = "value1", ["key2"] = "value=2" });
            testObj.SetCustomDataList([new CustomData { Id = 1, Name = "one" }, new CustomData { Id = 2, Name = "two" }]);
            testObj.SetCustomDataDict(new Dictionary<string, CustomData> { ["item1"] = new CustomData { Id = 10, Name = "first" }, ["item2"] = new CustomData { Id = 20, Name = "second" } });
            testObj.SetSavableCustomDataList([new SavableCustomData { savableId = 100, savableName = "item1" }, new SavableCustomData { savableId = 200, savableName = "item2" }]);
            testObj.SetIntArray([10, 20, 30]);
            testObj.SetFloatArray([1.1f, 2.2f]);
            testObj.SetBoolArray([true, false, true]);
            testObj.SetStringArray(["x", "y", "z"]);
            testObj.SetCustomDataArray([new CustomData { Id = 1, Name = "a" }, new CustomData { Id = 2, Name = "b" }]);
            testObj.SetSavableCustomDataArray([new SavableCustomData { savableId = 1, savableName = "arr1" }, new SavableCustomData { savableId = 2, savableName = "arr2" }]);
            testObj.SetSavableCustomDataDict(new Dictionary<string, SavableCustomData> { ["key1"] = new SavableCustomData { savableId = 1, savableName = "dict1" }, ["key2"] = new SavableCustomData { savableId = 2, savableName = "dict2" } });
            
            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("testObj", PathBuilder.Type.Property);
            testObj.Serialize(serializer, ctx);

            ctx.Path.Clear();
            ctx.Path.Push("testObj", PathBuilder.Type.Property);
            var result = serializer.ReadNoPushPath<ComplexSavableTestClass>(ctx);

            Assert.NotNull(result);
            
            Assert.Equal(testObj.IntList, result.IntList);
            Assert.Equal(testObj.FloatList, result.FloatList);
            Assert.Equal(testObj.BoolList, result.BoolList);
            Assert.Equal(testObj.StringList, result.StringList);
            Assert.Equal(testObj.IntDict, result.IntDict);
            Assert.Equal(testObj.FloatDict, result.FloatDict);
            Assert.Equal(testObj.BoolDict, result.BoolDict);
            Assert.Equal(testObj.StringDict, result.StringDict);
            Assert.Equivalent(testObj.CustomDataList, result.CustomDataList, true);
            Assert.Equivalent(testObj.CustomDataDict, result.CustomDataDict, true);

            Assert.Equal(testObj.savableCustomDataList.Count, result.SavableCustomDataList.Count);
            Assert.Equal(testObj.SavableCustomDataList[0]!.SavableId, result.SavableCustomDataList[0]!.SavableId);
            Assert.Equal(testObj.SavableCustomDataList[0]!.SavableName, result.SavableCustomDataList[0]!.SavableName);
            Assert.Equal(testObj.SavableCustomDataList[1]!.SavableId, result.SavableCustomDataList[1]!.SavableId);
            Assert.Equal(testObj.SavableCustomDataList[1]!.SavableName, result.SavableCustomDataList[1]!.SavableName);
            
            Assert.Equal(testObj.IntArray, result.IntArray);
            Assert.Equal(testObj.FloatArray, result.FloatArray);
            Assert.Equal(testObj.BoolArray, result.BoolArray);
            Assert.Equal(testObj.StringArray, result.StringArray);
            Assert.Equivalent(testObj.CustomDataArray, result.CustomDataArray, true);
            
            Assert.Equal(testObj.SavableCustomDataArray.Count, result.SavableCustomDataArray.Count);
            Assert.Equal(testObj.SavableCustomDataArray[0]!.SavableId, result.SavableCustomDataArray[0]!.SavableId);
            Assert.Equal(testObj.SavableCustomDataArray[1]!.SavableId, result.SavableCustomDataArray[1]!.SavableId);
            
            Assert.Equal(testObj.SavableCustomDataDict.Count, result.SavableCustomDataDict.Count);
            Assert.Equal(testObj.SavableCustomDataDict["key1"]!.SavableId, result.SavableCustomDataDict["key1"]!.SavableId);
            Assert.Equal(testObj.SavableCustomDataDict["key1"]!.SavableName, result.SavableCustomDataDict["key1"]!.SavableName);
            Assert.Equal(testObj.SavableCustomDataDict["key2"]!.SavableId, result.SavableCustomDataDict["key2"]!.SavableId);
            Assert.Equal(testObj.SavableCustomDataDict["key2"]!.SavableName, result.SavableCustomDataDict["key2"]!.SavableName);
        }
        
        
        [Fact]
        public void SerializeAndVerify_CustomDataValue()
        {
            var serializer = new InMemorySerializer();
            var testObj = new ComplexSavableTestClass();
            
            testObj.CustomDataValue = new CustomData { Id = 123, Name = "testData" };
            
            using (serializer.BeginFreshAction(out var ctx))
            {
                ctx.Path.Push("testObj", PathBuilder.Type.Property);
                testObj.Serialize(serializer, ctx);
            }

            Assert.True(serializer.Count > 0);

            using (serializer.BeginFreshAction(out var ctx))
            {
                ctx.Path.Push("testObj", PathBuilder.Type.Property);
                var result = serializer.ReadNoPushPath<ComplexSavableTestClass>(ctx);
                Assert.NotNull(result);
                Assert.Equivalent(testObj.CustomDataValue, result.CustomDataValue);
            }
        }

        [Fact]
        public void SerializeAndVerify_SavableCustomDataValue()
        {
            var serializer = new InMemorySerializer();
            var testObj = new ComplexSavableTestClass();
            
            testObj.SavableCustomDataValue = new SavableCustomData { SavableId = 456, SavableName = "savableTest" };
            
            using (serializer.BeginFreshAction(out var ctx))
            {
                ctx.Path.Push("testObj", PathBuilder.Type.Property);
                testObj.Serialize(serializer, ctx);
            }

            Assert.True(serializer.Count > 0);

            using (serializer.BeginFreshAction(out var ctx))
            {
                ctx.Path.Push("testObj", PathBuilder.Type.Property);
                var result = serializer.ReadNoPushPath<ComplexSavableTestClass>(ctx);
                Assert.NotNull(result);
                Assert.NotNull(result.SavableCustomDataValue);
                Assert.Equal(456, result.SavableCustomDataValue.SavableId);
                Assert.Equal("savableTest", result.SavableCustomDataValue.SavableName);
            }
        }

        [Fact]
        public void SerializeAndVerify_SeparatelySavedNonSavableData()
        {
            var serializer = new InMemorySerializer();
            var testObj = new ComplexSavableTestClass();
            testObj.SeparatelySavedCustomDataValue = new CustomData { Id = 99, Name = "separated" };
            using var locker = serializer.BeginFreshAction(out var ctx);

            ctx.Path.Push("testObj", PathBuilder.Type.Property);
            testObj.Serialize(serializer, ctx);

            ctx.Path.Clear();
            ctx.Path.Push("testObj", PathBuilder.Type.Property);
            var result = serializer.ReadNoPushPath<ComplexSavableTestClass>(ctx);

            Assert.NotNull(result);
            Assert.NotNull(result.SeparatelySavedCustomDataValue);
            Assert.Equal(99, result.SeparatelySavedCustomDataValue.Id);
            Assert.Equal("separated", result.SeparatelySavedCustomDataValue.Name);
        }
        
        [Fact]
        public void SerializeAndVerify_SeparatelySavedNonSavableCollections()
        {
            var serializer = new InMemorySerializer();
            var testObj = new ComplexSavableTestClass();
            
            testObj.SetSeparatelySavedIntList([999, 1000, 1010]);
            testObj.SetSeparatelySavedStringList(["a", "b", "c"]);
            testObj.SetSeparatelySavedIntArray([100, 200, 300]);
            testObj.SetSeparatelySavedStringArray(["x", "y", "z"]);
            testObj.SetSeparatelySavedIntDict(new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 });
            testObj.SetSeparatelySavedStringDict(new Dictionary<string, string> { ["sep1"] = "value1", ["sep2"] = "value2" });
            testObj.SetSeparatelySavedCustomDataDict(new Dictionary<string, CustomData> { ["d1"] = new CustomData { Id = 1, Name = "data1" }, ["d2"] = new CustomData { Id = 2, Name = "data2" } });
            
            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("testObj", PathBuilder.Type.Property);
            testObj.Serialize(serializer, ctx);
            
            ctx.Path.Clear();
            ctx.Path.Push("testObj", PathBuilder.Type.Property);
            var result = serializer.ReadNoPushPath<ComplexSavableTestClass>(ctx);
            Assert.NotNull(result);
            Assert.Equal(testObj.SeparatelySavedIntList, result.SeparatelySavedIntList);
            Assert.Equal(testObj.SeparatelySavedStringList, result.SeparatelySavedStringList);
            Assert.Equal(testObj.SeparatelySavedIntArray, result.SeparatelySavedIntArray);
            Assert.Equal(testObj.SeparatelySavedStringArray, result.SeparatelySavedStringArray);
            Assert.Equal(testObj.SeparatelySavedIntDict, result.SeparatelySavedIntDict);
            Assert.Equal(testObj.SeparatelySavedStringDict, result.SeparatelySavedStringDict);
            Assert.Equivalent(testObj.SeparatelySavedCustomDataDict, result.SeparatelySavedCustomDataDict, true);
        }

        [Fact]
        public void SerializeAndVerify_SavableCustomDataList()
        {
            var serializer = new InMemorySerializer();
            var testObj = new ComplexSavableTestClass();

            testObj.SetSeparatelySavedSavableCustomDataArray([new SavableCustomData { SavableId = 1, SavableName = "data1" }, new SavableCustomData { SavableId = 2, SavableName = "data2" }]);
            testObj.SetSeparatelySavedSavableCustomDataList([new SavableCustomData { SavableId = 1, SavableName = "data1" }, new SavableCustomData { SavableId = 2, SavableName = "data2" }]);
            testObj.SetSeparatelySavedSavableCustomDataDict(new Dictionary<string, SavableCustomData> { ["key1"] = new SavableCustomData { SavableId = 1, SavableName = "data1" }, ["key2"] = new SavableCustomData { SavableId = 2, SavableName = "data2" } });

            using var locker = serializer.BeginFreshAction(out var ctx);
            ctx.Path.Push("testObj", PathBuilder.Type.Property);
            testObj.Serialize(serializer, ctx);

            
            ctx.Path.Clear();
            ctx.Path.Push("testObj", PathBuilder.Type.Property);
            var result = serializer.ReadNoPushPath<ComplexSavableTestClass>(ctx);
            Assert.NotNull(result);
            Assert.NotNull(result.SeparatelySavedSavableCustomDataArray);
            Assert.NotNull(result.SeparatelySavedSavableCustomDataList);
            Assert.NotNull(result.SeparatelySavedSavableCustomDataDict);
        }
    }
}