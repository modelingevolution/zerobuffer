using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroBuffer.Serve.JsonRpc;

public class SimpleStepExecutor : IStepExecutor
{
    private readonly ILogger<SimpleStepExecutor> _logger;
    
    public SimpleStepExecutor(ILogger<SimpleStepExecutor> logger)
    {
        _logger = logger;
    }
    
    public async Task<StepResponse> ExecuteAsync(StepRequest request)
    {
        _logger.LogInformation("Executing step: {StepType} {Step}", request.StepType, request.Step);
        
        // For now, just handle a simple step
        if (request.Step.Contains("creates buffer") && request.Step.Contains("with size"))
        {
            _logger.LogInformation("Would create buffer here");
            
            return await Task.FromResult(new StepResponse
            {
                Success = true,
                Logs = new List<LogEntry>
                {
                    new() { Level = "INFO", Message = "Buffer created (mock)" }
                }
            });
        }
        
        return new StepResponse
        {
            Success = false,
            Error = $"Step not implemented: {request.Step}",
            Logs = new List<LogEntry>
            {
                new() { Level = "WARN", Message = $"No handler for: {request.Step}" }
            }
        };
    }
    
    public StepRegistry GetStepRegistry()
    {
        // SimpleStepExecutor doesn't use a registry, return empty one
        var serviceProvider = new ServiceCollection()
            .BuildServiceProvider();
        return new StepRegistry(Microsoft.Extensions.Logging.Abstractions.NullLogger<StepRegistry>.Instance, serviceProvider);
    }
}