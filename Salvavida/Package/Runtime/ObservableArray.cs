using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableArray<T> : ObservableCollection<ObservableArray<T>, T>, IList<T?>, IReadOnlyList<T?>, IList, IEnumerable<T?>, IEnumerable, ICollectionWrapper<T?[]>
    {
        public ObservableArray(string propName, T?[] src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _arr = default!;
            _isSavable = SvHelper.CheckIsSavable<T>();
            SwapSource(src, false);
        }

        private T?[] _arr;
        private readonly bool _isSavable;

        public override bool IsSavableCollection => _isSavable;

        public T? this[int index]
        {
            get => _arr[index];
            set
            {
                var oldVal = _arr[index];
                TryUnWatch(oldVal);
                _arr[index] = value;
                OnItemSet(value, index);
                OnCollectionChange(CollectionChangeInfo<T?>.Replace(oldVal, value, index));
            }
        }

        object? IList.this[int index] { get => _arr[index]; set => this[index] = (T?)value; }

        public int Count => _arr.Length;

        public bool IsReadOnly => false;

        bool IList.IsFixedSize => true;

        bool ICollection.IsSynchronized => _arr.IsSynchronized;

        object ICollection.SyncRoot => _arr.SyncRoot;

        public T?[] RetrieveSource() => _arr;

        public void SwapSource(T?[] array)
        {
            _isDirty = true;
            SwapSource(array, true);
        }

        private void SwapSource(T?[] array, bool notifyChanges)
        {
            if (_arr != null)
            {
                for (var i = 0; i < _arr.Length; i++)
                {
                    TryUnWatch(_arr[i]);
                }
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<T?>.Reset());
            }
            _arr = array;
            if (_arr != null)
            {
                for (var i = 0; i < _arr.Length; i++)
                {
                    OnItemSet(_arr[i], i);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private void OnItemSet(T? item, int index)
        {
            if (item is ISaveWithOrder swo)
                swo.SvOrder = index;
            if (item is ISavable sv)
                sv.SvId = index.ToString();
            TryWatch(item);
        }

        protected override void OnChildChanged(T child, string _)
        {
            var index = IndexOf(child);
            if (index >= 0)
                OnCollectionChange(CollectionChangeInfo<T?>.Replace(child, child, index));
        }

        protected override CollectionChangeInfo<T?> CreateSaveAllEvent()
        {
            return CollectionChangeInfo<T?>.Add(_arr, 0);
        }

        protected override void TrySaveSource(Serializer serializer, PathBuilder pathBuilder)
        {
            if (string.IsNullOrEmpty(SvId))
                throw new NullReferenceException(nameof(SvId));
            serializer.SaveArray(_arr, pathBuilder);
        }

        public void Add(T? item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Contains(T? item) => Array.IndexOf(_arr, item) >= 0;

        void ICollection<T?>.CopyTo(T?[] array, int arrayIndex) => _arr.CopyTo(array, arrayIndex);

        public ArrayEnumerator GetEnumerator() => new(_arr);

        IEnumerator<T?> IEnumerable<T?>.GetEnumerator() => GetEnumerator();

        public int IndexOf(T? item) => Array.IndexOf(_arr, item);

        public void Insert(int index, T? item) => throw new NotSupportedException();

        public bool Remove(T? item) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();

        int IList.Add(object value) => throw new NotSupportedException();

        bool IList.Contains(object value) => Contains((T?)value);

        void ICollection.CopyTo(Array array, int index) => _arr.CopyTo(array, index);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        int IList.IndexOf(object value) => IndexOf((T?)value);

        void IList.Insert(int index, object value) => throw new NotSupportedException();

        void IList.Remove(object value) => throw new NotSupportedException();

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
                if (_i <= _arr.Length)
                {
                    _arr[_i++] = _cur;
                    return true;
                }
                _i = _arr.Length + 1;
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
