using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public class ObservableArray<T> : ObservableCollection<ObservableArray<T>, T>, IList<T?>, IReadOnlyList<T?>, IList, IEnumerable<T?>, IEnumerable, ICollectionWrapper<T?[]>
    {
        public ObservableArray(string propName, T?[] src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _arr = default!;
            SwapSource(src, false);
        }

        private T?[] _arr;

        public T? this[int index]
        {
            get => _arr[index];
            set
            {
                var oldVal = _arr[index];
                _arr[index] = value;
                OnItemSet(value, index);
                OnCollectionChange(CollectionChangeInfo<T?>.Replace(oldVal, value, index));
                TryUnWatch(oldVal);
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
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<T?>.Reset());
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

        protected override void TrySaveSeparatelyByEvent(Serializer serializer, SerializeContext? ctx, CollectionChangeInfo<T?> e)
        {
            if (!SaveSeparately)
                throw new NotSupportedException();
            if (string.IsNullOrEmpty(SvId))
                throw new NullReferenceException(nameof(SvId));
            if (ctx == null)
                serializer.FreshActionByPolicy(this, path => serializer.SaveArray(_arr, path), null);
            else
                serializer.SaveArray(_arr, ctx);
        }

        public bool Contains(T? item) => Array.IndexOf(_arr, item) >= 0;
        bool IList.Contains(object value) => Contains((T?)value);

        public ArrayEnumerator GetEnumerator() => new(_arr);

        IEnumerator<T?> IEnumerable<T?>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<T?>.CopyTo(T?[] array, int arrayIndex) => _arr.CopyTo(array, arrayIndex);

        void ICollection.CopyTo(Array array, int index) => _arr.CopyTo(array, index);

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

    public sealed class ObservableArraySavable<T> : ObservableCollectionSavable<ObservableArraySavable<T>, T>, IList<T?>, IReadOnlyList<T?>, IList, IEnumerable<T?>, IEnumerable, ICollectionWrapper<T?[]>
        where T : ISavable
    {
        public ObservableArraySavable(string propName, T?[] src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _arr = default!;
            SwapSource(src, false);
        }
       
        private T?[] _arr; 

        public T? this[int index]
        {
            get => _arr[index];
            set
            {
                var oldVal = _arr[index];
                _arr[index] = value;
                OnItemSet(value, index);
                OnCollectionChange(CollectionChangeInfo<T?>.Replace(oldVal, value, index));
                TryUnWatch(oldVal);
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
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<T?>.Reset());
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

        public bool Contains(T? item) => Array.IndexOf(_arr, item) >= 0;

        bool IList.Contains(object value) => Contains((T?)value);

        public ArrayEnumerator GetEnumerator() => new(_arr);

        IEnumerator<T?> IEnumerable<T?>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<T?>.CopyTo(T?[] array, int arrayIndex) => _arr.CopyTo(array, arrayIndex);

        void ICollection.CopyTo(Array array, int index) => _arr.CopyTo(array, index);

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
