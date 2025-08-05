using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;
using Xunit.Abstractions;

namespace ZeroBuffer.Harmony.Tests;

/// <summary>
/// Base class for generated Harmony tests
/// </summary>
public abstract class HarmonyTestBase
{
    protected readonly ITestOutputHelper Output;
    protected readonly IProcessManager ProcessManager;
    protected readonly IStepExecutor StepExecutor;
    
    protected HarmonyTestBase(ITestOutputHelper output)
    {
        Output = output;
        
        // Initialize with real implementations
        var loggerFactory = NullLoggerFactory.Instance;
        ProcessManager = new ProcessManager(GetConfiguration(), loggerFactory);
        StepExecutor = new StepExecutor(ProcessManager, loggerFactory);
    }
    
    protected async Task ExecuteScenarioAsync(ScenarioExecution scenario)
    {
        Output.WriteLine($"Executing: {scenario}");
        
        var result = await scenario.RunAsync(StepExecutor, ProcessManager);
        
        // Output logs
        foreach (var log in result.Logs)
        {
            Output.WriteLine($"[{log.Timestamp:HH:mm:ss.fff}] [{log.Platform}:{log.Process}] [{log.Level}] {log.Message}");
        }
        
        if (!result.Success)
        {
            Output.WriteLine($"Error: {result.Error}");
            if (result.Exception != null)
            {
                Output.WriteLine($"Exception: {result.Exception}");
            }
            
            throw new Exception($"Scenario failed: {result.Error}");
        }
        
        Output.WriteLine($"Scenario completed successfully in {result.Duration.TotalMilliseconds:F0}ms");
    }
    
    protected ScenarioExecution DeserializeScenario(string json)
    {
        var data = JsonSerializer.Deserialize<ScenarioData>(json) 
            ?? throw new InvalidOperationException("Failed to deserialize scenario");
        
        // Reconstruct ScenarioDefinition
        var scenario = new ScenarioDefinition
        {
            Name = data.ScenarioName,
            Description = data.ScenarioDescription,
            Tags = data.Tags ?? new List<string>(),
            Background = data.Background != null ? new BackgroundDefinition
            {
                Steps = data.Background.Select(DeserializeStep).ToList()
            } : null,
            Steps = data.Steps.Select(DeserializeStep).ToList()
        };
        
        // Reconstruct PlatformCombination
        var platforms = new PlatformCombination(data.Platforms);
        
        return new ScenarioExecution(scenario, platforms);
    }
    
    private StepDefinition DeserializeStep(StepData data)
    {
        return new StepDefinition
        {
            Type = Enum.Parse<StepType>(data.Type),
            Text = data.Text,
            Process = data.Process,
            ProcessedText = data.ProcessedText,
            Parameters = data.Parameters ?? new Dictionary<string, object>()
        };
    }
    
    private static MultiprocessConfiguration GetConfiguration()
    {
        // Load from harmony-config.json if available
        var configPath = Path.Combine(AppContext.BaseDirectory, "harmony-config.json");
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<MultiprocessConfiguration>(json) 
                ?? CreateDefaultConfiguration();
        }
        
        return CreateDefaultConfiguration();
    }
    
    private static MultiprocessConfiguration CreateDefaultConfiguration()
    {
        return new MultiprocessConfiguration
        {
            FeaturesPath = "Features",
            DefaultTimeoutMs = 30000,
            ProcessInitializationDelayMs = 1000,
            Platforms = new Dictionary<string, PlatformConfiguration>
            {
                ["csharp"] = new()
                {
                    Executable = "dotnet",
                    Arguments = "run --project ../../../csharp/ZeroBuffer.Serve/ZeroBuffer.Serve.csproj",
                    WorkingDirectory = "."
                },
                ["python"] = new()
                {
                    Executable = "python",
                    Arguments = "../../../python/zerobuffer_serve.py",
                    WorkingDirectory = "."
                },
                ["cpp"] = new()
                {
                    Executable = "../../../cpp/build/zerobuffer-serve",
                    Arguments = "",
                    WorkingDirectory = "."
                }
            }
        };
    }
    
    // Data classes for deserialization
    protected class ScenarioData
    {
        public string TestId { get; set; } = "";
        public string ScenarioName { get; set; } = "";
        public string? ScenarioDescription { get; set; }
        public List<string>? Tags { get; set; }
        public Dictionary<string, string> Platforms { get; set; } = new();
        public List<StepData>? Background { get; set; }
        public List<StepData> Steps { get; set; } = new();
    }
    
    protected class StepData
    {
        public string Type { get; set; } = "";
        public string Text { get; set; } = "";
        public string? Process { get; set; }
        public string? ProcessedText { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
    }
}