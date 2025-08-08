using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;

namespace ModelingEvolution.Harmony.Tests;

public class AndButInheritanceTest
{
    private readonly ITestOutputHelper _output;
    
    public AndButInheritanceTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void DemonstrateAndButInheritanceIssue()
    {
        // Create a test feature to show the issue
        var featureContent = @"
Feature: And/But Inheritance Test

Scenario: Process context should be inherited
    Given the 'reader' process creates buffer 'test'
    And sets buffer size to '1024'
    And enables metadata support
    When the 'writer' process connects to buffer 'test'
    And writes 'Hello' to the buffer
    But should not block
    Then the 'reader' process should read 'Hello'
    And should verify the read position advances
";
        
        // Parse the feature
        var processExtractor = new ProcessContextExtractor();
        var parser = new GherkinParser(processExtractor);
        
        // Would need to write to a temp file since parser expects file path
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, featureContent);
        
        try
        {
            var scenarios = parser.ParseFeatureFile(tempFile, FeatureIdMapper.GetFeatureId).ToList();
            var scenario = scenarios.First();
            
            _output.WriteLine("=== Current Parsing (Incorrect) ===\n");
            
            foreach (var step in scenario.Steps)
            {
                _output.WriteLine($"Step: {step.Type} {step.Text}");
                _output.WriteLine($"  Process: {step.Process ?? "null"}");
                _output.WriteLine($"  Processed: {step.ProcessedText}");
                _output.WriteLine("");
            }
            
            // Show what SHOULD happen
            _output.WriteLine("\n=== Expected Behavior ===\n");
            _output.WriteLine("Step: Given the 'reader' process creates buffer 'test'");
            _output.WriteLine("  Process: reader");
            _output.WriteLine("  Processed: creates buffer 'test'");
            _output.WriteLine("");
            
            _output.WriteLine("Step: And sets buffer size to '1024'");
            _output.WriteLine("  Process: reader (INHERITED)");
            _output.WriteLine("  Processed: sets buffer size to '1024'");
            _output.WriteLine("");
            
            _output.WriteLine("Step: When the 'writer' process connects to buffer 'test'");
            _output.WriteLine("  Process: writer");
            _output.WriteLine("  Processed: connects to buffer 'test'");
            _output.WriteLine("");
            
            _output.WriteLine("Step: And writes 'Hello' to the buffer");
            _output.WriteLine("  Process: writer (INHERITED)");
            _output.WriteLine("  Processed: writes 'Hello' to the buffer");
            
            // Check actual behavior
            var readerSteps = scenario.Steps.Where(s => s.Process == "reader").Count();
            var writerSteps = scenario.Steps.Where(s => s.Process == "writer").Count();
            var noProcessSteps = scenario.Steps.Where(s => s.Process == null).Count();
            
            _output.WriteLine($"\n=== Analysis ===");
            _output.WriteLine($"Reader steps: {readerSteps} (should be 5)");
            _output.WriteLine($"Writer steps: {writerSteps} (should be 3)");
            _output.WriteLine($"No process steps: {noProcessSteps} (should be 0)");
            
            // Now this passes - And/But steps inherit process context
            Assert.Equal(0, noProcessSteps);
            Assert.Equal(5, readerSteps); // 1 Given + 2 And + 1 Then + 1 And
            Assert.Equal(3, writerSteps); // 1 When + 1 And + 1 But
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}