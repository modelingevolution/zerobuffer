using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.ProcessManagement;

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
        CancellationToken cancellationToken = default)
    {
        try
        {
            var targetProcesses = GetTargetProcesses(step, platforms);
            
            if (targetProcesses.Count == 0)
            {
                _logger.LogWarning("No processes available for step: {Step}", step.Text);
                return new StepExecutionResult
                {
                    Success = true,
                    Logs = [new() { Message = $"No processes available: {step.Text}" }]
                };
            }
            
            var isBroadcast = string.IsNullOrEmpty(step.Process);
            if (isBroadcast)
            {
                _logger.LogInformation("Broadcasting step to all processes: {Step}", step.Text);
            }
            
            var results = await Task.WhenAll(
                targetProcesses.Select(processName => 
                    ExecuteOnProcessAsync(step, processName, platforms, isBroadcast, cancellationToken)));
            
            return CombineResults(results, isBroadcast);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute step: {Step}", step.Text);
            
            return new StepExecutionResult
            {
                Success = false,
                Error = ex.Message,
                Logs = [CreateErrorLog(step.Process ?? "unknown", ex.Message)]
            };
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
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = _processManager.GetConnection(processName);
            var platform = platforms.GetPlatform(processName);
            
            _logger.LogDebug("Executing step on {Process} ({Platform}): {Step}", 
                processName, platform, step.Text);
            
            var request = CreateStepRequest(step, processName, isBroadcast);
            var response = await connection.InvokeAsync<StepResponse>(
                "executeStep", request, cancellationToken);
            
            return ConvertResponse(response, processName, platform);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute on {Process}", processName);
            
            return new StepExecutionResult
            {
                Success = false,
                Error = $"{processName}: {ex.Message}",
                Logs = [CreateErrorLog(processName, ex.Message)]
            };
        }
    }
    
    private object CreateStepRequest(StepDefinition step, string processName, bool isBroadcast)
    {
        return new
        {
            process = processName,
            stepType = step.Type.ToString().ToLowerInvariant(),
            step = step.Text,  // Send full text, not stripped version
            originalStep = step.Text,
            parameters = step.Parameters,
            isBroadcast
        };
    }
    
    private StepExecutionResult ConvertResponse(StepResponse response, string processName, string platform)
    {
        var logs = response.Logs?.Select(log => new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Process = processName,
            Platform = platform,
            Level = log.Level ?? "INFO",
            Message = log.Message ?? ""
        }).ToList() ?? new List<LogEntry>();
        
        return new StepExecutionResult
        {
            Success = response.Success,
            Error = response.Error,
            Data = response.Data ?? new Dictionary<string, object>(),
            Logs = logs
        };
    }
    
    private StepExecutionResult CombineResults(StepExecutionResult[] results, bool isBroadcast)
    {
        var allLogs = results.SelectMany(r => r.Logs).ToList();
        var failures = results.Where(r => !r.Success).ToList();
        
        if (failures.Count == 0)
        {
            return new StepExecutionResult
            {
                Success = true,
                Logs = allLogs
            };
        }
        
        var errorPrefix = isBroadcast ? "Broadcast failed on: " : "Execution failed: ";
        var errors = string.Join(", ", failures.Select(f => f.Error));
        
        return new StepExecutionResult
        {
            Success = false,
            Error = errorPrefix + errors,
            Logs = allLogs
        };
    }
    
    private LogEntry CreateErrorLog(string process, string message)
    {
        return new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Process = process,
            Platform = "unknown",
            Level = "ERROR",
            Message = $"Step execution failed: {message}"
        };
    }
    
    private class StepResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public Dictionary<string, object>? Data { get; set; }
        public List<LogResponse>? Logs { get; set; }
    }
    [DebuggerDisplay("{Level} {Message}")]
    private class LogResponse
    {
        public string? Level { get; set; }
        public string? Message { get; set; }
    }
}