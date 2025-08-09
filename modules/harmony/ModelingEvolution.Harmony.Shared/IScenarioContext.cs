namespace ModelingEvolution.Harmony.Shared
{
    /// <summary>
    /// Custom scenario context interface to replace SpecFlow's ScenarioContext
    /// for servo execution context management.
    /// </summary>
    public interface IScenarioContext
    {
        /// <summary>
        /// Gets or sets a value by key
        /// </summary>
        object this[string key] { get; set; }

        /// <summary>
        /// Tries to get a value by key
        /// </summary>
        bool TryGetValue(string key, out object value);

        /// <summary>
        /// Tries to get a typed value by key
        /// </summary>
        bool TryGetValue<T>(string key, out T value);

        /// <summary>
        /// Checks if a key exists
        /// </summary>
        bool ContainsKey(string key);

        /// <summary>
        /// Clears all context data
        /// </summary>
        void Clear();
    }
}