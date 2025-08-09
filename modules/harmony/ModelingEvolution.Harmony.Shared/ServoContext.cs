using System.Collections.Concurrent;

namespace ModelingEvolution.Harmony.Shared
{
    /// <summary>
    /// Context for storing servo initialization data and step execution context.
    /// Replaces SpecFlow's ScenarioContext for non-SpecFlow servo execution.
    /// </summary>
    public class ServoContext : IScenarioContext
    {
        private readonly ConcurrentDictionary<string, object> _data = new();

        /// <summary>
        /// Gets or sets a value by key
        /// </summary>
        public object this[string key]
        {
            get => _data.TryGetValue(key, out var value) ? value : null;
            set => _data[key] = value;
        }

        /// <summary>
        /// Tries to get a value by key
        /// </summary>
        public bool TryGetValue(string key, out object value)
        {
            return _data.TryGetValue(key, out value);
        }

        /// <summary>
        /// Tries to get a typed value by key
        /// </summary>
        public bool TryGetValue<T>(string key, out T value)
        {
            if (_data.TryGetValue(key, out var obj) && obj is T typedValue)
            {
                value = typedValue;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Checks if a key exists
        /// </summary>
        public bool ContainsKey(string key)
        {
            return _data.ContainsKey(key);
        }

        /// <summary>
        /// Clears all context data
        /// </summary>
        public void Clear()
        {
            _data.Clear();
        }
    }
}