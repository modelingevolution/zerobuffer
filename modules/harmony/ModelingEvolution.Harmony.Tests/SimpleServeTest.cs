using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;

namespace ModelingEvolution.Harmony.Tests;

/// <summary>
/// Simple test to verify basic JSON-RPC communication with the serve process
/// </summary>
public class SimpleServeTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Process? _serveProcess;
    private JsonRpc? _jsonRpc;
    
    public SimpleServeTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    // Response types - strongly typed instead of dynamic
    private class InitializeResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
    
    private class StepResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public Dictionary<string, object>? Data { get; set; }
        public List<LogEntry>? Logs { get; set; }
    }
    
    private class LogEntry
    {
        public string Level { get; set; } = "INFO";
        public string Message { get; set; } = "";
    }
    
    [Fact]
    public async Task TestBasicCommunication()
    {
        // Start the serve process
        var servePath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), 
            "../../../../../../csharp/ZeroBuffer.Serve/bin/Debug/net9.0/ZeroBuffer.Serve"));
        
        _output.WriteLine($"Starting serve: {servePath}");
        
        _serveProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = servePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        // Capture stderr for debugging
        _serveProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _output.WriteLine($"[SERVE] {e.Data}");
        };
        
        _serveProcess.Start();
        _serveProcess.BeginErrorReadLine();
        
        // Give it a moment to start
        await Task.Delay(500);
        
        // Set up JSON-RPC client
        var formatter = new SystemTextJsonFormatter();
        var handler = new LengthHeaderMessageHandler(
            _serveProcess.StandardInput.BaseStream,
            _serveProcess.StandardOutput.BaseStream,
            formatter);
        
        _jsonRpc = new JsonRpc(handler);
        _jsonRpc.StartListening();
        
        // Test initialize
        _output.WriteLine("Sending initialize request...");
        
        var initRequest = new
        {
            Role = "reader",
            Platform = "csharp",
            Scenario = "Test Scenario",
            TestRunId = "test-123"
        };
        
        // Use strongly typed response
        var initResponse = await _jsonRpc.InvokeAsync<InitializeResponse>(
            "initialize", 
            initRequest);
        
        _output.WriteLine($"Initialize response: Success={initResponse.Success}");
        Assert.NotNull(initResponse);
        Assert.True(initResponse.Success);
        
        // Test a simple step
        _output.WriteLine("Sending step request...");
        
        var stepRequest = new
        {
            StepType = "given",
            Step = "creates buffer 'test' with size '1024'"
        };
        
        var stepResponse = await _jsonRpc.InvokeAsync<StepResponse>(
            "executeStep",
            stepRequest);
        
        _output.WriteLine($"Step response: Success={stepResponse.Success}");
        Assert.NotNull(stepResponse);
        Assert.True(stepResponse.Success);
        
        // Check logs
        if (stepResponse.Logs != null && stepResponse.Logs.Count > 0)
        {
            _output.WriteLine("Logs from step:");
            foreach (var log in stepResponse.Logs)
            {
                _output.WriteLine($"  [{log.Level}] {log.Message}");
            }
        }
        
        // Cleanup - returns void/bool
        await _jsonRpc.InvokeAsync<bool>("cleanup");
        
        // Shutdown gracefully - returns void/bool
        _output.WriteLine("Sending shutdown...");
        await _jsonRpc.NotifyAsync("shutdown");
        await Task.Delay(100);
    }
    
    public void Dispose()
    {
        try
        {
            _jsonRpc?.Dispose();
        }
        catch { }
        
        if (_serveProcess != null && !_serveProcess.HasExited)
        {
            try
            {
                // Give it a chance to exit gracefully
                _serveProcess.WaitForExit(1000);
                
                if (!_serveProcess.HasExited)
                {
                    _serveProcess.Kill();
                    _serveProcess.WaitForExit(1000);
                }
            }
            catch { }
            
            _serveProcess.Dispose();
        }
    }
}