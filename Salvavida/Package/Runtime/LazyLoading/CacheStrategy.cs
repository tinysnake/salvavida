namespace Salvavida
{
    /// <summary>
    /// Cache strategy for lazy-loaded collection pages.
    /// </summary>
    public enum CacheStrategy
    {
        /// <summary>
        /// No cache eviction. Pages stay loaded once accessed.
        /// </summary>
        None = 0,

        /// <summary>
        /// Least Recently Used eviction. Evicts least recently accessed pages when cache is full.
        /// </summary>
        LRU = 1
    }
}
