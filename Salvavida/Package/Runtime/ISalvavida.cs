using System;
using System.Threading;

#if USE_UNITASK && !SV_FORCE_TASK
using Task = Cysharp.Threading.Tasks.UniTask;
#else
using Task = System.Threading.Tasks.Task;
#endif


namespace Salvavida
{
    public interface ISalvavida : IDisposable
    {
        string Id { get; }
        Task SaveAsync(CancellationToken token);
        Task LoadAsync(CancellationToken token);
        Task BackupAsync(CancellationToken token);

        void Save();
        void Load();
        void Backup();

        Serializer Serializer { get; }

        T CreateData<T>() where T : new();
    }

    public interface ISalvavida<T> : ISalvavida where T : ISavable, ISerializeRoot
    {
        T? Data { get; }
    }
}