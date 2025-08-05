using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace ZeroBuffer.Serve;

/// <summary>
/// Basic example to understand JSON-RPC client-server communication
/// </summary>
public class BasicJsonRpcExample
{
    public static async Task RunServerExample()
    {
        // Server process - listens on stdin/stdout
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        
        var formatter = new SystemTextJsonFormatter();
        var handler = new LengthHeaderMessageHandler(stdout, stdin, formatter);
        
        using var jsonRpc = new StreamJsonRpc.JsonRpc(handler);
        
        // Add methods
        jsonRpc.AddLocalRpcMethod("echo", new Func<string, string>(msg => $"Echo: {msg}"));
        jsonRpc.AddLocalRpcMethod("add", new Func<int, int, int>((a, b) => a + b));
        
        jsonRpc.StartListening();
        
        // Wait for completion
        await jsonRpc.Completion;
    }
    
    public static async Task RunClientExample()
    {
        // Start server process
        var serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run --project /path/to/server.csproj",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        serverProcess.Start();
        
        // Create JSON-RPC client
        var formatter = new SystemTextJsonFormatter();
        var handler = new LengthHeaderMessageHandler(
            serverProcess.StandardInput.BaseStream,
            serverProcess.StandardOutput.BaseStream,
            formatter);
        
        using var jsonRpc = new StreamJsonRpc.JsonRpc(handler);
        jsonRpc.StartListening();
        
        // Call methods
        var echoResult = await jsonRpc.InvokeAsync<string>("echo", "Hello World");
        Console.WriteLine($"Echo result: {echoResult}");
        
        var addResult = await jsonRpc.InvokeAsync<int>("add", 5, 3);
        Console.WriteLine($"Add result: {addResult}");
        
        // Cleanup
        jsonRpc.Dispose();
        serverProcess.Kill();
    }
}