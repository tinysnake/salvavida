namespace Salvavida
{
    /// <summary>
    /// Configuration for lazy-loaded collections.
    /// </summary>
    public class LazyLoadConfig
    {
        /// <summary>
        /// Element count threshold to trigger lazy loading.
        /// Collections with count > threshold will use lazy loading.
        /// Default: 1000
        /// </summary>
        public int Threshold { get; set; } = 1000;

        /// <summary>
        /// Number of elements per page.
        /// Default: 100
        /// </summary>
        public int PageSize { get; set; } = 100;

        /// <summary>
        /// Maximum number of pages to keep in memory cache.
        /// Only applies when CacheStrategy is LRU.
        /// Default: 10
        /// </summary>
        public int MaxCachedPages { get; set; } = 10;

        public int ChunkMultiplier { get; set; } = 10;

        /// <summary>
        /// Cache eviction strategy.
        /// Default: LRU
        /// </summary>
        public CacheStrategy CacheStrategy { get; set; } = CacheStrategy.LRU;
    }
}
