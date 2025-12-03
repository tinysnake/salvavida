namespace Salvavida
{
    public interface ISerializeRoot
    {
        Serializer? Serializer { get; }
        void SetSerializer(Serializer? serializer);
        void Save();
    }
}
