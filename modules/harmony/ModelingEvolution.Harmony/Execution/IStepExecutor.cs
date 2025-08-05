using ModelingEvolution.Harmony.Core;

namespace ModelingEvolution.Harmony.Execution;

/// <summary>
/// Executes individual steps by routing them to the appropriate process
/// </summary>
public interface IStepExecutor
{
    Task<StepExecutionResult> ExecuteStepAsync(
        StepDefinition step, 
        PlatformCombination platforms,
        CancellationToken cancellationToken = default);
}

public class StepExecutionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, object> Data { get; init; } = new();
    public List<LogEntry> Logs { get; init; } = new();
}