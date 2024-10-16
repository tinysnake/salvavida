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
                var originValue = Interlocked.CompareExchange(ref _serializer._pathBuilderLocker, 1, 0);
                if (originValue > 0)
                    throw new InvalidOperationException("path builder is already in use!");
                serializer._pathBuilder.Clear();
            }

            private readonly Serializer _serializer;

            public PathBuilder PathBuilder => _serializer._pathBuilder;

            public void Dispose()
            {
                Interlocked.CompareExchange(ref _serializer._pathBuilderLocker, 0, 1);
            }
        }

        private readonly PathBuilder _pathBuilder = new();
        private int _pathBuilderLocker = 0;
        protected IIdGenerator? _idGen;

        public IObjectPool<PathBuilder> PathBuilderPool { get; set; } = new DefaultObjectPool<PathBuilder>(() => new PathBuilder(), path => path.Clear(), 10);
        public SavePolicy SavePolicy { get; set; } = SavePolicy.Sync;
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

        private void ThrowIfPathIsEmpty(PathBuilder pb)
        {
            if (pb.IsEmpty)
                throw new ArgumentNullException("path is empty");
        }

        protected virtual bool CheckNotDirty<T>(T value)
        {
            if (value is ISavable sv)
                return !sv.IsDirty;
            return false;
        }

        public FreshActionLocker BeginFreshAction(out PathBuilder pathBuilder)
        {
            pathBuilder = _pathBuilder;
            return new FreshActionLocker(this);
        }

        public bool FreshHas<T>(T data) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var path);
            data.GetParentPathAsSpan(path);
            return Has(path);
        }

        public async Task<bool> FreshHasAsync<T>(T data, CancellationToken token) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            using var pathScope = PathBuilderPool.Get(out var path);
            data.GetParentPathAsSpan(path);
            var job = new AsyncValueJob<bool>(path, () => Has(path), token);
            AsyncIO.QueueJob(job);
            return await job;
        }

        public bool Has<T>(T data, PathBuilder path) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            path.Push(data.SvId!, PathBuilder.Type.Property);
            var result = Has(path);
            path.Pop();
            return result;
        }

        protected abstract bool Has(PathBuilder path);

        public bool HasCollection(PathBuilder path, ReadOnlySpan<char> propName)
        {
            var result = HasCollection(path.Push(propName, PathBuilder.Type.Collection));
            path.Pop();
            return result;
        }

        public abstract bool HasCollection(PathBuilder path);

        public void FreshUpdateIdByPolicy<T>(T data, ReadOnlySpan<char> oldId) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var path);
            data.GetParentPathAsSpan(path);
            // data may not have parent, so path will be empty
            // so no need to check
            DoUpdateId(data, path, oldId);
        }

        public void FreshUpdateIdAsync<T>(T data, ReadOnlyMemory<char> oldId) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            using var pathScope = PathBuilderPool.Get(out var path);
            data.GetParentPathAsSpan(path);
            DoUpdateId(data, path, oldId.Span);
        }

        public void FreshUpdateIdSync<T>(T data, ReadOnlySpan<char> oldId) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            AsyncIO.ForceComplete();
            using var locker = BeginFreshAction(out var path);
            data.GetParentPathAsSpan(path);
            DoUpdateId(data, path, oldId);
        }

        protected abstract void DoUpdateId<T>(T data, PathBuilder parentPath, ReadOnlySpan<char> oldId) where T : ISavable;

        public void FreshUpdateOrderByPolicy(ISavable data, int order)
        {
            if (SavePolicy == SavePolicy.Sync)
                FreshUpdateOrderSync(data, order);
            else
                FreshUpdateOrderAsync(data, order, default);
        }

        public void FreshUpdateOrderSync(ISavable data, int order)
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            AsyncIO.ForceComplete();
            using var locker = BeginFreshAction(out var path);
            data.GetSavePathAsSpan(path);
            ThrowIfPathIsEmpty(path);
            DoUpdateOrder(data, path, order);
        }

        public void FreshUpdateOrderAsync(ISavable data, int order, CancellationToken token)
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            using var pathScope = PathBuilderPool.Get(out var path);
            data.GetSavePathAsSpan(path);
            ThrowIfPathIsEmpty(path);
            AsyncIO.QueueJob(new AsyncVoidJob(path, () => DoUpdateOrder(data, path, order), token));
        }

        public void UpdateOrder(ISavable data, PathBuilder path, int order)
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            path.Push(data.SvId!, PathBuilder.Type.Collection);
            DoUpdateOrder(data, path, order);
            path.Pop();
        }


        public abstract void DoUpdateOrder(ISavable data, PathBuilder path, int order);

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
            AsyncIO.ForceComplete();
            using var locker = BeginFreshAction(out var path);
            data.GetSavePathAsSpan(path);
            ThrowIfPathIsEmpty(path);
            DoSaveObject(data, path);
        }

        public async Task FreshSaveAsync<T>(T data, CancellationToken token) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            using var pathScope = PathBuilderPool.Get(out var path);
            data.GetSavePathAsSpan(path);
            ThrowIfPathIsEmpty(path);
            var job = new AsyncVoidJob(path, () => DoSaveObject(data, path), token);
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
            using var pathScope = PathBuilderPool.Get(out var path);
            parent.GetSavePathAsSpan(path);
            ThrowIfPathIsEmpty(path);
            path.Push(propName.Span, type);
            var job = new AsyncVoidJob(path, () => DoSaveObject(data, path), default);
            AsyncIO.QueueJob(job);
            await job;
        }

        protected void FreshSaveSync<T>(ISavable parent, ReadOnlySpan<char> propName, T data, PathBuilder.Type type)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            if (CheckNotDirty(data))
                return;
            using var locker = BeginFreshAction(out var path);
            parent.GetSavePathAsSpan(path);
            ThrowIfPathIsEmpty(path);
            path.Push(propName, type);
            DoSaveObject(data, path);
        }

        public void Save<T>(T savable, PathBuilder path, PathBuilder.Type type) where T : ISavable
        {
            if (savable == null || string.IsNullOrEmpty(savable.SvId))
                throw new ArgumentNullException(nameof(savable));
            if (CheckNotDirty(savable))
                return;
            path.Push(savable.SvId!, type);
            DoSaveObject(savable, path);
            path.Pop();
        }

        public void SaveObject<T>(T obj, PathBuilder path, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            DoSaveObject(obj, path.Push(propName, type));
            path.Pop();
        }

        protected virtual void DoSaveObject<T>(T obj, PathBuilder path)
        {
            if (CheckNotDirty(obj))
                return;
            ISavable? sv = null;
            if (obj is ISavable x)
                sv = x;
            if (sv != null)
                BeforeSerialize(sv);
            DoSaveObjectImpl(obj, path);
            if (sv != null)
                AfterSerialize(sv, path);
        }

        protected abstract void DoSaveObjectImpl<T>(T obj, PathBuilder path);

        public abstract void SaveList<T>(List<T?> list, PathBuilder path);

        public abstract void SaveArray<T>(T?[] arr, PathBuilder path);

        public abstract void SaveDict<TKey, TValue>(Dictionary<TKey, TValue?> dict, PathBuilder path);

        public T? FreshReadSync<T>(ReadOnlySpan<char> svid) where T : ISavable
        {
            if (svid.IsEmpty)
                throw new ArgumentNullException(nameof(svid));
            AsyncIO.ForceComplete();
            using var locker = BeginFreshAction(out var path);
            path.Push(svid, PathBuilder.Type.Property);
            var result = DoRead<T>(path, out _);
            path.Pop();
            return result;
        }

        public async Task<T?> FreshReadAsync<T>(ReadOnlyMemory<char> svid, CancellationToken token) where T : ISavable
        {
            if (svid.IsEmpty)
                throw new ArgumentNullException(nameof(svid));
            using var pathScope = PathBuilderPool.Get(out var path);
            path.Push(svid.Span, PathBuilder.Type.Property);
            var job = new AsyncValueJob<T?>(path, () => DoRead<T>(path, out _), token);
            AsyncIO.QueueJob(job);
            return await job;
        }

        public T? ReadObject<T>(PathBuilder path, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            var result = DoRead<T>(path.Push(propName, type), out _);
            path.Pop();
            return result;
        }

        protected virtual T? DoRead<T>(PathBuilder path) => DoRead<T>(path, out _);

        protected virtual T? DoRead<T>(PathBuilder path, out int order)
        {
            var result = DoReadImpl<T>(path, out order);
            if (result is ISavable sv)
            {
                sv.SvId = path.GetSegmentString(^1);
                AfterDeserialize(sv, path);
                if (result is ISaveWithOrder swo)
                    swo.SvOrder = order;
            }
            return result;
        }

        protected abstract T? DoReadImpl<T>(PathBuilder path, out int order);

        public T?[]? ReadArray<T>(PathBuilder path, ReadOnlySpan<char> propName, bool saveSeparately)
        {
            var result = DoReadArray<T>(path.Push(propName, PathBuilder.Type.Property), saveSeparately);
            path.Pop();
            return result;
        }

        protected abstract T?[]? DoReadArray<T>(PathBuilder path, bool saveSeparately);

        public List<T?>? ReadList<T>(PathBuilder path, ReadOnlySpan<char> propName, bool saveSeparately)
        {
            var result = DoReadList<T>(path.Push(propName, PathBuilder.Type.Property), saveSeparately);
            path.Pop();
            return result;
        }

        protected abstract List<T?>? DoReadList<T>(PathBuilder path, bool saveSeparately);

        public Dictionary<TKey, TValue?>? ReadDict<TKey, TValue>(PathBuilder path, ReadOnlySpan<char> propName, bool saveSeparately)
        {
            var result = DoReadDict<TKey, TValue>(path.Push(propName, PathBuilder.Type.Property), saveSeparately);
            path.Pop();
            return result;
        }

        protected abstract Dictionary<TKey, TValue?>? DoReadDict<TKey, TValue>(PathBuilder path, bool saveSeparately);

        public void FreshDeleteSync<T>(T data) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            AsyncIO.ForceComplete();
            using var locker = BeginFreshAction(out var path);
            data.GetSavePathAsSpan(path);
            ThrowIfPathIsEmpty(path);
            DoDelete(path);
        }

        public void FreshDeleteAsync<T>(T data, CancellationToken token) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            using var pathScope = PathBuilderPool.Get(out var path);
            data.GetSavePathAsSpan(path);
            ThrowIfPathIsEmpty(path);
            AsyncIO.QueueJob(new AsyncVoidJob(path, () => DoDelete(path), token));
        }

        public void Delete<T>(T data, PathBuilder path, PathBuilder.Type type) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            path.Push(data.SvId!, type);
            DoDelete(path);
            path.Pop();
        }

        public void DeleteObject(PathBuilder path, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            path.Push(propName, type);
            DoDelete(path);
            path.Pop();
        }

        protected abstract void DoDelete(PathBuilder path);

        public void DeleteAll<T>(T savable, PathBuilder path, PathBuilder.Type type) where T : ISavable
        {
            if (savable == null || string.IsNullOrEmpty(savable.SvId))
                throw new ArgumentNullException(nameof(savable));
            DeleteAll(path.Push(savable.SvId!, type));
            path.Pop();
        }

        public void DeleteAll(PathBuilder path, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            DeleteAll(path.Push(propName, type));
            path.Pop();
        }

        public abstract void DeleteAll(PathBuilder path);

        protected virtual void BeforeSerialize(ISavable savable)
        {
            savable.BeforeSerialize(this);
        }
        protected virtual void AfterSerialize(ISavable savable, PathBuilder path)
        {
            savable.AfterSerialize(this, path);
            savable.SetDirty(false, false);
        }
        protected virtual void AfterDeserialize(ISavable savable, PathBuilder path)
        {
            savable.SvId ??= path.GetSegmentString(^1);
            savable.AfterDeserialize(this, path);
            savable.SetDirty(false, false);
        }

        public virtual void Dispose()
        {

        }
    }
}
