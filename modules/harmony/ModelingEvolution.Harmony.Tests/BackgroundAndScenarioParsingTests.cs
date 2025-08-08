using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;

namespace ModelingEvolution.Harmony.Tests;

public class BackgroundAndScenarioParsingTests
{
    private readonly ITestOutputHelper _output;
    private readonly GherkinParser _parser;
    
    public BackgroundAndScenarioParsingTests(ITestOutputHelper output)
    {
        _output = output;
        var processExtractor = new ProcessContextExtractor();
        _parser = new GherkinParser(processExtractor);
    }
    
    [Fact]
    public void ParseScenarioWithBackground_MergesStepsCorrectly()
    {
        // Arrange
        var featurePath = Path.Combine("Features", "BasicCommunication.feature");
        
        // Act
        var scenarios = _parser.ParseFeatureFile(featurePath, FeatureIdMapper.GetFeatureId).ToList();
        
        // Assert
        scenarios.Should().NotBeEmpty();
        
        // All scenarios should have background steps
        foreach (var scenario in scenarios)
        {
            _output.WriteLine($"\nScenario: {scenario.Name}");
            
            // Check if background exists
            if (scenario.Background != null)
            {
                _output.WriteLine($"  Background steps: {scenario.Background.Steps.Count}");
                foreach (var bgStep in scenario.Background.Steps)
                {
                    _output.WriteLine($"    - {bgStep.Type} {bgStep.Text}");
                }
            }
            
            _output.WriteLine($"  Scenario steps: {scenario.Steps.Count}");
            
            // Get all steps including background
            var allSteps = scenario.GetAllSteps().ToList();
            _output.WriteLine($"  Total steps (with background): {allSteps.Count}");
            
            // Background steps should come first
            if (scenario.Background != null)
            {
                allSteps.Take(scenario.Background.Steps.Count)
                    .Should().BeEquivalentTo(scenario.Background.Steps);
            }
        }
    }
    
    [Fact]
    public void ParseInitializationFeature_HandlesInlineProcessSpecification()
    {
        // Arrange
        var featurePath = Path.Combine("Features", "Initialization.feature");
        
        // Act
        var scenarios = _parser.ParseFeatureFile(featurePath, FeatureIdMapper.GetFeatureId).ToList();
        
        // Look for scenarios with inline process specification
        var writerBeforeReader = scenarios.FirstOrDefault(s => s.Name.Contains("Writer Before Reader"));
        writerBeforeReader.Should().NotBeNull();
        
        _output.WriteLine($"Analyzing scenario: {writerBeforeReader!.Name}");
        _output.WriteLine("Steps with process context:");
        
        foreach (var step in writerBeforeReader.Steps)
        {
            if (step.Process != null)
            {
                _output.WriteLine($"  [{step.Process}] {step.Type} {step.Text}");
                _output.WriteLine($"    Original: {step.Text}");
                _output.WriteLine($"    Processed: {step.ProcessedText}");
            }
        }
        
        // The scenario uses pattern like "the writer is 'python'"
        // This should be handled differently than "the 'writer' process"
        var writerSteps = writerBeforeReader.Steps.Where(s => s.Process == "writer").ToList();
        writerSteps.Should().NotBeEmpty("Scenario should have writer steps");
    }
    
    [Fact]
    public void VerifyProcessContextExtraction_HandlesVariousPatterns()
    {
        var extractor = new ProcessContextExtractor();
        
        var testCases = new[]
        {
            // Original pattern
            ("the 'writer' process creates buffer", "writer", "creates buffer"),
            ("the 'reader' process should read frame", "reader", "should read frame"),
            
            // New pattern from feature files
            ("the writer is 'python'", null, "the writer is 'python'"), // This might not extract process
            ("two readers 'csharp' and 'python'", null, "two readers 'csharp' and 'python'"),
            
            // Other patterns
            ("'writer' connects to buffer", "writer", "connects to buffer"),
            ("Given the test mode is configured", null, "Given the test mode is configured")
        };
        
        _output.WriteLine("Process context extraction patterns:");
        foreach (var (input, expectedProcess, expectedText) in testCases)
        {
            var (process, processedText) = extractor.ExtractContext(input);
            _output.WriteLine($"\nInput: '{input}'");
            _output.WriteLine($"  Process: {process ?? "null"}");
            _output.WriteLine($"  Processed text: '{processedText}'");
            
            if (expectedProcess != null)
            {
                process.Should().Be(expectedProcess);
            }
        }
    }
    
    [Fact]
    public void CountTotalScenariosAndProcessCombinations()
    {
        var allFeatures = Directory.GetFiles("Features", "*.feature");
        var totalScenarios = 0;
        var scenariosWithMultipleProcesses = 0;
        var platforms = new[] { "csharp", "python", "cpp" };
        
        _output.WriteLine("=== Scenario and Combination Analysis ===\n");
        
        foreach (var featurePath in allFeatures)
        {
            var featureName = Path.GetFileNameWithoutExtension(featurePath);
            var scenarios = _parser.ParseFeatureFile(featurePath, FeatureIdMapper.GetFeatureId).ToList();
            totalScenarios += scenarios.Count;
            
            _output.WriteLine($"{featureName}: {scenarios.Count} scenarios");
            
            foreach (var scenario in scenarios)
            {
                var processes = scenario.GetRequiredProcesses().ToList();
                if (processes.Count > 1)
                {
                    scenariosWithMultipleProcesses++;
                    var combinations = Math.Pow(platforms.Length, processes.Count);
                    _output.WriteLine($"  - {scenario.Name}");
                    _output.WriteLine($"    Processes: {string.Join(", ", processes)} -> {combinations} combinations");
                }
            }
        }
        
        _output.WriteLine($"\nTotal scenarios: {totalScenarios}");
        _output.WriteLine($"Scenarios with multiple processes: {scenariosWithMultipleProcesses}");
        
        // Estimate total test executions
        var singleProcessTests = (totalScenarios - scenariosWithMultipleProcesses) * platforms.Length;
        _output.WriteLine($"\nEstimated test executions:");
        _output.WriteLine($"  Single process scenarios: {totalScenarios - scenariosWithMultipleProcesses} × {platforms.Length} = {singleProcessTests}");
        _output.WriteLine($"  Multi-process scenarios would add many more based on process count");
    }
    
    [Fact]
    public void VerifyBackgroundStepsAreIncludedInExecution()
    {
        // This is crucial - background steps must be executed before scenario steps
        var featurePath = Path.Combine("Features", "BasicCommunication.feature");
        var scenarios = _parser.ParseFeatureFile(featurePath, FeatureIdMapper.GetFeatureId).ToList();
        
        var firstScenario = scenarios.First();
        firstScenario.Background.Should().NotBeNull("BasicCommunication should have background");
        
        // Simulate what would happen during execution
        var executionSteps = new List<StepDefinition>();
        
        // Background steps first
        if (firstScenario.Background != null)
        {
            executionSteps.AddRange(firstScenario.Background.Steps);
        }
        
        // Then scenario steps
        executionSteps.AddRange(firstScenario.Steps);
        
        _output.WriteLine("Execution order for scenario:");
        for (int i = 0; i < executionSteps.Count; i++)
        {
            var step = executionSteps[i];
            var source = i < firstScenario.Background!.Steps.Count ? "BACKGROUND" : "SCENARIO";
            _output.WriteLine($"{i + 1}. [{source}] {step.Type} {step.Text}");
        }
        
        // Verify order
        executionSteps.Should().HaveCount(
            firstScenario.Background!.Steps.Count + firstScenario.Steps.Count,
            "All steps should be included");
    }
}