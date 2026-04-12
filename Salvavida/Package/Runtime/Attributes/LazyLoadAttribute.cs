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
        /// Default value for BatchLoadCount.
        /// </summary>
        public const int DefaultBatchLoadCount = 5;

        /// <summary>
        /// Lazy loading mode.
        /// Default: None (no lazy loading, load all at once)
        /// </summary>
        public LazyLoadMode Mode { get; set; } = LazyLoadMode.None;

        /// <summary>
        /// Once it tries to load a data from serializer, it will batch load the next N data.
        /// </summary>
        public int BatchLoadCount { get; set; } = DefaultBatchLoadCount;

        /// <summary>
        /// Once this is true, the generated property type will not change whether you switch between Modes.
        /// </summary>
        public bool UseAbstractType { get; set; } = false;
    }
}
