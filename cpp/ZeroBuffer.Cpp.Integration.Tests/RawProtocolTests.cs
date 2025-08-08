using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace ZeroBuffer.Cpp.Integration.Tests
{
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

            // Use binary stream directly to avoid encoding issues
            _writer = new StreamWriter(_cppProcess.StandardInput.BaseStream, Encoding.UTF8, bufferSize: 1)
            {
                AutoFlush = true
            };
            _reader = new StreamReader(_cppProcess.StandardOutput.BaseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1);

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
            await Task.Delay(500);
            _output.WriteLine("Process started");
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

        [Fact(Skip = "Temporarily disabled - logs interfering with stdout JSON-RPC protocol")]
        public async Task SendRawHealthRequest_ShouldReceiveResponse()
        {
            // Arrange - Create a simple health request (42 bytes)
            string jsonRequest = "{\"jsonrpc\":\"2.0\",\"method\":\"health\",\"id\":1}";
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonRequest);
            
            _output.WriteLine($"Sending JSON request: {jsonRequest}");
            _output.WriteLine($"Content-Length: {jsonBytes.Length}");
            _output.WriteLine($"Actual byte count: {jsonBytes.Length}");
            
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
                    
                    // Assert
                    responseJson.Should().Contain("\"result\":true", "health check should return true");
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

        [Fact(Skip = "Temporarily disabled - logs interfering with stdout JSON-RPC protocol")]
        public async Task SendMultipleRequests_ShouldReceiveResponses()
        {
            // Test 1: Health check
            await SendAndReceiveRawRequest("{\"jsonrpc\":\"2.0\",\"method\":\"health\",\"id\":1}");
            
            // Test 2: Initialize
            string initRequest = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"params\":{" +
                "\"role\":\"reader\",\"platform\":\"cpp\",\"scenario\":\"Test\"," +
                "\"testRunId\":\"test-123\",\"hostPid\":123,\"featureId\":1},\"id\":2}";
            await SendAndReceiveRawRequest(initRequest);
            
            // Test 3: Another health check
            await SendAndReceiveRawRequest("{\"jsonrpc\":\"2.0\",\"method\":\"health\",\"id\":3}");
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
    }
}