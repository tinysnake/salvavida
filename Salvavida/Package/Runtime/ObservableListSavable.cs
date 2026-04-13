using System;
using System.Collections.Generic;
using System.Linq;

namespace Salvavida
{
    public sealed class ObservableListSavable<T> : ObservableListSavableBase<ObservableListSavable<T>, T>, ICollectionWrapper<List<T?>>
        where T : ISavable
    {
        public ObservableListSavable(string propName, List<T?>? src, bool saveSeparately)
           : base(propName, saveSeparately)
        {
            SwapSource(src, false);
        }


        private List<Slot> _list = new();
        private readonly HashSet<string> _idsDeleted = new();
        private bool _needsRebalance;

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty)
                    return true;
                foreach (var slot in _list)
                {
                    if (slot.IsTrueDirty)
                        return true;
                }
                return false;
            }
        }

        public override T? this[int index]
        {
            get => _list[index].Value;
            set
            {
                var slot = _list[index];
                var oldValue = slot.Value;
                if (EqualityComparer<T?>.Default.Equals(oldValue, value))
                    return;
                var oldId = slot.Id;
                var newId = slot.Id ?? GenerateIdForIndex(index);
                if (!string.IsNullOrEmpty(oldId) && oldId != newId)
                    _idsDeleted.Add(oldId);
                if (value != null)
                    value.SvId = newId;
                _list[index] = new Slot(newId, value, true, true);
                TryWatch(value);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Replace(this, oldValue, value, index));
                TryUnWatch(oldValue);
            }
        }

        public override int Count => _list.Count;

        public List<T?>? RetrieveSource() => _list.Select(x=>x.Value).ToList();

        public object? RetrieveSourceRaw() => RetrieveSource();

        public Type CollectionType => typeof(List<T?>);

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);
            if (recursive)
            {
                _isChildrenDirty = dirty;
                for(var i = 0;i<_list.Count;i++)
                {
                    var slot = _list[i];
                    slot.Value?.SetDirty(dirty, recursive);
                    _list[i] = new Slot(slot.Id, slot.Value, dirty, slot.IsLoaded);
                }
            }
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            if (_needsRebalance)
            {
                var precisionDigits = Math.Max(DEFAULT_PRECISION_DIGITS, LexoRank.CalculatePrecisionDigits(_list.Count));
                var initRank = LexoRank.GetInitValue(precisionDigits);
                var rank = LexoRank.Rebalance(initRank, _list.Count, out int step, out bool reverseOrder);
                Span<char> rankBuffer = stackalloc char[128];
                serializer.DeleteAllNoPushPath(ctx);
                for (int i = 0; i < _list.Count; i++)
                {
                    var slot = _list[i];
                    slot = new Slot(rank, slot.Value, slot.IsDirty, slot.IsLoaded);
                    if (slot.Value != null)
                        slot.Value.SvId = rank;
                    _list[i] = slot;
                    rank = LexoRank.Generate(rank, null, precisionDigits, stepSize: step);
                }
                for (int i = 0; i < _list.Count; i++)
                {
                    var slot = _list[i];
                    if(slot.Value == null)
                        serializer.Save<T?>(default, ctx, slot.Id, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(slot.Id, PathBuilder.Type.Collection); 
                        slot.Value.Serialize(serializer, ctx);
                    }
                    _list[i] = new Slot(slot.Id, slot.Value, false, slot.IsLoaded);
                }
                _needsRebalance = false;
            }
            else
            {
                for (int i = 0; i < _list.Count; i++)
                {
                    var slot = _list[i];
                    if(!slot.IsTrueDirty)
                        continue;
                    if(slot.Value == null)
                        serializer.Save<T?>(default, ctx, slot.Id, PathBuilder.Type.Collection);
                    else
                    {
                        using var _ = ctx.Path.UsePush(slot.Id, PathBuilder.Type.Collection); 
                        slot.Value.Serialize(serializer, ctx);
                    }
                }
            }

            SaveMetadata(serializer, ctx, _list.Count);

            if (_idsDeleted.Count > 0)
            {
                foreach (var deletedId in _idsDeleted)
                {
                    serializer.Delete(ctx, deletedId, PathBuilder.Type.Collection);
                }
                _idsDeleted.Clear();
            }
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            SwapSource(null, false);
            var ids = serializer.ListCollectionIds(ctx);
            foreach (var id in ids)
            {
                var item = serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection);
                if(item!=null)
                    item.SvId = id;
                _list.Add(new Slot(id, item, false, true));
                TryWatch(item);
                OnChildDeserialized(item);
            }
            
            _idsDeleted.Clear();
            _isDirty = false;
        }

        public override void SwapSource(List<T?>? list)
        {
            SwapSource(list, true);
        }

        private void SwapSource(List<T?>? list, bool notifyChanges)
        {
            foreach (var slot in _list)
            {
                if (!string.IsNullOrEmpty(slot.Id))
                    _idsDeleted.Add(slot.Id);
                TryUnWatch(slot.Value);
            }

            if (notifyChanges)
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));

            _list.Clear();
            _isDirty = true;
            if (list != null)
            {
                var id = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
                for (var i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    var slot = new Slot(id, item, item == null, true);
                    _list.Add(slot);
                    if (item != null)
                        item.SvId = id;
                    id = LexoRank.Generate(id, null, DEFAULT_PRECISION_DIGITS);
                    TryWatch(item);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private CollectionChangeInfo<ObservableListSavableBase<T>, T?> CreateSaveAllEvent()
        {
            var items = new List<T?>(_list.Count);
            foreach (var slot in _list)
                items.Add(slot.Value);
            return CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, items, 0);
        }

        public override void Add(T? item)
        {
            var index = _list.Count;
            var id = GenerateIdForIndex(index);
            var slot = new Slot(id, item, item == null, true);
            if (item != null)
                item.SvId = id;
            _list.Add(slot);
            TryWatch(item);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
        }

        public void AddRange(IList<T?> collection)
        {
            var index = _list.Count;
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                var id = GenerateIdForIndex(index + i);
                var slot = new Slot(id, item, item == null, true);
                if (item != null)
                    item.SvId = id;
                _list.Add(slot);
                TryWatch(item);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, collection, index));
        }

        private string GenerateIdForIndex(int index)
        {
            if (_list.Count == 0)
                return LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);

            string? prevId = index > 0 ? _list[index - 1].Id : null;
            string? nextId = index < _list.Count ? _list[index].Id : null;
            return GenerateIdBetween(prevId, nextId);
        }

        public override void Clear()
        {
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Reset(this));
            foreach (var slot in _list)
            {
                if (!string.IsNullOrEmpty(slot.Id))
                    _idsDeleted.Add(slot.Id);
                TryUnWatch(slot.Value);
            }
            _list.Clear();
        }

        public override bool Contains(T? item)
        {
            foreach (var slot in _list)
            {
                if (EqualityComparer<T?>.Default.Equals(slot.Value, item))
                    return true;
            }
            return false;
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            foreach (var slot in _list)
                yield return slot.Value;
        }

        public override int IndexOf(T? item)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                if (EqualityComparer<T?>.Default.Equals(_list[i].Value, item))
                    return i;
            }
            return -1;
        }

        public override void Insert(int index, T? item)
        {
            string? prevId = index > 0 ? _list[index - 1].Id : null;
            string? nextId = index < _list.Count ? _list[index].Id : null;
            var id = GenerateIdBetween(prevId, nextId);

            if (item != null)
                item.SvId = id;

            if (ShouldRebalance(id))
                _needsRebalance = true;

            _list.Insert(index, new Slot(id, item, item == null, true));
            TryWatch(item);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, item, index));
        }

        public void InsertRange(int index, IList<T?> collection)
        {
            for (var i = 0; i < collection.Count; i++)
            {
                var item = collection[i];
                string? prevId = (index + i) > 0 ? _list[index + i - 1].Id : null;
                string? nextId = (index + i) < _list.Count ? _list[index + i].Id : null;
                var id = GenerateIdBetween(prevId, nextId);
                if (item != null)
                    item.SvId = id;
                if (ShouldRebalance(id))
                    _needsRebalance = true;
                _list.Insert(index + i, new Slot(id, item, item == null, true));
                TryWatch(item);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Add(this, collection, index));
        }

        public override bool Remove(T? item)
        {
            var index = IndexOf(item);
            if (index >= 0)
            {
                var slot = _list[index];
                if (!string.IsNullOrEmpty(slot.Id))
                    _idsDeleted.Add(slot.Id);
                _list.RemoveAt(index);
                OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, item, index));
                TryUnWatch(item);
                return true;
            }
            return false;
        }

        public void RemoveRange(int index, int count)
        {
            var arr = new T?[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = _list[index + i].Value;
                if (!string.IsNullOrEmpty(_list[index + i].Id))
                    _idsDeleted.Add(_list[index + i].Id);
            }
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, arr, index));
            foreach (var item in arr)
            {
                TryUnWatch(item);
            }
            _list.RemoveRange(index, count);
        }

        public override void RemoveAt(int index)
        {
            var slot = _list[index];
            if (!string.IsNullOrEmpty(slot.Id))
                _idsDeleted.Add(slot.Id);
            _list.RemoveAt(index);
            OnCollectionChange(CollectionChangeInfo<ObservableListSavableBase<T>, T?>.Remove(this, slot.Value, index));
            TryUnWatch(slot.Value);
        }

        public void Move(int oldIndex, int newIndex)
        {
            var slot = _list[oldIndex];
            RemoveAt(oldIndex);
            Insert(newIndex, slot.Value);
        }
    }
}
