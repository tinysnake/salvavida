using System;

namespace Salvavida
{
    /// <summary>
    /// Metadata for a single bucket in LexoRank-based collections.
    /// Stored in CollectionMetadata.BucketMetas.
    /// </summary>
    [Serializable]
    public struct BucketMeta
    {
        /// <summary>
        /// Bucket prefix (e.g. "A", "B", "AA"). Sequential Base62 assignment.
        /// </summary>
        public string BucketId;

        /// <summary>
        /// Number of elements in this bucket.
        /// </summary>
        public int Count;
    }
}
