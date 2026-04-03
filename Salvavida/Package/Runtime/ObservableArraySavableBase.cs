using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    /// <summary>
    /// Abstract base class for ObservableArraySavable collections.
    /// Provides common interface for both full-load and lazy-load implementations.
    /// </summary>
    /// <typeparam name="TSelf">The concrete derived type (CRTP pattern)</typeparam>
    /// <typeparam name="T">Element type, must implement ISavable</typeparam>
    public abstract class ObservableArraySavableBase<TSelf, T> : ObservableCollectionSavable<TSelf, T>, IList<T?>, IReadOnlyList<T?>, IList
        where TSelf : ObservableArraySavableBase<TSelf, T>
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

        public abstract IEnumerator<T?> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}
