using ModelingEvolution.Harmony.Shared;

namespace ZeroBuffer.Serve.JsonRpc;

public interface IStepExecutor
{
    Task<StepResponse> ExecuteAsync(StepRequest request);
    StepRegistry GetStepRegistry();
}