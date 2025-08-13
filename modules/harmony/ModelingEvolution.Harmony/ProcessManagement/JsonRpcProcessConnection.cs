using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using ModelingEvolution.Harmony.Shared;

namespace ModelingEvolution.Harmony.ProcessManagement;

/// <summary>
/// Implements process connection using JSON-RPC over stdin/stdout
/// </summary>
public class JsonRpcProcessConnection : IProcessConnection, IDisposable
{
    private readonly Process _process;
    private readonly JsonRpc _jsonRpc;
    private readonly ILogger<JsonRpcProcessConnection> _logger;
    private readonly string _platform;
    private bool _disposed;
    
    public string ProcessName { get; }
    public string Platform => _platform;
    public bool IsConnected => !_disposed && !_process.HasExited;
    
    /// <summary>
    /// Exposed for special non-standard methods like "crash" used in testing
    /// </summary>
    internal JsonRpc JsonRpc => _jsonRpc;
    
    public JsonRpcProcessConnection(
        string processName, 
        string platform,
        ProcessStartInfo startInfo,
        ILogger<JsonRpcProcessConnection> logger)
    {
        ProcessName = processName;
        _platform = platform;
        _logger = logger;
        
        // Configure process for bidirectional communication
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        
        // Start the process
        _process = Process.Start(startInfo) 
            ?? throw new InvalidOperationException($"Failed to start process for {processName}");
            
        _logger.LogInformation("Started {Platform} process for {ProcessName} (PID: {ProcessId})", 
            platform, processName, _process.Id);
        
        // Set up JSON-RPC over stdin/stdout
        // Newtonsoft.Json is case-insensitive by default, which allows it to match
        // both "level"/"message" from C++ and "Level"/"Message" from C#
        var formatter = new JsonMessageFormatter();
        var handler = new HeaderDelimitedMessageHandler(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream, formatter);
        _jsonRpc = new JsonRpc(handler);
        
        // Handle error output
        _process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data) 
                && !e.Data.Contains("[info]", StringComparison.InvariantCultureIgnoreCase)
                && !e.Data.Contains("[Information]", StringComparison.InvariantCultureIgnoreCase)
                && !e.Data.Contains("[trace]", StringComparison.InvariantCultureIgnoreCase) 
                && !e.Data.Contains("[debug]", StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.LogWarning("Process {ProcessName} stderr: {Message}", processName, e.Data);
            }
        };
        _process.BeginErrorReadLine();
        
        // Start JSON-RPC listening
        _jsonRpc.StartListening();
        
        // Log JSON-RPC traffic if debug logging is enabled
        if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
        {
            _jsonRpc.TraceSource.Listeners.Add(new JsonRpcTraceListener(_logger, processName));
        }
    }
    
    public IServoClient CreateServoClient()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JsonRpcProcessConnection));
            
        return _jsonRpc.CreateServoClient(ProcessName);
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        try
        {
            _logger.LogInformation("Disposing connection to {ProcessName}", ProcessName);
            
            // Stop JSON-RPC
            _jsonRpc?.Dispose();
            
            // Give the process a chance to exit gracefully
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                
                if (!_process.WaitForExit(5000))
                {
                    _logger.LogWarning("Process {ProcessName} did not exit gracefully, terminating", ProcessName);
                    _process.Kill();
                }
            }
            
            _process.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing connection to {ProcessName}", ProcessName);
        }
    }
    
    /// <summary>
    /// Custom trace listener for logging JSON-RPC traffic
    /// </summary>
    private class JsonRpcTraceListener : System.Diagnostics.TraceListener
    {
        private readonly ILogger _logger;
        private readonly string _processName;
        
        public JsonRpcTraceListener(ILogger logger, string processName)
        {
            _logger = logger;
            _processName = processName;
        }
        
        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _logger.LogTrace("[{ProcessName} JSON-RPC] {Message}", _processName, message);
            }
        }
        
        public override void WriteLine(string? message)
        {
            Write(message);
        }
    }
}