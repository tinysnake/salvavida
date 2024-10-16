using System;
using System.Collections.Concurrent;

namespace Salvavida
{
    /// <summary>
    /// Let Save or Read opertions run asynchronously, this is only a half implementation, you need you implement your own other half:
    /// 1. Find a appropriate oppertunity to execute "RunJobs" method, usually in other thread.
    /// 2. Execute "CompletedFinishedJobs" right after "RunJobs" finishs, prefer in main thread.
    /// </summary>
    public abstract partial class AsyncIO : IDisposable
    {
        protected readonly ConcurrentDictionary<int, AsyncJob> _jobs = new();
        protected readonly ConcurrentBag<AsyncJob> _finishedJobs = new();

        public bool HasJobsToRun => _jobs.Count > 0;

        public void QueueJob(AsyncJob job)
        {
            var hashCode = job.PathBuilder.GetHashCode();
            if (_jobs.TryGetValue(hashCode, out var j))
            {
                job.JoinJob(j);
                _jobs.TryUpdate(hashCode, job, j);
            }
            else
                _jobs.TryAdd(hashCode, job);

            AfterQueueJob();
        }

        protected virtual void AfterQueueJob()
        {

        }

        protected void RunJobs()
        {
            while (_jobs.Count > 0)
            {
                var keys = _jobs.Keys;
                foreach (var key in keys)
                {
                    if (_jobs.TryRemove(key, out var job))
                    {
                        job.RunJob();
                        _finishedJobs.Add(job);
                    }
                }
            }
        }

        protected void CompleteFinishedJobs()
        {
            while (_finishedJobs.TryTake(out var job))
            {
                job.SetComplete();
            }
        }

        public abstract void ForceComplete();

        public virtual void Dispose()
        {
        }
    }
}
