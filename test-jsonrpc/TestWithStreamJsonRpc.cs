using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;

class TestWithStreamJsonRpc
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing Python zerobuffer-serve with StreamJsonRpc (same as ProcessManager)...");
        
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

        // Read stderr in background (for logging)
        var stderrTask = Task.Run(async () =>
        {
            using var reader = process.StandardError;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                Console.WriteLine($"[STDERR] {line}");
            }
        });

        try
        {
            // Create JsonRpc connection exactly like ProcessManager does
            Console.WriteLine("\n=== Creating JsonRpc connection (same as ProcessManager line 197) ===");
            var rpc = new JsonRpc(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
            rpc.StartListening();
            
            // Test 1: Health check (same as ProcessManager line 202)
            Console.WriteLine("\n=== Testing health check (same as ProcessManager line 202) ===");
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
            
            // Initialize process with resource isolation parameters (exactly like ProcessManager)
            var initParams = new { hostPid = 0, featureId = 0 };
            
            Console.WriteLine("Calling health with params: { hostPid = 0, featureId = 0 }");
            var healthResult = await rpc.InvokeWithCancellationAsync<bool>("health", new[] { initParams }, cancellationToken);
            Console.WriteLine($"Health check result: {healthResult}");
            
            if (!healthResult)
            {
                throw new InvalidOperationException("Process health check failed");
            }
            
            // Test 2: Initialize (same as ProcessManager line 209-211)
            Console.WriteLine("\n=== Testing initialize (conditional, same as ProcessManager line 209) ===");
            // ProcessManager only sends initialize if hostPid > 0 && featureId > 0
            // Let's test with actual values
            var realInitParams = new { hostPid = Process.GetCurrentProcess().Id, featureId = 1 };
            Console.WriteLine($"Calling initialize with params: {{ hostPid = {realInitParams.hostPid}, featureId = {realInitParams.featureId} }}");
            var initResult = await rpc.InvokeWithCancellationAsync<bool>("initialize", new[] { realInitParams }, cancellationToken);
            Console.WriteLine($"Initialize result: {initResult}");
            
            // Test 3: Execute a step (this would be called later via GetConnection())
            Console.WriteLine("\n=== Testing execute step (would be called via Connection) ===");
            var stepParams = new 
            { 
                stepType = "given",
                step = "the test environment is initialized"
            };
            Console.WriteLine($"Calling executeStep with params: {{ stepType = \"given\", step = \"the test environment is initialized\" }}");
            
            // Note: ProcessConnection uses InvokeAsync<T> which internally calls InvokeWithCancellationAsync
            var stepResult = await rpc.InvokeWithCancellationAsync<object>("executeStep", new[] { stepParams }, cancellationToken);
            Console.WriteLine($"Execute step result: {stepResult}");
            
            Console.WriteLine("\n=== All StreamJsonRpc tests completed successfully! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
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