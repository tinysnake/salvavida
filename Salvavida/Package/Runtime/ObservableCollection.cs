using System;

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

        protected string? _svid;
        protected bool _isDirty = true;
        protected bool _isChildrenDirty = false;

        public ISavable? SvParent { get; protected set; }
        public virtual bool IsDirty => _isDirty || _isChildrenDirty;
        public bool IsSelfDirty => _isDirty;

        public string? SvId
        {
            get => _svid;
            set { }
        }


        public bool SaveSeparately { get; protected set; }

        public abstract void Serialize(Serializer serializer, SerializeContext ctx);
        public abstract void Deserialize(Serializer serializer, SerializeContext ctx);

        public virtual void SetDirty(bool dirty, bool recursive)
        {
            _isDirty = dirty;
        }

        protected void ClearCollection(Serializer serializer, SerializeContext? ctx)
        {
            if (ctx == null)
                serializer.FreshDeleteAll(this);
            else
                serializer.DeleteAllNoPushPath(ctx);
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
    }

    public abstract class ObservableCollection<TCol, TElem> : ObservableCollection, ISavable<TCol>, ISvCollectionChanged<TCol, TElem>
        where TCol : ObservableCollection<TCol, TElem>
    {
        protected ObservableCollection()
        {
        }

        protected ObservableCollection(string svid, bool saveSeparately) : base(svid, saveSeparately)
        {
        }

        public event CollectionChanged<TCol, TElem?>? CollectionChanged;
        public event PropertyChangeEventHandler<TCol>? PropertyChanged;

        protected abstract void OnChildChanged(TElem obj, string propertyName);

        protected void TryWatch(TElem? obj)
        {
            if (obj is not ISavable<TElem> savable)
                return;

            this.SetChild(obj);
            savable.PropertyChanged += OnChildChanged;
        }

        protected virtual void OnChildDeserialized(TElem? elem)
        {
            if (elem is not ISavable<TElem> savable)
                return;
            this.ChildDeserialized(elem);
            savable.PropertyChanged += OnChildChanged;
        }

        protected void TryUnWatch(TElem? obj)
        {
            if (obj is not ISavable<TElem> savable)
                return;
            savable.SetParent(null);
            savable.PropertyChanged -= OnChildChanged;
        }

        protected void OnCollectionChange(CollectionChangeInfo<TCol, TElem?> e)
        {
            _isDirty = true;

            CollectionChanged?.Invoke(e);
        }
    }

    public abstract class ObservableCollectionSavable<TCol, TElem> : ObservableCollection<TCol, TElem>
        where TCol : ObservableCollectionSavable<TCol, TElem>
        where TElem : ISavable?
    {
        protected ObservableCollectionSavable(string svid, bool saveSeparately) : base(svid, saveSeparately)
        {
        }
    }
}