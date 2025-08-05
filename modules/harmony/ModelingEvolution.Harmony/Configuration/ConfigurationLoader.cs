using System.Text.Json;
using ModelingEvolution.Harmony.Core;

namespace ModelingEvolution.Harmony.Configuration;

/// <summary>
/// Loads configuration from JSON file
/// </summary>
public static class ConfigurationLoader
{
    public static MultiprocessConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file not found: {path}");
        }
        
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        
        var config = JsonSerializer.Deserialize<MultiprocessConfiguration>(json, options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize configuration");
        }
        
        return config;
    }
}