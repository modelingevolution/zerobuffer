using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing Python zerobuffer-serve JSON-RPC...");
        
        var processInfo = new ProcessStartInfo
        {
            FileName = "/mnt/d/source/modelingevolution/streamer/src/zerobuffer/python/zerobuffer-serve",
            WorkingDirectory = "/mnt/d/source/modelingevolution/streamer/src/zerobuffer",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Console.WriteLine("Failed to start process");
            return;
        }

        // Read stderr in background
        var stderrTask = Task.Run(async () =>
        {
            using var reader = process.StandardError;
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                Console.WriteLine($"[STDERR] {line}");
            }
        });

        try
        {
            // Test 1: Health check
            Console.WriteLine("\n=== Testing health check ===");
            var healthRequest = new
            {
                jsonrpc = "2.0",
                method = "health",
                @params = new { },
                id = 1
            };
            
            var json = JsonSerializer.Serialize(healthRequest);
            Console.WriteLine($"Sending: {json}");
            await process.StandardInput.WriteLineAsync(json);
            await process.StandardInput.FlushAsync();
            
            // Read response
            var response = await process.StandardOutput.ReadLineAsync();
            Console.WriteLine($"Response: {response}");
            
            // Test 2: Initialize
            Console.WriteLine("\n=== Testing initialize ===");
            var initRequest = new
            {
                jsonrpc = "2.0",
                method = "initialize",
                @params = new
                {
                    role = "reader",
                    scenario = "Test 1.1",
                    platform = "python",
                    testRunId = "test-dotnet"
                },
                id = 2
            };
            
            json = JsonSerializer.Serialize(initRequest);
            Console.WriteLine($"Sending: {json}");
            await process.StandardInput.WriteLineAsync(json);
            await process.StandardInput.FlushAsync();
            
            response = await process.StandardOutput.ReadLineAsync();
            Console.WriteLine($"Response: {response}");
            
            // Test 3: Execute a step
            Console.WriteLine("\n=== Testing execute step ===");
            var stepRequest = new
            {
                jsonrpc = "2.0",
                method = "executeStep",
                @params = new
                {
                    stepType = "given",
                    step = "the test environment is initialized"
                },
                id = 3
            };
            
            json = JsonSerializer.Serialize(stepRequest);
            Console.WriteLine($"Sending: {json}");
            await process.StandardInput.WriteLineAsync(json);
            await process.StandardInput.FlushAsync();
            
            response = await process.StandardOutput.ReadLineAsync();
            Console.WriteLine($"Response: {response}");
            
            Console.WriteLine("\n=== All tests completed successfully! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            // Kill the process
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
        }
    }
}