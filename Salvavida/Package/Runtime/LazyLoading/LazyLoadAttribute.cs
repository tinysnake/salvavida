using System;

namespace Salvavida
{
    /// <summary>
    /// Attribute to configure lazy loading for collection properties.
    /// Place on fields or properties that are List or Array types.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class LazyLoadAttribute : Attribute
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

        /// <summary>
        /// Cache eviction strategy.
        /// Default: LRU
        /// </summary>
        public CacheStrategy CacheStrategy { get; set; } = CacheStrategy.LRU;

        /// <summary>
        /// Convert attribute to LazyLoadConfig.
        /// </summary>
        public LazyLoadConfig ToConfig()
        {
            return new LazyLoadConfig
            {
                Threshold = Threshold,
                PageSize = PageSize,
                MaxCachedPages = MaxCachedPages,
                CacheStrategy = CacheStrategy
            };
        }
    }
}
