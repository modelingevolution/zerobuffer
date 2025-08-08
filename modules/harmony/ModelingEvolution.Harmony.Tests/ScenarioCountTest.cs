using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelingEvolution.Harmony.Tests;

public class ScenarioCountTest : TestBase
{
    private readonly ITestOutputHelper _output;
    
    public ScenarioCountTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void CountTotalScenariosGeneratedFromFeatureFiles()
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
        _output.WriteLine($"Platforms: {string.Join(", ", platforms)}");
        _output.WriteLine($"Platform count: {platforms.Length}");
        
        // Act - Generate all scenarios
        var allScenarios = scenarioGenerator.GenerateScenarios(
            configuration.FeaturesPath,
            platforms).ToList();
        
        // Count unique base scenarios
        var uniqueScenarios = allScenarios
            .Select(s => s.Scenario.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToList();
        
        // Count scenarios by feature file
        var scenariosByFeature = new Dictionary<string, List<string>>();
        foreach (var featureFile in Directory.GetFiles("Features", "*.feature"))
        {
            var featureName = Path.GetFileNameWithoutExtension(featureFile);
            var scenarios = gherkinParser.ParseFeatureFile(featureFile, FeatureIdMapper.GetFeatureId).ToList();
            scenariosByFeature[featureName] = scenarios.Select(s => s.Name).ToList();
        }
        
        // Output results
        _output.WriteLine("\n=== SCENARIO GENERATION SUMMARY ===");
        _output.WriteLine($"\nTotal test executions generated: {allScenarios.Count}");
        _output.WriteLine($"Unique scenarios: {uniqueScenarios.Count}");
        _output.WriteLine($"Platforms: {platforms.Length} ({string.Join(", ", platforms)})");
        
        _output.WriteLine("\n=== SCENARIOS BY FEATURE FILE ===");
        var totalBaseScenarios = 0;
        foreach (var (feature, scenarios) in scenariosByFeature.OrderBy(kvp => kvp.Key))
        {
            _output.WriteLine($"\n{feature}.feature: {scenarios.Count} scenarios");
            foreach (var scenario in scenarios)
            {
                var processCount = gherkinParser.ParseFeatureFile($"Features/{feature}.feature", FeatureIdMapper.GetFeatureId)
                    .First(s => s.Name == scenario)
                    .GetRequiredProcesses()
                    .Count();
                    
                var combinations = processCount > 0 ? (int)Math.Pow(platforms.Length, processCount) : 0;
                _output.WriteLine($"  - {scenario}");
                _output.WriteLine($"    Processes: {processCount}, Combinations: {combinations}");
                totalBaseScenarios++;
            }
        }
        
        _output.WriteLine($"\n=== TOTALS ===");
        _output.WriteLine($"Base scenarios (from feature files): {totalBaseScenarios}");
        _output.WriteLine($"Test executions (with platform combinations): {allScenarios.Count}");
        _output.WriteLine($"Multiplication factor: {(double)allScenarios.Count / totalBaseScenarios:F2}x");
        
        // The MultiprocessTests only takes the first 10
        _output.WriteLine($"\nNote: MultiprocessTests currently limits to first 10 executions using .Take(10)");
        
        // Assert to show the values
        Assert.True(allScenarios.Count == 441, $"Expected 441 total test executions but got {allScenarios.Count}");
        Assert.True(totalBaseScenarios == 57, $"Expected 57 base scenarios but got {totalBaseScenarios}");
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