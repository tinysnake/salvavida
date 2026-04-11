using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    /// <summary>
    /// Abstract base class for ObservableListSavable collections.
    /// Single generic parameter for user-facing API and polymorphism.
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

        public abstract T? this[int index] { get; set; }
        public abstract int Count { get; }
        public abstract void Add(T? item);
        public abstract void Clear();
        public abstract bool Contains(T? item);
        public abstract int IndexOf(T? item);
        public abstract void Insert(int index, T? item);
        public abstract bool Remove(T? item);
        public abstract void RemoveAt(int index);
        public abstract void SwapSource(List<T?>? list);
        public abstract IEnumerator<T?> GetEnumerator();

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

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }

    /// <summary>
    /// CRTP intermediate layer providing precise CollectionChangeInfo type.
    /// </summary>
    public abstract class ObservableListSavableBase<TSelf, T> : ObservableListSavableBase<T>
        where TSelf : ObservableListSavableBase<TSelf, T>
        where T : ISavable
    {
        protected ObservableListSavableBase(string propName, bool saveSeparately)
            : base(propName, saveSeparately)
        {
        }

        protected override void OnChildChanged(T obj, string _)
        {
            _isChildrenDirty = true;
            var index = IndexOf(obj);
            if (index >= 0)
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Replace((ObservableListSavableBase<T>)(object)this, obj, obj, index));
        }
    }
}
