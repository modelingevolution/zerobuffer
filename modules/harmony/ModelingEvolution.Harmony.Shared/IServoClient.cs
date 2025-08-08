using System.Threading;
using System.Threading.Tasks;

namespace ModelingEvolution.Harmony.Shared;

/// <summary>
/// Strongly-typed interface for servo process communication.
/// All servo implementations (C++, C#, Python) must implement these methods.
/// </summary>
public interface IServoClient
{
    /// <summary>
    /// Health check to verify servo is alive
    /// </summary>
    Task<bool> HealthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Initialize the servo process with test context
    /// </summary>
    Task<bool> InitializeAsync(InitializeRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Discover available step definitions
    /// </summary>
    Task<DiscoverResponse> DiscoverAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute a test step
    /// </summary>
    Task<StepResponse> ExecuteStepAsync(StepRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clean up resources
    /// </summary>
    Task CleanupAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Shutdown the servo process gracefully
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON-RPC method names used by servo processes
/// </summary>
public static class ServoMethods
{
    public const string Health = "health";
    public const string Initialize = "initialize";
    public const string Discover = "discover";
    public const string ExecuteStep = "executeStep";
    public const string Cleanup = "cleanup";
    public const string Shutdown = "shutdown";
}