using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroBuffer.Serve;
using ZeroBuffer.Serve.JsonRpc;
using ZeroBuffer.Serve.Logging;
using ZeroBuffer.Tests;
using ZeroBuffer.Tests.StepDefinitions;

var services = new ServiceCollection();

// Create the dual logger provider
var loggerProvider = new DualLoggerProvider();

// Configure logging - dual logging to capture for client AND file for debugging
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddProvider(loggerProvider);
});

// Register services
services.AddSingleton(loggerProvider);

// Register test contexts
services.AddSingleton<ZeroBuffer.Tests.ITestContext, TestContext>();
services.AddSingleton<ZeroBuffer.Serve.JsonRpc.ITestContext, SimpleTestContext>();

// Register buffer naming service
services.AddSingleton<ZeroBuffer.Tests.Services.IBufferNamingService, ZeroBuffer.Tests.Services.BufferNamingService>();

// Register StepRegistry and discover steps
services.AddSingleton<StepRegistry>();
services.AddSingleton<IStepExecutor, RegistryBasedStepExecutor>();

// Register step definition classes from ZeroBuffer.Tests
services.AddTransient<BasicCommunicationSteps>();
services.AddTransient<EdgeCasesSteps>();
services.AddTransient<ProcessLifecycleStepsStub>();
services.AddTransient<DuplexChannelStepsStub>();
// Add more step classes as they are created...

services.AddSingleton<ZeroBufferServe>();

var serviceProvider = services.BuildServiceProvider();

// Log startup to stderr - this happens before JSON-RPC starts
Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] ZeroBuffer C# Serve starting...");

// Discover and register all steps from ZeroBuffer.Tests assembly
var stepRegistry = serviceProvider.GetRequiredService<StepRegistry>();
var testsAssembly = typeof(BasicCommunicationSteps).Assembly;
stepRegistry.DiscoverSteps(testsAssembly);

try
{
    var serve = serviceProvider.GetRequiredService<ZeroBufferServe>();
    await serve.RunAsync(CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Fatal error: {ex}");
    Environment.Exit(1);
}