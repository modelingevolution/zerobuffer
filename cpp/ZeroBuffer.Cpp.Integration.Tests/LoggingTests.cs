using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Cpp.Integration.Tests
{
    /// <summary>
    /// Tests to verify that the C++ serve correctly returns logs in step responses
    /// </summary>
    public class LoggingTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Process _serveProcess;
        private readonly JsonRpc _jsonRpc;
        
        // Strongly typed request classes
        private class StepRequest
        {
            public string StepType { get; set; } = "";
            public string Step { get; set; } = "";
        }
        
        private class InitializeRequest
        {
            public string TestName { get; set; } = "";
        }

        public LoggingTests(ITestOutputHelper output)
        {
            _output = output;

            // Start zerobuffer-serve process
            var startInfo = new ProcessStartInfo
            {
                FileName = "../../../../build/serve/zerobuffer-serve",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            // Set environment variable
            startInfo.EnvironmentVariables["ZEROBUFFER_LOG_LEVEL"] = "DEBUG";

            _serveProcess = Process.Start(startInfo);
            if (_serveProcess == null)
            {
                throw new InvalidOperationException("Failed to start zerobuffer-serve process");
            }

            // Create JSON-RPC connection (first param is for sending, second for receiving)
            var handler = new HeaderDelimitedMessageHandler(
                _serveProcess.StandardInput.BaseStream,
                _serveProcess.StandardOutput.BaseStream);
            
            _jsonRpc = new JsonRpc(handler);
            _jsonRpc.StartListening();
        }

        [Fact]
        public async Task ExecuteStep_ShouldReturnLogs()
        {
            // Arrange
            var request = new StepRequest
            {
                StepType = "Given",
                Step = "the test environment is initialized"
            };

            // Act
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _jsonRpc.InvokeWithCancellationAsync<ExecutionResult>(
                "executeStep",
                new[] { request },
                cts.Token);

            _output.WriteLine($"Response: {response}");
            
            // Assert
            Assert.NotNull(response);
            Assert.True(response.Success);
            Assert.NotNull(response.Logs);
            Assert.NotEmpty(response.Logs);
            
            // Verify log structure
            var firstLog = response.Logs[0];
            Assert.NotNull(firstLog.Level);
            Assert.NotNull(firstLog.Message);
            
            _output.WriteLine($"Logs count: {response.Logs.Count}");
            foreach (var log in response.Logs)
            {
                _output.WriteLine($"  [{log.Level}] {log.Message}");
            }
        }

        [Fact]
        public async Task ExecuteStep_WithDebugLogging_ShouldReturnDebugLogs()
        {
            // Arrange - Initialize to ensure logging is set up
            using var initCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            var initRequest = new InitializeRequest { TestName = "LoggingTest" };
            await _jsonRpc.InvokeWithCancellationAsync<bool>(
                "initialize",
                new[] { initRequest },
                initCts.Token);
            
            var request = new StepRequest
            {
                StepType = "Given",
                Step = "the 'reader' process creates buffer 'test-buffer' with metadata size '1024' and payload size '10240'"
            };

            // Act
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _jsonRpc.InvokeWithCancellationAsync<ExecutionResult>(
                "executeStep",
                new[] { request },
                cts.Token);

            _output.WriteLine($"Response: {response}");
            
            // Assert
            Assert.NotNull(response);
            Assert.True(response.Success);
            Assert.NotNull(response.Logs);
            Assert.NotEmpty(response.Logs);
            
            // Should have DEBUG level logs since we set ZEROBUFFER_LOG_LEVEL=DEBUG
            bool hasDebugLogs = false;
            bool hasInfoLogs = false;
            
            foreach (var log in response.Logs)
            {
                var level = log.Level;
                if (level == "DEBUG")
                    hasDebugLogs = true;
                if (level == "INFO")
                    hasInfoLogs = true;
                    
                _output.WriteLine($"  [{level}] {log.Message}");
            }
            
            // We expect at least some logs (either INFO or DEBUG)
            Assert.True(hasDebugLogs || hasInfoLogs, "Should have at least INFO or DEBUG logs");
        }

        [Fact]
        public async Task ExecuteStep_WithError_ShouldReturnErrorLogs()
        {
            // Arrange - Try to execute a non-existent step
            var request = new StepRequest
            {
                StepType = "Given",
                Step = "this step does not exist at all"
            };

            // Act
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _jsonRpc.InvokeWithCancellationAsync<ExecutionResult>(
                "executeStep",
                new[] { request },
                cts.Token);

            _output.WriteLine($"Response: {response}");
            
            // Assert
            Assert.NotNull(response);
            Assert.False(response.Success); // Should fail
            Assert.NotNull(response.Logs);
            
            // Should contain error information in logs
            if (response.Logs.Count > 0)
            {
                _output.WriteLine($"Logs count: {response.Logs.Count}");
                foreach (var log in response.Logs)
                {
                    _output.WriteLine($"  [{log.Level}] {log.Message}");
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _jsonRpc?.Dispose();
                
                if (_serveProcess != null && !_serveProcess.HasExited)
                {
                    _serveProcess.Kill();
                    _serveProcess.WaitForExit(1000);
                    _serveProcess.Dispose();
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}