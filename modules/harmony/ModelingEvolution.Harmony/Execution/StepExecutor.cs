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
            // Determine target process
            var targetProcess = step.Process;
            if (string.IsNullOrEmpty(targetProcess))
            {
                // No specific process, might be a general step
                _logger.LogWarning("Step has no process context: {Step}", step.Text);
                return new StepExecutionResult
                {
                    Success = true,
                    Logs = new List<LogEntry>
                    {
                        new() { Message = $"Skipping step with no process context: {step.Text}" }
                    }
                };
            }
            
            // Get connection to the process
            var connection = _processManager.GetConnection(targetProcess);
            var platform = platforms.GetPlatform(targetProcess);
            
            _logger.LogDebug("Executing step on {Process} ({Platform}): {Step}", 
                targetProcess, platform, step.Text);
            
            // Prepare request
            var request = new
            {
                process = targetProcess,
                stepType = step.Type.ToString().ToLowerInvariant(),
                step = step.ProcessedText ?? step.Text,
                originalStep = step.Text,
                parameters = step.Parameters
            };
            
            // Execute via JSON-RPC
            var response = await connection.InvokeAsync<StepResponse>(
                "executeStep", 
                request, 
                cancellationToken);
            
            // Convert response to result
            var logs = new List<LogEntry>();
            
            if (response.Logs != null)
            {
                foreach (var log in response.Logs)
                {
                    logs.Add(new LogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        Process = targetProcess,
                        Platform = platform,
                        Level = log.Level ?? "INFO",
                        Message = log.Message ?? ""
                    });
                }
            }
            
            return new StepExecutionResult
            {
                Success = response.Success,
                Error = response.Error,
                Data = response.Data ?? new Dictionary<string, object>(),
                Logs = logs
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute step: {Step}", step.Text);
            
            return new StepExecutionResult
            {
                Success = false,
                Error = ex.Message,
                Logs = new List<LogEntry>
                {
                    new()
                    {
                        Timestamp = DateTime.UtcNow,
                        Process = step.Process ?? "unknown",
                        Platform = "unknown",
                        Level = "ERROR",
                        Message = $"Step execution failed: {ex.Message}"
                    }
                }
            };
        }
    }
    
    private class StepResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public Dictionary<string, object>? Data { get; set; }
        public List<LogResponse>? Logs { get; set; }
    }
    
    private class LogResponse
    {
        public string? Level { get; set; }
        public string? Message { get; set; }
    }
}