using StreamJsonRpc;

namespace ModelingEvolution.Harmony.Shared;

/// <summary>
/// Extension methods for JsonRpc to create strongly-typed clients
/// </summary>
public static class JsonRpcExtensions
{
    /// <summary>
    /// Creates a strongly-typed servo client from a JsonRpc connection
    /// </summary>
    /// <param name="jsonRpc">The JsonRpc connection</param>
    /// <param name="processName">Optional process name for debugging/logging</param>
    /// <returns>A strongly-typed servo client</returns>
    public static IServoClient CreateServoClient(this JsonRpc jsonRpc, string processName = "unknown")
    {
        return new JsonRpcServoClient(jsonRpc, processName);
    }
}