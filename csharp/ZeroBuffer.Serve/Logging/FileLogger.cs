using Microsoft.Extensions.Logging;

namespace ZeroBuffer.Serve.Logging;

/// <summary>
/// File logger for debugging - writes to stderr and optionally to a file
/// </summary>
public class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string? _logFilePath;
    private readonly object _lock = new();
    
    public FileLogger(string categoryName, string? logFilePath = null)
    {
        _categoryName = categoryName;
        _logFilePath = logFilePath ?? GetDefaultLogPath();
        
        // Ensure directory exists
        if (_logFilePath != null)
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
    
    private static string GetDefaultLogPath()
    {
        var tempPath = Path.GetTempPath();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(tempPath, "zerobuffer-serve", $"serve_{timestamp}.log");
    }
    
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    
    public bool IsEnabled(LogLevel logLevel) => true;
    
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        
        var message = formatter(state, exception);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var level = logLevel.ToString().PadRight(5);
        var logLine = $"[{timestamp}] [{level}] {_categoryName} - {message}";
        
        // Always write to stderr
        Console.Error.WriteLine(logLine);
        
        if (exception != null)
        {
            Console.Error.WriteLine(exception.ToString());
        }
        
        // Optionally write to file
        if (_logFilePath != null)
        {
            lock (_lock)
            {
                try
                {
                    using var writer = new StreamWriter(_logFilePath, append: true);
                    writer.WriteLine(logLine);
                    if (exception != null)
                    {
                        writer.WriteLine(exception.ToString());
                    }
                }
                catch
                {
                    // Ignore file write errors
                }
            }
        }
    }
    
    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}