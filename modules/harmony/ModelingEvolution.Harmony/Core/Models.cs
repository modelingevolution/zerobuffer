namespace ModelingEvolution.Harmony.Core;

/// <summary>
/// Represents a parsed scenario from Gherkin
/// </summary>
public class ScenarioDefinition
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public BackgroundDefinition? Background { get; init; }
    public List<StepDefinition> Steps { get; init; } = new();
    public List<string> Tags { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = new();
    public string? FeatureFile { get; set; }
}

/// <summary>
/// Represents background steps that run before each scenario
/// </summary>
public class BackgroundDefinition
{
    public List<StepDefinition> Steps { get; init; } = new();
}

/// <summary>
/// Represents a single Gherkin step
/// </summary>
public class StepDefinition
{
    public StepType Type { get; init; }
    public string Text { get; init; } = "";
    public string? Process { get; init; }
    public string? ProcessedText { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = new();
}

public enum StepType
{
    Given,
    When,
    Then,
    And,
    But
}

/// <summary>
/// Represents a combination of platforms for each process role
/// </summary>
public class PlatformCombination
{
    private readonly Dictionary<string, string> _mapping;
    
    public PlatformCombination(Dictionary<string, string> mapping)
    {
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
    }
    
    /// <summary>
    /// Gets platform for a specific process
    /// </summary>
    public string GetPlatform(string process)
    {
        if (_mapping.TryGetValue(process, out var platform))
        {
            return platform;
        }
        throw new InvalidOperationException($"No platform mapping for process '{process}'");
    }
    
    /// <summary>
    /// Gets all process-platform mappings
    /// </summary>
    public IEnumerable<(string Process, string Platform)> GetMappings(List<string> processes)
    {
        foreach (var process in processes)
        {
            yield return (process, GetPlatform(process));
        }
    }
    
    /// <summary>
    /// Gets all processes in this platform combination
    /// </summary>
    public IEnumerable<string> GetAllProcesses()
    {
        return _mapping.Keys;
    }
    
    /// <summary>
    /// Creates a human-readable representation
    /// </summary>
    public override string ToString()
    {
        var platforms = _mapping
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => kvp.Value);
        return string.Join("/", platforms);
    }
    
    public override bool Equals(object? obj)
    {
        if (obj is PlatformCombination other)
        {
            return _mapping.SequenceEqual(other._mapping);
        }
        return false;
    }
    
    public override int GetHashCode()
    {
        return _mapping.GetHashCode();
    }
}

/// <summary>
/// Configuration for the multiprocess runner
/// </summary>
public class MultiprocessConfiguration
{
    public Dictionary<string, PlatformConfiguration> Platforms { get; init; } = new();
    public string FeaturesPath { get; init; } = "Features";
    public int DefaultTimeoutMs { get; init; } = 30000;
    public int ProcessInitializationDelayMs { get; init; } = 1000;
}

/// <summary>
/// Configuration for a single platform
/// </summary>
public class PlatformConfiguration
{
    public string Executable { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string? WorkingDirectory { get; init; }
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new();
}