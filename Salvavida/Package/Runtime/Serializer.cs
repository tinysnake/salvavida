using Salvavida.DefaultImpl;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Salvavida
{
    public abstract class Serializer : IDisposable
    {
        public readonly struct FreshActionLocker : IDisposable
        {
            public FreshActionLocker(Serializer serializer)
            {
                _serializer = serializer;
                _context = serializer.GetContext();
            }

            private readonly Serializer _serializer;
            private readonly SerializeContext _context;

            public SerializeContext Context => _context;

            public void Dispose()
            {
                _serializer.ReturnContext(_context);
            }
        }

        private readonly PathBuilder _lockedPathBuilder = new();
        private SerializeContext? _serializeContext;
        private int _pathBuilderLocker = 0;
        protected IIdGenerator? _idGen;

        protected Serializer()
        {
        }

        public virtual IIdGenerator IdGenerator
        {
            get
            {
                _idGen ??= DefaultIdGenerator.Default;
                return _idGen;
            }
            set { _idGen = value; }
        }

        public virtual T CreateData<T>() where T : new() => new();

        protected virtual SerializeContext CreateContext() => new();

        protected virtual SerializeContext GetContext()
        {
            _serializeContext ??= CreateContext();
            var originValue = Interlocked.CompareExchange(ref _pathBuilderLocker, 1, 0);
            if (originValue > 0)
                throw new InvalidOperationException("path builder is already in use!");
            _serializeContext.GetFromPool();
            _lockedPathBuilder.Clear();
            _serializeContext.Path = _lockedPathBuilder;
            OnGetContext(_serializeContext, true);
            return _serializeContext;
        }

        protected virtual void OnGetContext(SerializeContext ctx, bool withUniqueLock)
        {
        }

        protected virtual void ReturnContext(SerializeContext ctx)
        {
            var path = ctx.Path;
            path.Clear();
            ctx.ReturnToPool();
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            ctx.Path = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            OnReturnContext(ctx);
            Interlocked.CompareExchange(ref _pathBuilderLocker, 0, 1);
        }

        protected virtual void OnReturnContext(SerializeContext ctx)
        {
        }

        private void ThrowIfPathIsEmpty(PathBuilder pb)
        {
            if (pb.IsEmpty)
                throw new ArgumentNullException("path is empty");
        }

        public FreshActionLocker BeginFreshAction(out SerializeContext ctx)
        {
            var locker = new FreshActionLocker(this);
            ctx = locker.Context;
            return locker;
        }

        public FreshActionLocker BeginFreshAction<T>(T parent, out SerializeContext ctx) where T : ISavable
        {
            var locker = BeginFreshAction(out ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            return locker;
        }

        public void FreshAction<T>(T parent, Action<SerializeContext> action, Action<SerializeContext, Exception>? onFail) where T : ISavable
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
            throw new SalvavidaSerializeException($"serializatin failed on: {nameof(DefaultOnActionFailed)}, at path:  {ctx?.Path.ToString() ?? "(empty)"}", ex);
        }

        public bool FreshHas<T>(T data) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            data.GetParentPathAsSpan(ctx.Path);
            return DoHas(ctx);
        }

        public bool Has<T>(T data, SerializeContext ctx) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));

            using var __s = ctx.Path.UsePush(data.SvId!, PathBuilder.Type.Property);
            return DoHas(ctx);
        }

        public bool Has(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type pathType)
        {
            using var __s = ctx.Path.UsePush(propName, pathType);
            return DoHas(ctx);
        }

        public bool HasNoPushPath(SerializeContext ctx) => DoHas(ctx);

        protected abstract bool DoHas(SerializeContext ctx);

        public void FreshSave<T>(T data) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            DoSaveObject(data, typeof(T), ctx);
        }

        public void FreshSave<T>(ISavable parent, T data, ReadOnlySpan<char> propName, PathBuilder.Type pathBuilderType)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            DoSaveObject(data, typeof(T), ctx);
        }

        public void FreshSave<T>(ISavable parent, T data, Type type, ReadOnlySpan<char> propName, PathBuilder.Type pathBuilderType)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);

            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            DoSaveObject(data, type, ctx);
        }

        public void Save<T>(T? savable, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (savable == null || string.IsNullOrEmpty(savable.SvId))
                throw new ArgumentNullException(nameof(savable));

            using var __s = ctx.Path.UsePush(savable.SvId!, type);
            DoSaveObject(savable, typeof(T), ctx);
        }

        public void Save<T>(T? data, SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type pathBuilderType)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            ThrowIfPathIsEmpty(ctx.Path);

            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            DoSaveObject(data, typeof(T), ctx);
        }

        public void Save<T>(T? data, Type type, SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type pathBuilderType)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            ThrowIfPathIsEmpty(ctx.Path);

            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            DoSaveObject(data, type, ctx);
        }

        public void SaveNoPushPath<T>(T? obj, SerializeContext ctx) => DoSaveObject(obj, typeof(T), ctx);

        public void SaveNoPushPath<T>(T? obj, Type type, SerializeContext ctx) => DoSaveObject(obj, type, ctx);

        protected virtual void DoSaveObject<T>(T obj, Type type, SerializeContext ctx)
        {
            // if (TryDeleteOnNull(obj, ctx))
            //     return;

            try
            {
                //BeforeSerialize(obj, type, ctx);
                DoSaveObjectImpl(obj, type, ctx);
                AfterSerialize(obj, type, ctx);
            }
            catch (Exception ex)
            {
                OnSaveObjectFailed(ctx, ex);
            }
        }

        protected abstract void DoSaveObjectImpl<T>(T obj, SerializeContext ctx);
        protected abstract void DoSaveObjectImpl<T>(T obj, Type type, SerializeContext ctx);

        protected virtual void OnSaveObjectFailed(SerializeContext ctx, Exception ex)
        {
            throw new SalvavidaSerializeException($"serializatin failed on: {nameof(OnSaveObjectFailed)}, at path:  {ctx?.Path.ToString() ?? "(empty)"}", ex);
        }

        public T? FreshRead<T>(ReadOnlySpan<char> svid) where T : ISavable
        {
            if (svid.IsEmpty)
                throw new ArgumentNullException(nameof(svid));
            using var locker = BeginFreshAction(out var ctx);
            using var __s = ctx.Path.UsePush(svid, PathBuilder.Type.Property);
            var result = DoReadObject<T>(ctx);
            return result;
        }

        public T? Read<T>(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            using var __s = ctx.Path.UsePush(propName, type);
            return DoReadObject<T>(ctx);
        }

        public virtual T? ReadNoPushPath<T>(SerializeContext ctx) => DoReadObject<T>(ctx);

        protected virtual T? DoReadObject<T>(SerializeContext ctx)
        {
            var result = DoReadImpl<T>(ctx);
            AfterDeserialize(result, ctx);
            return result;
        }

        protected abstract T? DoReadImpl<T>(SerializeContext ctx);

        public void FreshDelete<T>(T data) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            DeleteNoPushPath(ctx);
        }

        public void Delete<T>(T data, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));

            using var __s = ctx.Path.UsePush(data.SvId!, type);
            DeleteNoPushPath(ctx);
        }

        public void Delete(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            using var __s = ctx.Path.UsePush(propName, type);
            DeleteNoPushPath(ctx);
        }

        public void DeleteNoPushPath(SerializeContext ctx)
        {
            try
            {
                DoDelete(ctx);
            }
            catch (Exception ex)
            {
                OnDeleteFailed(ctx, ex);
            }
        }

        protected abstract void DoDelete(SerializeContext ctx);

        protected virtual void OnDeleteFailed(SerializeContext ctx, Exception ex)
        {
            throw new SalvavidaSerializeException($"serializatin failed on: {nameof(OnDeleteFailed)}, at path:  {ctx?.Path.ToString() ?? "(empty)"}", ex);
        }

        public void FreshDeleteAll<T>(T savable) where T : ISavable
        {
            if (savable == null)
                throw new ArgumentNullException(nameof(savable));
            using var locker = BeginFreshAction(out var ctx);
            savable.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            DeleteAllNoPushPath(ctx);
        }

        public void DeleteAll<T>(T savable, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (savable == null || string.IsNullOrEmpty(savable.SvId))
                throw new ArgumentNullException(nameof(savable));
            using var __s = ctx.Path.UsePush(savable.SvId!, type);
            DeleteAllNoPushPath(ctx);
        }

        public void DeleteAll(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            using var __s = ctx.Path.UsePush(propName, type);
            DeleteAllNoPushPath(ctx);
        }

        public void DeleteAllNoPushPath(SerializeContext ctx)
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

        public IEnumerable<string> ListCollectionIds(SerializeContext ctx, string propName)
        {
            using var _ = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            return ListCollectionIds(ctx);
        }

        /// <summary>
        /// Get all ordered IDs under a collection path (lexicographic order = LexoRank order).
        /// Implementation: scan all child keys under the collection path
        /// sort lexicographically, and return.
        /// </summary>
        public abstract IEnumerable<string> ListCollectionIds(SerializeContext ctx);

        public IEnumerable<string> ListCollectionIds(
            SerializeContext ctx, string propName, int skipCount, int pageSize)
        {
            var _ = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            return ListCollectionIds(ctx, skipCount, pageSize);
        }

        /// <summary>
        /// Get a page of IDs starting from a specific bucket.
        /// Implementation: scan keys matching [0-2]~{chunkId}~*, skip skipCount, collect pageSize.
        /// </summary>
        public abstract IEnumerable<string> ListCollectionIds(
            SerializeContext ctx, int skipCount, int pageSize);

        public IEnumerable<string> ListCollectionIdsPrefix(SerializeContext ctx, string propName, string prefix, int skipCount, int pageSize)
        {
            var _ = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            return ListCollectionIdsPrefix(ctx, prefix, skipCount, pageSize);
        }
        public abstract IEnumerable<string> ListCollectionIdsPrefix(SerializeContext ctx, string prefix, int skipCount, int pageSize);

        protected virtual void OnDeleteAllFailed(SerializeContext ctx, Exception ex)
        {
            throw new SalvavidaSerializeException($"serializatin failed on: {nameof(OnDeleteAllFailed)}, at path: {ctx?.Path.ToString() ?? "(empty)"}", ex);
        }
        // private bool TryDeleteOnNull<T>(T obj, SerializeContext ctx)
        // {
        //     if (obj == null)
        //     {
        //         try
        //         {
        //             DoDelete(ctx);
        //         }
        //         catch (Exception ex)
        //         {
        //             OnDeleteFailed(ctx, ex);
        //         }

        //         return true;
        //     }

        //     return false;
        // }

        protected virtual void AfterDeserialize<T>(T obj, SerializeContext ctx)
        {
            if (obj is not ISavable sv)
                return;
            var path = ctx.Path;
            sv.SvId ??= path.GetSegmentString(^1);
            sv.AfterDeserialize(this, ctx);
            sv.SetDirty(false, false);
        }

        protected virtual void AfterSerialize<T>(T obj, Type t, SerializeContext ctx)
        {
            if (obj is not ISavable sv)
                return;
            sv.SetDirty(false, false);
        }

        public virtual void Dispose()
        {
        }
    }
}