using Salvavida.DefaultImpl;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableArray<T> : ObservableCollection<ObservableArray<T>, T>, IList<T?>, IReadOnlyList<T?>, IList, IEnumerable<T?>, IEnumerable, ICollectionWrapper<T?[]>
    {
        public ObservableArray()
        {

        }

        public ObservableArray(string propName, T?[]? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            SwapSource(src, false);
        }

        private T?[]? _arr;

        public override bool IsDirty => _arr != null && base.IsDirty;

        public T? this[int index]
        {
            get => _arr == null ? default : _arr[index];
            set
            {
                if (_arr == null)
                    throw new NullReferenceException(nameof(_arr));
                var oldVal = _arr[index];
                if (EqualityComparer<T?>.Default.Equals(oldVal, value))
                    return;
                _arr[index] = value;
                TryWatch(value);
                OnCollectionChange(CollectionChangeInfo<ObservableArray<T>, T?>.Replace(this, oldVal, value, index));
                TryUnWatch(oldVal);
            }
        }

        object? IList.this[int index] { get => this[index]; set => this[index] = (T?)value; }

        public int Count => _arr?.Length ?? 0;

        public bool IsReadOnly => false;

        bool IList.IsFixedSize => true;

        bool ICollection.IsSynchronized => _arr?.IsSynchronized ?? false;

        object? ICollection.SyncRoot => _arr?.SyncRoot ?? null;

        public T?[]? RetrieveSource() => _arr;

        public object? RetrieveSourceRaw() => _arr;

        public Type CollectionType => typeof(T?[]);

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            serializer.SaveNoPushPath(_arr, ctx);
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            var arr = serializer.ReadNoPushPath<T?[]>(ctx);
            SwapSource(arr, false);
        }

        public void SwapSource(T?[]? array)
        {
            _isDirty = true;
            SwapSource(array, true);
        }

        private void SwapSource(T?[]? array, bool notifyChanges)
        {
            if (_arr != null)
            {
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<ObservableArray<T>, T?>.Reset(this));
                for (var i = 0; i < _arr.Length; i++)
                {
                    TryUnWatch(_arr[i]);
                }
            }
            _arr = array;
            if (_arr != null)
            {
                for (var i = 0; i < _arr.Length; i++)
                {
                    if (notifyChanges)
                        TryWatch(_arr[i]);
                    else
                        OnChildDeserialized(_arr[i]);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        protected override void OnChildChanged(T child, string _)
        {
            _isDirty = true;
            var index = IndexOf(child);
            if (index >= 0)
                OnCollectionChange(CollectionChangeInfo<ObservableArray<T>, T?>.Replace(this, child, child, index));
        }

        private CollectionChangeInfo<ObservableArray<T>, T?> CreateSaveAllEvent()
        {
            if (_arr == null)
                throw new NullReferenceException(nameof(_arr));
            return CollectionChangeInfo<ObservableArray<T>, T?>.Add(this, _arr, 0);
        }

        public bool Contains(T? item) => Array.IndexOf(_arr, item) >= 0;
        bool IList.Contains(object value) => Contains((T?)value);

        public ArrayEnumerator GetEnumerator() => _arr == null ? throw new NullReferenceException(nameof(_arr)) : new(_arr);

        IEnumerator<T?> IEnumerable<T?>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<T?>.CopyTo(T?[] array, int arrayIndex) => _arr?.CopyTo(array, arrayIndex);

        void ICollection.CopyTo(Array array, int index) => _arr?.CopyTo(array, index);

        public int IndexOf(T? item) => Array.IndexOf(_arr, item);

        int IList.IndexOf(object value) => IndexOf((T?)value);

        void IList<T?>.Insert(int index, T? item) => throw new NotSupportedException();

        void IList.Insert(int index, object value) => throw new NotSupportedException();

        void ICollection<T?>.Add(T? item) => throw new NotSupportedException();

        int IList.Add(object value) => throw new NotSupportedException();

        void ICollection<T?>.Clear() => throw new NotSupportedException();

        void IList.Clear() => throw new NotSupportedException();

        void IList.Remove(object value) => throw new NotSupportedException();

        void IList<T?>.RemoveAt(int index) => throw new NotSupportedException();

        bool ICollection<T?>.Remove(T? item) => throw new NotSupportedException();

        void IList.RemoveAt(int index) => throw new NotSupportedException();


        public struct ArrayEnumerator : IEnumerator<T?>
        {
            public ArrayEnumerator(T?[] _arr)
            {
                this._arr = _arr;
                _i = 0;
                _cur = default;
            }

            private readonly T?[] _arr;
            private int _i;
            private T? _cur;

            public readonly T? Current => _cur;

            readonly object? IEnumerator.Current => _cur;

            public bool MoveNext()
            {
                if (_i < _arr.Length)
                {
                    _arr[_i++] = _cur;
                    return true;
                }
                _i = _arr.Length;
                _cur = default;
                return false;
            }

            public void Reset()
            {
                _i = 0;
                _cur = default;
            }

            public void Dispose()
            {
            }
        }
    }

    public sealed class ObservableArraySavable<T> : ObservableArraySavableBase<ObservableArraySavable<T>, T>, ICollectionWrapper<T?[]>
        where T : ISavable
    {
        public ObservableArraySavable(string propName, T?[]? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            SwapSource(src, false);
        }

        private T?[]? _arr;
        private string[]? _idsOnDeserialized;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty)
                    return true;
                if (_arr == null)
                    return false;
                foreach (var item in _arr)
                {
                    if (item == null)
                        continue;
                    if (item.IsDirty)
                        return true;
                }

                return false;
            }
        }

        public override T? this[int index]
        {
            get => _arr == null ? throw new NullReferenceException(nameof(_arr)) : _arr[index];
            set
            {
                if (_arr == null)
                    throw new NullReferenceException(nameof(_arr));
                var oldVal = _arr[index];
                if (EqualityComparer<T?>.Default.Equals(oldVal, value))
                    return;
                _arr[index] = value;
                TryWatch(value);
                OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Replace(this, oldVal, value, index));
                TryUnWatch(oldVal);
            }
        }

        public override int Count => _arr?.Length ?? 0;

        public T?[]? RetrieveSource() => _arr;

        public object? RetrieveSourceRaw() => _arr;

        public Type CollectionType => typeof(T[]);

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);
            if (recursive && _arr != null)
            {
                _isChildrenDirty = dirty;
                foreach (var item in _arr)
                {
                    item?.SetDirty(dirty, recursive);
                }
            }
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            var tempIds = SvHelper.idListPool.Get();
            try
            {
                if (_arr != null)
                {
                    for (var i = 0; i < _arr.Length; i++)
                    {
                        var elem = _arr[i];
                        if (elem == null)
                            continue;
                        if (elem.SvId == null)
                            throw new ArgumentNullException("elem.SvId");
                        using (ctx.Path.UsePush(elem.SvId, PathBuilder.Type.Collection))
                        {
                            if (elem.IsDirty)
                                elem.Serialize(serializer, ctx);
                            if(serializer.HasNoPushPath(ctx))
                                tempIds.Add(elem.SvId);
                        }
                    }
                    serializer.Save(tempIds, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection);
                }
                else
                {
                    serializer.Delete(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection);
                }
                if (_idsOnDeserialized != null)
                {
                    for (var i = 0; i < _idsOnDeserialized.Length; i++)
                    {
                        var oldId = _idsOnDeserialized[i];
                        if (tempIds.IndexOf(oldId) < 0)
                        {
                            serializer.Delete(ctx, oldId, PathBuilder.Type.Collection);
                        }
                    }
                }
            }
            finally
            {
                SvHelper.idListPool.Return(tempIds);
            }

            _idsOnDeserialized = null;
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            string[] tempIds = null;
            using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection))
            {
                if (serializer.HasNoPushPath(ctx))
                    tempIds = serializer.ReadNoPushPath<string[]?>(ctx);
            }
            T?[]? arr;
            if (tempIds == null)
                arr = null;
            else
            {
                _idsOnDeserialized = tempIds;
                arr = new T[_idsOnDeserialized.Length];
                for (var i = 0; i < _idsOnDeserialized.Length; i++)
                {
                    arr[i] = serializer.Read<T?>(ctx, _idsOnDeserialized[i], PathBuilder.Type.Collection);
                }
            }
            SwapSource(arr, false);
        }


        public override void SwapSource(T?[]? array)
        {
            _isDirty = true;
            SwapSource(array, true);
        }

        private void SwapSource(T?[]? array, bool notifyChanges)
        {
            if (_arr != null)
            {
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Reset(this));
                for (var i = 0; i < _arr.Length; i++)
                {
                    TryUnWatch(_arr[i]);
                }
            }
            _arr = array;
            if (_arr != null)
            {
                for (var i = 0; i < _arr.Length; i++)
                {
                    if (notifyChanges)
                        TryWatch(_arr[i]);
                    else
                        OnChildDeserialized(_arr[i]);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private CollectionChangeInfo<ObservableArraySavableBase<T>, T?> CreateSaveAllEvent()
        {
            if (_arr == null)
                throw new NullReferenceException(nameof(_arr));
            return CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Add(this, _arr, 0);
        }

        public override bool Contains(T? item) => Array.IndexOf(_arr, item) >= 0;

        public ArrayEnumerator GetEnumeratorStruct() => _arr == null ? throw new NullReferenceException(nameof(_arr)) : new(_arr);

        public override IEnumerator<T?> GetEnumerator() => GetEnumeratorStruct();

        public override int IndexOf(T? item) => Array.IndexOf(_arr, item);

        public struct ArrayEnumerator : IEnumerator<T?>
        {
            public ArrayEnumerator(T?[] _arr)
            {
                this._arr = _arr;
                _i = 0;
                _cur = default;
            }

            private readonly T?[] _arr;
            private int _i;
            private T? _cur;

            public readonly T? Current => _cur;

            readonly object? IEnumerator.Current => _cur;

            public bool MoveNext()
            {
                if (_i < _arr.Length)
                {
                    _arr[_i++] = _cur;
                    return true;
                }
                _i = _arr.Length;
                _cur = default;
                return false;
            }

            public void Reset()
            {
                _i = 0;
                _cur = default;
            }

            public void Dispose()
            {
            }
        }
    }
}
