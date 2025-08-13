using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Shared;

namespace ZeroBuffer.Serve.JsonRpc;

public class RegistryBasedStepExecutor : IStepExecutor
{
    private readonly StepRegistry _stepRegistry;
    private readonly ILogger<RegistryBasedStepExecutor> _logger;
    
    public RegistryBasedStepExecutor(StepRegistry stepRegistry, ILogger<RegistryBasedStepExecutor> logger)
    {
        _stepRegistry = stepRegistry;
        _logger = logger;
    }
    
    public async Task<StepResponse> ExecuteAsync(StepRequest request)
    {
        //_logger.LogInformation("Executing step via registry: {StepType} {Step}", request.StepType, request.Step);
        
        // The StepRegistry handles all the execution logic
        return await _stepRegistry.ExecuteStepAsync(request);
    }
    
    public StepRegistry GetStepRegistry()
    {
        return _stepRegistry;
    }
}