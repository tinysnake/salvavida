using System;
using System.Collections.Generic;
using System.Linq;

namespace Salvavida.Tests
{
    /// <summary>
    /// In-memory implementation of Serializer for testing purposes.
    /// Stores serialized data in memory using a simple Dictionary.
    /// </summary>
    public class InMemorySerializer : Serializer
    {
        private readonly Dictionary<string, object?> _storage = new();

        /// <summary>
        /// Provides read-only access to the internal storage for test verification.
        /// </summary>
        public IReadOnlyDictionary<string, object?> Storage => _storage;

        #region Test Helper Methods

        /// <summary>
        /// Clears all stored data.
        /// </summary>
        public void Clear() => _storage.Clear();

        /// <summary>
        /// Gets the number of items in storage.
        /// </summary>
        public int Count => _storage.Count;

        /// <summary>
        /// Checks if a specific path exists in storage.
        /// </summary>
        public bool ContainsPath(string path) => _storage.ContainsKey(path);

        /// <summary>
        /// Gets all child paths under a parent path.
        /// </summary>
        public IEnumerable<string> GetChildPaths(string parentPath) =>
            _storage.Keys.Where(key => key.StartsWith(parentPath));

        /// <summary>
        /// Prints all stored data to console for debugging.
        /// </summary>
        public void DebugPrint()
        {
            foreach (var kvp in _storage)
            {
                Console.WriteLine($"{kvp.Key} = {kvp.Value}");
            }
        }

        #endregion

        #region Serializer Implementation

        protected override void DoSaveObjectImpl<T>(T obj, Type type, SerializeContext ctx)
        {
            var path = ctx.Path.ToString();
            _storage[path] = obj;
        }

        protected override void DoSaveObjectImpl<T>(T obj, SerializeContext ctx)
        {
            DoSaveObjectImpl(obj, typeof(T), ctx);
        }

        protected override T DoReadImpl<T>(SerializeContext ctx)
        {
            var path = ctx.Path.ToString();
            if (_storage.TryGetValue(path, out var value))
            {
                return (T)value!;
            }
            return default!;
        }

        protected override bool DoHas(SerializeContext ctx)
        {
            var path = ctx.Path.ToString();
            return _storage.ContainsKey(path);
        }

        protected override void DoDelete(SerializeContext ctx)
        {
            var path = ctx.Path.ToString();
            _storage.Remove(path);
        }

        protected override void DoDeleteAll(SerializeContext ctx)
        {
            var prefix = ctx.Path.ToString();
            var keysToRemove = _storage.Keys
                .Where(key => key.StartsWith(prefix))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _storage.Remove(key);
            }
        }

        public override IEnumerable<string> ListCollectionIds(SerializeContext ctx, string propName)
        {
            var basePath = ctx.Path.ToString();
            var collectionPath = $"{basePath}.{propName}";

            return _storage.Keys
                .Where(key => key.StartsWith(collectionPath + "/"))
                .Select(key =>
                {
                    var relative = key.Substring(collectionPath.Length + 1);
                    var slashIndex = relative.IndexOf('/');
                    return slashIndex < 0 ? relative : relative.Substring(0, slashIndex);
                })
                .Distinct()
                .OrderBy(id => id)
                .ToList();
        }

        public override string[] ListCollectionIds(
            SerializeContext ctx, string propName, string? bucketId, int skipCount, int pageSize)
        {
            // For simplicity, ignore bucketId and just implement pagination
            var allIds = ListCollectionIds(ctx, propName);
            return allIds.Skip(skipCount).Take(pageSize).ToArray();
        }

        #endregion
    }
}
