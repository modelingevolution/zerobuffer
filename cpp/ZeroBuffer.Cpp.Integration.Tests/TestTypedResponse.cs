using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using StreamJsonRpc;
using System.Collections.Generic;

namespace ZeroBuffer.Cpp.Integration.Tests
{
    public class TestTypedResponse : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _cppProcess;
        private JsonRpc? _jsonRpc;
        private HeaderDelimitedMessageHandler? _messageHandler;

        public TestTypedResponse(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _output.WriteLine("Starting C++ zerobuffer-serve process...");
            
            var cppRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var servePath = Path.Combine(cppRoot, "build", "serve", "zerobuffer-serve");
            
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
            
            psi.Environment["ZEROBUFFER_LOG_LEVEL"] = "DEBUG";

            _cppProcess = Process.Start(psi);
            if (_cppProcess == null)
            {
                throw new InvalidOperationException("Failed to start C++ process");
            }

            // Setup JSON-RPC communication
            var formatter = new JsonMessageFormatter();
            _messageHandler = new HeaderDelimitedMessageHandler(_cppProcess.StandardInput.BaseStream, _cppProcess.StandardOutput.BaseStream, formatter);
            _jsonRpc = new JsonRpc(_messageHandler);
            _jsonRpc.StartListening();

            await Task.Delay(500);
            
            var health = await _jsonRpc.InvokeAsync<bool>("health");
            Assert.True(health);
            
            var initParams = new
            {
                role = "reader",
                platform = "cpp",
                scenario = "Test",
                testRunId = Guid.NewGuid().ToString(),
                hostPid = Process.GetCurrentProcess().Id,
                featureId = 1
            };
            
            var initResult = await _jsonRpc.InvokeAsync<bool>("initialize", initParams);
            Assert.True(initResult);
        }

        public async Task DisposeAsync()
        {
            _jsonRpc?.Dispose();
            _messageHandler?.Dispose();
            
            if (_cppProcess != null && !_cppProcess.HasExited)
            {
                _cppProcess.Kill();
                await _cppProcess.WaitForExitAsync();
                _cppProcess.Dispose();
            }
        }

        private class StepResponse
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public Dictionary<string, object>? Data { get; set; }
            public List<LogResponse>? Logs { get; set; }
        }

        private class LogResponse
        {
            public DateTime Timestamp { get; set; } = DateTime.MinValue;
            public string? Level { get; set; }
            public string? Message { get; set; }
        }

        [Fact]
        public async Task TestWithTypedResponse()
        {
            // Act - use typed response like Harmony does
            var response = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", new { step = "all processes are ready" });
            
            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Logs);
            Assert.NotEmpty(response.Logs);
            
            _output.WriteLine($"Got {response.Logs.Count} logs");
            foreach (var log in response.Logs)
            {
                _output.WriteLine($"[{log.Level}] {log.Message}");
            }
            
            // Should have the expected log
            Assert.Contains(response.Logs, log => log.Message?.Contains("All processes ready") == true);
        }
    }
}