using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

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
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;
    
    public string ProcessName { get; }
    public string Platform => _platform;
    public bool IsConnected => !_disposed && !_process.HasExited;
    
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
        _jsonRpc = new JsonRpc(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream);
        
        // Handle error output
        _process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogWarning("Process {ProcessName} stderr: {Message}", processName, e.Data);
            }
        };
        _process.BeginErrorReadLine();
        
        // Start JSON-RPC listening
        _jsonRpc.StartListening();
        
        // Log JSON-RPC traffic if debug logging is enabled
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _jsonRpc.TraceSource.Listeners.Add(new JsonRpcTraceListener(_logger, processName));
        }
    }
    
    public async Task<T> InvokeAsync<T>(string method, object parameters, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JsonRpcProcessConnection));
            
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Invoking {Method} on {ProcessName} ({Platform})", method, ProcessName, Platform);
            
            // Log the request if debug is enabled
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var requestJson = JsonSerializer.Serialize(parameters, new JsonSerializerOptions { WriteIndented = true });
                _logger.LogDebug("Request to {ProcessName}:\n{Request}", ProcessName, requestJson);
            }
            
            // Invoke the method
            var result = await _jsonRpc.InvokeWithParameterObjectAsync<T>(
                method, 
                parameters, 
                cancellationToken);
                
            // Log the response if debug is enabled
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var responseJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                _logger.LogDebug("Response from {ProcessName}:\n{Response}", ProcessName, responseJson);
            }
            
            return result;
        }
        catch (RemoteInvocationException ex)
        {
            _logger.LogError(ex, "Remote invocation failed for {Method} on {ProcessName}", method, ProcessName);
            throw new InvalidOperationException($"Remote process {ProcessName} failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invoke {Method} on {ProcessName}", method, ProcessName);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
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
            _connectionLock.Dispose();
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