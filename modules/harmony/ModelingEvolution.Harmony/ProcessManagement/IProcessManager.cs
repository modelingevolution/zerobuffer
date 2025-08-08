using ModelingEvolution.Harmony.Shared;

namespace ModelingEvolution.Harmony.ProcessManagement;

/// <summary>
/// Manages the lifecycle of test processes
/// </summary>
public interface IProcessManager : IDisposable
{
    Task StartProcessAsync(string processName, string platform, CancellationToken cancellationToken = default);
    Task StartProcessAsync(string processName, string platform, int hostPid, int featureId, CancellationToken cancellationToken = default);
    Task StopProcessAsync(string processName);
    Task StopAllProcessesAsync();
    Task<bool> IsProcessRunningAsync(string processName);
    IProcessConnection GetConnection(string processName);
}

/// <summary>
/// Represents a connection to a test process
/// </summary>
public interface IProcessConnection
{
    string ProcessName { get; }
    string Platform { get; }
    bool IsConnected { get; }
    IServoClient CreateServoClient();
}