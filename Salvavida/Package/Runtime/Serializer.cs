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
            set
            {
                _idGen = value;
            }
        }

        public virtual T CreateData<T>() where T : new()
        {
            var obj = new T();
            if (obj is ISavable sv)
                sv.SvId = IdGenerator.GetId();
            return obj;
        }

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

        protected FreshActionLocker BeginFreshAction(out SerializeContext ctx)
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
            return Has(ctx);
        }

        public bool Has<T>(T data, SerializeContext ctx) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));

            using var __s = ctx.Path.UsePush(data.SvId!, PathBuilder.Type.Property);
            return Has(ctx);
        }

        public bool Has(SerializeContext ctx, ReadOnlySpan<char> propName)
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            return Has(ctx);
        }

        public abstract bool Has(SerializeContext ctx);

        public bool HasCollection(SerializeContext ctx, ReadOnlySpan<char> propName)
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            return HasCollection(ctx);
        }

        public abstract bool HasCollection(SerializeContext ctx);

        public void FreshUpdateId<T>(T data, ReadOnlySpan<char> oldId) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));
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
            throw new SalvavidaSerializeException($"serializatin failed on: {nameof(OnUpdateIdFailed)}, at path:  {ctx?.Path.ToString() ?? "(empty)"}", ex);
        }

        public void FreshSave<T>(T data) where T : ISavable
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            data.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            SaveObject(data, ctx);
        }

        public void SaveSelf<T>(T savable, SerializeContext ctx) where T : ISavable
        {
            ThrowIfPathIsEmpty(ctx.Path);
            SaveObject(savable, ctx);
        }

        public void Save<T>(T savable, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (savable == null || string.IsNullOrEmpty(savable.SvId))
                throw new ArgumentNullException(nameof(savable));

            using var __s = ctx.Path.UsePush(savable.SvId!, type);
            SaveObject(savable, ctx);
        }

        public void Save<T>(ISavable parent, SerializeContext ctx, ReadOnlySpan<char> propName, T data, PathBuilder.Type pathBuilderType)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            ThrowIfPathIsEmpty(ctx.Path);

            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            SaveObject(data, ctx);
        }

        public void Save<T>(ISavable parent, SerializeContext ctx, ReadOnlySpan<char> propName, T data, Type type, PathBuilder.Type pathBuilderType)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            ThrowIfPathIsEmpty(ctx.Path);

            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            DoSaveObject(data, type, ctx);
        }

        public void FreshSave<T>(ISavable parent, ReadOnlySpan<char> propName, T data, PathBuilder.Type pathBuilderType)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);
            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            SaveObject(data, ctx);
        }

        public void FreshSave<T>(ISavable parent, ReadOnlySpan<char> propName, T data, Type type, PathBuilder.Type pathBuilderType)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(data));
            using var locker = BeginFreshAction(out var ctx);
            parent.GetSavePathAsSpan(ctx.Path);
            ThrowIfPathIsEmpty(ctx.Path);

            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            DoSaveObject(data, type, ctx);
        }

        public void SaveObject<T>(T? obj, SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type pathBuilderType)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));

            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            SaveObject(obj, ctx);
        }

        public void SaveObject<T>(T? obj, Type type, SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type pathBuilderType)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));

            using var __s = ctx.Path.UsePush(propName, pathBuilderType);
            DoSaveObject(obj, type, ctx);
        }

        private bool TryDeleteOnNull<T>(T obj, SerializeContext ctx)
        {
            if (obj == null)
            {
                try
                {
                    DoDelete(ctx);
                }
                catch (Exception ex)
                {
                    OnDeleteFailed(ctx, ex);
                }
                return true;
            }
            return false;
        }

        public virtual void SaveObject<T>(T? obj, SerializeContext ctx)
        {
            DoSaveObject(obj, typeof(T), ctx);
        }

        protected virtual void DoSaveObject<T>(T obj, Type type, SerializeContext ctx)
        {
            if (TryDeleteOnNull(obj, ctx))
                return;

            try
            {
                //BeforeSerialize(obj, type, ctx);
                DoSaveObjectImpl(obj, type, ctx);
                //AfterSerialize(obj, type, ctx);
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

        //public virtual void SaveList<T>(List<T?> list, SerializeContext ctx, ReadOnlySpan<char> propertyName)
        //{
        //    using var __s = ctx.Path.UsePush(propertyName, PathBuilder.Type.Property);
        //    SaveList(list, ctx);
        //}

        //public virtual void SaveArray<T>(T?[]? arr, SerializeContext ctx, ReadOnlySpan<char> propertyName)
        //{
        //    using var __s = ctx.Path.UsePush(propertyName, PathBuilder.Type.Property);
        //    SaveArray(arr, ctx);
        //}

        //public virtual void SaveDict<TKey, TValue>(Dictionary<TKey, TValue?> dict, SerializeContext ctx, ReadOnlySpan<char> propertyName)
        //{
        //    using var __s = ctx.Path.UsePush(propertyName, PathBuilder.Type.Property);
        //    SaveDict(dict, ctx);
        //}

        //public abstract void SaveList<T>(List<T?>? list, SerializeContext ctx);

        //public abstract void SaveArray<T>(T?[]? arr, SerializeContext ctx);

        //public abstract void SaveDict<TKey, TValue>(Dictionary<TKey, TValue?>? dict, SerializeContext ctx);

        public T? FreshRead<T>(ReadOnlySpan<char> svid) where T : ISavable
        {
            if (svid.IsEmpty)
                throw new ArgumentNullException(nameof(svid));
            using var locker = BeginFreshAction(out var ctx);
            using var __s = ctx.Path.UsePush(svid, PathBuilder.Type.Property);
            var result = ReadObject<T>(ctx);
            return result;
        }

        public T? ReadObject<T>(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            using var __s = ctx.Path.UsePush(propName, type);
            return ReadObject<T>(ctx);
        }

        public virtual T? ReadObject<T>(SerializeContext ctx)
        {
            var result = DoReadImpl<T>(ctx);
            if (result is ISavable sv)
            {
                sv.SvId = ctx.Path.GetSegmentString(^1);
                //if (result is ISaveWithOrder swo)
                //    swo.SvOrder = order;
                sv.SetDirty(false, false);
            }
            AfterDeserialize(result, ctx);
            return result;
        }

        protected abstract T? DoReadImpl<T>(SerializeContext ctx);

        public ObservableArraySavable<T> LoadCollectionSavable<T>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref T?[]? src) where T : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableArraySavable<T>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(this, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        public ObservableListSavable<T> LoadCollectionSavable<T>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref List<T?>? src) where T : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableListSavable<T>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(this, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        public ObservableDictionarySavable<TKey, TValue?> LoadCollectionSavable<TKey, TValue>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref Dictionary<TKey, TValue?>? src)
            where TKey : notnull
            where TValue : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableDictionarySavable<TKey, TValue?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(this, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        public ObservableArray<T?> LoadCollection<T>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref T?[]? src)
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableArray<T?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(this, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        public ObservableList<T?> LoadCollection<T>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref List<T?>? src)
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableList<T?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(this, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        public ObservableDictionary<TKey, TValue?> LoadCollection<TKey, TValue>(SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref Dictionary<TKey, TValue?>? src)
            where TKey : notnull
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableDictionary<TKey, TValue?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(this, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        // public T?[]? ReadArray<T>(SerializeContext ctx, ReadOnlySpan<char> propName)
        // {
        //     using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
        //     return ReadArray<T>(ctx);
        // }
        //
        // public abstract T?[]? ReadArray<T>(SerializeContext ctx);
        //
        // public List<T?>? ReadList<T>(SerializeContext ctx, ReadOnlySpan<char> propName)
        // {
        //     using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
        //     return ReadList<T>(ctx);
        // }
        //
        // public abstract List<T?>? ReadList<T>(SerializeContext ctx);
        //
        // public Dictionary<TKey, TValue?>? ReadDict<TKey, TValue>(SerializeContext ctx, ReadOnlySpan<char> propName)
        // {
        //     using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
        //     return ReadDict<TKey, TValue>(ctx);
        // }
        //
        // public abstract Dictionary<TKey, TValue?>? ReadDict<TKey, TValue>(SerializeContext ctx);

        public void FreshDelete<T>(T data) where T : ISavable
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

        public void Delete<T>(T data, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (data == null || string.IsNullOrEmpty(data.SvId))
                throw new ArgumentNullException(nameof(data));

            using var __s = ctx.Path.UsePush(data.SvId!, type);
            try
            {
                DoDelete(ctx);
            }
            catch (Exception ex)
            {
                OnDeleteFailed(ctx, ex);
            }
        }

        public void DeleteObject(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            if (propName.IsEmpty)
                throw new ArgumentNullException(nameof(propName));
            using var __s = ctx.Path.UsePush(propName, type);
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
            DeleteAll(ctx);
        }

        public void DeleteAll<T>(T savable, SerializeContext ctx, PathBuilder.Type type) where T : ISavable
        {
            if (savable == null || string.IsNullOrEmpty(savable.SvId))
                throw new ArgumentNullException(nameof(savable));
            using var __s = ctx.Path.UsePush(savable.SvId!, type);
            DeleteAll(ctx);
        }

        public void DeleteAll(SerializeContext ctx, ReadOnlySpan<char> propName, PathBuilder.Type type)
        {
            using var __s = ctx.Path.UsePush(propName, type);
            DeleteAll(ctx);
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
            throw new SalvavidaSerializeException($"serializatin failed on: {nameof(OnDeleteAllFailed)}, at path: {ctx?.Path.ToString() ?? "(empty)"}", ex);
        }

        //protected virtual void BeforeSerialize<T>(T obj, SerializeContext ctx)
        //{
        //    if (obj is not ISavable sv)
        //        return;
        //    sv.BeforeSerialize(this, ctx);
        //}
        //protected virtual void AfterSerialize<T>(T obj, SerializeContext ctx)
        //{
        //    if (obj is not ISavable sv)
        //        return;
        //    sv.AfterSerialize(this, ctx);
        //    sv.SetDirty(false, false);
        //}

        protected virtual void AfterDeserialize<T>(T obj, SerializeContext ctx)
        {
            if (obj is not ISavable sv)
                return;
            var path = ctx.Path;
            sv.SvId ??= path.GetSegmentString(^1);
            sv.AfterDeserialize(this, ctx);
            sv.SetDirty(false, false);
        }

        //protected virtual void BeforeSerialize<T>(T obj, Type t, SerializeContext ctx)
        //{
        //    if (obj is not ISavable sv)
        //        return;
        //    sv.BeforeSerialize(this, ctx);
        //}
        //protected virtual void AfterSerialize<T>(T obj, Type t, SerializeContext ctx)
        //{
        //    if (obj is not ISavable sv)
        //        return;
        //    sv.AfterSerialize(this, ctx);
        //    sv.SetDirty(false, false);
        //}

        public virtual void Dispose()
        {

        }
    }
}
