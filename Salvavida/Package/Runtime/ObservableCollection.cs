using System;
using System.Collections.Generic;

namespace Salvavida
{
    public abstract class ObservableCollection : ISavable
    {
        protected ObservableCollection()
        {
            
        }

        protected ObservableCollection(string svid, bool saveSeparately)
        {
            if (string.IsNullOrEmpty(svid))
                throw new ArgumentNullException(nameof(svid));
            SaveSeparately = saveSeparately;
            _svid = svid;
        }

        protected string _svid;
        protected bool _isDirty = true;

        public ISavable? SvParent { get; protected set; }
        public virtual bool IsDirty => _isDirty;
        public bool IsSelfDirty => _isDirty;

        public string? SvId
        {
            get => _svid;
            set { }
        }

        public string SvIdDeserialized => null;

        public bool SaveSeparately { get; protected set; }

        public abstract void Serialize(Serializer? serializer, SerializeContext ctx);
        public abstract void Deserialize(Serializer? serializer, SerializeContext ctx);

        void ISavable.SetDirty(bool dirty, bool _)
        {
            _isDirty = dirty;
        }

        //public void TrySave(Serializer? serializer, SerializeContext ctx)
        //{
        //    if (SvParent == null || string.IsNullOrEmpty(SvId) || !_isDirty)
        //        return;
        //    serializer ??= SvParent.GetSerializer();
        //    if (serializer == null)
        //        return;
        //    var path = ctx.Path;
        //    path.Push(SvId, PathBuilder.Type.Property);
        //    if (SaveSeparately)
        //        TrySaveSeparatelyByEvent(serializer, ctx);
        //    path.Pop();
        //    _isDirty = false;
        //}

        //protected abstract void TrySaveSeparatelyByEvent(Serializer serializer, SerializeContext? ctx);



        protected void ClearCollection(Serializer serializer, SerializeContext? ctx)
        {
            if (ctx == null)
                serializer.FreshDeleteAll(this);
            else
                serializer.DeleteAll(ctx);
        }

        void ISavable.SetParent(ISavable? parent)
        {
            SetParent(parent);
        }

        protected void SetParent(ISavable? parent)
        {
            SvParent = parent;
        }

        public virtual void BeforeSerialize(Serializer serializer, SerializeContext ctx)
        {
        }

        public virtual void AfterSerialize(Serializer serializer, SerializeContext ctx)
        {
        }

        public virtual void AfterDeserialize(Serializer serializer, SerializeContext ctx)
        {
        }

        public abstract void Invalidate(bool recursively);
    }

    public abstract class ObservableCollection<TCol, TElem> : ObservableCollection, ISavable<TCol>, ISvCollectionChanged<TElem>
        where TCol : ObservableCollection<TCol, TElem>
    {
        protected ObservableCollection()
        {
        }

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
            //var serializer = SvParent?.GetSerializer();
            //if (serializer != null)
            //{
            //    if (SaveSeparately)
            //    {
            //        TrySaveSeparatelyByEvent(serializer, null, e);
            //    }
            //    else
            //    {
            //        PropertyChanged?.Invoke((TCol)this, SvId!);
            //    }
            //}

            CollectionChanged?.Invoke(e);
        }

        //protected override void TrySaveSeparatelyByEvent(Serializer serializer, SerializeContext? ctx)
        //{
        //    TrySaveSeparatelyByEvent(serializer, ctx, CreateSaveAllEvent());
        //}

        //protected abstract void TrySaveSeparatelyByEvent(Serializer serializer, SerializeContext? ctx, CollectionChangeInfo<TElem?> e);
    }

    public abstract class ObservableCollectionSavable<TCol, TElem> : ObservableCollection<TCol, TElem>
        where TCol : ObservableCollection<TCol, TElem>
        where TElem : ISavable
    {
        protected ObservableCollectionSavable(string svid, bool saveSeparately) : base(svid, saveSeparately)
        {
        }

//        protected override void TrySaveSeparatelyByEvent(Serializer serializer, SerializeContext? ctx, CollectionChangeInfo<TElem?> e)
//        {
//            if (!SaveSeparately)
//                throw new NotSupportedException();
//            TrySaveItems(serializer, ctx, e);
//        }

//        protected virtual void TrySaveItems(Serializer serializer, SerializeContext? ctx, CollectionChangeInfo<TElem?> e)
//        {
//            if (string.IsNullOrEmpty(SvId) || SvParent == null)
//                return;
//            switch (e.Action)
//            {
//                case CollectionChangedAction.Add:
//                case CollectionChangedAction.Remove:
//                    if (e.IsSingleItem)
//                        CollectionSave(serializer, ctx, e.OldItem, e.NewItem, e.Action == CollectionChangedAction.Add ? e.NewStartingIndex : e.OldStartingIndex);
//                    else
//                        CollectionSave(serializer, ctx, e.OldItems!, e.NewItems!, e.Action == CollectionChangedAction.Add ? e.NewStartingIndex : e.OldStartingIndex);
//                    break;
//                case CollectionChangedAction.Replace:
//                    ReplaceSave(serializer, ctx, e.OldItem, e.NewItem);
//                    break;
//                case CollectionChangedAction.Reset:
//                    ClearCollection(serializer, ctx);
//                    break;
//                default:
//                    throw new NotSupportedException();
//            }
//        }

//        protected void CollectionSave(Serializer serializer, SerializeContext? ctx, TElem? oldItem, TElem? newItem, int startingIndex)
//        {
//            if (oldItem != null)
//            {
//                if (ctx == null)
//                    serializer.FreshDelete(oldItem);
//                else
//                    serializer.Delete(oldItem, ctx, PathBuilder.Type.Collection);
//            }

////#if DEBUG
////            if (newItem is ISaveWithOrder swo && swo.SvOrder != startingIndex)
////                throw new Exception($"index mismatch, svOrder: {swo.SvOrder}, index: {startingIndex}");
////#endif
//            if (newItem != null)
//            {
//                if (ctx == null)
//                    serializer.FreshSave(newItem);
//                else
//                    serializer.Save(newItem, ctx, PathBuilder.Type.Collection);
//            }
//        }

//        protected void CollectionSave(Serializer serializer, SerializeContext? ctx, IList<TElem?> oldItems, IList<TElem?> newItems, int startingIndex)
//        {
//            if (ctx == null)
//            {
//                serializer.FreshAction(this, pathBuilder => { CollectionSaveAction(serializer, pathBuilder, oldItems, newItems, startingIndex); }, null);
//            }
//            else
//                CollectionSaveAction(serializer, ctx, oldItems, newItems, startingIndex);
//        }

//        private void CollectionSaveAction(Serializer serializer, SerializeContext ctx, IList<TElem?> oldItems, IList<TElem?> newItems, int startingIndex)
//        {
//            if (oldItems != null)
//            {
//                for (var i = 0; i < oldItems.Count; i++)
//                {
//                    var oldItem = oldItems[i];
//                    if (oldItem != null)
//                        serializer.Delete(oldItem, ctx, PathBuilder.Type.Collection);
//                }
//            }

//            if (newItems != null)
//            {
//                for (var i = 0; i < newItems.Count; i++)
//                {
//                    var newItem = newItems[i];
//                    if (newItem != null)
//                    {
////#if DEBUG
////                        if (newItem is ISaveWithOrder swo && swo.SvOrder != startingIndex + i)
////                            throw new Exception($"index mismatch, svOrder: {swo.SvOrder}, index: {startingIndex + i}");
////#endif
//                        serializer.Save(newItem, ctx, PathBuilder.Type.Collection);
//                    }
//                }
//            }
//        }

//        protected void ReplaceSave(Serializer serializer, SerializeContext? ctx, TElem? oldItem, TElem? newItem)
//        {
//            if (oldItem != null)
//            {
//                if (newItem == null || oldItem.SvId != newItem.SvId)
//                {
//                    if (ctx == null)
//                        serializer.FreshDeleteAll(oldItem);
//                    else
//                        serializer.Delete(oldItem, ctx, PathBuilder.Type.Collection);
//                }
//            }

//            if (newItem != null)
//            {
//                if (ctx == null)
//                    serializer.FreshSave(newItem);
//                else
//                    serializer.Save(newItem, ctx, PathBuilder.Type.Collection);
//            }
//        }

        //protected void CollectionUpdateOrder(Serializer serializer, SerializeContext? ctx, IList<TElem?> items)
        //{
        //    if (ctx == null)
        //    {
        //        serializer.FreshAction(this, path => { CollectionUpdateOrderAction(serializer, path, items); }, null);
        //    }
        //    else
        //    {
        //        CollectionUpdateOrder(serializer, ctx, items);
        //    }
        //}

        //protected void CollectionUpdateOrderAction(Serializer serializer, SerializeContext ctx, IList<TElem?> items)
        //{
        //    for (var i = 0; i < items.Count; i++)
        //    {
        //        var item = items[i];
        //        if (item != null)
        //            serializer.UpdateOrder(item, ctx, i);
        //    }
        //}
    }
}