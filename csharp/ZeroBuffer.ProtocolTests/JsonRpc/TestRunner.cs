using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;
using ZeroBuffer.ProtocolTests.JsonRpc;

namespace ZeroBuffer.ProtocolTests.JsonRpcTest
{
    /// <summary>
    /// Example test runner that demonstrates how to use the JSON-RPC interface
    /// </summary>
    public class TestRunner : IDisposable
    {
        private readonly Process _process;
        private readonly StreamJsonRpc.JsonRpc _rpc;

        public TestRunner(string executablePath)
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "serve",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            _process.Start();
            
            // Capture stderr for logging
            _process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine($"[STDERR] {e.Data}");
                }
            };
            _process.BeginErrorReadLine();

            _rpc = new StreamJsonRpc.JsonRpc(_process.StandardOutput.BaseStream, _process.StandardInput.BaseStream);
            _rpc.StartListening();
        }

        /// <summary>
        /// Run a simple given/when/then test scenario
        /// </summary>
        public async Task RunSimpleTest()
        {
            Console.WriteLine("=== Running Simple Test 101 via JSON-RPC ===");
            
            try
            {
                // GIVEN: Setup reader and writer
                Console.WriteLine("\nGIVEN: A reader creates a buffer and a writer connects");
                
                var readerSetup = await _rpc.InvokeWithParameterObjectAsync<TestSetupResponse>(
                    "setup",
                    new TestSetupRequest 
                    { 
                        TestId = 101, 
                        Role = "reader", 
                        BufferName = "test-simple-101" 
                    });
                Console.WriteLine($"  Reader setup: handle={readerSetup.Handle}");
                
                var writerSetup = await _rpc.InvokeWithParameterObjectAsync<TestSetupResponse>(
                    "setup",
                    new TestSetupRequest 
                    { 
                        TestId = 101, 
                        Role = "writer", 
                        BufferName = "test-simple-101" 
                    });
                Console.WriteLine($"  Writer setup: handle={writerSetup.Handle}");
                
                // Create buffer on reader side
                var createResult = await ExecuteStep(readerSetup.Handle, "createBuffer", new
                {
                    metadataSize = 1024,
                    payloadSize = 10240
                });
                Console.WriteLine($"  Buffer created: {JsonSerializer.Serialize(createResult.Data)}");
                
                // Connect writer
                var connectResult = await ExecuteStep(writerSetup.Handle, "connectToBuffer", null);
                Console.WriteLine($"  Writer connected: {JsonSerializer.Serialize(connectResult.Data)}");
                
                // WHEN: Writer sends data
                Console.WriteLine("\nWHEN: Writer sends metadata and a frame");
                
                var metadata = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
                var writeMetaResult = await ExecuteStep(writerSetup.Handle, "writeMetadata", new
                {
                    data = metadata
                });
                Console.WriteLine($"  Metadata written: {JsonSerializer.Serialize(writeMetaResult.Data)}");
                
                var frameData = Convert.ToBase64String(Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
                var writeFrameResult = await ExecuteStep(writerSetup.Handle, "writeFrame", new
                {
                    data = frameData,
                    sequence = 1
                });
                Console.WriteLine($"  Frame written: {JsonSerializer.Serialize(writeFrameResult.Data)}");
                
                // THEN: Reader receives the data
                Console.WriteLine("\nTHEN: Reader receives the metadata and frame");
                
                var readMetaResult = await ExecuteStep(readerSetup.Handle, "readMetadata", null);
                Console.WriteLine($"  Metadata read: {JsonSerializer.Serialize(readMetaResult.Data)}");
                
                var readFrameResult = await ExecuteStep(readerSetup.Handle, "readFrame", new
                {
                    timeoutMs = 5000
                });
                Console.WriteLine($"  Frame read: {JsonSerializer.Serialize(readFrameResult.Data)}");
                
                // Verify data
                var verifyMetaResult = await ExecuteStep(readerSetup.Handle, "verifyMetadata", new
                {
                    expected = metadata
                });
                Console.WriteLine($"  Metadata verified: {JsonSerializer.Serialize(verifyMetaResult.Data)}");
                
                var verifyFrameResult = await ExecuteStep(readerSetup.Handle, "verifyFrame", new
                {
                    expectedData = frameData,
                    expectedSequence = 1
                });
                Console.WriteLine($"  Frame verified: {JsonSerializer.Serialize(verifyFrameResult.Data)}");
                
                // Teardown
                Console.WriteLine("\nTeardown:");
                var readerTeardown = await _rpc.InvokeWithParameterObjectAsync<TestTeardownResponse>(
                    "teardown",
                    new TestTeardownRequest { Handle = readerSetup.Handle });
                Console.WriteLine($"  Reader: {readerTeardown.Summary}");
                
                var writerTeardown = await _rpc.InvokeWithParameterObjectAsync<TestTeardownResponse>(
                    "teardown",
                    new TestTeardownRequest { Handle = writerSetup.Handle });
                Console.WriteLine($"  Writer: {writerTeardown.Summary}");
                
                Console.WriteLine("\n=== Test Completed Successfully ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n=== Test Failed: {ex.Message} ===");
                throw;
            }
        }

        private async Task<TestStepResponse> ExecuteStep(Guid handle, string stepName, object? args)
        {
            var request = new TestStepRequest
            {
                Handle = handle,
                StepName = stepName,
                Args = args != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(
                    JsonSerializer.Serialize(args)) : null
            };

            return await _rpc.InvokeWithParameterObjectAsync<TestStepResponse>("step", request);
        }

        public void Dispose()
        {
            _rpc?.Dispose();
            if (!_process.HasExited)
            {
                _process.Kill();
            }
            _process?.Dispose();
        }
    }
    
    /// <summary>
    /// Simple test to verify JSON-RPC infrastructure
    /// </summary>
    public static class SimpleJsonRpcTest
    {
        public static async Task Run()
        {
            Console.WriteLine("=== Testing JSON-RPC Infrastructure ===");
            
            try
            {
                var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using var runner = new TestRunner(executablePath);
                await runner.RunSimpleTest();
                Console.WriteLine("\n=== JSON-RPC Test Completed Successfully ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n=== JSON-RPC Test Failed: {ex.Message} ===");
                throw;
            }
        }
    }
}