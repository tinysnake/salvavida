namespace Salvavida
{
    public delegate void PropertyChangeEventHandler<in T>(T obj, string propertyName);

    public interface ISvPropertyChanged<out T>
    {
        event PropertyChangeEventHandler<T> PropertyChanged;
    }
}
