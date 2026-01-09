namespace Salvavida
{
    public interface ISavable
    {
        ISavable? SvParent { get; }

        string? SvId { get; set; }
        bool IsDirty { get; }
        bool IsSelfDirty { get; }

        void SetParent(ISavable? parent);
        void SetDirty(bool dirty, bool recursively);

        // void Invalidate(bool recursively);
        //
        // void BeforeSerialize(Serializer serializer, SerializeContext ctx);
        // void AfterSerialize(Serializer serializer, SerializeContext ctx);
        
        void Serialize(Serializer serializer, SerializeContext ctx);
        void AfterDeserialize(Serializer serializer, SerializeContext ctx);
    }

    public interface ISavable<T> : ISavable, ISvPropertyChanged<T>
    {

    }
}