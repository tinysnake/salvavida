#if USE_UNITASK && !SV_FORCE_TASK
using System.Threading;
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
