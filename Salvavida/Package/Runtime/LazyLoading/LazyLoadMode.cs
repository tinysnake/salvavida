namespace Salvavida
{
    /// <summary>
    /// Lazy loading mode for collection properties.
    /// </summary>
    public enum LazyLoadMode
    {
        /// <summary>
        /// No lazy loading. Load all elements at once during deserialization.
        /// </summary>
        None = 0,

        /// <summary>
        /// Load all elements when the collection is first accessed.
        /// </summary>
        LoadAll = 1,

        /// <summary>
        /// Load individual elements only when they are accessed.
        /// </summary>
        LoadIndividual = 2
    }
}
