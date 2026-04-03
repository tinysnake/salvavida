namespace Salvavida
{
    /// <summary>
    /// Global configuration defaults for lazy loading.
    /// </summary>
    public static class GlobalConfig
    {
        /// <summary>
        /// Default threshold for lazy loading.
        /// Collections with count > threshold will use lazy loading.
        /// </summary>
        public static int DefaultLazyLoadThreshold { get; set; } = 1000;

        /// <summary>
        /// Default page size for lazy loading.
        /// </summary>
        public static int DefaultPageSize { get; set; } = 100;

        /// <summary>
        /// Default maximum cached pages for LRU strategy.
        /// </summary>
        public static int DefaultMaxCachedPages { get; set; } = 10;

        /// <summary>
        /// Default cache eviction strategy.
        /// </summary>
        public static CacheStrategy DefaultCacheStrategy { get; set; } = CacheStrategy.LRU;
    }
}
