namespace Salvavida.DefaultImpl
{
    public class SerializeRoot : ISerializeRoot
    {
        public Serializer? Serializer { get; private set; }

        public void Save()
        {
            throw new System.NotImplementedException();
        }

        internal void SetSerializer(Serializer serializer) => Serializer = serializer;
    }
}
