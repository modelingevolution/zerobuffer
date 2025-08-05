using Microsoft.Extensions.Logging;

namespace ZeroBuffer.Serve.Logging;

/// <summary>
/// Logger that captures logs for return to client AND writes to file for debugging
/// </summary>
public class DualLogger : ILogger
{
    private readonly string _categoryName;
    private readonly LogCollector _logCollector;
    private readonly FileLogger _fileLogger;
    
    public DualLogger(string categoryName, LogCollector logCollector, FileLogger fileLogger)
    {
        _categoryName = categoryName;
        _logCollector = logCollector;
        _fileLogger = fileLogger;
    }
    
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return new CompositeScope(
            _logCollector.BeginScope(state),
            _fileLogger.BeginScope(state)
        );
    }
    
    public bool IsEnabled(LogLevel logLevel) => true;
    
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        
        // Log to both destinations
        _logCollector.Log(logLevel, eventId, state, exception, formatter);
        _fileLogger.Log(logLevel, eventId, state, exception, formatter);
    }
    
    private class CompositeScope : IDisposable
    {
        private readonly IDisposable _scope1;
        private readonly IDisposable _scope2;
        
        public CompositeScope(IDisposable scope1, IDisposable scope2)
        {
            _scope1 = scope1;
            _scope2 = scope2;
        }
        
        public void Dispose()
        {
            _scope1?.Dispose();
            _scope2?.Dispose();
        }
    }
}