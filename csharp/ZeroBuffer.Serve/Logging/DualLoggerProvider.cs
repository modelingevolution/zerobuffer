using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Shared;

namespace ZeroBuffer.Serve.Logging;

public class DualLoggerProvider : ILoggerProvider
{
    private readonly Dictionary<string, LogCollector> _collectors = new();
    private readonly string? _logFilePath;
    private readonly object _lock = new();
    
    public DualLoggerProvider(string? logFilePath = null)
    {
        _logFilePath = logFilePath;
    }
    
    public ILogger CreateLogger(string categoryName)
    {
        lock (_lock)
        {
            if (!_collectors.TryGetValue(categoryName, out var collector))
            {
                collector = new LogCollector(categoryName);
                _collectors[categoryName] = collector;
            }
            
            var fileLogger = new FileLogger(categoryName, _logFilePath);
            return new DualLogger(categoryName, collector, fileLogger);
        }
    }
    
    public LogCollector GetCollector(string categoryName)
    {
        lock (_lock)
        {
            return _collectors.TryGetValue(categoryName, out var collector) 
                ? collector 
                : throw new InvalidOperationException($"No collector for category {categoryName}");
        }
    }
    
    public List<LogResponse> GetAllLogs()
    {
        lock (_lock)
        {
            var allLogs = new List<LogResponse>();
            foreach (var collector in _collectors.Values)
            {
                allLogs.AddRange(collector.GetAndClearLogs());
            }
            return allLogs;
        }
    }
    
    public void Dispose()
    {
        // Nothing to dispose
    }
}