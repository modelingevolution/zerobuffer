using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using StreamJsonRpc;
using ModelingEvolution.Harmony.ProcessManagement;

namespace ModelingEvolution.Harmony.Tests;

public class StepResponseTest
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public List<LogEntryTest> Logs { get; set; } = new();
}

public class LogEntryTest
{
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
}

public class CSharpServeIntegrationTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Process? _serveProcess;
    private JsonRpc? _jsonRpc;
    
    public CSharpServeIntegrationTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public async Task TestSingleStepExecution()
    {
        // First, let's understand the working directory
        _output.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
        
        // Start the C# serve process
        var servePath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), 
            "../../../../../../csharp/ZeroBuffer.Serve/bin/Debug/net9.0/ZeroBuffer.Serve"));
        
        if (!File.Exists(servePath))
        {
            _output.WriteLine($"Serve executable not found at: {servePath}");
            _output.WriteLine("Building serve project...");
            
            // Try to build it
            var buildProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build ../../../../../../csharp/ZeroBuffer.Serve/ZeroBuffer.Serve.csproj",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            
            await buildProcess!.WaitForExitAsync();
            
            if (buildProcess.ExitCode != 0)
            {
                var error = await buildProcess.StandardError.ReadToEndAsync();
                throw new Exception($"Failed to build serve: {error}");
            }
        }
        
        _output.WriteLine($"Starting serve process: {servePath}");
        
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
        
        _serveProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _output.WriteLine($"[SERVE ERR] {e.Data}");
        };
        
        _serveProcess.Start();
        _serveProcess.BeginErrorReadLine();
        
        // Set up JSON-RPC
        var formatter = new SystemTextJsonFormatter();
        var handler = new LengthHeaderMessageHandler(
            _serveProcess.StandardInput.BaseStream,  // sending stream first
            _serveProcess.StandardOutput.BaseStream, // receiving stream second
            formatter);
        
        _jsonRpc = new JsonRpc(handler);
        _jsonRpc.StartListening();
        
        // Initialize the serve
        _output.WriteLine("Initializing serve...");
        
        var initRequest = new
        {
            hostPid = 1234,
            featureId = 5678,
            role = "reader",
            platform = "csharp",
            scenario = "Test Scenario",
            testRunId = "test-123"
        };
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var initResponse = await _jsonRpc.InvokeWithParameterObjectAsync<bool>(
            "initialize", 
            initRequest,
            cts.Token);
        
        _output.WriteLine($"Initialize response: Success={initResponse}");
        Assert.True(initResponse);
        
        // Execute a simple step
        _output.WriteLine("Executing step...");
        
        var stepRequest = new
        {
            stepType = "given",
            step = "creates buffer 'test' with size '1024'",
            parameters = new Dictionary<string, object>
            {
                ["buffer_name"] = "test",
                ["size"] = "1024"
            }
        };
        
        var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stepResponse = await _jsonRpc.InvokeWithParameterObjectAsync<StepResponseTest>(
            "executeStep",
            stepRequest,
            cts2.Token);
        
        _output.WriteLine($"Step response: Success={stepResponse.Success}");
        Assert.True(stepResponse.Success);
        
        // Cleanup
        await _jsonRpc.InvokeAsync("cleanup");
        
        // Shutdown the serve process gracefully
        try
        {
            await _jsonRpc.NotifyAsync("shutdown");
            await Task.Delay(100); // Give it time to shutdown
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Shutdown error (expected): {ex.Message}");
        }
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
                _serveProcess.Kill();
                _serveProcess.WaitForExit(1000);
            }
            catch { }
            finally
            {
                _serveProcess.Dispose();
            }
        }
    }
}