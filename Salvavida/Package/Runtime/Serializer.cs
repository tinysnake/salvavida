using Salvavida.DefaultImpl;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


#if USE_UNITASK && !SV_FORCE_TASK
using Task = Cysharp.Threading.Tasks.UniTask;
#else
using Task = System.Threading.Tasks.Task;
#endif

namespace Salvavida
{
    public abstract class Serializer : IDisposable
    {
        public readonly struct FreshActionLocker : IDisposable
        {
            public FreshActionLocker(Serializer serializer)
            {
                _serializer = serializer;
                _context = serializer.GetSyncContext();
            }

            private readonly Serializer _serializer;
            private readonly SerializeContext _context;

            public SerializeContext Context => _context;

            public void Dispose()
            {
                _serializer.ContextBuilderPool.Return(_context);
                Interlocked.CompareExchange(ref _serializer._pathBuilderLocker, 0, 1);
            }
        }

        private readonly PathBuilder _lockedPathBuilder = new();
        private int _pathBuilderLocker = 0;
        protected IIdGenerator? _idGen;
        protected Random _random;

        protected Serializer()
        {
            ContextBuilderPool = new DefaultObjectPool<SerializeContext>(CreateContext, ReturnContext, 10);
            PathBuilderPool = new DefaultObjectPool<PathBuilder>(() => new PathBuilder(), x => x.Clear(), 10);
            _random = new((int)DateTimeOffset.UtcNow.Ticks);
        }

        public IObjectPool<SerializeContext> ContextBuilderPool { get; set; }
        public IObjectPool<PathBuilder> PathBuilderPool { get; set; }
        public SavePolicy SavePolicy { get; set; } = SavePolicy.Sync;
        public bool DisableDirtyCheck { get; set; }
        public virtual IIdGenerator IdGenerator
        {
            get
            {
                _idGen ??= DefaultIdGenerator.Default;
                return _idGen;
            }
            set
            {
                _idGen = value;
            }
        }

        public abstract AsyncIO AsyncIO { get; }

        public virtual T CreateData<T>() where T : new()
        {
            var obj = new T();
            if (obj is ISavable sv)
                sv.SvId = IdGenerator.GetId();
            return obj;
        }

        protected virtual SerializeContext CreateContext() => new();

        protected virtual IObjectPool<SerializeContext>.UsingScope GetContextScope(out SerializeContext ctx)
        {
            var ctxScope = ContextBuilderPool.Get(out ctx);
            ctx.GetFromPool();
            ctx.Path = PathBuilderPool.Get();
            OnGetContext(ctx, false);
            return ctxScope;
        }

        protected virtual SerializeContext GetSyncContext()
        {
            AsyncIO.ForceComplete();
            var ctx = ContextBuilderPool.Get();
            ctx.GetFromPool();
            ctx.UniqueLocked = true;
            var originValue = Interlocked.CompareExchange(ref _pathBuilderLocker, 1, 0);
            if (originValue > 0)
                throw new InvalidOperationException("path builder is already in use!");
            _lockedPathBuilder.Clear();
            ctx.Path = _lockedPathBuilder;
            OnGetContext(ctx, true);
            return ctx;
        }

        protected virtual void OnGetContext(SerializeContext ctx, bool withUniqueLock)
        {
        }

        protected virtual void ReturnContext(SerializeContext ctx)
        {
            ctx.ReturnToPool();
            var path = ctx.Path;
            if (ctx.UniqueLocked)
            {
                Interlocked.CompareExchange(ref _pathBuilderLocker, 0, 1);
            }
            else
            {
                PathBuilderPool.Return(path);
            }
            ctx.Path = null;
            OnReturnContext(ctx);
        }

        protected virtual void OnReturnContext(SerializeContext ctx)
        {

        }

        private void ThrowIfPathIsEmpty(PathBuilder pb)
        {
            if (pb.IsEmpty)
                throw new ArgumentNullException("path is empty");
        }

        protected virtual bool CheckNotDirty<T>(T value)
        {
            if (DisableDirtyCheck)
                return false;
            if (value is ISavable sv)
                return !sv.IsDirty;
            return false;
        }

        protected FreshActionLocker BeginFreshAction(out SerializeContext ctxBuilder)
        {
            var locker = new FreshActionLocker(this);
            ctxBuilder = locker.Context;
            return locker;
        }


        public void FreshActionByPolicy<T>(T parent, Action<SerializeContext> action, Action<SerializeContext, Exception> onFail) where T : ISavable
        {
            if (SavePolicy == SavePolicy.Sync)
                FreshActionSync(parent, action, onFail);
            else
                FreshActionAsync(parent, action, onFail, default);
        }

        public void FreshActionAsync<T>(T parent, Action<SerializeContext> action, Action<SerializeContext, Exception> onFail, CancellationToken token) where T : ISavable
        {
            var ctxScope = GetContextScope(out var ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            AsyncIO.QueueJob(new AsyncVoidJob<SerializeContext>(ctxScope, x =>
            {
                try
                {
                    action.Invoke(x);
                }
                catch (Exception ex)
                {
                    if (onFail != null)
                        onFail(ctx, ex);
                    else
                    {
                        DefaultOnActionFailed(ctx, ex);
                    }
                }
            }, token));
        }

        public void FreshActionSync<T>(T parent, Action<SerializeContext> action, Action<SerializeContext, Exception> onFail) where T : ISavable
        {
            using var locker = BeginFreshAction(out var ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            try
            {
                action(ctx);
            }
            catch (Exception ex)
            {
                if (onFail != null)
                    onFail(ctx, ex);
                else
                {
                    DefaultOnActionFailed(ctx, ex);
                }
            }
        }

        protected virtual void DefaultOnActionFailed(SerializeContext ctx, Exception ex)
        {
            throw ex;
        }

        public bool FreshHas<T>(T data) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            data.GetParentPathAsSpan(ctx.Path);
            return Has(ctx);
        }

        public async Task<bool> FreshHasAsync<T>(T data, CancellationToken token) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            var ctxScope = GetContextScope(out var ctx);
            data.GetParentPathAsSpan(ctx.Path);
            var job = new AsyncValueJob<SerializeContext, bool>(ctxScope, x => Has(x), token);
            AsyncIO.QueueJob(job);
            return await job;
        }

        public bool Has<T>(T data, SerializeContext ctx) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            var path = ctx.Path;
            path.Push(data.SvId!, PathBuilder.Type.Property);
            var result = Has(ctx);
            path.Pop();
            return result;
        }

        protected abstract bool Has(SerializeContext ctx);

        public bool HasCollection(SerializeContext ctx, ReadOnlySpan<char> propName)
        {
            var path = ctx.Path;

            path.Push(propName, PathBuilder.Type.Collection);
            bool result = false;
            try
            {
                result = HasCollection(ctx);
            }
            finally
            {
                path.Pop();
            }
            return result;
        }

        public abstract bool HasCollection(SerializeContext ctx);

        public async void FreshUpdateIdByPolicy<T>(T data, ReadOnlyMemory<char> oldId) where T : ISavable
        {
            if (SavePolicy == SavePolicy.Sync)
                FreshUpdateIdSync(data, oldId.Span);
            else
                await FreshUpdateIdAsync(data, oldId, default);
        }

        public async Task FreshUpdateIdAsync<T>(T data, ReadOnlyMemory<char> oldId, CancellationToken token) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            var ctxScope = GetContextScope(out var ctx);
            data.GetParentPathAsSpan(ctx.Path);
            var job = new AsyncVoidJob<SerializeContext>(ctxScope, x =>
            {
                try
                {
                    DoUpdateId(data, ctx, oldId.Span);
                }
                catch (Exception ex)
                {
                    OnUpdateIdFailed(ctx, ex);
                }
            }, token);
            AsyncIO.QueueJob(job);
            await job;
        }

        public void FreshUpdateIdSync<T>(T data, ReadOnlySpan<char> oldId) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            using var locker = BeginFreshAction(out var ctx);
            data.GetParentPathAsSpan(ctx.Path);
            try
            {
                DoUpdateId(data, ctx, oldId);
            }
            catch (Exception ex)
            {
                OnUpdateIdFailed(ctx, ex);
            }
        }

        protected abstract void DoUpdateId<T>(T data, SerializeContext ctx, ReadOnlySpan<char> oldId) where T : ISavable;

        protected virtual void OnUpdateIdFailed(SerializeContext ctx, Exception ex)
        {
            throw ex;
        }

        public async void FreshUpdateOrderByPolicy(ISavable data, int order)
        {
            if (SavePolicy == SavePolicy.Sync)
                FreshUpdateOrderSync(data, order);
            else
                await FreshUpdateOrderAsync(data, order, default);
        }

        public void FreshUpdateOrderSync(ISavable data, int order)
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            using var locker = BeginFreshAction(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            try
            {
                DoUpdateOrder(data, ctx, order);
            }
            catch (Exception ex)
            {
                OnUpdateOrderFailed(ctx, ex);
            }
        }

        public async Task FreshUpdateOrderAsync(ISavable data, int order, CancellationToken token)
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            var ctxScope = GetContextScope(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            var job = new AsyncVoidJob<SerializeContext>(ctxScope, x =>
            {
                try
                {
                    DoUpdateOrder(data, x, order);
                }
                catch (Exception ex)
                {
                    OnUpdateOrderFailed(ctx, ex);
                }
            }, token);
            AsyncIO.QueueJob(job);
            await job;
        }

        public void UpdateOrder(ISavable data, SerializeContext ctx, int order)
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            ctx.Path.Push(data.SvId!, PathBuilder.Type.Collection);
            try
            {
                DoUpdateOrder(data, ctx, order);
            }
            catch (Exception ex)
            {
                OnUpdateOrderFailed(ctx, ex);
            }
            finally
            {
                ctx.Path.Pop();
            }
        }


        protected abstract void DoUpdateOrder(ISavable data, SerializeContext ctx, int order);
        protected virtual void OnUpdateOrderFailed(SerializeContext ctx, Exception ex)
        {
            throw ex;
        }

        public async void FreshSaveByPolicy<T>(T data) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (SavePolicy == SavePolicy.Sync)
                FreshSaveSync(data);
            else
                await FreshSaveAsync(data, default);
        }

        public void FreshSaveSync<T>(T data) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            using var locker = BeginFreshAction(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            DoSaveObject(data, ctx);
        }

        public async Task FreshSaveAsync<T>(T data, CancellationToken token) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            var ctxScope = GetContextScope(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            var job = new AsyncVoidJob<SerializeContext>(ctxScope, x => DoSaveObject(data, x), token);
            AsyncIO.QueueJob(job);
            await job;
        }

        public async void FreshSaveByPolicy<T>(ISavable parent, ReadOnlyMemory<char> propName, T data, PathBuilder.Type type)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            if (SavePolicy == SavePolicy.Sync)
                FreshSaveSync(parent, propName.Span, data, type);
            else
                await FreshSaveAsync(parent, propName, data, type);
        }

        public async Task FreshSaveAsync<T>(ISavable parent, ReadOnlyMemory<char> propName, T data, PathBuilder.Type type)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            var ctxScope = GetContextScope(out var ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            ctx.Path.Push(propName.Span, type);
            var job = new AsyncVoidJob<SerializeContext>(ctxScope, x => DoSaveObject(data, x), default);
            AsyncIO.QueueJob(job);
            await job;
        }

        protected void FreshSaveSync<T>(ISavable parent, ReadOnlySpan<char> propName, T data, PathBuilder.Type type)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            using var locker = BeginFreshAction(out var ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            ctx.Path.Push(propName, type);
            DoSaveObject(data, ctx);
        }

        public void Save<T>(T savable, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (savable == null || string.IsNullOrEmpty(savable.SvId))
                throw new ArgumentNullException(nameof(savable));
            if (CheckNotDirty(savable))
                return;
            ctx.Path.Push(savable.SvId!, type);
            try
            {
                DoSaveObject(savable, ctx);
            }
            finally
            {
                ctx.Path.Pop();
            }
        }

        public void SaveObject<T>(T obj, SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            ctx.Path.Push(propName, type);
            try
            {
                DoSaveObject(obj, ctx);
            }
            finally
            {
                ctx.Path.Pop();
            }
        }

        protected virtual void DoSaveObject<T>(T obj, SerializeContext ctx)
        {
            if (CheckNotDirty(obj))
                return;
            try
            {
                ISavable? sv = null;
                if (obj is ISavable x)
                    sv = x;
                if (sv != null)
                    BeforeSerialize(sv);
                DoSaveObjectImpl(obj, ctx);
                if (sv != null)
                    AfterSerialize(sv, ctx);
            }
            catch (Exception ex)
            {
                OnSaveObjectFailed(ctx, ex);
            }
        }

        protected abstract void DoSaveObjectImpl<T>(T obj, SerializeContext ctx);

        protected virtual void OnSaveObjectFailed(SerializeContext ctx, Exception ex)
        {
            throw ex;
        }

        public abstract void SaveList<T>(List<T?> list, SerializeContext ctx);

        public abstract void SaveArray<T>(T?[] arr, SerializeContext ctx);

        public abstract void SaveDict<TKey, TValue>(Dictionary<TKey, TValue?> dict, SerializeContext ctx);

        public T? FreshReadSync<T>(ReadOnlySpan<char> svid) where T : ISavable
        {
            if (svid.IsEmpty)
                throw new ArgumentNullException(nameof(svid));
            using var locker = BeginFreshAction(out var ctx);
            ctx.Path.Push(svid, PathBuilder.Type.Property);
            var result = DoRead<T>(ctx, out _);
            ctx.Path.Pop();
            return result;
        }

        public async Task<T?> FreshReadAsync<T>(ReadOnlyMemory<char> svid, CancellationToken token) where T : ISavable
        {
            if (svid.IsEmpty)
                throw new ArgumentNullException(nameof(svid));
            var ctxScope = GetContextScope(out var ctx);
            ctx.Path.Push(svid.Span, PathBuilder.Type.Property);
            var job = new AsyncValueJob<SerializeContext, T?>(ctxScope, x => DoRead<T>(x, out _), token);
            AsyncIO.QueueJob(job);
            return await job;
        }

        public T? ReadObject<T>(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            ctx.Path.Push(propName, type);
            var result = DoRead<T>(ctx, out _);
            ctx.Path.Pop();
            return result;
        }

        protected virtual T? DoRead<T>(SerializeContext ctx) => DoRead<T>(ctx, out _);

        protected virtual T? DoRead<T>(SerializeContext ctx, out int order)
        {
            var result = DoReadImpl<T>(ctx, out order);
            if (result is ISavable sv)
            {
                sv.SvId = ctx.Path.GetSegmentString(^1);
                AfterDeserialize(sv, ctx);
                if (result is ISaveWithOrder swo)
                    swo.SvOrder = order;
            }
            return result;
        }

        protected abstract T? DoReadImpl<T>(SerializeContext ctx, out int order);

        public T?[]? ReadArray<T>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately)
        {
            ctx.Path.Push(propName, PathBuilder.Type.Property);
            var result = DoReadArray<T>(ctx, saveSeparately);
            ctx.Path.Pop();
            return result;
        }

        protected abstract T?[]? DoReadArray<T>(SerializeContext ctx, bool saveSeparately);

        public List<T?>? ReadList<T>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately)
        {
            var path = ctx.Path;
            path.Push(propName, PathBuilder.Type.Property);
            var result = DoReadList<T>(ctx, saveSeparately);
            path.Pop();
            return result;
        }

        protected abstract List<T?>? DoReadList<T>(SerializeContext ctx, bool saveSeparately);

        public Dictionary<TKey, TValue?>? ReadDict<TKey, TValue>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately)
        {
            var path = ctx.Path;
            path.Push(propName, PathBuilder.Type.Property);
            var result = DoReadDict<TKey, TValue>(ctx, saveSeparately);
            path.Pop();
            return result;
        }

        protected abstract Dictionary<TKey, TValue?>? DoReadDict<TKey, TValue>(SerializeContext ctx, bool saveSeparately);

        public async void FreshDeleteByPolicy<T>(T data) where T : ISavable
        {
            if (SavePolicy == SavePolicy.Sync)
                FreshDeleteSync(data);
            else
                await FreshDeleteAsync(data, default);
        }

        public void FreshDeleteSync<T>(T data) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            try
            {
                DoDelete(ctx);
            }
            catch (Exception ex)
            {
                OnDeleteFailed(ctx, ex);
            }
        }

        public async Task FreshDeleteAsync<T>(T data, CancellationToken token) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            var ctxScope = GetContextScope(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            var job = new AsyncVoidJob<SerializeContext>(ctxScope, x =>
            {
                try
                {
                    DoDelete(x);
                }
                catch (Exception ex)
                {
                    OnDeleteFailed(ctx, ex);
                }
            }, token);
            AsyncIO.QueueJob(job);
            await job;
        }

        public void Delete<T>(T data, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            var path = ctx.Path;
            path.Push(data.SvId!, type);
            try
            {
                DoDelete(ctx);
            }
            catch (Exception ex)
            {
                OnDeleteFailed(ctx, ex);
            }
            finally
            {
                path.Pop();
            }
        }

        public void DeleteObject(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            var path = ctx.Path;
            path.Push(propName, type);
            try
            {
                DoDelete(ctx);
            }
            catch (Exception ex)
            {
                OnDeleteFailed(ctx, ex);
            }
            finally
            {
                path.Pop();
            }
        }

        protected abstract void DoDelete(SerializeContext ctx);

        protected virtual void OnDeleteFailed(SerializeContext ctx, Exception ex)
        {
            throw ex;
        }

        public async void FreshDeleteAllByPolicy<T>(T savable) where T : ISavable
        {
            if (SavePolicy == SavePolicy.Sync)
                FreshDeleteAllSync(savable);
            else
                await FreshDeleteAllAsync(savable, default);
        }

        public void FreshDeleteAllSync<T>(T savable) where T : ISavable
        {
            if (savable == null)
                throw new ArgumentNullException(nameof(savable));
            using var locker = BeginFreshAction(out var ctx);
            savable.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            DeleteAll(ctx);
        }

        public async Task FreshDeleteAllAsync<T>(T savable, CancellationToken token) where T : ISavable
        {
            if (savable == null)
                throw new ArgumentNullException(nameof(savable));
            var ctxScope = GetContextScope(out var ctx);
            savable.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            DeleteAll(ctx);
            var job = new AsyncVoidJob<SerializeContext>(ctxScope, x => DeleteAll(x), token);
            AsyncIO.QueueJob(job);
            await job;
        }

        public void DeleteAll<T>(T savable, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (savable == null || string.IsNullOrEmpty(savable.SvId))
                throw new ArgumentNullException(nameof(savable));
            var path = ctx.Path;
            path.Push(savable.SvId!, type);
            try
            {
                DeleteAll(ctx);
            }
            finally
            {
                path.Pop();
            }
        }

        public void DeleteAll(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            var path = ctx.Path;
            path.Push(propName, type);
            try
            {
                DeleteAll(ctx);
            }
            finally
            {
                path.Pop();
            }
        }

        public void DeleteAll(SerializeContext ctx)
        {
            try
            {
                DoDeleteAll(ctx);
            }
            catch (Exception ex)
            {
                OnDeleteAllFailed(ctx, ex);
            }
        }

        protected abstract void DoDeleteAll(SerializeContext ctx);

        protected virtual void OnDeleteAllFailed(SerializeContext ctx, Exception ex)
        {
            throw ex;
        }

        protected virtual void BeforeSerialize(ISavable savable)
        {
            savable.BeforeSerialize(this);
        }
        protected virtual void AfterSerialize(ISavable savable, SerializeContext ctx)
        {
            savable.AfterSerialize(this, ctx);
            savable.SetDirty(false, false);
        }
        protected virtual void AfterDeserialize(ISavable savable, SerializeContext ctx)
        {
            var path = ctx.Path;
            savable.SvId ??= path.GetSegmentString(^1);
            savable.AfterDeserialize(this, ctx);
            savable.SetDirty(false, false);
        }

        public virtual void Dispose()
        {

        }
    }
}
