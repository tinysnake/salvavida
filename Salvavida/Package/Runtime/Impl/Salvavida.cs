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
            Serializer.Dispose();
        }

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

        public TData? Data { get; private set; }

        public override void Load()
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            var data = Serializer.FreshReadSync<TData>(Id);
            if (data != null)
            {
                data.SetSerializer(Serializer);
                Data = data;
            }
        }

        public override async Task LoadAsync(CancellationToken token)
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            var data = await Serializer.FreshReadAsync<TData>(Id.AsMemory(), token);
            if (data != null)
            {
                data.SetSerializer(Serializer);
                Data = data;
            }
        }

        public override void Save()
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            if (Data != null)
                Serializer.FreshSaveSync(Data);
        }

        public override async Task SaveAsync(CancellationToken token)
        {
            if (Serializer == null)
                throw new NullReferenceException(nameof(Serializer));
            if (Data != null)
                await Serializer.FreshSaveAsync(Data, token);
        }
    }
}
