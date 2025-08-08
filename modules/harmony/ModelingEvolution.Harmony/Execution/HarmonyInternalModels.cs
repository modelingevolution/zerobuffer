using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Shared;

namespace ModelingEvolution.Harmony.Execution;

/// <summary>
/// Internal Harmony result that enriches StepResponse with orchestration context
/// </summary>
[DebuggerDisplay("Success={Success}, Error={Error}, Duration={Duration.TotalMilliseconds}ms, Logs={Logs.Count}")]
public record StepExecutionResult(
    bool Success,
    string? Error,
    ImmutableDictionary<string, string> Context,
    Exception? Exception,
    ImmutableList<LogEntry> Logs,
    TimeSpan Duration
);

/// <summary>
/// Internal Harmony log entry that enriches LogResponse with process/platform context
/// </summary>
[DebuggerDisplay("{Process} {Level} {Message}")]
public record LogEntry(
    DateTime Timestamp,
    string Process,
    string Platform,
    LogLevel Level,
    string Message
);