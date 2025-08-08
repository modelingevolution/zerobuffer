using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;

namespace ModelingEvolution.Harmony.Tests;

public class FeatureParsingTests
{
    private readonly ITestOutputHelper _output;
    private readonly GherkinParser _parser;
    
    public FeatureParsingTests(ITestOutputHelper output)
    {
        _output = output;
        var processExtractor = new ProcessContextExtractor();
        _parser = new GherkinParser(processExtractor);
    }
    
    [Fact]
    public void ParseBasicCommunicationFeature_ExtractsScenarios()
    {
        // Arrange
        var featurePath = Path.Combine("Features", "BasicCommunication.feature");
        
        // Act
        var scenarios = _parser.ParseFeatureFile(featurePath, FeatureIdMapper.GetFeatureId).ToList();
        
        // Assert
        scenarios.Should().NotBeEmpty();
        
        // Log what we found
        _output.WriteLine($"Found {scenarios.Count} scenarios in BasicCommunication.feature:");
        foreach (var scenario in scenarios)
        {
            var processes = scenario.GetRequiredProcesses().ToList();
            _output.WriteLine($"\n{scenario.Name}:");
            _output.WriteLine($"  Required processes: {string.Join(", ", processes)}");
            _output.WriteLine($"  Total steps: {scenario.Steps.Count}");
            
            // Show first few steps
            foreach (var step in scenario.Steps.Take(3))
            {
                _output.WriteLine($"    - [{step.Process ?? "none"}] {step.Type} {step.Text}");
            }
            if (scenario.Steps.Count > 3)
            {
                _output.WriteLine($"    ... and {scenario.Steps.Count - 3} more steps");
            }
        }
        
        // Verify specific scenarios exist
        scenarios.Should().Contain(s => s.Name.Contains("Simple Write-Read Cycle"));
        scenarios.Should().Contain(s => s.Name.Contains("Multiple Frames Sequential"));
        scenarios.Should().Contain(s => s.Name.Contains("Buffer Full Handling"));
    }
    
    [Fact]
    public void ParseScenario_ExtractsProcessesCorrectly()
    {
        // Arrange
        var featurePath = Path.Combine("Features", "BasicCommunication.feature");
        var scenarios = _parser.ParseFeatureFile(featurePath, FeatureIdMapper.GetFeatureId).ToList();
        var simpleWriteRead = scenarios.First(s => s.Name.Contains("Simple Write-Read Cycle"));
        
        // Act
        var processes = simpleWriteRead.GetRequiredProcesses().ToList();
        
        // Assert
        processes.Should().HaveCount(2);
        processes.Should().Contain("reader");
        processes.Should().Contain("writer");
        
        _output.WriteLine("Process usage in Simple Write-Read Cycle:");
        foreach (var process in processes)
        {
            var stepCount = simpleWriteRead.Steps.Count(s => s.Process == process);
            _output.WriteLine($"  {process}: {stepCount} steps");
        }
    }
    
    [Fact]
    public void VerifyAllFeatureFiles_AreValid()
    {
        // Arrange
        var featuresDir = "Features";
        var featureFiles = Directory.GetFiles(featuresDir, "*.feature");
        
        _output.WriteLine($"Found {featureFiles.Length} feature files:");
        
        // Act & Assert
        foreach (var featureFile in featureFiles)
        {
            var fileName = Path.GetFileName(featureFile);
            _output.WriteLine($"\n{fileName}:");
            
            var scenarios = _parser.ParseFeatureFile(featureFile, FeatureIdMapper.GetFeatureId).ToList();
            scenarios.Should().NotBeEmpty($"{fileName} should contain scenarios");
            
            var totalProcesses = new HashSet<string>();
            foreach (var scenario in scenarios)
            {
                var processes = scenario.GetRequiredProcesses();
                foreach (var process in processes)
                {
                    totalProcesses.Add(process);
                }
            }
            
            _output.WriteLine($"  Scenarios: {scenarios.Count}");
            _output.WriteLine($"  Unique processes: {string.Join(", ", totalProcesses.OrderBy(p => p))}");
        }
    }
}