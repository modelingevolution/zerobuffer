using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelingEvolution.Harmony.Tests;

/// <summary>
/// Tests the broadcasting feature when Process is null/empty on steps
/// </summary>
public class BroadcastStepTests
{
    private readonly ITestOutputHelper _output;
    
    public BroadcastStepTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public async Task TestGetAllProcesses()
    {
        var platforms = new PlatformCombination(new Dictionary<string, string>
        {
            ["reader"] = "csharp",
            ["writer"] = "python"
        });
        
        var processes = platforms.GetAllProcesses().ToList();
        _output.WriteLine($"Process count: {processes.Count}");
        foreach (var p in processes)
        {
            _output.WriteLine($"Process: {p}");
        }
        
        Assert.Equal(2, processes.Count);
        Assert.Contains("reader", processes);
        Assert.Contains("writer", processes);
    }
    
    [Fact]
    public async Task TestBroadcastExecution()
    {
        var processManager = new DebugProcessManager(_output);
        var stepExecutor = new StepExecutor(processManager, NullLoggerFactory.Instance);
        
        var platforms = new PlatformCombination(new Dictionary<string, string>
        {
            ["reader"] = "csharp",
            ["writer"] = "python"
        });
        
        var step = new StepDefinition
        {
            Type = StepType.Then,
            Text = "verify system state",
            Process = null,  // This should trigger broadcast
            ProcessedText = "verify system state"
        };
        
        var result = await stepExecutor.ExecuteStepAsync(step, platforms);
        
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"Error: {result.Error ?? "none"}");
        _output.WriteLine($"Log count: {result.Logs.Count}");
        foreach (var log in result.Logs)
        {
            _output.WriteLine($"Log: [{log.Process}] {log.Message}");
        }
        
        Assert.True(result.Success);
    }
    
    private class DebugProcessManager : IProcessManager
    {
        private readonly ITestOutputHelper _output;
        
        public DebugProcessManager(ITestOutputHelper output)
        {
            _output = output;
        }
        
        public IProcessConnection GetConnection(string processName)
        {
            _output.WriteLine($"GetConnection called for: {processName}");
            return new DebugConnection(processName, _output);
        }
        
        public Task StartProcessAsync(string processName, string platform, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        
        public Task StartProcessAsync(string processName, string platform, int hostPid, int featureId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        
        public Task StopProcessAsync(string processName)
            => Task.CompletedTask;
        
        public Task StopAllProcessesAsync()
            => Task.CompletedTask;
        
        public Task<bool> IsProcessRunningAsync(string processName)
            => Task.FromResult(true);
        
        public void Dispose() { }
    }
    
    private class DebugConnection : IProcessConnection
    {
        private readonly string _processName;
        private readonly ITestOutputHelper _output;
        
        public DebugConnection(string processName, ITestOutputHelper output)
        {
            _processName = processName;
            _output = output;
        }
        
        public string ProcessName => _processName;
        public string Platform => "test";
        public bool IsConnected => true;
        
        public Task<T> InvokeAsync<T>(string method, object parameters, CancellationToken cancellationToken = default)
        {
            _output.WriteLine($"InvokeAsync called for {_processName} with method: {method}");
            _output.WriteLine($"Type T: {typeof(T).Name}");
            
            // Create a simple response
            var responseType = typeof(T);
            var response = Activator.CreateInstance<T>();
            
            // Set Success = true
            var successProp = responseType.GetProperty("Success");
            if (successProp != null)
            {
                successProp.SetValue(response, true);
                _output.WriteLine("Set Success = true");
            }
            
            // Set empty Data
            var dataProp = responseType.GetProperty("Data");
            if (dataProp != null)
            {
                dataProp.SetValue(response, new Dictionary<string, object>());
                _output.WriteLine("Set Data = empty dictionary");
            }
            
            // Set empty Logs
            var logsProp = responseType.GetProperty("Logs");
            if (logsProp != null)
            {
                var logsType = logsProp.PropertyType;
                if (logsType.IsGenericType)
                {
                    var logList = Activator.CreateInstance(logsType);
                    logsProp.SetValue(response, logList);
                    _output.WriteLine("Set Logs = empty list");
                }
            }
            
            return Task.FromResult(response);
        }
    }
}