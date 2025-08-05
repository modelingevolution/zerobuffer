namespace ZeroBuffer.Serve.JsonRpc;

public interface IStepExecutor
{
    Task<StepResponse> ExecuteAsync(StepRequest request);
}