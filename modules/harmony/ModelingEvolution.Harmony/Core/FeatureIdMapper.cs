namespace ModelingEvolution.Harmony.Core;

/// <summary>
/// Maps feature file names to consistent numeric IDs for shared resource isolation
/// </summary>
public static class FeatureIdMapper
{
    private static readonly Dictionary<string, int> FeatureIdMap = new()
    {
        { "BasicCommunication", 1 },
        { "Benchmarks", 2 },
        { "DuplexAdvanced", 3 },
        { "DuplexChannel", 4 },
        { "EdgeCases", 5 },
        { "ErrorHandling", 6 },
        { "Initialization", 7 },
        { "Performance", 8 },
        { "PlatformSpecific", 9 },
        { "ProcessLifecycle", 10 },
        { "StressTests", 11 },
        { "Synchronization", 12 }
    };

    /// <summary>
    /// Gets the numeric ID for a feature file name
    /// </summary>
    /// <param name="featureFileName">The feature file name (without extension)</param>
    /// <returns>The numeric feature ID</returns>
    /// <exception cref="ArgumentException">Thrown when feature file name is not mapped</exception>
    public static int GetFeatureId(string featureFileName)
    {
        if (string.IsNullOrEmpty(featureFileName))
        {
            throw new ArgumentException("Feature file name cannot be null or empty", nameof(featureFileName));
        }

        // Remove .feature extension if present
        var fileName = featureFileName.EndsWith(".feature") 
            ? featureFileName[..^8] 
            : featureFileName;

        if (FeatureIdMap.TryGetValue(fileName, out var id))
        {
            return id;
        }

        throw new ArgumentException($"No feature ID mapping found for '{fileName}'. Available features: {string.Join(", ", FeatureIdMap.Keys)}", nameof(featureFileName));
    }

    /// <summary>
    /// Gets all mapped feature names
    /// </summary>
    public static IEnumerable<string> GetAllFeatureNames()
    {
        return FeatureIdMap.Keys;
    }

    /// <summary>
    /// Gets the feature name for a given ID
    /// </summary>
    /// <param name="featureId">The feature ID</param>
    /// <returns>The feature name</returns>
    /// <exception cref="ArgumentException">Thrown when feature ID is not mapped</exception>
    public static string GetFeatureName(int featureId)
    {
        var entry = FeatureIdMap.FirstOrDefault(kvp => kvp.Value == featureId);
        if (entry.Key != null)
        {
            return entry.Key;
        }

        throw new ArgumentException($"No feature name found for ID {featureId}. Available IDs: {string.Join(", ", FeatureIdMap.Values)}", nameof(featureId));
    }
}