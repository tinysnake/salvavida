using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    /// <summary>
    /// Abstract base class for ObservableArraySavable collections.
    /// Single generic parameter for user-facing API and polymorphism.
    /// </summary>
    /// <typeparam name="T">Element type, must implement ISavable</typeparam>
    public abstract class ObservableArraySavableBase<T> : ObservableCollectionSavable<ObservableArraySavableBase<T>, T>, IList<T?>, IReadOnlyList<T?>, IList
        where T : ISavable
    {
        protected ObservableArraySavableBase(string propName, bool saveSeparately)
            : base(propName, saveSeparately)
        {
        }

        #region Abstract Methods

        public abstract T? this[int index] { get; set; }
        public abstract int Count { get; }
        public abstract bool Contains(T? item);
        public abstract int IndexOf(T? item);
        public abstract void SwapSource(T?[]? array);
        public abstract IEnumerator<T?> GetEnumerator();

        #endregion

        #region Not Supported (Fixed Size)

        void ICollection<T?>.Add(T? item) => throw new NotSupportedException();
        int IList.Add(object? value) => throw new NotSupportedException();
        void ICollection<T?>.Clear() => throw new NotSupportedException();
        void IList.Clear() => throw new NotSupportedException();
        void IList<T?>.Insert(int index, T? item) => throw new NotSupportedException();
        void IList.Insert(int index, object? value) => throw new NotSupportedException();
        bool ICollection<T?>.Remove(T? item) => throw new NotSupportedException();
        void IList.Remove(object? value) => throw new NotSupportedException();
        void IList<T?>.RemoveAt(int index) => throw new NotSupportedException();
        void IList.RemoveAt(int index) => throw new NotSupportedException();

        #endregion

        #region IList Explicit Implementation

        bool ICollection<T?>.IsReadOnly => false;
        bool IList.IsReadOnly => false;
        bool IList.IsFixedSize => true;
        bool ICollection.IsSynchronized => false;
        object? ICollection.SyncRoot => null;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (T?)value;
        }

        bool IList.Contains(object? value) => Contains((T?)value);
        int IList.IndexOf(object? value) => IndexOf((T?)value);

        public void CopyTo(T?[] array, int arrayIndex)
        {
            for (int i = 0; i < Count; i++)
                array[arrayIndex + i] = this[i];
        }

        void ICollection.CopyTo(Array array, int index)
        {
            for (int i = 0; i < Count; i++)
                array.SetValue(this[i], index + i);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion

        protected static string GetPaddedIndex(int index, int totalCount)
        {
            var paddingLength = index / totalCount;
            if(index % totalCount > 0)
                paddingLength++;
            return index.ToString($"D{paddingLength}"); // Pad to the required number of digits for proper lexicographical ordering
        }
    }

    /// <summary>
    /// CRTP intermediate layer providing precise CollectionChangeInfo type.
    /// </summary>
    public abstract class ObservableArraySavableBase<TSelf, T> : ObservableArraySavableBase<T>
        where TSelf : ObservableArraySavableBase<TSelf, T>
        where T : ISavable
    {
        protected ObservableArraySavableBase(string propName, bool saveSeparately)
            : base(propName, saveSeparately)
        {
        }

        protected override void OnChildChanged(T obj, string _)
        {
            _isChildrenDirty = true;
            var index = IndexOf(obj);
            if (index >= 0)
                OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Replace((ObservableArraySavableBase<T>)(object)this, obj, obj, index));
        }
    }
}
