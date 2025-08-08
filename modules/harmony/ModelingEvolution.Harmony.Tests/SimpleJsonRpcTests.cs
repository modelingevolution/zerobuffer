using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.Gherkin;

namespace ModelingEvolution.Harmony.Tests;

/// <summary>
/// Simpler tests that demonstrate JSON-RPC request generation without complex mocking
/// </summary>
public class SimpleJsonRpcTests
{
    private readonly ITestOutputHelper _output;
    private readonly GherkinParser _parser;
    private readonly IScenarioGenerator _generator;
    
    public SimpleJsonRpcTests(ITestOutputHelper output)
    {
        _output = output;
        var processExtractor = new ProcessContextExtractor();
        _parser = new GherkinParser(processExtractor);
        _generator = new ScenarioGenerator(_parser);
    }
    
    [Fact]
    public void GenerateScenarioExecutions_ShowsPlatformCombinations()
    {
        // Arrange
        var featurePath = Path.Combine("Features", "BasicCommunication.feature");
        var platforms = new[] { "csharp", "python", "cpp" };
        
        // Act - Parse feature file and get first scenario
        var scenarios = _parser.ParseFeatureFile(featurePath, FeatureIdMapper.GetFeatureId).ToList();
        var scenario = scenarios.First(s => s.Name.Contains("Simple Write-Read Cycle"));
        var processes = scenario.GetRequiredProcesses().ToList();
        
        // Generate all platform combinations
        var combinations = GenerateAllCombinations(processes, platforms);
        
        // Assert and display
        _output.WriteLine($"Scenario: {scenario.Name}");
        _output.WriteLine($"Required processes: {string.Join(", ", processes)}");
        _output.WriteLine($"Platforms: {string.Join(", ", platforms)}");
        _output.WriteLine($"Total combinations: {combinations.Count} (expected: {Math.Pow(platforms.Length, processes.Count)})\n");
        
        foreach (var combo in combinations)
        {
            _output.WriteLine($"Combination: {combo}");
            
            // Show what JSON-RPC calls would be made
            _output.WriteLine("  JSON-RPC sequence:");
            foreach (var step in scenario.Steps.Where(s => s.Process != null))
            {
                var platform = combo.GetPlatform(step.Process!);
                _output.WriteLine($"    → {step.Process} ({platform}): executeStep");
                
                // Show the request that would be sent
                var request = new
                {
                    process = step.Process,
                    stepType = step.Type.ToString().ToLowerInvariant(),
                    step = step.Text,  // Send full text, not stripped version
                    originalStep = step.Text,
                    parameters = ExtractStepParameters(step.Text)
                };
                
                if (step == scenario.Steps.First(s => s.Process != null))
                {
                    _output.WriteLine($"      Request: {JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = false })}");
                }
            }
            _output.WriteLine("");
        }
        
        combinations.Should().HaveCount((int)Math.Pow(platforms.Length, processes.Count));
    }
    
    [Fact]
    public void ShowJsonRpcRequestStructure_ForDifferentStepTypes()
    {
        // Arrange
        var testSteps = new[]
        {
            new StepDefinition
            {
                Process = "reader",
                Type = StepType.Given,
                Text = "creates buffer 'test' with metadata size '1024' and payload size '10240'",
                ProcessedText = "creates buffer 'test' with metadata size '1024' and payload size '10240'"
            },
            new StepDefinition
            {
                Process = "writer",
                Type = StepType.When,
                Text = "writes frame with size '2048' and sequence '42'",
                ProcessedText = "writes frame with size '2048' and sequence '42'"
            },
            new StepDefinition
            {
                Process = "reader",
                Type = StepType.Then,
                Text = "should read frame with sequence '42'",
                ProcessedText = "should read frame with sequence '42'"
            }
        };
        
        _output.WriteLine("JSON-RPC Request Examples:\n");
        
        foreach (var step in testSteps)
        {
            var request = new
            {
                process = step.Process,
                stepType = step.Type.ToString().ToLowerInvariant(),
                step = step.Text,  // Send full text, not stripped version
                originalStep = step.Text,
                parameters = ExtractStepParameters(step.Text)
            };
            
            _output.WriteLine($"{step.Type} step from {step.Process}:");
            _output.WriteLine($"  Text: {step.Text}");
            _output.WriteLine($"  JSON-RPC Request:");
            _output.WriteLine($"{JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true })}\n");
        }
    }
    
    [Fact]
    public void AnalyzeFeatureFiles_ShowsProcessDistribution()
    {
        // Analyze all feature files to understand process usage
        var featuresDir = "Features";
        var featureFiles = Directory.GetFiles(featuresDir, "*.feature").Take(3); // Just first 3 for brevity
        
        var processStats = new Dictionary<string, int>();
        var scenarioCount = 0;
        
        foreach (var featureFile in featureFiles)
        {
            var scenarios = _parser.ParseFeatureFile(featureFile, FeatureIdMapper.GetFeatureId).ToList();
            scenarioCount += scenarios.Count;
            
            foreach (var scenario in scenarios)
            {
                var processes = scenario.GetRequiredProcesses();
                foreach (var process in processes)
                {
                    processStats[process] = processStats.GetValueOrDefault(process) + 1;
                }
            }
        }
        
        _output.WriteLine("Process Usage Analysis:");
        _output.WriteLine($"Total scenarios analyzed: {scenarioCount}");
        _output.WriteLine("\nProcess frequency:");
        foreach (var (process, count) in processStats.OrderByDescending(kvp => kvp.Value))
        {
            _output.WriteLine($"  {process}: {count} scenarios");
        }
        
        // Show example of multi-process scenario
        var multiProcessScenario = _parser.ParseFeatureFile(featureFiles.First(), FeatureIdMapper.GetFeatureId)
            .FirstOrDefault(s => s.GetRequiredProcesses().Count() > 2);
            
        if (multiProcessScenario != null)
        {
            _output.WriteLine($"\nExample multi-process scenario: {multiProcessScenario.Name}");
            _output.WriteLine($"Processes: {string.Join(", ", multiProcessScenario.GetRequiredProcesses())}");
            _output.WriteLine($"With 3 platforms, this generates {Math.Pow(3, multiProcessScenario.GetRequiredProcesses().Count())} test combinations!");
        }
    }
    
    private List<PlatformCombination> GenerateAllCombinations(List<string> processes, string[] platforms)
    {
        var results = new List<PlatformCombination>();
        GenerateCombinationsRecursive(processes, platforms, 0, new Dictionary<string, string>(), results);
        return results;
    }
    
    private void GenerateCombinationsRecursive(
        List<string> processes, 
        string[] platforms, 
        int index,
        Dictionary<string, string> current, 
        List<PlatformCombination> results)
    {
        if (index == processes.Count)
        {
            results.Add(new PlatformCombination(new Dictionary<string, string>(current)));
            return;
        }
        
        foreach (var platform in platforms)
        {
            current[processes[index]] = platform;
            GenerateCombinationsRecursive(processes, platforms, index + 1, current, results);
        }
    }
    
    private Dictionary<string, string> ExtractStepParameters(string stepText)
    {
        var parameters = new Dictionary<string, string>();
        
        // Simple parameter extraction based on quoted values
        var quotedPattern = @"'([^']+)'";
        var matches = System.Text.RegularExpressions.Regex.Matches(stepText, quotedPattern);
        
        if (matches.Count > 0)
        {
            // First quoted value is often the main parameter
            parameters["value"] = matches[0].Groups[1].Value;
            
            // Look for specific patterns
            if (stepText.Contains("buffer") && matches.Count > 0)
                parameters["buffer_name"] = matches[0].Groups[1].Value;
                
            if (stepText.Contains("size") && matches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out _))
                    {
                        parameters["size"] = match.Groups[1].Value;
                        break;
                    }
                }
            }
            
            if (stepText.Contains("sequence") && matches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out _) && !parameters.ContainsKey("size"))
                    {
                        parameters["sequence"] = match.Groups[1].Value;
                        break;
                    }
                }
            }
        }
        
        return parameters;
    }
}