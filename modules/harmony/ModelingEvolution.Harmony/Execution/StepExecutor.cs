using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.ProcessManagement;
using ModelingEvolution.Harmony.Shared;
using StreamJsonRpc;

namespace ModelingEvolution.Harmony.Execution;

/// <summary>
/// Executes steps by routing them to the appropriate process
/// </summary>
public class StepExecutor : IStepExecutor
{
    private readonly IProcessManager _processManager;
    private readonly ILogger<StepExecutor> _logger;
    
    public StepExecutor(IProcessManager processManager, ILoggerFactory loggerFactory)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _logger = loggerFactory.CreateLogger<StepExecutor>();
    }
    
    public async Task<StepExecutionResult> ExecuteStepAsync(
        StepDefinition step,
        PlatformCombination platforms,
        IReadOnlyDictionary<string, string> context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var targetProcesses = GetTargetProcesses(step, platforms);
            
            if (targetProcesses.Count == 0)
            {
                _logger.LogWarning("No processes available for step: {Step}", step.Text);
                return new StepExecutionResult(
                    Success: true,
                    Error: null,
                    Context: ImmutableDictionary<string, string>.Empty,
                    Exception: null,
                    Logs: ImmutableList.Create(new LogEntry(
                        Timestamp: DateTime.UtcNow,
                        Process: "system",
                        Platform: "harmony",
                        Level: LogLevel.Information,
                        Message: $"No processes available: {step.Text}"
                    )),
                    Duration: TimeSpan.Zero
                );
            }
            
            var isBroadcast = string.IsNullOrEmpty(step.Process);
            if (isBroadcast)
            {
                _logger.LogInformation("Broadcasting step to all processes: {Step}", step.Text);
            }
            
            var results = await Task.WhenAll(
                targetProcesses.Select(processName => 
                    ExecuteOnProcessAsync(step, processName, platforms, isBroadcast, context,cancellationToken)));
            
            return CombineResults(results, isBroadcast);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute step: {Step}", step.Text);
            
            return new StepExecutionResult(
                Success: false,
                Error: ex.Message,
                Context: ImmutableDictionary<string, string>.Empty,
                Exception: ex,
                Logs: ImmutableList.Create(CreateErrorLog(step.Process ?? "unknown", ex.Message)),
                Duration: TimeSpan.Zero
            );
        }
    }
    
    private List<string> GetTargetProcesses(StepDefinition step, PlatformCombination platforms)
    {
        return string.IsNullOrEmpty(step.Process) 
            ? platforms.GetAllProcesses().ToList() 
            : [step.Process];
    }
    
    private async Task<StepExecutionResult> ExecuteOnProcessAsync(
        StepDefinition step, 
        string processName, 
        PlatformCombination platforms,
        bool isBroadcast,
        IReadOnlyDictionary<string,string> context,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = _processManager.GetConnection(processName);
            var platform = platforms.GetPlatform(processName);
            
            _logger.LogDebug("Executing step on {Process} ({Platform}): {Step}", processName, platform, step.Text);
            
            // Create strongly-typed client for this connection
            var client = connection.CreateServoClient();
            
            var request = CreateStepRequest(step, processName, context, isBroadcast);
            var response = await client.ExecuteStepAsync(request, cancellationToken);
            
            return ConvertResponse(response, processName, platform);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute on {Process}", processName);
            
            return new StepExecutionResult(
                Success: false,
                Error: $"{processName}: {ex.Message}",
                Context: ImmutableDictionary<string, string>.Empty,
                Exception: ex,
                Logs: ImmutableList.Create(CreateErrorLog(processName, ex.Message)),
                Duration: TimeSpan.Zero
            );
        }
    }
    
    private StepRequest CreateStepRequest(StepDefinition step, string processName,
        IReadOnlyDictionary<string, string> context, bool isBroadcast)
    {
        var parameters = step.Parameters.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "");
        return new StepRequest(
            Process: processName,
            StepType: step.Type,
            Step: step.Text,
            Parameters: parameters,
            Context: context.ToImmutableDictionary(),
            IsBroadcast: isBroadcast
        );
    }
    
    private StepExecutionResult ConvertResponse(StepResponse response, string processName, string platform)
    {
        // Debug: Check what we got
        //_logger.LogInformation($"ConvertResponse for {processName}: Success={response.Success}, LogsCount={response.Logs?.Count ?? 0}");
        if (response.Logs != null)
        {
            foreach (var log in response.Logs)
            {
                //_logger.LogInformation($"  Log: [{log.Level}] {log.Message}");
            }
        }
        
        var logs = response.Logs?.Select(log => new LogEntry(
            Timestamp: log.Timestamp,
            Process: processName,
            Platform: platform,
            Level: log.Level,
            Message: log.Message ?? ""
        )).ToImmutableList() ?? ImmutableList<LogEntry>.Empty;
        
        return new StepExecutionResult(
            Success: response.Success,
            Error: response.Error,
            Context: response.Context ?? ImmutableDictionary<string, string>.Empty,
            Exception: null,
            Logs: logs,
            Duration: TimeSpan.Zero
        );
    }
    
    private StepExecutionResult CombineResults(StepExecutionResult[] results, bool isBroadcast)
    {
        var allLogs = results.SelectMany(r => r.Logs).ToList();
        var failures = results.Where(r => !r.Success).ToList();
        
        if (failures.Count == 0)
        {
            // BUG: Context is always returned as Empty when combining results from broadcast
            // This means any Context modifications from broadcast steps are lost
            // Should consider: merge contexts, use first non-empty, or track per-process contexts
            return new StepExecutionResult(
                Success: true,
                Error: null,
                Context: ImmutableDictionary<string, string>.Empty,  // TODO: Context is discarded!
                Exception: null,
                Logs: allLogs.ToImmutableList(),
                Duration: TimeSpan.Zero
            );
        }
        
        var errorPrefix = isBroadcast ? "Broadcast failed on: " : "Execution failed: ";
        var errors = string.Join(", ", failures.Select(f => f.Error));
        
        // BUG: Same issue here - Context is discarded on failure
        return new StepExecutionResult(
            Success: false,
            Error: errorPrefix + errors,
            Context: ImmutableDictionary<string, string>.Empty,  // TODO: Context is discarded on failure too!
            Exception: null,
            Logs: allLogs.ToImmutableList(),
            Duration: TimeSpan.Zero
        );
    }
    
    private LogEntry CreateErrorLog(string process, string message)
    {
        return new LogEntry(
            Timestamp: DateTime.UtcNow,
            Process: process,
            Platform: "unknown",
            Level: LogLevel.Error,
            Message: $"Step execution failed: {message}"
        );
    }
    
    // StepResponse, LogResponse, and StepRequest moved to ModelingEvolution.Harmony.Shared
}