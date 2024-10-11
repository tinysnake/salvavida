namespace Salvavida
{
    public delegate void PropertyChangeEventHandler<T>(T obj, string propertyName);

    public interface ISvPropertyChanged<T>
    {
        event PropertyChangeEventHandler<T> PropertyChanged;
    }
}
