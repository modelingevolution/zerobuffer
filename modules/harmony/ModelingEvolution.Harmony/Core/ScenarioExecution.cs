using System.Diagnostics;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;

namespace ModelingEvolution.Harmony.Core;

/// <summary>
/// Represents a single test execution with a specific platform combination
/// </summary>
public class ScenarioExecution
{
    public ScenarioDefinition Scenario { get; }
    public PlatformCombination Platforms { get; }
    public string TestId { get; }
    
    
    
    public ScenarioExecution(
        ScenarioDefinition scenario, 
        PlatformCombination platforms)
    {
        Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        Platforms = platforms ?? throw new ArgumentNullException(nameof(platforms));
        
        
        TestId = GenerateTestId();
    }
    
    /// <summary>
    /// Executes the scenario with the configured platform combination
    /// </summary>
    public async Task<ExecutionResult> RunAsync(IStepExecutor stepExecutor, IProcessManager processManager, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var logs = new List<LogEntry>();
        
        try
        {
            // Start required processes
            await StartProcessesAsync(processManager,cancellationToken);
            
            // Execute background steps if any
            if (Scenario.Background != null)
            {
                foreach (var step in Scenario.Background.Steps)
                {
                    var result = await stepExecutor.ExecuteStepAsync(step, Platforms, cancellationToken);
                    logs.AddRange(result.Logs);
                    
                    if (!result.Success)
                    {
                        return new ExecutionResult
                        {
                            Success = false,
                            Duration = stopwatch.Elapsed,
                            Error = result.Error,
                            Logs = logs
                        };
                    }
                }
            }
            
            // Execute scenario steps
            foreach (var step in Scenario.Steps)
            {
                var result = await stepExecutor.ExecuteStepAsync(step, Platforms, cancellationToken);
                logs.AddRange(result.Logs);
                
                if (!result.Success)
                {
                    return new ExecutionResult
                    {
                        Success = false,
                        Duration = stopwatch.Elapsed,
                        Error = result.Error,
                        Logs = logs
                    };
                }
            }
            
            return new ExecutionResult
            {
                Success = true,
                Duration = stopwatch.Elapsed,
                Logs = logs
            };
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                Success = false,
                Duration = stopwatch.Elapsed,
                Error = ex.Message,
                Exception = ex,
                Logs = logs
            };
        }
        finally
        {
            await StopProcessesAsync(processManager);
        }
    }
    
    /// <summary>
    /// Provides a human-readable test name
    /// </summary>
    public override string ToString()
    {
        return $"{Platforms} | {Scenario.Name}";
    }
    
    private string GenerateTestId()
    {
        var scenarioId = Scenario.Name
            .Replace(" ", "-")
            .Replace(".", "-")
            .ToLowerInvariant();
            
        var platformId = Platforms.ToString()
            .Replace("/", "-")
            .ToLowerInvariant();
            
        return $"{platformId}-{scenarioId}";
    }
    
    private async Task StartProcessesAsync(IProcessManager processManager, CancellationToken cancellationToken)
    {
        var processes = ExtractProcessNames();
        var hostPid = Environment.ProcessId;
        var featureId = Scenario.FeatureId;
        
        foreach (var (processName, platform) in Platforms.GetMappings(processes))
        {
            await processManager.StartProcessAsync(processName, platform, hostPid, featureId, cancellationToken);
        }
    }
    
    private async Task StopProcessesAsync(IProcessManager processManager)
    {
        await processManager.StopAllProcessesAsync();
    }
    
    private List<string> ExtractProcessNames()
    {
        var processNames = new HashSet<string>();
        
        // Extract from background
        if (Scenario.Background != null)
        {
            foreach (var step in Scenario.Background.Steps)
            {
                if (!string.IsNullOrEmpty(step.Process))
                {
                    processNames.Add(step.Process);
                }
            }
        }
        
        // Extract from scenario steps
        foreach (var step in Scenario.Steps)
        {
            if (!string.IsNullOrEmpty(step.Process))
            {
                processNames.Add(step.Process);
            }
        }
        
        return processNames.ToList();
    }
}

public class ExecutionResult
{
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public string? Error { get; init; }
    public Exception? Exception { get; init; }
    public List<LogEntry> Logs { get; init; } = new();
}

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Process { get; init; } = "";
    public string Platform { get; init; } = "";
    public string Level { get; init; } = "INFO";
    public string Message { get; init; } = "";
}