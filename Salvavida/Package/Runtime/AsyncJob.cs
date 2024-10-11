using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Salvavida
{
    public abstract class AsyncJob : INotifyCompletion
    {
        protected AsyncJob(PathBuilder pathBuilder, CancellationToken token)
        {
            PathBuilder = pathBuilder;
            _token = token;
        }

        private Action? _continuation;
        private CancellationTokenRegistration _cancelReg;
        protected CancellationToken _token;
        private int _jobFinished;
        private int _completed;
        private AsyncJob? _joinedJob;

        public bool IsCompleted => _completed > 0;
        public bool JobFinished => _jobFinished > 0;

        public PathBuilder PathBuilder { get; }

        public void RunJob()
        {
            var origin = Interlocked.Exchange(ref _jobFinished, 1);
            if (origin > 0)
                return;
            if (_token.IsCancellationRequested)
                return;
            DoRunJob();
        }

        protected abstract void DoRunJob();

        public void SetComplete()
        {
            var origin = Interlocked.Exchange(ref _completed, 1);
            if (origin > 0)
                return;
            if (_cancelReg != default)
                _cancelReg.Dispose();
            Interlocked.Exchange(ref _continuation, null)?.Invoke();
            if (_joinedJob != null)
            {
                JoinedJobOnComplete(_joinedJob);
                _joinedJob.SetComplete();
            }
        }

        public void OnCompleted(Action continuation)
        {
            _continuation = continuation;
            if (_token.CanBeCanceled)
                _cancelReg = _token.Register(() => Interlocked.Exchange(ref _continuation, null)?.Invoke());
        }

        public void JoinJob(AsyncJob job)
        {
            if (_joinedJob != null)
                throw new InvalidOperationException("a job can only job 1 extra job");
            CheckJoin(job);
            _joinedJob = job;
        }

        protected abstract void CheckJoin(AsyncJob job);
        protected abstract void JoinedJobOnComplete(AsyncJob job);
    }

    public class AsyncVoidJob : AsyncJob
    {
        public AsyncVoidJob(PathBuilder pathBuilder, Action action, CancellationToken token)
            : base(pathBuilder, token)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        private Action? _action;

        protected override void DoRunJob()
        {
            if (_token.IsCancellationRequested)
                return;
            Interlocked.Exchange(ref _action, null)?.Invoke();
        }

        public void GetResult()
        {
            _token.ThrowIfCancellationRequested();
        }

        public AsyncVoidJob GetAwaiter() => this;

        protected override void CheckJoin(AsyncJob job)
        {
            if (job is not AsyncVoidJob)
                throw new InvalidCastException();
        }

        protected override void JoinedJobOnComplete(AsyncJob job)
        {

        }
    }

    public class AsyncValueJob<T> : AsyncJob
    {
        public AsyncValueJob(PathBuilder pathBuilder, Func<T> valueGetter, CancellationToken token)
            : base(pathBuilder, token)
        {
            _valueGetter = valueGetter ?? throw new ArgumentNullException(nameof(valueGetter));
        }

        private Func<T>? _valueGetter;

        public T? Result { get; private set; }

        protected override void DoRunJob()
        {
            var action = Interlocked.Exchange(ref _valueGetter, null);
            if (action == null)
                return;
            Result = action.Invoke();
        }

        public T? GetResult()
        {
            _token.ThrowIfCancellationRequested();
            return Result;
        }

        public AsyncValueJob<T> GetAwaiter() => this;

        protected override void CheckJoin(AsyncJob job)
        {
            if (job is not AsyncValueJob<T>)
                throw new InvalidCastException();
        }

        protected override void JoinedJobOnComplete(AsyncJob job)
        {
            (job as AsyncValueJob<T>)!.Result = Result;
        }
    }
}
