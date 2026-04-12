using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Salvavida
{
    public sealed class ObservableArraySavable<T> : ObservableArraySavableBase<ObservableArraySavable<T>, T>, ICollectionWrapper<T?[]>
        where T : ISavable
    {
        public ObservableArraySavable(string propName, T?[]? src, bool saveSeparately)
            : base(propName, saveSeparately)
        {
            _idxDeleted = new();
            SwapSource(src, false);
        }

        private T?[]? _arr;
        private readonly HashSet<int> _idxDeleted;

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
            get => _arr == null ? throw new NullReferenceException(nameof(_arr)) : _arr[index];
            set
            {
                if (_arr == null)
                    throw new NullReferenceException(nameof(_arr));
                var oldVal = _arr[index];
                if (EqualityComparer<T?>.Default.Equals(oldVal, value))
                    return;
                if(oldVal!=null)
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
            if (recursive && _arr != null)
            {
                _isChildrenDirty = dirty;
                foreach (var item in _arr)
                {
                    item?.SetDirty(dirty, recursive);
                }
            }
        }

        public override void Serialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;

            if (_arr == null)
                return;

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

            foreach(var idx in _idxDeleted)
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

        public override void Deserialize(Serializer serializer, SerializeContext ctx)
        {
            if (!SaveSeparately)
                return;
            var ids = serializer.ListCollectionIds(ctx).ToArray().AsSpan();
            if (ids.Length == 0)
            {
                SwapSource(null, false);
                return;
            }
            var arr = new T?[ids.Length];
            // var idConverter = SvIdConverter.GetConverter<int>() ?? throw new InvalidOperationException($"No SvId converter found for type int, which is required for deserializing {GetType().FullName}");
            var index = 0;
            foreach (var id in serializer.ListCollectionIds(ctx))
            {
                var item = serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection);
                arr[index++] = item;
                if (item != null)
                    TryWatch(item);
            }
            SwapSource(arr, false);
        }


        public override void SwapSource(T?[]? array)
        {
            _isDirty = true;
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
            if (_arr != null)
            {
                for (var i = 0; i < _arr.Length; i++)
                {
                    var item = _arr[i];
                    if (item != null && item.SvId == null)
                        item.SvId = GetPaddedIndex(i, _arr.Length);
                    if (notifyChanges)
                        TryWatch(item);
                    else
                        OnChildDeserialized(item);
                }
                if (notifyChanges)
                    OnCollectionChange(CreateSaveAllEvent());
            }
        }

        private CollectionChangeInfo<ObservableArraySavableBase<T>, T?> CreateSaveAllEvent()
        {
            if (_arr == null)
                throw new NullReferenceException(nameof(_arr));
            return CollectionChangeInfo<ObservableArraySavableBase<T>, T?>.Add(this, _arr, 0);
        }

        public override bool Contains(T? item) => Array.IndexOf(_arr, item) >= 0;

        public ArrayEnumerator GetEnumeratorStruct() => _arr == null ? throw new NullReferenceException(nameof(_arr)) : new(_arr);

        public override IEnumerator<T?> GetEnumerator() => GetEnumeratorStruct();

        public override int IndexOf(T? item) => Array.IndexOf(_arr, item);

        public struct ArrayEnumerator : IEnumerator<T?>
        {
            public ArrayEnumerator(T?[] _arr)
            {
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
                    _arr[_i++] = _cur;
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
