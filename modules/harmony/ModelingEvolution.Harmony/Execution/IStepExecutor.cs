using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Shared;

namespace ModelingEvolution.Harmony.Execution;

/// <summary>
/// Executes individual steps by routing them to the appropriate process
/// </summary>
public interface IStepExecutor
{
    Task<StepExecutionResult> ExecuteStepAsync(
        StepDefinition step, 
        PlatformCombination platforms,
        IReadOnlyDictionary<string, string> context,
        CancellationToken cancellationToken = default);
}