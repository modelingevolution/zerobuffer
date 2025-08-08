using System.Collections.Immutable;
using System.Text.Json;
using TechTalk.SpecFlow;

namespace ZeroBuffer.Serve.JsonRpc;

/// <summary>
/// Extension methods for ScenarioContext to support JSON-RPC context bridging
/// </summary>
public static class ScenarioContextExtensions
{
    /// <summary>
    /// Sets a value in the ScenarioContext, converting to string if needed
    /// </summary>
    public static void SetValue(this ScenarioContext context, string key, object value)
    {
        context[key] = value?.ToString() ?? string.Empty;
    }
    
    /// <summary>
    /// Gets a string value from ScenarioContext
    /// </summary>
    public static string? GetString(this ScenarioContext context, string key)
    {
        return context.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
    
    /// <summary>
    /// Gets a value from ScenarioContext and converts it to the specified type
    /// </summary>
    public static T? GetValue<T>(this ScenarioContext context, string key)
    {
        if (!context.TryGetValue(key, out var value))
            return default;
        
        if (value is T typedValue)
            return typedValue;
        
        var stringValue = value?.ToString();
        if (string.IsNullOrEmpty(stringValue))
            return default;
        
        // Handle common type conversions
        var targetType = typeof(T);
        
        if (targetType == typeof(string))
            return (T)(object)stringValue;
        
        if (targetType == typeof(int))
            return (T)(object)int.Parse(stringValue);
        
        if (targetType == typeof(long))
            return (T)(object)long.Parse(stringValue);
        
        if (targetType == typeof(bool))
            return (T)(object)bool.Parse(stringValue);
        
        if (targetType == typeof(double))
            return (T)(object)double.Parse(stringValue);
        
        if (targetType == typeof(decimal))
            return (T)(object)decimal.Parse(stringValue);
        
        if (targetType == typeof(DateTime))
            return (T)(object)DateTime.Parse(stringValue);
        
        if (targetType == typeof(Guid))
            return (T)(object)Guid.Parse(stringValue);
        
        // Check if type implements IParsable<T>
        var iParsableInterface = targetType.GetInterface("IParsable`1");
        if (iParsableInterface != null)
        {
            // Get the Parse method from IParsable<T>
            var parseMethod = targetType.GetMethod("Parse", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(IFormatProvider) },
                null);
            
            if (parseMethod != null)
            {
                try
                {
                    var result = parseMethod.Invoke(null, new object?[] { stringValue, null });
                    if (result != null)
                        return (T)result;
                }
                catch { }
            }
        }
        
        // For complex types, try JSON deserialization
        try
        {
            return JsonSerializer.Deserialize<T>(stringValue);
        }
        catch
        {
            // If JSON deserialization fails, try to convert
            return (T)Convert.ChangeType(stringValue, targetType);
        }
    }
    
    /// <summary>
    /// Tries to get a value from ScenarioContext and convert it to the specified type
    /// </summary>
    public static bool TryGetValue<T>(this ScenarioContext context, string key, out T? value)
    {
        try
        {
            value = GetValue<T>(context, key);
            return context.ContainsKey(key);
        }
        catch
        {
            value = default;
            return false;
        }
    }
    
    /// <summary>
    /// Converts a ScenarioContext to an ImmutableDictionary<string, string>
    /// </summary>
    public static ImmutableDictionary<string, string> ToImmutableDictionary(this ScenarioContext context)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        
        foreach (var key in context.Keys)
        {
            if (context.TryGetValue(key, out var value))
            {
                builder[key] = value switch
                {
                    string s => s,
                    null => string.Empty,
                    _ => JsonSerializer.Serialize(value)
                };
            }
        }
        
        return builder.ToImmutable();
    }
    
    /// <summary>
    /// Loads values from an ImmutableDictionary into the ScenarioContext
    /// </summary>
    public static void LoadFrom(this ScenarioContext context, ImmutableDictionary<string, string>? source)
    {
        if (source == null)
            return;
        
        foreach (var kvp in source)
        {
            context[kvp.Key] = kvp.Value;
        }
    }
}