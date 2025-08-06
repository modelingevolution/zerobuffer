namespace ModelingEvolution.Harmony.Core;

/// <summary>
/// Maps feature file names to consistent numeric IDs for shared resource isolation
/// </summary>
public static class FeatureIdMapper
{


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

        // feature name is like 01-FeatureName.feature
        string prefix = fileName.Contains('-') ? fileName.Split('-')[0] : string.Empty;
        if (int.TryParse(prefix, out var value))
            return value;

        throw new ArgumentException(
            $"No feature ID mapping found for '{fileName}'. Feature file name must start with a numeric prefix (e.g., '01-BasicCommunication')",
            nameof(featureFileName));
    }

}