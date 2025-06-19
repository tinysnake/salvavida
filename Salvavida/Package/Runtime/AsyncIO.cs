using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Salvavida
{
    /// <summary>
    /// Let Save or Read operations run asynchronously, this is only a half implementation, you need you implement your own other half:
    /// 1. Find an appropriate opportunity to execute "RunJobs" method, usually in other thread.
    /// 2. Execute "CompleteFinishedJobs" right after "RunJobs" finishes, prefer in main thread.
    /// </summary>
    public abstract class AsyncIO : IDisposable
    {
        protected readonly Dictionary<int, AsyncJob> _jobs = new();
        protected readonly HashSet<AsyncJob> _jobsToRun = new();
        protected readonly ConcurrentBag<AsyncJob> _finishedJobs = new();
        protected readonly object _lock = new();

        public bool HasJobsToRun => _jobs.Count > 0;

        public void QueueJob(AsyncJob job)
        {
            var hashCode = job.GetHashCode();
            lock (_lock)
            {
                var replaced = false;
                if (_jobs.TryGetValue(hashCode, out var j))
                {
                    replaced = true;
                    job.JoinJob(j);
                    _jobsToRun.Remove(j);
                }
                _jobsToRun.Add(job);
                _jobs[hashCode] = job;

                AfterQueueJob();
            }
        }

        protected virtual void AfterQueueJob()
        {

        }

        protected void RunJobs()
        {
            while (true)
            {
                AsyncJob job = null;
                lock (_lock)
                {
                    if (_jobsToRun.Count <= 0)
                        break;
                    foreach (var j in _jobsToRun)
                    {
                        job = j;
                        break;
                    }
                    if (job == null)
                        break;
                    _jobsToRun.Remove(job);
                    var hashCode = job.GetHashCode();
                    _jobs.Remove(hashCode);
                }

                job.RunJob();
                _finishedJobs.Add(job);
            }
        }

        protected virtual void CompleteFinishedJobs()
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
