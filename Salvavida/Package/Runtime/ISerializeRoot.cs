namespace Salvavida
{
    public interface ISerializeRoot
    {
        Serializer? Serializer { get; }
        void Save();
    }
}
