using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Shared;
using StreamJsonRpc;
using ZeroBuffer.Serve.JsonRpc;
using ZeroBuffer.Serve.Logging;

namespace ZeroBuffer.Serve;

public class ZeroBufferServe
{
    private readonly ILogger<ZeroBufferServe> _logger;
    private readonly IStepExecutor _stepExecutor;
    private readonly DualLoggerProvider _loggerProvider;
    private readonly IServiceProvider _serviceProvider;
    
    public ZeroBufferServe(
        ILogger<ZeroBufferServe> logger,
        IStepExecutor stepExecutor,
        DualLoggerProvider loggerProvider,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _stepExecutor = stepExecutor;
        _loggerProvider = loggerProvider;
        _serviceProvider = serviceProvider;
    }
    
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting JSON-RPC server on stdin/stdout");
        
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        
        using var jsonRpc = new StreamJsonRpc.JsonRpc(stdout, stdin);
        
        // Register JSON-RPC methods
        jsonRpc.AddLocalRpcMethod("health", new Func<Task<bool>>(HealthAsync));
        jsonRpc.AddLocalRpcMethod("initialize", new Func<InitializeRequest, Task<bool>>(InitializeAsync));
        jsonRpc.AddLocalRpcMethod("executeStep", new Func<StepRequest, Task<StepResponse>>(ExecuteStepAsync));
        jsonRpc.AddLocalRpcMethod("discover", new Func<Task<DiscoverResponse>>(DiscoverAsync));
        jsonRpc.AddLocalRpcMethod("cleanup", new Func<Task>(CleanupAsync));
        jsonRpc.AddLocalRpcMethod("shutdown", new Action(() => 
        {
            _logger.LogInformation("Shutdown requested - performing graceful shutdown");
            jsonRpc.Dispose();
            Environment.Exit(0); // Graceful exit
        }));
        jsonRpc.AddLocalRpcMethod("crash", new Action(() => 
        {
            _logger.LogInformation("Crash requested - injecting segmentation fault");
            InjectCrash();
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
    
    private async Task<bool> HealthAsync()
    {
        _logger.LogInformation("Health check requested");
        return await Task.FromResult(true);
    }
    
    private async Task<DiscoverResponse> DiscoverAsync()
    {
        _logger.LogInformation("Discovering available step definitions");
        
        var steps = _stepExecutor.GetStepRegistry().GetAllSteps();
        
        var response = new DiscoverResponse(steps.ToImmutableList());
        
        _logger.LogInformation("Discovered {Count} step definitions", response.Steps.Count);
        return await Task.FromResult(response);
    }
    
    private async Task<bool> InitializeAsync(InitializeRequest request)
    {
        _logger.LogInformation("Initializing with hostPid: {HostPid}, featureId: {FeatureId}, role: {Role}, platform: {Platform}, scenario: {Scenario}", 
            request.HostPid, request.FeatureId, request.Role, request.Platform, request.Scenario);
        
        try
        {
            // Store initialization data in IScenarioContext for step access
            var scenarioContext = _serviceProvider.GetService<ModelingEvolution.Harmony.Shared.IScenarioContext>();
            if (scenarioContext != null)
            {
                scenarioContext["role"] = request.Role;
                scenarioContext["platform"] = request.Platform;
                scenarioContext["scenario"] = request.Scenario;
                scenarioContext["testRunId"] = request.TestRunId;
                scenarioContext["harmony_host_pid"] = request.HostPid.ToString();
                scenarioContext["harmony_feature_id"] = request.FeatureId.ToString();
            }
            
            _logger.LogInformation("Initialization successful");
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize: {Error}", ex.Message);
            return false;
        }
    }
    
    private async Task<StepResponse> ExecuteStepAsync(StepRequest request)
    {
        _logger.LogInformation("Executing step: [{StepType}] {Step}", request.StepType.ToString(), request.Step);
        
        try
        {
            var result = await _stepExecutor.ExecuteAsync(request);
            
            
            _logger.LogInformation("Step executed successfully");
            
            // Collect all logs generated during this step
            var logs = _loggerProvider.GetAllLogs();
            
            // Combine existing logs with new logs
            var allLogs = (result.Logs ?? ImmutableList<LogResponse>.Empty)
                .AddRange(logs);
            
            return new StepResponse(
                Success: result.Success,
                Error: result.Error,
                Context: result.Context,
                Logs: allLogs
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step execution failed");
            
            // Get all logs including the error
            var logs = _loggerProvider.GetAllLogs();
            
            return new StepResponse(
                Success: false,
                Error: ex.Message,
                Context: null,
                Logs: logs.ToImmutableList()
            );
        }
    }
    
    private async Task CleanupAsync()
    {
        _logger.LogInformation("Cleaning up resources");
        
        try
        {
            // Clear IScenarioContext
            var scenarioContext = _serviceProvider.GetService<ModelingEvolution.Harmony.Shared.IScenarioContext>();
            if (scenarioContext != null)
            {
                scenarioContext.Clear();
            }
            
            // Note: TestContext cleanup removed - using IScenarioContext only
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup failed");
            throw;
        }
    }
    
    private unsafe void InjectCrash()
    {
        // Inject a segmentation fault using unsafe code
        // This will cause the process to crash immediately
        try
        {
            int* ptr = null;
            *ptr = 42; // Null pointer dereference - will cause segfault
        }
        catch
        {
            // If the above doesn't work, force an unhandled exception
            throw new AccessViolationException("Injected crash");
        }
    }
}