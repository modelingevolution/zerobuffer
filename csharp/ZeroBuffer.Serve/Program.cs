using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechTalk.SpecFlow;
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

// Test context removed - using custom IScenarioContext for context bridging

// Register custom IScenarioContext for servo context bridging (replaces SpecFlow's ScenarioContext)
// This allows us to bridge JSON-RPC Context with step execution
services.AddScoped<ModelingEvolution.Harmony.Shared.IScenarioContext, ModelingEvolution.Harmony.Shared.ServoContext>();

// Register buffer naming service
services.AddSingleton<ZeroBuffer.Tests.Services.IBufferNamingService, ZeroBuffer.Tests.Services.BufferNamingService>();

// Register StepRegistry and discover steps
services.AddSingleton<StepRegistry>();
services.AddSingleton<IStepExecutor, RegistryBasedStepExecutor>();

// Automatically discover and register all step definition classes as singletons
var testsAssembly = typeof(BasicCommunicationSteps).Assembly;
var stepDefinitionTypes = testsAssembly.GetTypes()
    .Where(t => t.GetCustomAttribute<BindingAttribute>() != null && !t.IsAbstract && t.IsClass)
    .ToList();

Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Registering {stepDefinitionTypes.Count} step definition classes as singletons");

foreach (var stepType in stepDefinitionTypes)
{
    services.AddSingleton(stepType);
    Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Registered: {stepType.Name}");
}

services.AddSingleton<ZeroBufferServe>();

var serviceProvider = services.BuildServiceProvider();

// Log startup to stderr - this happens before JSON-RPC starts
Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] ZeroBuffer C# Serve starting...");

// Discover and register all steps from ZeroBuffer.Tests assembly
var stepRegistry = serviceProvider.GetRequiredService<StepRegistry>();
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