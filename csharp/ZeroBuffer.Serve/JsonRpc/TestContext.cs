using Microsoft.Extensions.Logging;

namespace ZeroBuffer.Serve.JsonRpc;

public class TestContext : ITestContext
{
    private readonly ILogger<TestContext> _logger;
    private readonly Dictionary<string, object> _data = new();
    
    public string Role { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public string Scenario { get; private set; } = string.Empty;
    public string TestRunId { get; private set; } = string.Empty;
    
    public TestContext(ILogger<TestContext> logger)
    {
        _logger = logger;
    }
    
    public void Initialize(string role, string platform, string scenario, string testRunId)
    {
        Role = role;
        Platform = platform;
        Scenario = scenario;
        TestRunId = testRunId;
        
        _logger.LogInformation("Test context initialized - Role: {Role}, Platform: {Platform}", role, platform);
    }
    
    public void Cleanup()
    {
        _logger.LogInformation("Cleaning up test context");
        _data.Clear();
        
        // Cleanup ZeroBuffer resources
        foreach (var value in _data.Values)
        {
            if (value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
    
    public void SetData(string key, object value)
    {
        _data[key] = value;
        _logger.LogDebug("Set context data: {Key}", key);
    }
    
    public T GetData<T>(string key)
    {
        if (!_data.TryGetValue(key, out var value))
        {
            throw new KeyNotFoundException($"Context data '{key}' not found");
        }
        
        return (T)value;
    }
    
    public bool TryGetData<T>(string key, out T value)
    {
        if (_data.TryGetValue(key, out var obj) && obj is T typedValue)
        {
            value = typedValue;
            return true;
        }
        
        value = default(T)!;
        return false;
    }
}