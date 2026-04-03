using System;

namespace Salvavida
{
    /// <summary>
    /// Metadata for lazy-loaded collections, stored in __ob_metadata__.
    /// </summary>
    [Serializable]
    public class CollectionMetadata
    {
        /// <summary>
        /// Array of element IDs (indices as strings).
        /// </summary>
        public string[]? Ids { get; set; }

        /// <summary>
        /// Total element count.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Number of elements per page.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Maximum number of cached pages.
        /// </summary>
        public int MaxCachedPages { get; set; }

        /// <summary>
        /// Cache eviction strategy.
        /// </summary>
        public CacheStrategy CacheStrategy { get; set; }

        /// <summary>
        /// Whether this collection uses lazy loading format.
        /// </summary>
        public bool IsLazyLoaded { get; set; }

        /// <summary>
        /// Bucket metadata table for LexoRank-based ordering.
        /// </summary>
        public BucketMeta[]? BucketMetas { get; set; }

        /// <summary>
        /// Multiplier for bucket size calculation: bucketSize = BucketMultiplier * PageSize.
        /// Default is 3.
        /// </summary>
        public int BucketMultiplier { get; set; } = 3;
    }
}
