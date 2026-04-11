namespace Salvavida
{
    public struct CollectionOptions
    {
        /// <summary>
        /// Number of elements per page.
        /// </summary>
        public int PageSize;

        /// <summary>
        /// Maximum number of cached pages.
        /// </summary>
        public int MaxCachedPages;

        /// <summary>
        /// Cache eviction strategy.
        /// </summary>
        public CacheStrategy CacheStrategy;

        /// <summary>
        /// Whether this collection uses lazy loading format.
        /// </summary>
        public bool IsLazyLoaded;

        /// <summary>
        /// Chunk metadata table for LexoRank-based ordering.
        /// </summary>

        /// <summary>
        /// Multiplier for chunk size calculation: chunkSize = ChunkMultiplier * PageSize.
        /// </summary>
        public int ChunkMultiplier; 
    }
}
