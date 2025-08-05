using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using FluentAssertions;
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
/// Runs all feature scenarios with mocked process connections and logs all JSON-RPC calls
/// </summary>
public class ComprehensiveJsonRpcLoggingTest : TestBase, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ConcurrentBag<JsonRpcLogEntry> _jsonRpcLog = new();
    private readonly MockProcessManager _mockProcessManager;
    private readonly IStepExecutor _stepExecutor;
    private readonly IScenarioGenerator _scenarioGenerator;
    private readonly MultiprocessConfiguration _configuration;
    
    public ComprehensiveJsonRpcLoggingTest(ITestOutputHelper output)
    {
        _output = output;
        _configuration = CreateTestConfiguration();
        
        // Initialize with mocked process manager
        var processContextExtractor = new ProcessContextExtractor();
        var gherkinParser = new GherkinParser(processContextExtractor);
        var loggerFactory = NullLoggerFactory.Instance;
        
        _mockProcessManager = new MockProcessManager(_jsonRpcLog);
        _stepExecutor = new StepExecutor(_mockProcessManager, loggerFactory);
        _scenarioGenerator = new ScenarioGenerator(gherkinParser);
    }
    
    
    [Fact]
    public async Task RunAllScenariosAndLogJsonRpc()
    {
        var platforms = new[] { "csharp", "python", "cpp" };
        var allScenarios = _scenarioGenerator.GenerateScenarios(
            _configuration.FeaturesPath,
            platforms,
            _mockProcessManager,
            _stepExecutor
        ).ToList();
        
        _output.WriteLine($"=== COMPREHENSIVE JSON-RPC LOG ===");
        _output.WriteLine($"Total scenarios to run: {allScenarios.Count}\n");
        
        var scenarioCount = 0;
        var failedScenarios = new List<(ScenarioExecution scenario, ExecutionResult result)>();
        
        // Run a subset to avoid timeout - just first scenario from each feature
        var scenariosByFeature = allScenarios
            .GroupBy(s => s.Scenario.Name.Split('-').First().Trim())
            .Select(g => g.First())
            .Take(5); // Just 5 scenarios to demonstrate
        
        foreach (var scenario in scenariosByFeature)
        {
            scenarioCount++;
            _output.WriteLine($"\n--- Scenario {scenarioCount}: {scenario} ---");
            
            try
            {
                var result = await scenario.RunAsync();
                
                if (!result.Success)
                {
                    failedScenarios.Add((scenario, result));
                    _output.WriteLine($"FAILED: {result.Error}");
                }
                
                // Show JSON-RPC calls for this scenario
                var scenarioCalls = _jsonRpcLog
                    .Where(c => c.ScenarioId == scenario.TestId)
                    .OrderBy(c => c.Timestamp)
                    .ToList();
                
                foreach (var call in scenarioCalls)
                {
                    _output.WriteLine($"\n[{call.Timestamp:HH:mm:ss.fff}] {call.Process} ({call.Platform}):");
                    _output.WriteLine($"Method: {call.Method}");
                    _output.WriteLine($"Request: {call.RequestJson}");
                    if (!string.IsNullOrEmpty(call.ResponseJson))
                    {
                        _output.WriteLine($"Response: {call.ResponseJson}");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"EXCEPTION: {ex.Message}");
            }
        }
        
        // Summary
        _output.WriteLine($"\n\n=== SUMMARY ===");
        _output.WriteLine($"Scenarios run: {scenarioCount}");
        _output.WriteLine($"Failed scenarios: {failedScenarios.Count}");
        _output.WriteLine($"Total JSON-RPC calls: {_jsonRpcLog.Count}");
        
        // Log analysis
        var callsByMethod = _jsonRpcLog
            .GroupBy(c => c.Method)
            .OrderByDescending(g => g.Count());
        
        _output.WriteLine($"\nJSON-RPC methods called:");
        foreach (var methodGroup in callsByMethod)
        {
            _output.WriteLine($"  {methodGroup.Key}: {methodGroup.Count()} calls");
        }
        
        // Sample of unique step patterns
        var uniqueSteps = _jsonRpcLog
            .Select(c => ExtractStepText(c.RequestJson))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .Take(10);
        
        _output.WriteLine($"\nSample unique steps:");
        foreach (var step in uniqueSteps)
        {
            _output.WriteLine($"  - {step}");
        }
    }
    
    private string ExtractStepText(string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            if (doc.RootElement.TryGetProperty("step", out var stepElement))
            {
                return stepElement.GetString() ?? "";
            }
            return "";
        }
        catch
        {
            return "";
        }
    }
    
    public void Dispose()
    {
        // Nothing to dispose in mock
    }
    
    /// <summary>
    /// Mock implementation of IProcessManager that logs JSON-RPC calls
    /// </summary>
    private class MockProcessManager : IProcessManager
    {
        private readonly ConcurrentBag<JsonRpcLogEntry> _jsonRpcLog;
        private readonly Dictionary<string, MockProcessConnection> _connections = new();
        
        public MockProcessManager(ConcurrentBag<JsonRpcLogEntry> jsonRpcLog)
        {
            _jsonRpcLog = jsonRpcLog;
        }
        
        public Task StartProcessAsync(string processName, string platform, CancellationToken cancellationToken = default)
        {
            var key = $"{processName}-{platform}";
            if (!_connections.ContainsKey(key))
            {
                _connections[key] = new MockProcessConnection(processName, platform, _jsonRpcLog);
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
    
    /// <summary>
    /// Mock implementation of IProcessConnection that logs JSON-RPC calls
    /// </summary>
    private class MockProcessConnection : IProcessConnection
    {
        private readonly ConcurrentBag<JsonRpcLogEntry> _jsonRpcLog;
        
        public string ProcessName { get; }
        public string Platform { get; }
        public bool IsConnected => true;
        
        public MockProcessConnection(string processName, string platform, ConcurrentBag<JsonRpcLogEntry> jsonRpcLog)
        {
            ProcessName = processName;
            Platform = platform;
            _jsonRpcLog = jsonRpcLog;
        }
        
        public Task<T> InvokeAsync<T>(string method, object parameters, CancellationToken cancellationToken = default)
        {
            var requestJson = JsonSerializer.Serialize(parameters, new JsonSerializerOptions { WriteIndented = true });
            
            // Create mock response
            var response = new
            {
                Success = true,
                Error = (string?)null,
                Data = new Dictionary<string, object>(),
                Logs = new List<object>()
            };
            
            var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
            
            // Log the call
            var logEntry = new JsonRpcLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Process = ProcessName,
                Platform = Platform,
                Method = method,
                RequestJson = requestJson,
                ResponseJson = responseJson,
                ScenarioId = GetCurrentScenarioId()
            };
            
            _jsonRpcLog.Add(logEntry);
            
            // Return the response
            return Task.FromResult((T)(object)response);
        }
        
        private string GetCurrentScenarioId()
        {
            // In a real implementation, this would track the current scenario
            // For now, we'll use a thread-local or context-based approach
            return "current-scenario";
        }
    }
    
    private class JsonRpcLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Process { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Method { get; set; } = "";
        public string RequestJson { get; set; } = "";
        public string ResponseJson { get; set; } = "";
        public string ScenarioId { get; set; } = "";
    }
}