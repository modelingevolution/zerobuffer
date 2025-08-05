using ModelingEvolution.Harmony.Core;
using Xunit;

namespace ModelingEvolution.Harmony.Tests;

public class StepDefinitionParametersTest
{
    [Fact]
    public void StepDefinition_Should_Have_Initialized_Parameters_Dictionary()
    {
        // Arrange & Act
        var step = new StepDefinition
        {
            Type = StepType.Given,
            Text = "the 'reader' process creates buffer 'test-buffer' with metadata size '1024'",
            Process = "reader",
            ProcessedText = "creates buffer 'test-buffer' with metadata size '1024'"
        };
        
        // Assert - Parameters should be initialized by default
        Assert.NotNull(step.Parameters);
        Assert.Empty(step.Parameters);
    }
    
    [Fact]
    public void StepDefinition_Parameters_Should_Be_Settable()
    {
        // Arrange
        var step = new StepDefinition
        {
            Type = StepType.Given,
            Text = "the 'reader' process creates buffer 'test-buffer' with metadata size '1024'",
            Process = "reader",
            ProcessedText = "creates buffer 'test-buffer' with metadata size '1024'",
            Parameters = new Dictionary<string, object>
            {
                ["bufferName"] = "test-buffer",
                ["metadataSize"] = "1024"
            }
        };
        
        // Assert
        Assert.Equal(2, step.Parameters.Count);
        Assert.Equal("test-buffer", step.Parameters["bufferName"]);
        Assert.Equal("1024", step.Parameters["metadataSize"]);
    }
    
    [Fact]
    public void GherkinParser_Currently_Does_Not_Extract_Parameters()
    {
        // This test demonstrates the current issue:
        // The GherkinParser creates StepDefinition objects but never populates the Parameters dictionary
        
        // Looking at GherkinParser.cs line 147-154:
        // return new StepDefinition
        // {
        //     Type = stepType,
        //     Text = step.Text,
        //     Process = process,
        //     ProcessedText = processedText
        //     // Note: Parameters is not set here!
        // };
        
        // The Parameters property gets its default value (empty dictionary) from the init expression
        // This means all steps have empty Parameters dictionaries
        
        // To fix this, the GherkinParser would need to:
        // 1. Parse the step text to extract parameter values
        // 2. Match them against known step patterns (from SpecFlow bindings or similar)
        // 3. Populate the Parameters dictionary with extracted values
        
        Assert.True(true); // This test just documents the issue
    }
    
    [Fact]
    public void Demonstrate_What_Parameter_Extraction_Should_Do()
    {
        // This test shows what proper parameter extraction would look like
        
        // Given a step text like:
        var stepText = "creates buffer 'test-buffer' with metadata size '1024' and payload size '10240'";
        
        // The parser should extract parameters based on the step pattern
        // For example, if the pattern is:
        // "creates buffer '(.+)' with metadata size '(\d+)' and payload size '(\d+)'"
        
        // Then the extracted parameters should be:
        var expectedParameters = new Dictionary<string, object>
        {
            ["bufferName"] = "test-buffer",
            ["metadataSize"] = "1024", 
            ["payloadSize"] = "10240"
        };
        
        // This would allow serve processes to receive structured data
        // instead of having to parse the text themselves
        
        // Currently, serve processes receive the full text and must parse it themselves
        // With proper parameter extraction, they would receive a Parameters dictionary
        
        Assert.True(true); // This test is just for documentation
    }
}