using System;
using System.Threading;

#if USE_UNITASK && !SV_FORCE_TASK
using Task = Cysharp.Threading.Tasks.UniTask;
#else
using Task = System.Threading.Tasks.Task;
#endif

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
        public IBackupService? BackupService { get; set; }

        public T CreateData<T>() where T : new() => Serializer.CreateData<T>();

        public virtual void Dispose()
        {
            Serializer.Dispose();
        }

        public abstract void Load();

        public abstract void Save();

        public virtual void Backup()
        {
            BackupService?.Backup();
        }
    }

    public class Salvavida<TData> : Salvavida, ISalvavida<TData> where TData : SerializeRoot, ISavable, new()
    {
        public Salvavida(string id, Serializer serializer)
            : base(id, serializer)
        {
        }

        protected Action _onDataLoaded;
        protected Func<Task> _onDataLoadedAsync;
        protected Action _onDataSaved;
        protected Func<Task> _onDataSavedAsync;

        public TData? Data { get; private set; }

        public void SetDataLoadedCallbacks(Action onDataLoaded, Func<Task> onDataLoadedAsync)
        {
            _onDataLoaded = onDataLoaded;
            _onDataLoadedAsync = onDataLoadedAsync;
        }

        public void SetDataSavedCallbacks(Action onDataSaved, Func<Task> onDataSavedAsync)
        {
            _onDataSaved = onDataSaved;
            _onDataSavedAsync = onDataSavedAsync;
        }

        public override void Load()
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            var data = Serializer.FreshRead<TData>(Id);
            var needsSave = false;
            if (data == null)
            {
                data = Serializer.CreateData<TData>();
                data.SvId = Id;
                needsSave = true;
            }
            data.SetSerializer(Serializer);
            Data = data;
            _onDataLoaded?.Invoke();
            if (needsSave)
                Save();
        }

        public override void Save()
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            if (Data != null)
                Serializer.FreshSave(Data);
            _onDataSaved?.Invoke();
        }
    }
}
