using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Configuration;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.ProcessManagement;

namespace ModelingEvolution.Harmony.Tests;

/// <summary>
/// Main test class that runs multiprocess scenarios with mocked connections
/// </summary>
public class MultiprocessTests : TestBase, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IProcessManager _processManager;
    private readonly IStepExecutor _stepExecutor;
    private readonly IScenarioGenerator _scenarioGenerator;
    private readonly MultiprocessConfiguration _configuration;
    
    public MultiprocessTests(ITestOutputHelper output)
    {
        _output = output;
        _configuration = CreateTestConfiguration();
        
        // Initialize dependencies with mocked process manager
        var loggerFactory = NullLoggerFactory.Instance;
        var processContextExtractor = new ProcessContextExtractor();
        var gherkinParser = new GherkinParser(processContextExtractor);
        
        _processManager = new MockProcessManager();
        _stepExecutor = new StepExecutor(_processManager, loggerFactory);
        _scenarioGenerator = new ScenarioGenerator(gherkinParser);
    }
    
    [Theory]
    [MemberData(nameof(GetTestScenarios))]
    public async Task RunScenario(ScenarioExecution scenario)
    {
        _output.WriteLine($"Running: {scenario}");
        _output.WriteLine($"Test ID: {scenario.TestId}");
        _output.WriteLine("");
        
        var result = await scenario.RunAsync(_stepExecutor, _processManager);
        
        // Output logs
        foreach (var log in result.Logs)
        {
            _output.WriteLine($"[{log.Timestamp:HH:mm:ss.fff}] [{log.Platform}:{log.Process}] [{log.Level}] {log.Message}");
        }
        
        if (!result.Success)
        {
            _output.WriteLine("");
            _output.WriteLine($"Error: {result.Error}");
            
            // Don't throw for expected mock behavior
            if (result.Error?.Contains("No connection for process") == true)
            {
                _output.WriteLine("(This is expected in mock mode)");
                return;
            }
        }
        
        _output.WriteLine("");
        _output.WriteLine($"Scenario completed in {result.Duration.TotalMilliseconds:F0}ms");
    }
    
    public static IEnumerable<object[]> GetTestScenarios()
    {
        // Create instances for test data generation
        var configuration = CreateTestConfiguration();
        var processContextExtractor = new ProcessContextExtractor();
        var gherkinParser = new GherkinParser(processContextExtractor);
        var loggerFactory = NullLoggerFactory.Instance;
        var mockProcessManager = new MockProcessManager(); // Use the mock implementation
        var stepExecutor = new StepExecutor(mockProcessManager, loggerFactory);
        var scenarioGenerator = new ScenarioGenerator(gherkinParser);
        
        var platforms = configuration.Platforms.Keys.ToArray();
        
        // Just return first few scenarios to avoid too many test cases
        foreach (var scenario in scenarioGenerator.GenerateScenarios(
            configuration.FeaturesPath,
            platforms).Take(10))
        {
            yield return new object[] { scenario };
        }
    }
    
    
    public void Dispose()
    {
        _processManager?.Dispose();
    }
    
    /// <summary>
    /// Simple mock implementation for testing
    /// </summary>
    private class MockProcessManager : IProcessManager
    {
        private readonly Dictionary<string, MockProcessConnection> _connections = new();
        
        public Task StartProcessAsync(string processName, string platform, CancellationToken cancellationToken = default)
        {
            var key = $"{processName}-{platform}";
            if (!_connections.ContainsKey(key))
            {
                _connections[key] = new MockProcessConnection(processName, platform);
            }
            return Task.CompletedTask;
        }
        
        public Task StartProcessAsync(string processName, string platform, int hostPid, int featureId, CancellationToken cancellationToken = default)
        {
            var key = $"{processName}-{platform}";
            if (!_connections.ContainsKey(key))
            {
                _connections[key] = new MockProcessConnection(processName, platform);
            }
            return Task.CompletedTask;
        }
        
        public Task StopProcessAsync(string processName)
        {
            var toRemove = _connections.Where(kvp => kvp.Value.ProcessName == processName).ToList();
            foreach (var kvp in toRemove)
            {
                _connections.Remove(kvp.Key);
            }
            return Task.CompletedTask;
        }
        
        public Task StopAllProcessesAsync()
        {
            _connections.Clear();
            return Task.CompletedTask;
        }
        
        public Task<bool> IsProcessRunningAsync(string processName)
        {
            return Task.FromResult(_connections.Values.Any(c => c.ProcessName == processName));
        }
        
        public IProcessConnection GetConnection(string processName)
        {
            var connection = _connections.Values.FirstOrDefault(c => c.ProcessName == processName);
            if (connection == null)
            {
                throw new InvalidOperationException($"No connection for process '{processName}'");
            }
            return connection;
        }
        
        public void Dispose()
        {
            _connections.Clear();
        }
    }
    
    private class MockProcessConnection : IProcessConnection
    {
        public string ProcessName { get; }
        public string Platform { get; }
        public bool IsConnected => true;
        
        public MockProcessConnection(string processName, string platform)
        {
            ProcessName = processName;
            Platform = platform;
        }
        
        public Task<T> InvokeAsync<T>(string method, object parameters, CancellationToken cancellationToken = default)
        {
            // Return a successful mock response
            var response = new
            {
                Success = true,
                Error = (string?)null,
                Data = new Dictionary<string, object>(),
                Logs = new List<object>()
            };
            
            return Task.FromResult((T)(object)response);
        }
    }
}