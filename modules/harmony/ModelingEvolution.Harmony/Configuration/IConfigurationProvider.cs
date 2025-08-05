using ModelingEvolution.Harmony.Core;

namespace ModelingEvolution.Harmony.Configuration;

/// <summary>
/// Provides configuration for the multiprocess runner
/// </summary>
public interface IConfigurationProvider
{
    /// <summary>
    /// Gets the current configuration
    /// </summary>
    MultiprocessConfiguration GetConfiguration();
}

/// <summary>
/// Loads configuration from a JSON file
/// </summary>
public class FileConfigurationProvider : IConfigurationProvider
{
    private readonly string _configPath;
    
    public FileConfigurationProvider(string configPath)
    {
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
    }
    
    public MultiprocessConfiguration GetConfiguration()
    {
        return ConfigurationLoader.Load(_configPath);
    }
}

/// <summary>
/// Provides in-memory configuration for testing
/// </summary>
public class InMemoryConfigurationProvider : IConfigurationProvider
{
    private readonly MultiprocessConfiguration _configuration;
    
    public InMemoryConfigurationProvider(MultiprocessConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
    
    public MultiprocessConfiguration GetConfiguration()
    {
        return _configuration;
    }
}