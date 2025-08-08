using System;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;

namespace ModelingEvolution.Harmony.Shared;

/// <summary>
/// Strongly-typed JSON-RPC client for servo communication.
/// Wraps StreamJsonRpc to provide type-safe method calls.
/// </summary>
public class JsonRpcServoClient : IServoClient
{
    private readonly JsonRpc _jsonRpc;
    private readonly string _processName;
    
    /// <summary>
    /// Creates a new servo client
    /// </summary>
    /// <param name="jsonRpc">The underlying JsonRpc connection</param>
    /// <param name="processName">Name of the process for logging/debugging</param>
    public JsonRpcServoClient(JsonRpc jsonRpc, string processName = "unknown")
    {
        _jsonRpc = jsonRpc ?? throw new ArgumentNullException(nameof(jsonRpc));
        _processName = processName;
    }
    
    /// <inheritdoc/>
    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Health check is parameterless
            return await _jsonRpc.InvokeAsync<bool>(
                ServoMethods.Health, 
                cancellationToken);
        }
        catch (RemoteInvocationException)
        {
            // Health check failure is expected if process is down
            return false;
        }
    }
    
    /// <inheritdoc/>
    public async Task<bool> InitializeAsync(InitializeRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        
        try
        {
            // Use InvokeWithParameterObjectAsync to ensure proper serialization
            return await _jsonRpc.InvokeWithParameterObjectAsync<bool>(
                ServoMethods.Initialize, 
                request, 
                cancellationToken);
        }
        catch (RemoteInvocationException ex)
        {
            // Wrap remote exceptions with more context
            throw new InvalidOperationException($"Failed to initialize servo process '{_processName}': {ex.Message}", ex);
        }
    }
    
    /// <inheritdoc/>
    public async Task<DiscoverResponse> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _jsonRpc.InvokeAsync<DiscoverResponse>(
                ServoMethods.Discover, 
                cancellationToken);
        }
        catch (RemoteInvocationException ex)
        {
            throw new InvalidOperationException($"Failed to discover steps from servo process '{_processName}': {ex.Message}", ex);
        }
    }
    
    /// <inheritdoc/>
    public async Task<StepResponse> ExecuteStepAsync(StepRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        
        try
        {
            return await _jsonRpc.InvokeWithParameterObjectAsync<StepResponse>(
                ServoMethods.ExecuteStep, 
                request, 
                cancellationToken);
        }
        catch (RemoteInvocationException ex)
        {
            // Return error response instead of throwing
            return new StepResponse(
                Success: false,
                Error: $"Remote execution failed: {ex.Message}",
                Context: null,
                Logs: null
            );
        }
    }
    
    /// <inheritdoc/>
    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _jsonRpc.InvokeAsync(
                ServoMethods.Cleanup, 
                cancellationToken);
        }
        catch (RemoteInvocationException)
        {
            // Cleanup failures are not critical
        }
    }
    
    /// <inheritdoc/>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _jsonRpc.InvokeAsync(
                ServoMethods.Shutdown, 
                cancellationToken);
        }
        catch (RemoteInvocationException)
        {
            // Shutdown might fail if process is already terminating
        }
    }
    
    /// <summary>
    /// Disposes the underlying JSON-RPC connection
    /// </summary>
    public void Dispose()
    {
        _jsonRpc?.Dispose();
    }
}