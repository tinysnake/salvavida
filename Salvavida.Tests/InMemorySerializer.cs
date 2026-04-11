using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Salvavida.Tests
{
    /// <summary>
    /// In-memory implementation of Serializer for testing purposes.
    /// Stores serialized data in memory using a simple Dictionary.
    /// </summary>
    public class InMemorySerializer : Serializer
    {
        public InMemorySerializer()
        {
            _serializeOptions = new JsonSerializerOptions
            {
                IncludeFields = true,
                IgnoreReadOnlyProperties = true,
            };
        }
        private JsonSerializerOptions _serializeOptions;
        private readonly Dictionary<string, string?> _storage = new();

        /// <summary>
        /// Provides read-only access to the internal storage for test verification.
        /// </summary>
        public IReadOnlyDictionary<string, string?> Storage => _storage;

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
            _storage[path] = obj == null ? null : JsonSerializer.Serialize(obj, _serializeOptions);
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
                if (value == null)
                    return default!;
                return JsonSerializer.Deserialize<T>(value, _serializeOptions)!;
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

        public override IEnumerable<string> ListCollectionIds(SerializeContext ctx)
        {
            var basePath = ctx.Path.ToString() + "/";

            return _storage.Keys
                .Where(key => key.AsSpan().StartsWith(basePath.AsSpan(), StringComparison.Ordinal))
                .Select(key => key[basePath.Length..])
                .Where(key => !key.Contains('/')) // Only consider direct children, ignore deeper nested paths
                .Distinct()
                .OrderBy(id => id);
        }

        public override IEnumerable<string> ListCollectionIdsPrefix(SerializeContext ctx, string prefix, int skipCount, int pageSize)
        {
            var basePath = ctx.Path.ToString() + "/";
            return _storage.Keys
                .Where(key => key.AsSpan().StartsWith(basePath.AsSpan(), StringComparison.Ordinal))
                .Select(key => key[basePath.Length..])
                .Where(key => !key.Contains('/')) // Only consider direct children, ignore deeper nested paths
                .Where(key=>string.Compare(key, prefix, StringComparison.Ordinal) >= 0)
                .Distinct()
                .OrderBy(id => id);
        }

        public override IEnumerable<string> ListCollectionIds(
            SerializeContext ctx, int skipCount, int pageSize)
        {
            var basePath = ctx.Path.ToString() + "/";
            var pathSpan = ctx.Path.AsSpan();
            var prefixLen = 2;
            var prefixPathArray = new char[prefixLen];
            var prefixPath = prefixPathArray.AsSpan();
            "0~".CopyTo(prefixPath);

            return _storage.Keys
                .Where(key =>
                {
                    var keySpan = key.AsSpan();
                    if (!keySpan.StartsWith(basePath.AsSpan(), StringComparison.Ordinal))
                        return false;
                    keySpan = keySpan[basePath.Length..];
                    var prefixSpan = prefixPathArray.AsSpan();
                    prefixSpan[0] = '0';
                    if (keySpan.StartsWith(prefixSpan, StringComparison.Ordinal))
                        return true;
                    prefixSpan[0] = '1';
                    if (keySpan.StartsWith(prefixSpan, StringComparison.Ordinal))
                        return true;
                    prefixSpan[0] = '2';
                    if (keySpan.StartsWith(prefixSpan, StringComparison.Ordinal))
                        return true;
                    return false;
                })
                .Select(key => key[basePath.Length..])
                .Where(key => !key.Contains('/')) // Only consider direct children, ignore deeper nested paths
                .Distinct()
                .OrderBy(id => id);
        }

        #endregion
    }
}
