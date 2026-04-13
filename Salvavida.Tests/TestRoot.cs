namespace Salvavida.Tests
{
    public partial class TestRoot<T> : ISavable, ISerializeRoot where T : ISavable
    {
        private Serializer _serializer;
        public Serializer? Serializer => _serializer;

        private T? _data;
        public T? Data
        {
            get => _data;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_data, value))
                    return;
                _data?.SetParent(null);
                _data = value;
                _data?.SetParent(this);
            }
        }

        public ISavable? SvParent => null;

        public string? SvId { get; set; } = "TestRoot";

        public bool IsDirty => false;

        public bool IsSelfDirty => false;

        public void Save()
        {
        }

        public void SetSerializer(Serializer? serializer)
        {
            _serializer = serializer;
        }

        public void SetParent(ISavable? parent)
        {
        }

        public void SetDirty(bool dirty, bool recursively)
        {
        }

        public void Serialize(Serializer serializer, SerializeContext ctx)
        {
        }

        public void AfterDeserialize(Serializer serializer, SerializeContext ctx)
        {
        }
    }
}