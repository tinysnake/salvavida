using MemoryPack;
using Salvavida;
using System.ComponentModel;
using UnityEngine;
#nullable disable

namespace Salvavida.Generator.Debug
{
    [Savable]
    [MemoryPackable]
    public partial class MemPacker
    {
        [MemoryPackInclude]
        private string _field0;
        public string field1;
        [MemoryPackIgnore]
        public string field2;
        [MemoryPackInclude]
        private string Prop0 { get; set; }
        public string Prop1 { get; private set; }
        [MemoryPackIgnore]
        public string Prop2 { get; private set; }
    }

    //[Savable(SerializeWithOrder = true)]
    //[Serializable]
    public partial class Person
    {
        [SerializeField]
        private string name;
        [SerializeField]
        private string isActive;
        [SerializeField]
        private string email;
        [SerializeField]
        private long birthday;
        [SerializeField]
        private string about;
        [SerializeField]
        private string apiKey;
        [SerializeField]
        private string[] roles;
        [SerializeField]
        private long createdAt;
        [SerializeField]
        private long updatedAt;
        [SerializeField]
        private string[] visitedPlaces;
        [SerializeField]
        private List<WorkHistoryData> workHistories;
        [SerializeField]
        private HouseData[] houses;
        [SaveSeparately]
        private string[] hobbies;
        [SaveSeparately]
        [NonSerialized]
        public List<InventoryItemData> inventory;

    }
    [Savable]
    [Serializable]
    public partial class WorkHistoryData
    {
        [SerializeField]
        private string company;
        [SerializeField]
        private long joinedIn;
        [SerializeField]
        private int months;
        [SerializeField]
        private float salary;
    }
    [Serializable]
    public class HouseData
    {
        [SerializeField]
        private string address;
        [SerializeField]
        private int size;
        [SerializeField]
        private float price;
    }
    [Savable(SerializeWithOrder = true)]
    [Serializable]
    public partial class InventoryItemData
    {
        [SerializeField]
        private string id;
        [SerializeField]
        private string name;
        [SerializeField]
        private int count;
        [SaveSeparately]
        private string[] attributes;
        [SerializeField]
        private string quality;
    }
}