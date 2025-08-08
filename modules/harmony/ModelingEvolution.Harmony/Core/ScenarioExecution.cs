using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;
using ModelingEvolution.Harmony.Shared;

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
    public async Task<ScenarioExecutionResult> RunAsync(IStepExecutor stepExecutor, IProcessManager processManager,
        Action<string> Log, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var logs = new List<LogEntry>();
        
        try
        {
            // Start required processes
            await StartProcessesAsync(processManager,cancellationToken);
            ImmutableDictionary<string,string> context = ImmutableDictionary<string, string>.Empty;
            // Execute background steps if any
            if (Scenario.Background != null)
            {
                foreach (var step in Scenario.Background.Steps)
                {
                    // Check for special steps in background too
                    var (isSpecial, specialResult) = await HandleSpecialStepAsync(step, processManager, cancellationToken);
                    
                    if (isSpecial)
                    {
                        logs.AddRange(specialResult.Logs);
                        if (!specialResult.Success)
                        {
                            return new ScenarioExecutionResult
                            {
                                Success = false,
                                Duration = stopwatch.Elapsed,
                                Error = specialResult.Error,
                                Logs = logs
                            };
                        }
                        continue;
                    }
                    
                    var result = await stepExecutor.ExecuteStepAsync(step, Platforms, context,cancellationToken);
                    context = result.Context;
                    logs.AddRange(result.Logs);

                    foreach(var log in result.Logs)
                        Log($"[{log.Timestamp:HH:mm:ss.fff}] [{log.Platform}:{log.Process}] [{log.Level}] {log.Message}");

                    if (!result.Success)
                    {
                        return new ScenarioExecutionResult
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
                // Check for special process lifecycle steps
                var (isSpecial, specialResult) = await HandleSpecialStepAsync(step, processManager, cancellationToken);
                
                if (isSpecial)
                {
                    logs.AddRange(specialResult.Logs);
                    if (!specialResult.Success)
                    {
                        return new ScenarioExecutionResult
                        {
                            Success = false,
                            Duration = stopwatch.Elapsed,
                            Error = specialResult.Error,
                            Logs = logs
                        };
                    }
                    continue;
                }
                Debug.WriteLine($"==> {step}");
                var result = await stepExecutor.ExecuteStepAsync(step, Platforms, context, cancellationToken);
                context = result.Context;
                logs.AddRange(result.Logs);

                foreach (var log in result.Logs)
                {
                    var m = $"[{log.Timestamp:HH:mm:ss.fff}] [{log.Platform}:{log.Process}] [{log.Level}] {log.Message}";
                    Log(m);
                    Debug.WriteLine(m);
                }

                if (!result.Success)
                {
                    return new ScenarioExecutionResult
                    {
                        Success = false,
                        Duration = stopwatch.Elapsed,
                        Error = result.Error,
                        Logs = logs
                    };
                }
            }
            
            return new ScenarioExecutionResult
            {
                Success = true,
                Duration = stopwatch.Elapsed,
                Logs = logs
            };
        }
        catch (Exception ex)
        {
            return new ScenarioExecutionResult
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
    
    private async Task<(bool isSpecial, StepExecutionResult result)> HandleSpecialStepAsync(
        StepDefinition step, 
        IProcessManager processManager,
        CancellationToken cancellationToken)
    {
        // Check for special Harmony-handled steps
        
        // 1. Wait step (any step type)
        // Pattern: "wait for 'X' seconds"
        var waitMatch = System.Text.RegularExpressions.Regex.Match(
            step.Text,
            @"wait\s+for\s+'(\d+)'\s+seconds",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (waitMatch.Success)
        {
            var seconds = int.Parse(waitMatch.Groups[1].Value);
            var logs = new List<LogEntry>
            {
                new LogEntry(
                    Timestamp: DateTime.UtcNow,
                    Process: "harmony",
                    Platform: "harmony",
                    Level: LogLevel.Information,
                    Message: $"Waiting for {seconds} seconds"
                )
            };
            
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            
            return (true, new StepExecutionResult(
                Success: true,
                Error: null,
                Context: ImmutableDictionary<string, string>.Empty,
                Exception: null,
                Logs: logs.ToImmutableList(),
                Duration: TimeSpan.Zero
            ));
        }
        
        // 2. Process lifecycle steps (When only)
        // Pattern: "When the 'processName' process {shutdown|is shutdown|killed|is killed|crashes}"
        
        if (step.Type != StepType.When)
        {
            return (false, CreateEmptyStepExecutionResult());
        }
        
        var text = step.Text.ToLowerInvariant();
        
        // Extract process name from patterns like "the 'writer' process"
        var processMatch = System.Text.RegularExpressions.Regex.Match(
            step.Text, 
            @"the\s+'([^']+)'\s+process\s+(shutdown|is\s+shutdown|killed|is\s+killed|crashes)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (!processMatch.Success)
        {
            return (false, CreateEmptyStepExecutionResult());
        }
        
        var processName = processMatch.Groups[1].Value;
        var action = processMatch.Groups[2].Value.ToLowerInvariant();
        
        try
        {
            var logs = new List<LogEntry>();
            
            if (action.Contains("shutdown"))
            {
                // Graceful shutdown
                logs.Add(new LogEntry(
                    Timestamp: DateTime.UtcNow,
                    Process: processName,
                    Platform: "unknown",
                    Level: LogLevel.Information,
                    Message: $"Gracefully shutting down process '{processName}'"
                ));
                
                await GracefulShutdownAsync(processManager, processName, cancellationToken);
            }
            else if (action.Contains("killed"))
            {
                // Force kill
                logs.Add(new LogEntry(
                    Timestamp: DateTime.UtcNow,
                    Process: processName,
                    Platform: "unknown",
                    Level: LogLevel.Information,
                    Message: $"Force killing process '{processName}'"
                ));
                
                await ForceKillProcessAsync(processManager, processName);
            }
            else if (action == "crashes")
            {
                // Inject crash via JSON-RPC
                logs.Add(new LogEntry(
                    Timestamp: DateTime.UtcNow,
                    Process: processName,
                    Platform: "unknown",
                    Level: LogLevel.Information,
                    Message: $"Injecting crash into process '{processName}'"
                ));
                
                await InjectCrashAsync(processManager, processName, cancellationToken);
            }
            
            return (true, new StepExecutionResult(
                Success: true,
                Error: null,
                Context: ImmutableDictionary<string, string>.Empty,
                Exception: null,
                Logs: logs.ToImmutableList(),
                Duration: TimeSpan.Zero
            ));
        }
        catch (Exception ex)
        {
            return (true, new StepExecutionResult(
                Success: false,
                Error: $"Failed to {action} process '{processName}': {ex.Message}",
                Context: ImmutableDictionary<string, string>.Empty,
                Exception: ex,
                Logs: ImmutableList.Create(new LogEntry(
                    Timestamp: DateTime.UtcNow,
                    Process: processName,
                    Platform: "unknown",
                    Level: LogLevel.Error,
                    Message: ex.Message
                )),
                Duration: TimeSpan.Zero
            ));
        }
    }
    
    private async Task GracefulShutdownAsync(IProcessManager processManager, string processName, CancellationToken cancellationToken)
    {
        // Send shutdown signal via JSON-RPC
        var connection = processManager.GetConnection(processName);
        var client = connection.CreateServoClient();
        await client.ShutdownAsync(cancellationToken);
        
        // Give it a moment to shutdown gracefully
        await Task.Delay(1000, cancellationToken);
    }
    
    private async Task ForceKillProcessAsync(IProcessManager processManager, string processName)
    {
        // Use ProcessManager's internal mechanism to kill the process
        await processManager.StopProcessAsync(processName);
    }
    
    private async Task InjectCrashAsync(IProcessManager processManager, string processName, CancellationToken cancellationToken)
    {
        // Send crash command via JSON-RPC (not part of standard servo interface)
        var connection = processManager.GetConnection(processName);
        
        // Cast to concrete type to access JsonRpc for non-standard methods
        if (connection is JsonRpcProcessConnection jsonRpcConnection)
        {
            await jsonRpcConnection.JsonRpc.InvokeAsync("crash", cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Cannot inject crash into non-JsonRpc connection for {processName}");
        }
        
        // Give it a moment to crash
        await Task.Delay(500, cancellationToken);
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
    
    private static StepExecutionResult CreateEmptyStepExecutionResult()
    {
        return new StepExecutionResult(
            Success: true,
            Error: null,
            Context: ImmutableDictionary<string, string>.Empty,
            Exception: null,
            Logs: ImmutableList<LogEntry>.Empty,
            Duration: TimeSpan.Zero
        );
    }
}

/// <summary>
/// Local execution result with additional properties for scenario execution
/// </summary>
public class ScenarioExecutionResult
{
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public string? Error { get; init; }
    public Exception? Exception { get; init; }
    public List<LogEntry> Logs { get; init; } = new();
}