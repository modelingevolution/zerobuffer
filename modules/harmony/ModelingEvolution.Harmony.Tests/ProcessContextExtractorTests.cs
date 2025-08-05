using FluentAssertions;
using ModelingEvolution.Harmony.Gherkin;
using Xunit;

namespace ModelingEvolution.Harmony.Tests;

public class ProcessContextExtractorTests
{
    private readonly ProcessContextExtractor _extractor = new();
    
    [Theory]
    [InlineData("the 'writer' process creates buffer", "writer", "creates buffer")]
    [InlineData("the 'reader' process should read frame", "reader", "should read frame")]
    [InlineData("'writer' process writes data", "writer", "writes data")]
    [InlineData("'server' starts listening", "server", "starts listening")]
    [InlineData("the 'client' connects", "client", "connects")]
    public void ExtractContext_WithProcessReference_ExtractsCorrectly(
        string input, string expectedProcess, string expectedText)
    {
        // Act
        var (process, text) = _extractor.ExtractContext(input);
        
        // Assert
        process.Should().Be(expectedProcess);
        text.Should().Be(expectedText);
    }
    
    [Theory]
    [InlineData("test environment is initialized")]
    [InlineData("all processes are ready")]
    [InlineData("Given some general setup")]
    public void ExtractContext_WithoutProcessReference_ReturnsNullProcess(string input)
    {
        // Act
        var (process, text) = _extractor.ExtractContext(input);
        
        // Assert
        process.Should().BeNull();
        text.Should().Be(input);
    }
    
    [Fact]
    public void ExtractContext_WithComplexStep_ExtractsCorrectly()
    {
        // Arrange
        var input = "the 'writer' process writes frame with size '1024' and sequence '42'";
        
        // Act
        var (process, text) = _extractor.ExtractContext(input);
        
        // Assert
        process.Should().Be("writer");
        text.Should().Be("writes frame with size '1024' and sequence '42'");
    }
}