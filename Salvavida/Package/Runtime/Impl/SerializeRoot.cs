namespace Salvavida.DefaultImpl
{
    public class SerializeRoot : ISerializeRoot
    {
        public Serializer? Serializer { get; private set; }

        internal void SetSerializer(Serializer serializer) => Serializer = serializer;
    }
}
