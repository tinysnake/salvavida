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

    public readonly ref struct CollectionChangeInfo<TCol, T>
    {
        private CollectionChangeInfo(TCol source, CollectionChangedAction action, bool isSingleItem, T? newItem = default, T? oldItem = default,
            IList<T>? newItems = null, IList<T>? oldItems = null, int newStartingIndex = -1, int oldStartingIndex = -1)
        {
            SourceCollection = source;
            Action = action;
            IsSingleItem = isSingleItem;
            NewItem = newItem;
            OldItem = oldItem;
            NewItems = newItems;
            OldItems = oldItems;
            NewStartingIndex = newStartingIndex;
            OldStartingIndex = oldStartingIndex;
        }

        public static CollectionChangeInfo<TCol, T> Reset(TCol source) => new(source, CollectionChangedAction.Reset, true);

        public static CollectionChangeInfo<TCol, T> Add(TCol source, T newItem, int index) =>
            new(source, CollectionChangedAction.Add, true, newItem, newStartingIndex: index);

        public static CollectionChangeInfo<TCol, T> Add(TCol source, IList<T> newItems, int index) =>
            new(source, CollectionChangedAction.Add, false, newItems: newItems, newStartingIndex: index);

        public static CollectionChangeInfo<TCol, T> Remove(TCol source, T oldItem, int index) =>
            new(source, CollectionChangedAction.Remove, true, oldItem: oldItem, oldStartingIndex: index);

        public static CollectionChangeInfo<TCol, T> Remove(TCol source, IList<T> oldItems, int index) =>
            new(source, CollectionChangedAction.Remove, false, oldItems: oldItems, oldStartingIndex: index);

        public static CollectionChangeInfo<TCol, T> Replace(TCol source, T oldItem, T newItem, int newIndex) =>
            new(source, CollectionChangedAction.Replace, true, oldItem: oldItem, oldStartingIndex: newIndex, newItem: newItem, newStartingIndex: newIndex);

        public static CollectionChangeInfo<TCol, T> Replace(TCol source, IList<T> oldItems, int oldIndex, IList<T> newItems, int newIndex) =>
            new(source, CollectionChangedAction.Replace, false, oldItems: oldItems, oldStartingIndex: oldIndex, newItems: newItems, newStartingIndex: newIndex);

        public TCol SourceCollection { get; }
        public CollectionChangedAction Action { get; }
        public bool IsSingleItem { get; }
        public T? NewItem { get; }
        public T? OldItem { get; }
        public IList<T>? NewItems { get; }
        public IList<T>? OldItems { get; }

        public int NewStartingIndex { get; }
        public int OldStartingIndex { get; }
    }

    public delegate void CollectionChanged<TCol, TElem>(CollectionChangeInfo<TCol, TElem?> changeInfo);

    public interface ISvCollectionChanged<TCol, TElem>
    {
        event CollectionChanged<TCol, TElem?> CollectionChanged;
    }
}
