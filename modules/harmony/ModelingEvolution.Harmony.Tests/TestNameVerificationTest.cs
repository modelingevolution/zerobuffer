using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelingEvolution.Harmony.Tests;

public class TestNameVerificationTest : TestBase
{
    private readonly ITestOutputHelper _output;
    
    public TestNameVerificationTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void ShowGeneratedTestNames()
    {
        // Arrange
        var configuration = CreateTestConfiguration();
        var processContextExtractor = new ProcessContextExtractor();
        var gherkinParser = new GherkinParser(processContextExtractor);
        var loggerFactory = NullLoggerFactory.Instance;
        var mockProcessManager = new MockProcessManager();
        var stepExecutor = new StepExecutor(mockProcessManager, loggerFactory);
        var scenarioGenerator = new ScenarioGenerator(gherkinParser);
        
        var platforms = configuration.Platforms.Keys.ToArray();
        
        // Act - Generate first 20 scenarios
        var scenarios = scenarioGenerator.GenerateScenarios(
            configuration.FeaturesPath,
            platforms).Take(20).ToList();
        
        // Output test names
        _output.WriteLine("=== GENERATED TEST NAMES (First 20) ===\n");
        
        foreach (var scenario in scenarios)
        {
            _output.WriteLine($"Test name: {scenario}");
            _output.WriteLine($"Test ID: {scenario.TestId}");
            _output.WriteLine($"Scenario: {scenario.Scenario.Name}");
            _output.WriteLine($"Platforms: {scenario.Platforms}");
            _output.WriteLine("");
        }
    }
    
    private class MockProcessManager : IProcessManager
    {
        public Task StartProcessAsync(string processName, string platform, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
            
        public Task StartProcessAsync(string processName, string platform, int hostPid, int featureId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
            
        public Task StopProcessAsync(string processName)
            => Task.CompletedTask;
            
        public Task StopAllProcessesAsync()
            => Task.CompletedTask;
            
        public Task<bool> IsProcessRunningAsync(string processName)
            => Task.FromResult(false);
            
        public IProcessConnection GetConnection(string processName)
            => throw new NotImplementedException();
            
        public void Dispose() { }
    }
}