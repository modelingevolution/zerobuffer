using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using ZeroBuffer.Serve.JsonRpc;
using ZeroBuffer.Serve.Logging;

namespace ZeroBuffer.Serve;

public class ZeroBufferServe
{
    private readonly ILogger<ZeroBufferServe> _logger;
    private readonly IStepExecutor _stepExecutor;
    private readonly ITestContext _testContext;
    private readonly DualLoggerProvider _loggerProvider;
    
    public ZeroBufferServe(
        ILogger<ZeroBufferServe> logger,
        IStepExecutor stepExecutor,
        ITestContext testContext,
        DualLoggerProvider loggerProvider)
    {
        _logger = logger;
        _stepExecutor = stepExecutor;
        _testContext = testContext;
        _loggerProvider = loggerProvider;
    }
    
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting JSON-RPC server on stdin/stdout");
        
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        
        using var jsonRpc = new StreamJsonRpc.JsonRpc(stdout, stdin);
        
        // Register JSON-RPC methods
        jsonRpc.AddLocalRpcMethod("health", new Func<object, Task<bool>>(async _ => await Task.FromResult(true)));
        jsonRpc.AddLocalRpcMethod("initialize", new Func<InitializeRequest, Task<InitializeResponse>>(InitializeAsync));
        jsonRpc.AddLocalRpcMethod("executeStep", new Func<StepRequest, Task<StepResponse>>(ExecuteStepAsync));
        jsonRpc.AddLocalRpcMethod("cleanup", new Func<Task>(CleanupAsync));
        jsonRpc.AddLocalRpcMethod("shutdown", new Action(() => 
        {
            _logger.LogInformation("Shutdown requested");
            jsonRpc.Dispose();
        }));
        
        jsonRpc.StartListening();
        
        _logger.LogInformation("JSON-RPC server started, waiting for requests...");
        
        try
        {
            await jsonRpc.Completion.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("JSON-RPC server cancelled");
        }
        
        _logger.LogInformation("JSON-RPC server stopped");
    }
    
    private async Task<InitializeResponse> InitializeAsync(InitializeRequest request)
    {
        _logger.LogInformation("Initializing with role: {Role}, platform: {Platform}, scenario: {Scenario}", 
            request.Role, request.Platform, request.Scenario);
        
        try
        {
            _testContext.Initialize(request.Role, request.Platform, request.Scenario, request.TestRunId);
            
            return await Task.FromResult(new InitializeResponse
            {
                Success = true,
                Version = "1.0.0",
                Capabilities = new List<string> { "zerobuffer", "shared-memory", "metadata" }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize");
            return new InitializeResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
    
    private async Task<StepResponse> ExecuteStepAsync(StepRequest request)
    {
        _logger.LogInformation("Executing step: {StepType} {Step}", request.StepType, request.Step);
        
        try
        {
            var result = await _stepExecutor.ExecuteAsync(request);
            
            _logger.LogInformation("Step executed successfully");
            
            // Collect all logs generated during this step
            var logs = _loggerProvider.GetAllLogs();
            result.Logs.AddRange(logs);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step execution failed");
            
            // Get all logs including the error
            var logs = _loggerProvider.GetAllLogs();
            
            return new StepResponse
            {
                Success = false,
                Error = ex.Message,
                Logs = logs
            };
        }
    }
    
    private async Task CleanupAsync()
    {
        _logger.LogInformation("Cleaning up resources");
        
        try
        {
            _testContext.Cleanup();
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup failed");
            throw;
        }
    }
}