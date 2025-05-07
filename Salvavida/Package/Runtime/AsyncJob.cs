using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Salvavida
{
    public interface IJobJoinable
    {
        int GetJoinableSignature();
    }
    public abstract class AsyncJob : INotifyCompletion
    {
        protected AsyncJob(CancellationToken token)
        {
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

        public virtual void SetComplete()
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

        public virtual int GetJoinableSignature() => 0;
    }

    public abstract class AsyncJob<T> : AsyncJob where T : class, IJobJoinable
    {
        public AsyncJob(IObjectPool<T>.UsingScope tScope, CancellationToken token)
            : base(token)
        {
            _tScope = tScope;
        }

        private IObjectPool<T>.UsingScope _tScope;
        public T? ScopedValue => _tScope.Value;

        public sealed override void SetComplete()
        {
            base.SetComplete();
            _tScope.Dispose();
        }

        public sealed override int GetJoinableSignature()
        {
            return ScopedValue?.GetJoinableSignature() ?? -1;
        }
    }

    public class AsyncVoidJob : AsyncJob
    {
        public AsyncVoidJob(Action action, CancellationToken token)
            : base(token)
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

    public class AsyncVoidJob<T> : AsyncJob<T> where T : class, IJobJoinable
    {
        public AsyncVoidJob(IObjectPool<T>.UsingScope tScope, Action<T> action, CancellationToken token)
            : base(tScope, token)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        private Action<T>? _action;

        protected override void DoRunJob()
        {
            if (_token.IsCancellationRequested)
                return;
            Interlocked.Exchange(ref _action, null)?.Invoke(ScopedValue);
        }

        public void GetResult()
        {
            _token.ThrowIfCancellationRequested();
        }

        public AsyncVoidJob<T> GetAwaiter() => this;

        protected override void CheckJoin(AsyncJob job)
        {
            if (job is not AsyncVoidJob)
                throw new InvalidCastException();
        }

        protected override void JoinedJobOnComplete(AsyncJob job)
        {

        }
    }

    public class AsyncValueJob<TResult> : AsyncJob
    {
        public AsyncValueJob(Func<TResult> valueGetter, CancellationToken token)
            : base(token)
        {
            _valueGetter = valueGetter ?? throw new ArgumentNullException(nameof(valueGetter));
        }

        private Func<TResult>? _valueGetter;

        public TResult? Result { get; private set; }

        protected override void DoRunJob()
        {
            var action = Interlocked.Exchange(ref _valueGetter, null);
            if (action == null)
                return;
            Result = action.Invoke();
        }

        public TResult? GetResult()
        {
            _token.ThrowIfCancellationRequested();
            return Result;
        }

        public AsyncValueJob<TResult> GetAwaiter() => this;

        protected override void CheckJoin(AsyncJob job)
        {
            if (job is not AsyncValueJob<TResult>)
                throw new InvalidCastException();
        }

        protected override void JoinedJobOnComplete(AsyncJob job)
        {
            (job as AsyncValueJob<TResult>)!.Result = Result;
        }
    }

    public class AsyncValueJob<T, TResult> : AsyncJob<T> where T : class, IJobJoinable
    {
        public AsyncValueJob(IObjectPool<T>.UsingScope tScope, Func<T, TResult> valueGetter, CancellationToken token)
            : base(tScope, token)
        {
            _valueGetter = valueGetter ?? throw new ArgumentNullException(nameof(valueGetter));
        }

        private Func<T, TResult>? _valueGetter;

        public TResult? Result { get; private set; }

        protected override void DoRunJob()
        {
            var action = Interlocked.Exchange(ref _valueGetter, null);
            if (action == null)
                return;
            Result = action.Invoke(ScopedValue);
        }

        public TResult? GetResult()
        {
            _token.ThrowIfCancellationRequested();
            return Result;
        }

        public AsyncValueJob<T, TResult> GetAwaiter() => this;

        protected override void CheckJoin(AsyncJob job)
        {
            if (job is not AsyncValueJob<T, TResult>)
                throw new InvalidCastException();
        }

        protected override void JoinedJobOnComplete(AsyncJob job)
        {
            (job as AsyncValueJob<T, TResult>)!.Result = Result;
        }
    }
}
