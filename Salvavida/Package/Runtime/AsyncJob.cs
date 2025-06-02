using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Salvavida
{
    public abstract class AsyncJob : INotifyCompletion
    {
        protected AsyncJob(int hashCode, CancellationToken token) :
            this(hashCode, 0, token)
        {
        }

        protected AsyncJob(int hashCode, int extraHashCode, CancellationToken token)
        {
            _hashCode = hashCode;
            _extraHashCode = extraHashCode;
            _token = token;
        }

        private Action? _continuation;
        private CancellationTokenRegistration _cancelReg;
        protected CancellationToken _token;
        private int _jobFinished;
        private int _completed;
        private AsyncJob? _joinedJob;
        private int _hashCode;
        protected int _extraHashCode;

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

        protected abstract int GetBaseHashCode();
        protected virtual int GetExtraHashCode() => _extraHashCode;

        public override int GetHashCode()
        {
            var hc = new HashCode();
            hc.Add(GetBaseHashCode());
            hc.Add(_hashCode);
            hc.Add(GetExtraHashCode());
            return hc.ToHashCode();
        }
    }

    public abstract class AsyncJob<T> : AsyncJob where T : class
    {
        public AsyncJob(IObjectPool<T>.UsingScope tScope, CancellationToken token)
            : this(tScope, 0, token)
        {
        }

        public AsyncJob(IObjectPool<T>.UsingScope tScope, int extraHashCode, CancellationToken token)
            : base(GetHashCodeFromScope(tScope), extraHashCode, token)
        {
            _tScope = tScope;
        }

        protected static int GetHashCodeFromScope(IObjectPool<T>.UsingScope tScope)
        {
            if (tScope.Value == null)
                return 0;
            return tScope.Value.GetHashCode();
        }

        private IObjectPool<T>.UsingScope _tScope;
        public T? ScopedValue => _tScope.Value;

        public sealed override void SetComplete()
        {
            base.SetComplete();
            _tScope.Dispose();
        }
    }

    public sealed class AsyncVoidJob : AsyncJob
    {
        public AsyncVoidJob(int hashCode, Action action, CancellationToken token)
            : this(hashCode, 0, action, token)
        {
        }

        public AsyncVoidJob(int hashCode, int extraHashCode, Action action, CancellationToken token)
            : base(hashCode, extraHashCode, token)
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

        protected override int GetBaseHashCode() => -1611251449;
    }

    public sealed class AsyncVoidJob<T> : AsyncJob<T> where T : class
    {
        public AsyncVoidJob(IObjectPool<T>.UsingScope tScope, Action<T> action, CancellationToken token)
            : this(tScope, 0, action, token)
        {
        }

        public AsyncVoidJob(IObjectPool<T>.UsingScope tScope, int extraHashCode, Action<T> action, CancellationToken token)
            : base(tScope, extraHashCode, token)
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
            if (job is not AsyncVoidJob<T>)
                throw new InvalidCastException();
        }

        protected override void JoinedJobOnComplete(AsyncJob job)
        {

        }

        protected override int GetBaseHashCode() => 1042905230;
    }

    public sealed class AsyncValueJob<TResult> : AsyncJob
    {
        public AsyncValueJob(int hashCode, Func<TResult> valueGetter, CancellationToken token)
            : this(hashCode, 0, valueGetter, token)
        {
        }

        public AsyncValueJob(int hashCode, int extraHashCode, Func<TResult> valueGetter, CancellationToken token)
            : base(hashCode, extraHashCode, token)
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

        protected override int GetBaseHashCode() => -470662071;
    }

    public sealed class AsyncValueJob<T, TResult> : AsyncJob<T> where T : class
    {
        public AsyncValueJob(IObjectPool<T>.UsingScope tScope, Func<T, TResult> valueGetter, CancellationToken token)
            : this(tScope, 0, valueGetter, token)
        {
        }

        public AsyncValueJob(IObjectPool<T>.UsingScope tScope, int extraHashCode, Func<T, TResult> valueGetter, CancellationToken token)
            : base(tScope, extraHashCode, token)
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

        protected override int GetBaseHashCode() => 303723914;
    }
}