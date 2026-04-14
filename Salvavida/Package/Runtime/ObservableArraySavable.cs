using System;
using System.Collections;
using System.Collections.Generic;

namespace Salvavida
{
    public sealed class ObservableArraySavable<T> : ObservableArraySavableBase<ObservableArraySavable<T>, T>, ICollectionWrapper<T?[]>
        where T : ISavable
    {
        public ObservableArraySavable(string propName, T?[]? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            SwapSource(src, false);
        }

        private T?[]? _arr;
        private readonly HashSet<int> _idxDeleted = new();

        public override bool IsDirty
        {
            get
            {
                if (IsSelfDirty)
                    return true;
                if (_arr == null)
                    return false;
                foreach (var item in _arr)
                {
                    if (item == null)
                        continue;
                    if (item.IsDirty)
                        return true;
                }

                return false;
            }
        }

        public override T? this[int index]
        {
            get
            {
                if (_arr == null) throw new NullReferenceException(nameof(_arr));
                ValidateIndex(index);
                return _arr[index];
            }
            set
            {
                if (_arr == null) throw new NullReferenceException(nameof(_arr));
                ValidateIndex(index);
                var oldVal = _arr[index];
                if (EqualityComparer<T?>.Default.Equals(oldVal, value))
                    return;
                if (oldVal != null)
                    _idxDeleted.Add(index);
                _arr[index] = value;
                if (value != null)
                    value.SvId = GetPaddedIndex(index, _arr.Length);
                TryWatch(value);
                OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Replace(this, oldVal, value, index));
                TryUnWatch(oldVal);
            }
        }

        public override int Count => _arr?.Length ?? 0;

        public T?[]? RetrieveSource() => _arr;

        public object? RetrieveSourceRaw() => _arr;

        public Type CollectionType => typeof(T[]);

        public override void SetDirty(bool dirty, bool recursive)
        {
            base.SetDirty(dirty, recursive);
            if (!recursive || _arr == null)
                return;
            _isChildrenDirty = dirty;
            foreach (var item in _arr)
            {
                item?.SetDirty(dirty, recursive);
            }
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= _arr!.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            if (_arr != null)
            {
                for (var i = 0; i < _arr.Length; i++)
                {
                    var elem = _arr[i];
                    if (elem == null)
                        continue;
                    else if (elem.IsDirty)
                    {
                        using var _ = ctx.Path.UsePush(elem.SvId, PathBuilder.Type.Collection);
                        elem.Serialize(serializer, ctx);
                    }
                }

                foreach (var idx in _idxDeleted)
                {
                    var id = GetPaddedIndex(idx, _arr.Length);
                    serializer.Delete(ctx, id, PathBuilder.Type.Collection);
                }
                _idxDeleted.Clear();

                var meta = new CollectionMetadata
                {
                    Count = _arr.Length,
                };
                serializer.Save(meta, ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
            }
            else
            {
                serializer.Delete(ctx, SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property);
            }
        }

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;


            SwapSource(null, false);
            T?[]? arr = null;
            using (ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Property))
            {
                if (serializer.HasNoPushPath(ctx))
                {
                    CollectionMetadata metadata = serializer.ReadNoPushPath<CollectionMetadata>(ctx);
                    arr = new T?[metadata.Count];
                }
            }

            if (arr == null)
                return;

            foreach (var id in serializer.ListCollectionIds(ctx))
            {
                if (!int.TryParse(id, out var index))
                    throw new FormatException("invalid format for a int typed index value");
                var item = serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection);
                arr[index] = item;
                if (item != null)
                {
                    item.SvId = id;
                    TryWatch(item);
                    OnChildDeserialized(item);
                }
            }
            _arr = arr;
            _isDirty = false;
            _isChildrenDirty = false;
        }


        public override void SwapSource(T?[]? array)
        {
            SwapSource(array, true);
        }

        private void SwapSource(T?[]? array, bool notifyChanges)
        {
            if (_arr != null)
            {
                if (notifyChanges)
                    OnCollectionChange(CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Reset(this));
                for (var i = 0; i < _arr.Length; i++)
                {
                    TryUnWatch(_arr[i]);
                }
            }
            _arr = array;
            _isDirty = true;
            if (_arr != null)
            {
                for (var i = 0; i < _arr.Length; i++)
                {
                    var item = _arr[i];
                    if (item != null && item.SvId == null)
                        item.SvId = GetPaddedIndex(i, _arr.Length);
                    TryWatch(item);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private CollectionChangeInfo<ObservableArraySavableBase<T>, T?> CreateSaveAllEvent()
        {
            return CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Add(this, _arr, 0);
        }

        public override bool Contains(T? item)
        {
            if (_arr == null) throw new NullReferenceException(nameof(_arr));
            return Array.IndexOf(_arr, item) >= 0;
        }

        public override IEnumerator<T?> GetEnumerator()
        {
            if (_arr == null) throw new NullReferenceException(nameof(_arr));
            return new ArrayEnumerator(_arr);
        }

        public override int IndexOf(T? item)
        {
            if (_arr == null) throw new NullReferenceException(nameof(_arr));
            return Array.IndexOf(_arr, item);
        }

        public struct ArrayEnumerator : IEnumerator<T?>
        {
            public ArrayEnumerator(T?[] _arr)
            {
                if (_arr == null) throw new NullReferenceException(nameof(_arr));
                this._arr = _arr;
                _i = 0;
                _cur = default;
            }

            private readonly T?[] _arr;
            private int _i;
            private T? _cur;

            public readonly T? Current => _cur;

            readonly object? IEnumerator.Current => _cur;

            public bool MoveNext()
            {
                if (_i < _arr.Length)
                {
                    _cur = _arr[_i++];
                    return true;
                }
                _i = _arr.Length;
                _cur = default;
                return false;
            }

            public void Reset()
            {
                _i = 0;
                _cur = default;
            }

            public void Dispose()
            {
            }
        }
    }
}
