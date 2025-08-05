using FluentAssertions;
using Moq;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.ProcessManagement;
using Xunit;

namespace ModelingEvolution.Harmony.Tests;

public class ScenarioGeneratorTests
{
    private readonly Mock<IGherkinParser> _parserMock;
    private readonly Mock<IProcessManager> _processManagerMock;
    private readonly Mock<IStepExecutor> _stepExecutorMock;
    private readonly ScenarioGenerator _generator;
    
    public ScenarioGeneratorTests()
    {
        _parserMock = new Mock<IGherkinParser>();
        _processManagerMock = new Mock<IProcessManager>();
        _stepExecutorMock = new Mock<IStepExecutor>();
        _generator = new ScenarioGenerator(_parserMock.Object);
    }
    
    [Fact]
    public void GenerateScenarios_WithTwoProcessesAndThreePlatforms_GeneratesNineCombinations()
    {
        // Arrange
        var scenario = new ScenarioDefinition
        {
            Name = "Test Scenario",
            Steps = new List<StepDefinition>
            {
                new() { Process = "writer", Text = "writer writes" },
                new() { Process = "reader", Text = "reader reads" }
            }
        };
        
        _parserMock
            .Setup(p => p.ParseFeatureFiles(It.IsAny<string>()))
            .Returns(new[] { scenario });
        
        var platforms = new[] { "csharp", "python", "cpp" };
        
        // Act
        var scenarios = _generator.GenerateScenarios(
            "Features",
            platforms
        ).ToList();
        
        // Assert
        scenarios.Should().HaveCount(9); // 3^2 combinations
        
        var combinations = scenarios.Select(s => s.Platforms.ToString()).ToList();
        combinations.Should().Contain("csharp/csharp");
        combinations.Should().Contain("csharp/python");
        combinations.Should().Contain("csharp/cpp");
        combinations.Should().Contain("python/csharp");
        combinations.Should().Contain("python/python");
        combinations.Should().Contain("python/cpp");
        combinations.Should().Contain("cpp/csharp");
        combinations.Should().Contain("cpp/python");
        combinations.Should().Contain("cpp/cpp");
    }
    
    [Fact]
    public void GenerateScenarios_WithSingleProcess_GeneratesThreeCombinations()
    {
        // Arrange
        var scenario = new ScenarioDefinition
        {
            Name = "Single Process Test",
            Steps = new List<StepDefinition>
            {
                new() { Process = "writer", Text = "writer does something" }
            }
        };
        
        _parserMock
            .Setup(p => p.ParseFeatureFiles(It.IsAny<string>()))
            .Returns(new[] { scenario });
        
        var platforms = new[] { "csharp", "python", "cpp" };
        
        // Act
        var scenarios = _generator.GenerateScenarios(
            "Features",
            platforms
        ).ToList();
        
        // Assert
        scenarios.Should().HaveCount(3); // One for each platform
    }
    
    [Fact]
    public void GenerateScenarios_ExtractsProcessesFromBackgroundSteps()
    {
        // Arrange
        var scenario = new ScenarioDefinition
        {
            Name = "Test with Background",
            Background = new BackgroundDefinition
            {
                Steps = new List<StepDefinition>
                {
                    new() { Process = "server", Text = "server starts" }
                }
            },
            Steps = new List<StepDefinition>
            {
                new() { Process = "client", Text = "client connects" }
            }
        };
        
        _parserMock
            .Setup(p => p.ParseFeatureFiles(It.IsAny<string>()))
            .Returns(new[] { scenario });
        
        var platforms = new[] { "csharp", "python" };
        
        // Act
        var scenarios = _generator.GenerateScenarios(
            "Features",
            platforms
        ).ToList();
        
        // Assert
        scenarios.Should().HaveCount(4); // 2^2 combinations
    }
    
    [Fact]
    public void GenerateScenarios_SkipsScenarioWithNoProcesses()
    {
        // Arrange
        var scenario = new ScenarioDefinition
        {
            Name = "No Process Scenario",
            Steps = new List<StepDefinition>
            {
                new() { Process = null, Text = "general setup" }
            }
        };
        
        _parserMock
            .Setup(p => p.ParseFeatureFiles(It.IsAny<string>()))
            .Returns(new[] { scenario });
        
        // Act
        var scenarios = _generator.GenerateScenarios(
            "Features",
            new[] { "csharp" }
        ).ToList();
        
        // Assert
        scenarios.Should().BeEmpty();
    }
}