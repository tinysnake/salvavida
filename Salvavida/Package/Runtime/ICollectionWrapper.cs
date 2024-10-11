namespace Salvavida
{
    public interface ICollectionWrapper<TCollection>
    {
        TCollection RetrieveSource();
        void SwapSource(TCollection source);
    }
}
