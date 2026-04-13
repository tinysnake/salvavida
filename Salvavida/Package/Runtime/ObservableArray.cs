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
                    _cur = _arr[_i++];
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
