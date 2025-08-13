using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using Newtonsoft.Json;
using ModelingEvolution.Harmony.Shared;
using System.Collections.Immutable;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace ZeroBuffer.Cpp.Integration.Tests
{
    /// <summary>
    /// Tests the raw JSON-RPC protocol using shared Harmony contracts.
    /// These tests verify the protocol at the wire level while still using proper contract types.
    /// </summary>
    public class RawProtocolTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _cppProcess;
        private StreamWriter? _writer;
        private StreamReader? _reader;

        public RawProtocolTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _output.WriteLine("Starting C++ zerobuffer-serve process for raw protocol test...");
            
            var cppRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var servePath = Path.Combine(cppRoot, "build", "serve", "zerobuffer-serve");
            
            _output.WriteLine($"Serve executable: {servePath}");
            
            if (!File.Exists(servePath))
            {
                throw new FileNotFoundException($"C++ zerobuffer-serve not found at: {servePath}");
            }
            
            var psi = new ProcessStartInfo
            {
                FileName = servePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(servePath),
                CreateNoWindow = true
            };
            
            // Set environment to disable buffering
            psi.Environment["ZEROBUFFER_LOG_LEVEL"] = "ERROR"; // Reduce log noise

            _cppProcess = Process.Start(psi);
            if (_cppProcess == null)
            {
                throw new InvalidOperationException("Failed to start C++ process");
            }

            // Use streams with reasonable buffer sizes
            _writer = new StreamWriter(_cppProcess.StandardInput.BaseStream, Encoding.UTF8)
            {
                AutoFlush = true
            };
            _reader = new StreamReader(_cppProcess.StandardOutput.BaseStream, Encoding.UTF8);

            // Capture stderr in background
            _ = Task.Run(async () =>
            {
                var errorReader = _cppProcess.StandardError;
                string? line;
                while ((line = await errorReader.ReadLineAsync()) != null)
                {
                    _output.WriteLine($"[STDERR] {line}");
                }
            });

            // Give process time to start
            await Task.Delay(100);
            _output.WriteLine("Process started and ready");
        }

        public async Task DisposeAsync()
        {
            _writer?.Dispose();
            _reader?.Dispose();
            
            if (_cppProcess != null && !_cppProcess.HasExited)
            {
                _cppProcess.Kill();
                await _cppProcess.WaitForExitAsync();
                _cppProcess.Dispose();
            }
        }

        [Fact]
        public async Task SendRawHealthRequest_ShouldReceiveResponse()
        {
            // Arrange - Create a health request using proper JSON-RPC format
            var request = new
            {
                jsonrpc = "2.0",
                method = ServoMethods.Health,
                id = 1
            };
            string jsonRequest = JsonConvert.SerializeObject(request);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonRequest);
            
            _output.WriteLine($"Sending health request: {jsonRequest}");
            _output.WriteLine($"Content-Length: {jsonBytes.Length}");
            
            // Act - Send with Content-Length header
            await _writer!.WriteAsync($"Content-Length: {jsonBytes.Length}\r\n");
            await _writer.WriteAsync("\r\n");  // Empty line to end headers
            await _writer.WriteAsync(jsonRequest);
            await _writer.FlushAsync();
            
            _output.WriteLine("Request sent, waiting for response...");
            
            // Use a timeout for reading to avoid hanging forever
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            
            try
            {
                // Read response headers
                string? headerLine;
                int responseLength = 0;
                
                while ((headerLine = await ReadLineWithTimeoutAsync(cts.Token)) != null)
                {
                    _output.WriteLine($"Response header: '{headerLine}'");
                    
                    if (string.IsNullOrEmpty(headerLine))
                    {
                        _output.WriteLine("End of headers");
                        break;
                    }
                    
                    if (headerLine.StartsWith("Content-Length: "))
                    {
                        responseLength = int.Parse(headerLine.Substring(16));
                        _output.WriteLine($"Response Content-Length: {responseLength}");
                    }
                }
                
                // Read response body
                if (responseLength > 0)
                {
                    char[] buffer = new char[responseLength];
                    int totalRead = 0;
                    
                    while (totalRead < responseLength)
                    {
                        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        readCts.CancelAfter(TimeSpan.FromSeconds(1));
                        
                        try
                        {
                            int read = await _reader!.ReadAsync(buffer, totalRead, responseLength - totalRead);
                            if (read == 0)
                            {
                                _output.WriteLine("Unexpected end of stream");
                                break;
                            }
                            totalRead += read;
                        }
                        catch (OperationCanceledException)
                        {
                            _output.WriteLine($"Read timeout. Read {totalRead} of {responseLength} bytes");
                            break;
                        }
                    }
                    
                    string responseJson = new string(buffer, 0, totalRead);
                    _output.WriteLine($"Response body: {responseJson}");
                    
                    // Assert - Deserialize and verify the response
                    dynamic response = JsonConvert.DeserializeObject(responseJson)!;
                    ((bool)response.result).Should().BeTrue("health check should return true");
                    responseJson.Should().Contain("\"id\":1", "response should echo the request ID");
                }
                else
                {
                    throw new Exception("No response received or no Content-Length header");
                }
            }
            catch (OperationCanceledException)
            {
                _output.WriteLine("Test timed out waiting for response");
                throw new TimeoutException("No response received from C++ serve within 5 seconds");
            }
        }
        
        private async Task<string?> ReadLineWithTimeoutAsync(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<string?>();
            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                var readLineTask = _reader!.ReadLineAsync();
                var completedTask = await Task.WhenAny(readLineTask, tcs.Task);
                if (completedTask == readLineTask)
                {
                    return await readLineTask;
                }
                throw new OperationCanceledException();
            }
        }

        [Fact]
        public async Task SendMultipleRequests_ShouldReceiveResponses()
        {
            // Test 1: Health check using proper contract
            var healthRequest = new
            {
                jsonrpc = "2.0",
                method = ServoMethods.Health,
                id = 1
            };
            await SendAndReceiveRawRequest(JsonConvert.SerializeObject(healthRequest));
            
            // Test 2: Initialize using shared InitializeRequest contract
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "cpp",
                Scenario: "RawProtocolTest",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 1
            );
            
            // Serialize the InitializeRequest properly for JSON-RPC
            var initJsonRpc = new
            {
                jsonrpc = "2.0",
                method = ServoMethods.Initialize,
                @params = initRequest,  // Use the shared contract object
                id = 2
            };
            await SendAndReceiveRawRequest(JsonConvert.SerializeObject(initJsonRpc));
            
            // Test 3: Another health check
            var healthRequest2 = new
            {
                jsonrpc = "2.0",
                method = ServoMethods.Health,
                id = 3
            };
            await SendAndReceiveRawRequest(JsonConvert.SerializeObject(healthRequest2));
        }

        private async Task<string> SendAndReceiveRawRequest(string jsonRequest)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonRequest);
            
            _output.WriteLine($"Sending: {jsonRequest}");
            
            // Send request
            await _writer!.WriteAsync($"Content-Length: {jsonBytes.Length}\r\n");
            await _writer.WriteAsync("\r\n");
            await _writer.WriteAsync(jsonRequest);
            await _writer.FlushAsync();
            
            // Read response headers
            string? headerLine;
            int responseLength = 0;
            
            while ((headerLine = await _reader!.ReadLineAsync()) != null)
            {
                if (string.IsNullOrEmpty(headerLine))
                    break;
                
                if (headerLine.StartsWith("Content-Length: "))
                {
                    responseLength = int.Parse(headerLine.Substring(16));
                }
            }
            
            // Read response body
            if (responseLength > 0)
            {
                char[] buffer = new char[responseLength];
                await _reader.ReadBlockAsync(buffer, 0, responseLength);
                string responseJson = new string(buffer);
                _output.WriteLine($"Received: {responseJson}");
                return responseJson;
            }
            
            throw new Exception("No response received");
        }

        [Fact]
        public async Task SendStepRequest_UsingSharedContract_ShouldWork()
        {
            // First initialize using the shared contract
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "cpp",
                Scenario: "StepTestScenario",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 42
            );
            
            var initJsonRpc = new
            {
                jsonrpc = "2.0",
                method = ServoMethods.Initialize,
                @params = initRequest,
                id = 1
            };
            
            string initResponse = await SendAndReceiveRawRequest(JsonConvert.SerializeObject(initJsonRpc));
            _output.WriteLine($"Initialize response: {initResponse}");
            
            // Now send a step request using the shared StepRequest contract
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the test environment is initialized",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            
            // The C++ servo expects simpler format, so we adapt it
            var adaptedStepRequest = new
            {
                stepType = stepRequest.StepType.ToString(),
                step = stepRequest.Step
            };
            
            var stepJsonRpc = new
            {
                jsonrpc = "2.0",
                method = ServoMethods.ExecuteStep,
                @params = adaptedStepRequest,
                id = 2
            };
            
            string stepResponse = await SendAndReceiveRawRequest(JsonConvert.SerializeObject(stepJsonRpc));
            _output.WriteLine($"Step response: {stepResponse}");
            
            // Verify the response
            dynamic response = JsonConvert.DeserializeObject(stepResponse)!;
            ((bool)response.result.success).Should().BeTrue("step should execute successfully");
        }
    }
}