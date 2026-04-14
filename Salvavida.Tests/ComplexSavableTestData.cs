using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Salvavida;

namespace Salvavida.Tests
{
    public class CustomData
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    // 标记[Savable]的自定义类型
    [Savable]
    [UseSystemTextJson]
    public partial class SavableCustomData
    {
        public int savableId;
        public string? savableName;
    }

    // 主要测试 Savable 类
    [Savable]
    [UseSystemTextJson]
    public partial class ComplexSavableTestClass
    {
        // 常用基础类型字段
        public int intValue;
        public float floatValue;
        public bool boolValue;
        public string? stringValue;

        [SaveSeparately]
        [JsonIgnore]
        public string? separatelySavedStringValue;

        // 自定义类型字段
        public CustomData? customDataValue;

        // 标记[SaveSeparately]的自定义类型字段
        [SaveSeparately]
        [JsonIgnore]
        public CustomData? separatelySavedCustomDataValue;

        // 标记[Savable]的自定义类型字段
        public SavableCustomData? savableCustomDataValue;

        [SaveSeparately]
        [JsonIgnore]
        public SavableCustomData? separatelySavedSavableCustomDataValue;

        // List<基础类型> 字段
        public List<int>? intList;
        public List<float>? floatList;
        public List<bool>? boolList;
        public List<string>? stringList;
        public List<CustomData?>? customDataList;

        public List<SavableCustomData?>? savableCustomDataList;

        // 标记[SaveSeparately]的 List<基础类型> 字段
        [SaveSeparately]
        [JsonIgnore]
        public List<int>? separatelySavedIntList;
        
        [SaveSeparately]
        [JsonIgnore]
        public List<string>? separatelySavedStringList;

        [SaveSeparately]
        [JsonIgnore]
        public List<CustomData?>? separatelySavedCustomDataList;

        [SaveSeparately]
        [JsonIgnore]
        public List<SavableCustomData?>? separatelySavedSavableCustomDataList;

        [SaveSeparately]
        [JsonIgnore]
        [LazyLoad(Mode = LazyLoadMode.LoadAll, UseAbstractType = true)]
        public List<SavableCustomData?>? separatelySavedSavableCustomDataListLazy;

        // 基础类型[] 字段
        public int[]? intArray;
        public float[]? floatArray;
        public bool[]? boolArray;
        public string[]? stringArray;
        public CustomData[]? customDataArray;
        public SavableCustomData?[]? savableCustomDataArray;
        
        [SaveSeparately]
        [JsonIgnore]
        public int[]? separatelySavedIntArray;
        
        [SaveSeparately]
        [JsonIgnore]
        public string[]? separatelySavedStringArray;

        [SaveSeparately]
        [JsonIgnore]
        public CustomData[]? separatelySavedCustomDataArray;
        
        [SaveSeparately]
        [JsonIgnore]
        public SavableCustomData?[]? separatelySavedSavableCustomDataArray;
        
        [SaveSeparately]
        [JsonIgnore]
        [LazyLoad(Mode = LazyLoadMode.LoadIndividual, BatchLoadCount = 2)]
        public SavableCustomData?[]? separatelySavedSavableCustomDataArrayLazy;

        // Dictionary<string, 基础类型> 字段
        public Dictionary<string, int>? intDict;
        public Dictionary<string, float>? floatDict;
        public Dictionary<string, bool>? boolDict;
        public Dictionary<string, string>? stringDict;
        public Dictionary<string, CustomData?>? customDataDict;
        public Dictionary<string, SavableCustomData?>? savableCustomDataDict;

        // 标记[SaveSeparately]的 Dictionary<string, 基础类型> 字段
        [SaveSeparately]
        [JsonIgnore]
        public Dictionary<string, int>? separatelySavedIntDict;
        
        [SaveSeparately]
        [JsonIgnore]
        public Dictionary<string, string>? separatelySavedStringDict;

        // Dictionary<string, 自定义类型> 字段
        [SaveSeparately]
        [JsonIgnore]
        public Dictionary<string, CustomData?>? separatelySavedCustomDataDict;
        
        [SaveSeparately]
        [JsonIgnore]
        public Dictionary<string, SavableCustomData?>? separatelySavedSavableCustomDataDict;
        
        [SaveSeparately]
        [JsonIgnore]
        [LazyLoad]
        public Dictionary<string, SavableCustomData?>? separatelySavedSavableCustomDataDictLazy;
    }
}