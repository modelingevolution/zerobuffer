using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;
using Xunit.Abstractions;
using Xunit.Extensions.Logging;

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
        
        // Create xUnit logger factory
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddXunit(output);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        ProcessManager = new ProcessManager(GetConfiguration(), loggerFactory);
        StepExecutor = new StepExecutor(ProcessManager, loggerFactory);
    }
    
    protected async Task ExecuteScenarioAsync(ScenarioExecution scenario)
    {
        Output.WriteLine($"Executing: {scenario}");
        
        var result = await scenario.RunAsync(StepExecutor, ProcessManager, Output.WriteLine);
        
       
        
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
        return DeserializeScenarioStatic(json);
    }
    
    protected static ScenarioExecution DeserializeScenarioStatic(string json)
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
                Steps = data.Background.Select(DeserializeStepStatic).ToList()
            } : null,
            Steps = data.Steps.Select(DeserializeStepStatic).ToList(),
            FeatureId = data.FeatureId
        };
        
        // Reconstruct PlatformCombination
        var platforms = new PlatformCombination(data.Platforms);
        
        return new ScenarioExecution(scenario, platforms);
    }
    
    private StepDefinition DeserializeStep(StepData data)
    {
        return DeserializeStepStatic(data);
    }
    
    private static StepDefinition DeserializeStepStatic(StepData data)
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
        var configPath = Path.Combine(AppContext.BaseDirectory, "harmony-config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configPath}");
        }
        
        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        
        var config = JsonSerializer.Deserialize<MultiprocessConfiguration>(json, options);
        if (config == null)
        {
            throw new InvalidOperationException($"Failed to deserialize configuration from: {configPath}");
        }
        
        if (config.Platforms == null || config.Platforms.Count == 0)
        {
            throw new InvalidOperationException($"No platforms configured in: {configPath}");
        }
        
        return config;
    }
    
    
    // Data classes for deserialization
    protected class ScenarioData
    {
        public string TestId { get; set; } = "";
        public int FeatureId { get; set; }
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