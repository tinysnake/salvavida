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

        public void Dispose()
        {
            OnDispose();
            Serializer.Dispose();
        }

        protected virtual void OnDispose() { }

        public abstract void Load();

        public abstract Task LoadAsync(CancellationToken token);

        public abstract void Save();

        public abstract Task SaveAsync(CancellationToken token);

        public virtual void Backup()
        {
            BackupService?.Backup();
        }

        public virtual async Task BackupAsync(CancellationToken token)
        {
            if (BackupService == null)
                return;
            await BackupService.BackupAsync(Serializer.AsyncIO, token);
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
            var data = Serializer.FreshReadSync<TData>(Id);
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

        public override async Task LoadAsync(CancellationToken token)
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            var data = await Serializer.FreshReadAsync<TData>(Id.AsMemory(), token);
            var needsSave = false;
            if (data == null)
            {
                data = Serializer.CreateData<TData>();
                data.SvId = Id;
                needsSave = true;
            }
            data.SetSerializer(Serializer);
            Data = data;
#if USE_UNITASK && !SV_FORCE_TASK
            var t = _onDataLoadedAsync != null ? _onDataLoadedAsync.Invoke() : default;
#else
            var t = _onDataLoadedAsync?.Invoke();
            if(t!=null)
#endif
            {
                await t;
            }
            if (needsSave)
                await SaveAsync(token);
        }

        public override void Save()
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            if (Data != null)
                Serializer.FreshSaveSync(Data);
            _onDataSaved?.Invoke();
        }

        public override async Task SaveAsync(CancellationToken token)
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            if (Data != null)
                await Serializer.FreshSaveAsync(Data, token);
#if USE_UNITASK && !SV_FORCE_TASK
            var t = _onDataLoadedAsync != null ? _onDataLoadedAsync.Invoke() : default;
#else
            var t = _onDataLoadedAsync?.Invoke();
            if(t!=null)
#endif
            {
                await t;
            }
        }
    }
}
