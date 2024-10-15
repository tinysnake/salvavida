using System.Threading;

#if USE_UNITASK && !SV_FORCE_TASK
using Task = Cysharp.Threading.Tasks.UniTask;
#else
using Task = System.Threading.Tasks.Task;
#endif

namespace Salvavida
{
    public interface IBackupService
    {
        void Backup();
        Task BackupAsync(AsyncIO asyncio, CancellationToken token);
    }
}
