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
        
        // Don't pass the cancellation token directly - let JSON-RPC handle it
        var initResponse = await _jsonRpc.InvokeAsync<dynamic>(
            "initialize", 
            initRequest);
        
        _output.WriteLine($"Initialize response: {initResponse}");
        Assert.NotNull(initResponse);
        
        // Access the JSON properties correctly
        var jsonElement = (System.Text.Json.JsonElement)initResponse;
        Assert.True(jsonElement.GetProperty("Success").GetBoolean());
        
        // Test a simple step
        _output.WriteLine("Sending step request...");
        
        var stepRequest = new
        {
            StepType = "given",
            Step = "creates buffer 'test' with size '1024'"
        };
        
        var stepResponse = await _jsonRpc.InvokeAsync<dynamic>(
            "executeStep",
            stepRequest);
        
        _output.WriteLine($"Step response: {stepResponse}");
        Assert.NotNull(stepResponse);
        
        var stepJsonElement = (System.Text.Json.JsonElement)stepResponse;
        Assert.True(stepJsonElement.GetProperty("Success").GetBoolean());
        
        // Check logs
        if (stepJsonElement.TryGetProperty("Logs", out var logs))
        {
            _output.WriteLine("Logs from step:");
            foreach (var log in logs.EnumerateArray())
            {
                var level = log.GetProperty("Level").GetString();
                var message = log.GetProperty("Message").GetString();
                _output.WriteLine($"  [{level}] {message}");
            }
        }
        
        // Cleanup
        await _jsonRpc.InvokeAsync("cleanup");
        
        // Shutdown gracefully
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
            finally
            {
                _serveProcess.Dispose();
            }
        }
    }
}