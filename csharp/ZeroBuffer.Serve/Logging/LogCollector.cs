using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Shared;


namespace ZeroBuffer.Serve.Logging;

/// <summary>
/// Collects logs to return to the client via JSON-RPC response
/// </summary>
public class LogCollector : ILogger
{
    private readonly string _categoryName;
    private readonly List<LogResponse> _logs = new();
    private readonly object _lock = new();
    
    public LogCollector(string categoryName)
    {
        _categoryName = categoryName;
    }
    
    public List<LogResponse> GetAndClearLogs()
    {
        lock (_lock)
        {
            var logs = _logs.ToList();
            _logs.Clear();
            return logs;
        }
    }
    
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        
        var message = formatter(state, exception);
        
        lock (_lock)
        {
            LogLevel harmonyLevel = logLevel;
            _logs.Add(new LogResponse(
                Timestamp: DateTime.UtcNow,
                Level: harmonyLevel,
                Message: message
            ));
            
            if (exception != null)
            {
                _logs.Add(new LogResponse(
                    Timestamp: DateTime.UtcNow,
                    Level: LogLevel.Error,
                    Message: exception.ToString()
                ));
            }
        }
    }
    
    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}