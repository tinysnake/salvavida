using System.Collections.Generic;

namespace Salvavida
{
    public enum CollectionChangedAction
    {
        Add,
        Replace,
        Remove,
        Reset
    }

    public readonly ref struct CollectionChangeInfo<T>
    {
        private CollectionChangeInfo(CollectionChangedAction action, bool isSingleItem, T? newItem = default, T? oldItem = default,
            IList<T>? newItems = null, IList<T>? oldItems = null, int newStartingIndex = -1, int oldStartingIndex = -1)
        {
            Action = action;
            IsSingleItem = isSingleItem;
            NewItem = newItem;
            OldItem = oldItem;
            NewItems = newItems;
            OldItems = oldItems;
            NewStartingIndex = newStartingIndex;
            OldStartingIndex = oldStartingIndex;
        }

        public static CollectionChangeInfo<T> Reset() => new(CollectionChangedAction.Reset, true);

        public static CollectionChangeInfo<T> Add(T newItem, int index) =>
            new(CollectionChangedAction.Add, true, newItem, newStartingIndex: index);

        public static CollectionChangeInfo<T> Add(IList<T> newItems, int index) =>
            new(CollectionChangedAction.Add, false, newItems: newItems, newStartingIndex: index);

        public static CollectionChangeInfo<T> Remove(T oldItem, int index) =>
            new(CollectionChangedAction.Remove, true, oldItem: oldItem, oldStartingIndex: index);

        public static CollectionChangeInfo<T> Remove(IList<T> oldItems, int index) =>
            new(CollectionChangedAction.Remove, false, oldItems: oldItems, oldStartingIndex: index);

        public static CollectionChangeInfo<T> Replace(T oldItem, T newItem, int newIndex) =>
            new(CollectionChangedAction.Replace, true, oldItem: oldItem, oldStartingIndex: newIndex, newItem: newItem, newStartingIndex: newIndex);

        public static CollectionChangeInfo<T> Replace(IList<T> oldItems, int oldIndex, IList<T> newItems, int newIndex) =>
            new(CollectionChangedAction.Replace, false, oldItems: oldItems, oldStartingIndex: oldIndex, newItems: newItems, newStartingIndex: newIndex);

        public CollectionChangedAction Action { get; }
        public bool IsSingleItem { get; }
        public T? NewItem { get; }
        public T? OldItem { get; }
        public IList<T>? NewItems { get; }
        public IList<T>? OldItems { get; }

        public int NewStartingIndex { get; }
        public int OldStartingIndex { get; }
    }

    public delegate void CollectionChanged<TElem>(CollectionChangeInfo<TElem?> changeInfo);

    public interface ISvCollectionChanged<TElem>
    {
        event CollectionChanged<TElem?> CollectionChanged;
    }
}
