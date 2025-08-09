using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ModelingEvolution.Harmony.Shared;

public enum StepType
{
    Given,
    When,
    Then
}
/// <summary>
/// Request to execute a step in a test process
/// </summary>
[DebuggerDisplay("{Process} {StepType}: {Step}")]
public record StepRequest(
    string Process,
    StepType StepType,
    string Step,
    ImmutableDictionary<string, string> Parameters,
    ImmutableDictionary<string, string> Context,
    bool IsBroadcast = false
);

/// <summary>
/// Response from a step execution
/// </summary>
[DebuggerDisplay("Success={Success}, Error={Error}")]
public record StepResponse(
    bool Success,
    string? Error,
    ImmutableDictionary<string, string>? Context,
    ImmutableList<LogResponse>? Logs
);

/// <summary>
/// Log entry from a test process
/// </summary>
[DebuggerDisplay("{Level} {Message}")]
public record LogResponse(
    DateTime Timestamp,
    LogLevel Level,
    string Message
);

/// <summary>
/// Request to initialize a test process
/// </summary>
[DebuggerDisplay("{Role} on {Platform} - {Scenario}")]
public record InitializeRequest(
    string Role,
    string Platform,
    string Scenario,
    int HostPid,
    int FeatureId
)
{
    public string TestRunId => $"{HostPid}_{FeatureId}";
}

// Note: Health check doesn't need a request model - it's a parameterless ping

// HealthRequest removed - health check should be parameterless

/// <summary>
/// Response from discover method
/// </summary>
[DebuggerDisplay("Steps Count={Steps.Count}")]
public record DiscoverResponse(
    ImmutableList<StepInfo> Steps
);

/// <summary>
/// Information about a step definition
/// </summary>
[DebuggerDisplay("{Type} {Pattern}")]
public record StepInfo(
    string Type,
    string Pattern
);

// StepExecutionResult and LogEntry are Harmony-internal types and should not be in shared contracts