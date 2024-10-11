using System;

namespace Salvavida
{
    public static class SvHelper
    {
        internal static Type typeOfSavableInterface = typeof(ISavable);
        internal static Type typeOfSaveOrderInterface = typeof(ISaveWithOrder);

        private static PathBuilder _tempPathBuilder = new();

        public static bool CheckIsSavable<T>()
        {
            return typeOfSavableInterface.IsAssignableFrom(typeof(T));
        }

        public static bool CheckIsSaveWithOrder<T>()
        {
            return typeOfSaveOrderInterface.IsAssignableFrom(typeof(T));
        }

        public static void SetChild<T>(this ISavable parent, T child)
        {
            if (child is not ISavable sv)
                return;
            if (string.IsNullOrWhiteSpace(sv.SvId))
                throw new NullReferenceException("SvId");
            sv.SetParent(parent);
        }

        public static ReadOnlySpan<char> GetParentPathAsSpan(this ISavable? savable, PathBuilder pathBuilder)
        {
            return GetSavePathAsSpan(savable?.Parent, pathBuilder);
        }

        public static ReadOnlySpan<char> GetSavePathAsSpan(this ISavable? savable, PathBuilder pathBuilder)
        {
            if (pathBuilder == null)
                throw new ArgumentNullException(nameof(pathBuilder));
            if (!pathBuilder.IsEmpty)
                throw new ArgumentNullException(nameof(pathBuilder) + " is not empty");
            if (_tempPathBuilder.MaxLength < pathBuilder.MaxLength)
                _tempPathBuilder = new PathBuilder(pathBuilder.MaxLength);
            else
                _tempPathBuilder.Clear();
            while (savable != null)
            {
                TryThrowOnSvIdEmpty(savable);
                _tempPathBuilder.Push(savable.SvId!, GetPathType(savable));
                savable = savable.Parent;
            }
            while (!_tempPathBuilder.IsEmpty)
            {
                var span = _tempPathBuilder.PopAsSpan(out var t);
                pathBuilder.Push(span, t);
            }

            return pathBuilder.AsSpan();
        }

        public static PathBuilder.Type GetPathType<T>(T sv) where T : ISavable
        {
            //if (sv is ObservableCollection col && col.IsSavableCollection)
            //    return PathBuilder.Type.Collection;
            return PathBuilder.Type.Property;
        }

        public static Serializer? GetSerializer(this ISavable? savable)
        {
            var x = 0;
            while (savable != null && x++ < 100)
            {
                if (savable is ISerializeRoot root)
                    return root.Serializer;
                savable = savable.Parent;
            }
            if (x > 100)
                throw new StackOverflowException();
            return null;
        }

        public static void TrySave<TParent, T>(this TParent savable, string propName, T value, string[]? separatedProperties, string[]? separatedCollections) where TParent : ISavable
        {
            if (savable == null)
                return;
            if (propName == nameof(savable.SvId)) // propName == oldId should call TryUpdateId()
                return;

            if (separatedCollections != null && Array.IndexOf(separatedCollections, propName) >= 0)
            {
                // collections will not call this method
                return;
            }

            if (separatedProperties != null && Array.IndexOf(separatedProperties, propName) >= 0)
            {
                var serializer = GetSerializer(savable);
                if (serializer == null)
                    return;
                if (value is ISavable sv)
                    serializer.FreshSaveByPolicy(sv);
                else
                    serializer.FreshSaveByPolicy(savable, propName.AsMemory(), value, PathBuilder.Type.Property);
                return;
            }

            // if savable's parent is not null, then the save action will perform by it's parent, not it self.
            var parent = savable.Parent;
            if (parent != null)
                return;

            GetSerializer(savable)?.FreshSaveByPolicy(savable);
        }

        public static void TryUpdateId<T>(this T savable, string oldId) where T : ISavable
        {
            if (savable == null)
                return;
            var serializer = GetSerializer(savable);
            if (serializer == null)
                return;
            serializer.FreshUpdateIdByPolicy(savable, oldId);
        }

        public static void TryUpdateOrder<T>(this T savable, int order) where T : ISavable
        {
            if (savable == null)
                return;
            var serializer = GetSerializer(savable);
            if (serializer == null)
                return;
            serializer.FreshUpdateOrderByPolicy(savable, order);
        }

        public static void TryThrowOnSvIdEmpty<T>(T sv) where T : ISavable
        {
            if (sv == null)
                throw new NullReferenceException(nameof(sv));
            if (string.IsNullOrEmpty(sv.SvId))
            {
                if (sv.Parent != null)
                    throw new ArgumentNullException($"The child object of {sv.GetSavePathAsSpan(new PathBuilder()).ToString()} has a empty SvId");
                else
                    throw new ArgumentException(nameof(sv.SvId));
            }
        }
    }
}
