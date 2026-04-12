namespace Salvavida
{
    /// <summary>
    /// Configuration for lazy-loaded collections.
    /// </summary>
    public struct CollectionOptions
    {
        /// <summary>
        /// Lazy loading mode.
        /// Default: None (no lazy loading, load all at once)
        /// </summary>
        public LazyLoadMode Mode { get; set; }

        public int BatchLoadCount { get; set; }
    }
}
