using System;
using System.Collections.Generic;

namespace Salvavida
{
    public abstract class ObservableCollection : ISavable
    {
        public ObservableCollection(string svid, bool saveSeparately)
        {
            if (string.IsNullOrEmpty(svid))
                throw new ArgumentNullException(nameof(svid));
            SaveSeparately = saveSeparately;
            _svid = svid;
        }

        private readonly string _svid;
        protected bool _isDirty = true;

        public ISavable? SvParent { get; protected set; }
        bool ISavable.IsDirty => _isDirty;

        public abstract bool IsSavableCollection { get; }

        public string? SvId
        {
            get => _svid;
            set { }
        }

        public bool SaveSeparately { get; }

        void ISavable.SetDirty(bool dirty, bool _)
        {
            _isDirty = dirty;
        }

        public void TrySave(Serializer? serializer, PathBuilder pathBuilder)
        {
            if (SvParent == null || string.IsNullOrEmpty(SvId) || !_isDirty)
                return;
            serializer ??= SvParent.GetSerializer();
            if (serializer == null)
                return;
            pathBuilder.Push(SvId, SvHelper.GetPathType(this));
            if (SaveSeparately)
                TrySaveSeparatelyByEvent(serializer, pathBuilder);
            pathBuilder.Pop();
            _isDirty = false;
        }

        protected abstract void TrySaveSeparatelyByEvent(Serializer serializer, PathBuilder pathBuilder);



        protected abstract void TrySaveSource(Serializer serializer, PathBuilder pathBuilder);


        protected void ClearCollection(Serializer serializer, PathBuilder pathBuilder)
        {
            serializer.DeleteAll(pathBuilder);
        }

        void ISavable.SetParent(ISavable? parent)
        {
            SetParent(parent);
        }

        protected void SetParent(ISavable? parent)
        {
            SvParent = parent;
        }

        public virtual void BeforeSerialize(Serializer serializer)
        {
        }

        public virtual void AfterSerialize(Serializer serializer, PathBuilder builder)
        {
        }

        public virtual void AfterDeserialize(Serializer serializer, PathBuilder path)
        {
        }

        public abstract void Invalidate(bool recursively);
    }

    public abstract class ObservableCollection<TCol, TElem> : ObservableCollection, ISavable<TCol>, ISvCollectionChanged<TElem>
        where TCol : ObservableCollection<TCol, TElem>
    {
        protected ObservableCollection(string svid, bool saveSeparately) : base(svid, saveSeparately)
        {

        }

        public event CollectionChanged<TElem?>? CollectionChanged;
        public event PropertyChangeEventHandler<TCol>? PropertyChanged;

        public override void Invalidate(bool _)
        {
            OnCollectionChange(CreateSaveAllEvent());
        }

        protected abstract void OnChildChanged(TElem obj, string propertyName);

        protected void TryWatch(TElem? obj)
        {
            if (obj is not ISavable<TElem> savable)
                return;

            this.SetChild(obj);
            savable.PropertyChanged += OnChildChanged;
        }

        protected void TryUnWatch(TElem? obj)
        {
            if (obj is not ISavable<TElem> savable)
                return;
            savable.SetParent(null);
            savable.PropertyChanged -= OnChildChanged;
        }

        protected abstract CollectionChangeInfo<TElem?> CreateSaveAllEvent();

        protected void OnCollectionChange(CollectionChangeInfo<TElem?> e)
        {
            _isDirty = true;
            var serializer = SvParent?.GetSerializer();
            if (serializer != null)
            {
                if (SaveSeparately)
                {
                    using var action = serializer.BeginFreshAction(out var pathBuilder);
                    this.GetSavePathAsSpan(pathBuilder);
                    TrySaveSeparatelyByEvent(serializer, pathBuilder, e);
                }
                else
                {
                    PropertyChanged?.Invoke((TCol)this, SvId!);
                }
            }
            CollectionChanged?.Invoke(e);
        }

        protected override void TrySaveSeparatelyByEvent(Serializer serializer, PathBuilder pathBuilder)
        {
            TrySaveSeparatelyByEvent(serializer, pathBuilder, CreateSaveAllEvent());
        }

        protected void TrySaveSeparatelyByEvent(Serializer serializer, PathBuilder pathBuilder, CollectionChangeInfo<TElem?> e)
        {
            if (!SaveSeparately)
                throw new NotSupportedException();
            if (!IsSavableCollection)
                TrySaveSource(serializer, pathBuilder);
            else
                TrySaveItems(serializer, pathBuilder, e);
        }

        protected virtual void TrySaveItems(Serializer serializer, PathBuilder pathBuilder, CollectionChangeInfo<TElem?> e)
        {
            if (string.IsNullOrEmpty(SvId) || SvParent == null)
                return;
            switch (e.Action)
            {
                case CollectionChangedAction.Add:
                case CollectionChangedAction.Remove:
                    if (e.IsSingleItem)
                        CollectionSave(serializer, pathBuilder, e.OldItem, e.NewItem, e.Action == CollectionChangedAction.Add ? e.NewStartingIndex : e.OldStartingIndex);
                    else
                        CollectionSave(serializer, pathBuilder, e.OldItems!, e.NewItems!, e.Action == CollectionChangedAction.Add ? e.NewStartingIndex : e.OldStartingIndex);
                    break;
                case CollectionChangedAction.Replace:
                    ReplaceSave(serializer, pathBuilder, e.OldItem as ISavable, e.NewItem as ISavable);
                    break;
                case CollectionChangedAction.Reset:
                    ClearCollection(serializer, pathBuilder);
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        protected void CollectionSave(Serializer serializer, PathBuilder path, TElem? oldItem, TElem? newItem, int startingIndex)
        {
            if (oldItem is ISavable old)
                serializer.Delete(old, path, PathBuilder.Type.Collection);
            if (newItem is ISavable nuevo)
            {
#if DEBUG
                if (newItem is ISaveWithOrder swo && swo.SvOrder != startingIndex)
                    throw new Exception($"index mismatch, svOrder: {swo.SvOrder}, index: {startingIndex}");
#endif
                serializer.Save(nuevo, path, PathBuilder.Type.Collection);
            }
        }

        protected void CollectionSave(Serializer serializer, PathBuilder path, IList<TElem?> oldItems, IList<TElem?> newItems, int startingIndex)
        {
            if (oldItems != null)
            {
                for (var i = 0; i < oldItems.Count; i++)
                {
                    var oldItem = oldItems[i];
                    if (oldItem is ISavable old)
                        serializer.Delete(old, path, PathBuilder.Type.Collection);
                }
            }

            if (newItems != null)
            {
                for (var i = 0; i < newItems.Count; i++)
                {
                    var newItem = newItems[i];
                    if (newItem is ISavable nuevo)
                    {
#if DEBUG
                        if (newItem is ISaveWithOrder swo && swo.SvOrder != startingIndex + i)
                            throw new Exception($"index mismatch, svOrder: {swo.SvOrder}, index: {startingIndex + i}");
#endif
                        serializer.Save(nuevo, path, PathBuilder.Type.Collection);
                    }
                }
            }
        }

        protected void ReplaceSave(Serializer serializer, PathBuilder pathBuilder, ISavable? oldItem, ISavable? newItem)
        {
            if (oldItem != null)
            {
                if (newItem == null || oldItem.SvId != newItem.SvId)
                {
                    serializer.Delete(oldItem, pathBuilder, PathBuilder.Type.Collection);
                }
            }
            if (newItem != null)
                serializer.Save(newItem, pathBuilder, PathBuilder.Type.Collection);
        }

        protected void CollectionUpdateOrder(Serializer serializer, PathBuilder pathBuilder, IList<TElem?> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item is ISavable sv)
                    serializer.UpdateOrder(sv, pathBuilder, i);
            }
        }

    }
}
