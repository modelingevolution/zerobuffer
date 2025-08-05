using Xunit;

namespace ModelingEvolution.Harmony.Tests;

public class BuildVerificationTest
{
    [Fact]
    public void Solution_Builds_Successfully()
    {
        // This test verifies that the solution builds successfully
        // If this test runs, it means the build was successful
        Assert.True(true);
    }
    
    [Fact]
    public void MultiprocessRunner_Assembly_Exists()
    {
        var assembly = typeof(Core.ScenarioExecution).Assembly;
        Assert.NotNull(assembly);
        Assert.Equal("ModelingEvolution.Harmony", assembly.GetName().Name);
    }
}