using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    /// <summary>
    /// Abstract base class for ObservableListSavable collections.
    /// Provides common interface for both full-load and lazy-load implementations.
    /// </summary>
    /// <typeparam name="T">Element type, must implement ISavable</typeparam>
    public abstract class ObservableListSavableBase<T> : ObservableCollectionSavable<ObservableListSavableBase<T>, T>, IList<T?>, IReadOnlyList<T?>, IList
        where T : ISavable
    {
        protected ObservableListSavableBase(string propName, bool saveSeparately)
            : base(propName, saveSeparately)
        {
        }

        #region Abstract Methods

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        public abstract T? this[int index] { get; set; }

        /// <summary>
        /// Gets the number of elements in the collection.
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// Adds an item to the collection.
        /// </summary>
        public abstract void Add(T? item);

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public abstract void Clear();

        /// <summary>
        /// Determines whether the collection contains a specific value.
        /// </summary>
        public abstract bool Contains(T? item);

        /// <summary>
        /// Determines the index of a specific item.
        /// </summary>
        public abstract int IndexOf(T? item);

        /// <summary>
        /// Inserts an item at the specified index.
        /// </summary>
        public abstract void Insert(int index, T? item);

        /// <summary>
        /// Removes the first occurrence of a specific object.
        /// </summary>
        public abstract bool Remove(T? item);

        /// <summary>
        /// Removes the item at the specified index.
        /// </summary>
        public abstract void RemoveAt(int index);

        /// <summary>
        /// Replaces the entire collection with a new list.
        /// </summary>
        public abstract void SwapSource(List<T?>? list);

        #endregion

        #region IList<T?> Explicit Implementation

        bool ICollection<T?>.IsReadOnly => false;
        bool IList.IsReadOnly => false;
        bool IList.IsFixedSize => false;
        bool ICollection.IsSynchronized => false;
        object? ICollection.SyncRoot => null;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (T?)value;
        }

        int IList.Add(object? value)
        {
            Add((T?)value);
            return Count - 1;
        }

        void IList.Clear() => Clear();

        bool IList.Contains(object? value) => Contains((T?)value);

        int IList.IndexOf(object? value) => IndexOf((T?)value);

        void IList.Insert(int index, object? value) => Insert(index, (T?)value);

        void IList.Remove(object? value) => Remove((T?)value);

        void IList.RemoveAt(int index) => RemoveAt(index);

        public void CopyTo(T?[] array, int arrayIndex)
        {
            for (int i = 0; i < Count; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            for (int i = 0; i < Count; i++)
            {
                array.SetValue(this[i], index + i);
            }
        }

        public abstract IEnumerator<T?> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}
