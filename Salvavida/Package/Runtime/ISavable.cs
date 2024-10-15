namespace Salvavida
{
    public interface ISavable
    {
        ISavable? SvParent { get; }

        string? SvId { get; set; }
        bool IsDirty { get; }

        void SetParent(ISavable? parent);
        void SetDirty(bool dirty, bool recursively);

        void Invalidate(bool recursively);

        void BeforeSerialize(Serializer serializer);
        void AfterSerialize(Serializer serializer, PathBuilder path);
        void AfterDeserialize(Serializer serializer, PathBuilder path);
    }

    public interface ISavable<T> : ISavable, ISvPropertyChanged<T>
    {

    }
}