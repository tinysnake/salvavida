using Salvavida.DefaultImpl;
using System;
using System.Collections.Generic;

namespace Salvavida
{
    public static class SvHelper
    {
        public const string PROPNAME_COLLECTION_METADATA = "__ob_metadata__";
        internal static Type typeOfSavableInterface = typeof(ISavable);
        internal static Random randomizer = new();
        internal static readonly DefaultObjectPool<List<string>> idListPool = new(() => new List<string>(), l => l.Clear(), 10);
        private static Stack<(string, PathBuilder.Type)> _tempPathBuilder = new();

        public static bool CheckIsSavable<T>()
        {
            return typeOfSavableInterface.IsAssignableFrom(typeof(T));
        }

        public static void ChildDeserialized<T>(this ISavable parent, T child)
        {
            if (child is not ISavable sv)
                return;
            if (string.IsNullOrWhiteSpace(sv.SvId))
                throw new NullReferenceException("SvId");
            sv.SetParent(parent);
            sv.SetDirty(false, false);
        }

        public static void SetChild<T>(this ISavable parent, T child)
        {
            if (child is not ISavable sv)
                return;
            if (string.IsNullOrWhiteSpace(sv.SvId))
                throw new NullReferenceException("SvId");
            sv.SetParent(parent);
            sv.SetDirty(true, true);
        }

        public static ReadOnlySpan<char> GetParentPathAsSpan(this ISavable? savable, PathBuilder pathBuilder)
        {
            return GetSavePathAsSpan(savable?.SvParent, pathBuilder);
        }

        public static ReadOnlySpan<char> GetSavePathAsSpan(this ISavable? savable, PathBuilder pathBuilder)
        {
            if (pathBuilder == null)
                throw new ArgumentNullException(nameof(pathBuilder));
            if (!pathBuilder.IsEmpty)
                throw new ArgumentNullException(nameof(pathBuilder) + " is not empty");
            _tempPathBuilder.Clear();
            while (savable != null)
            {
                TryThrowOnSvIdEmpty(savable);
                _tempPathBuilder.Push((savable.SvId!, GetPathType(savable)));
                savable = savable.SvParent;
            }
            var lastPathType = PathBuilder.Type.Property;
            while (_tempPathBuilder.Count > 0)
            {
                var item = _tempPathBuilder.Pop();
                pathBuilder.Push(item.Item1, lastPathType);
                lastPathType = item.Item2;
            }

            return pathBuilder.AsSpan();
        }

        private static PathBuilder.Type GetPathType<T>(T sv) where T : ISavable
        {
            if (sv is ObservableCollection)
                return PathBuilder.Type.Collection;
            return PathBuilder.Type.Property;
        }

        public static Serializer? GetSerializer(this ISavable? savable)
        {
            var x = 0;
            while (savable != null && x++ < 100)
            {
                if (savable is ISerializeRoot root)
                    return root.Serializer;
                savable = savable.SvParent;
            }
            if (x > 100)
                throw new StackOverflowException();
            return null;
        }

        public static void TrySerialize<T>(this T savable, Serializer serializer, SerializeContext ctx, string propName, PathBuilder.Type pathType = PathBuilder.Type.Property)
        {
            using var __p = ctx.Path.UsePush(propName, pathType);
            serializer.SaveNoPushPath(savable, ctx);
        }
        public static void TrySerialize<T>(this T savable, Serializer serializer, SerializeContext ctx, PathBuilder.Type pathType = PathBuilder.Type.Property) where T : ISavable
        {
            if (!savable.IsDirty)
                return;
            using var __p = ctx.Path.UsePush(savable.SvId, pathType);
            savable.Serialize(serializer, ctx);
        }

        public static ObservableArraySavableBase<TElem> LoadCollectionSavableAbstract<T, TElem>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref TElem?[]? src, CollectionOptions config)
            where T : ISavable
            where TElem : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);

            var mode = config.Mode;

            if (mode == LazyLoadMode.None)
            {
                var ob = new ObservableArraySavable<TElem>(propName.ToString(), src, saveSeparately);
                if (saveSeparately)
                {
                    ob.Deserialize(serializer, ctx);
                    src = ob.RetrieveSource();
                }
                return ob;
            }
            else
            {
                var lazyCol = new ObservableArraySavableLazy<TElem>(propName.ToString(), saveSeparately, config);
                if (saveSeparately)
                {
                    lazyCol.Deserialize(serializer, ctx);
                }
                return lazyCol;
            }
        }

        public static ObservableArraySavable<TElem> LoadCollectionSavable<T, TElem>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref TElem?[]? src, CollectionOptions config)
            where T : ISavable
            where TElem : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableArraySavable<TElem>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(serializer, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        public static ObservableArraySavableLazy<TElem> LoadCollectionSavableLazy<T, TElem>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref TElem?[]? src, CollectionOptions config)
            where T : ISavable
            where TElem : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var lazyCol = new ObservableArraySavableLazy<TElem>(propName.ToString(), saveSeparately, config);
            if (saveSeparately)
            {
                lazyCol.Deserialize(serializer, ctx);
            }
            return lazyCol;
        }

        public static ObservableListSavableBase<TElem> LoadCollectionSavableAbstract<T, TElem>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref List<TElem?>? src, CollectionOptions config)
            where T : ISavable
            where TElem : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);

            var mode = config.Mode;

            if (mode == LazyLoadMode.None)
            {
                var ob = new ObservableListSavable<TElem>(propName.ToString(), src, saveSeparately);
                if (saveSeparately)
                {
                    ob.Deserialize(serializer, ctx);
                    src = ob.RetrieveSource();
                }
                return ob;
            }
            else
            {
                var lazyCol = new ObservableListSavableLazy<TElem>(propName.ToString(), null, saveSeparately);
                if (saveSeparately)
                {
                    lazyCol.Deserialize(serializer, ctx);
                }
                return lazyCol;
            }
        }

        public static ObservableListSavable<TElem> LoadCollectionSavable<T, TElem>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref List<TElem?>? src, CollectionOptions config)
            where T : ISavable
            where TElem : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableListSavable<TElem>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(serializer, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        public static ObservableListSavableLazy<TElem> LoadCollectionSavableLazy<T, TElem>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref List<TElem?>? src, CollectionOptions config)
            where T : ISavable
            where TElem : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var lazyCol = new ObservableListSavableLazy<TElem>(propName.ToString(), null, saveSeparately);
            if (saveSeparately)
            {
                lazyCol.Deserialize(serializer, ctx);
            }
            return lazyCol;
        }

        public static ObservableDictionarySavable<TKey, TValue?> LoadCollectionSavableAbstract<T, TKey, TValue>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref Dictionary<TKey, TValue?>? src, CollectionOptions config)
            where T : ISavable
            where TKey : notnull
            where TValue : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableDictionarySavable<TKey, TValue?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(serializer, ctx);
                src = ob.RetrieveSource();
            }

            return ob;
        }

        public static ObservableDictionarySavable<TKey, TValue?> LoadCollectionSavable<T, TKey, TValue>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref Dictionary<TKey, TValue?>? src, CollectionOptions config)
            where T : ISavable
            where TKey : notnull
            where TValue : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableDictionarySavable<TKey, TValue?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(serializer, ctx);
                src = ob.RetrieveSource();
            }
            return ob;
        }

        // public static ObservableDictionarySavable<TKey, TValue?> LoadCollectionSavableLazy<T, TKey, TValue>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref Dictionary<TKey, TValue?>? src, CollectionOptions config)
        //     where T : ISavable
        //     where TKey : notnull
        //     where TValue : ISavable
        // {
        //     throw new NotImplementedException();
        // }

        public static ObservableArray<TElem?> LoadCollection<T, TElem>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref TElem?[]? src) where T : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableArray<TElem?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(serializer, ctx);
                src = ob.RetrieveSource();
            }

            return ob;
        }

        public static ObservableList<TElem?> LoadCollection<T, TElem>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref List<TElem?>? src) where T : ISavable
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableList<TElem?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(serializer, ctx);
                src = ob.RetrieveSource();
            }

            return ob;
        }

        public static ObservableDictionary<TKey, TValue?> LoadCollection<T, TKey, TValue>(this T _, Serializer serializer, SerializeContext ctx, ReadOnlySpan<char> propName, bool saveSeparately, ref Dictionary<TKey, TValue?>? src)
            where T : ISavable
            where TKey : notnull
        {
            using var __s = ctx.Path.UsePush(propName, PathBuilder.Type.Property);
            var ob = new ObservableDictionary<TKey, TValue?>(propName.ToString(), src, saveSeparately);
            if (saveSeparately)
            {
                ob.Deserialize(serializer, ctx);
                src = ob.RetrieveSource();
            }

            return ob;
        }

        public static void TryThrowOnSvIdEmpty<T>(T? sv) where T : ISavable
        {
            if (sv == null)
                throw new NullReferenceException(nameof(sv));
            if (string.IsNullOrEmpty(sv.SvId))
            {
                if (sv.SvParent != null)
                    throw new ArgumentNullException($"The child object of {sv.GetSavePathAsSpan(new PathBuilder()).ToString()} has a empty SvId");
                else
                    throw new ArgumentException(nameof(sv.SvId));
            }
        }

        public static int GetHashCodeFromSpan<T>(ReadOnlySpan<T> span)
        {
            var hc = new HashCode();
            foreach (var t in span)
            {
                hc.Add(t);
            }
            return hc.ToHashCode();
        }
    }
}
