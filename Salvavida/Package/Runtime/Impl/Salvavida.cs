using System;

namespace Salvavida.DefaultImpl
{
    public abstract class Salvavida : ISalvavida
    {
        public Salvavida(string id, Serializer serializer)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentNullException(nameof(id));
            Id = id;
            Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public string Id { get; }

        public Serializer Serializer { get; }

        public T CreateData<T>() where T : new() => Serializer.CreateData<T>();

        public virtual void Dispose()
        {
            Serializer.Dispose();
        }

        public abstract void Load();

        public abstract void Save();
    }

    public class Salvavida<TData> : Salvavida, ISalvavida<TData> where TData : ISerializeRoot, ISavable, new()
    {
        public Salvavida(string id, Serializer serializer)
            : base(id, serializer)
        {
        }

        protected Action? _onDataLoaded;
        protected Action? _onDataSaved;

        public TData? Data { get; private set; }

        public void SetDataLoadedCallback(Action onDataLoaded)
        {
            _onDataLoaded = onDataLoaded;
        }

        public void SetDataSavedCallback(Action onDataSaved)
        {
            _onDataSaved = onDataSaved;
        }

        public override void Load()
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            var data = Serializer.FreshRead<TData>(Id);
            if (data == null)
            {
                data = Serializer.CreateData<TData>();
                data.SvId = Id;
            }
            data.SetSerializer(Serializer);
            Data = data;
            _onDataLoaded?.Invoke();
        }

        public override void Save()
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            Data.Save();
            _onDataSaved?.Invoke();
        }
    }
}
