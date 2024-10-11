using System.Threading.Tasks;

namespace Salvavida.DefaultImpl
{
    public class DefaultAsyncIOImpl : AsyncIO
    {
        private Task? _threadedTask;

        public int RunInternalInMs { get; set; } = 20;

        public override void ForceComplete()
        {
            if (_threadedTask != null && !_threadedTask.IsCompleted)
                _threadedTask.Wait();
        }

        protected override async void AfterQueueJob()
        {
            if (_threadedTask == null || _threadedTask.IsCompleted)
            {
                _threadedTask = RunJobsThreaded();
            }
            await _threadedTask;
        }

        protected async Task RunJobsThreaded()
        {
            await Task.Run(async () =>
            {
                await Task.Delay(RunInternalInMs);
                RunJobs();
            }).ConfigureAwait(true);
            CompleteFinishedJobs();
        }
    }
}
