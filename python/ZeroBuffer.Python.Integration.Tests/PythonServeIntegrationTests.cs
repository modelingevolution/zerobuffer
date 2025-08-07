using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZeroBuffer.Python.Integration.Tests
{
    public class PythonServeIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _pythonProcess;
        private JsonRpc? _jsonRpc;
        private readonly List<string> _pythonErrors = new();

        public PythonServeIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _output.WriteLine("Starting Python zerobuffer-serve process...");
            
            // Find the Python project root (where zerobuffer-serve executable is located)
            var pythonRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "python"));
            var servePath = Path.Combine(pythonRoot, "zerobuffer-serve");
            
            _output.WriteLine($"Python root: {pythonRoot}");
            _output.WriteLine($"Serve executable: {servePath}");
            
            // Start Python serve process using the SAME executable that Harmony uses
            var psi = new ProcessStartInfo
            {
                FileName = servePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = pythonRoot,
                CreateNoWindow = true
            };

            _pythonProcess = Process.Start(psi);
            if (_pythonProcess == null)
            {
                throw new InvalidOperationException("Failed to start Python process");
            }

            // Capture stderr in background
            _ = Task.Run(async () =>
            {
                var reader = _pythonProcess.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    _pythonErrors.Add(line);
                }
            });

            // Create JSON-RPC client with StreamJsonRpc
            _jsonRpc = new JsonRpc(_pythonProcess.StandardInput.BaseStream, _pythonProcess.StandardOutput.BaseStream);
            _jsonRpc.StartListening();

            // Give the process time to initialize
            await Task.Delay(500);
            _output.WriteLine("Python process started successfully");
        }

        public async Task DisposeAsync()
        {
            if (_jsonRpc != null)
            {
                _jsonRpc.Dispose();
            }

            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                _pythonProcess.Kill();
                await _pythonProcess.WaitForExitAsync();
                _pythonProcess.Dispose();
            }

            // Output any Python errors for debugging
            if (_pythonErrors.Any())
            {
                _output.WriteLine("Python stderr output:");
                foreach (var error in _pythonErrors)
                {
                    _output.WriteLine($"  {error}");
                }
            }
        }

        private void LogStepResult(ExecutionResult result, string stepName)
        {
            _output.WriteLine($"Step '{stepName}' result: {(result.Success ? "SUCCESS" : "FAILED")}");
            
            if (!string.IsNullOrEmpty(result.Error))
            {
                _output.WriteLine($"  Error: {result.Error}");
            }
            
            // Log all execution logs from Python
            if (result.Logs != null && result.Logs.Count > 0)
            {
                _output.WriteLine($"  Execution logs ({result.Logs.Count} entries):");
                foreach (var log in result.Logs)
                {
                    // Skip very verbose DEBUG logs unless they're important
                    if (log.Level == "DEBUG" && log.Message.StartsWith("Sent response") && result.Logs.Count > 20)
                        continue;
                        
                    _output.WriteLine($"    [{log.Level}] {log.Message}");
                }
            }
            
            // Log duration if available
            if (result.Duration != TimeSpan.Zero)
            {
                _output.WriteLine($"  Duration: {result.Duration.TotalMilliseconds}ms");
            }
        }

        [Fact]
        public async Task HealthCheck_ShouldReturnTrue()
        {
            // Act
            var result = await _jsonRpc!.InvokeAsync<bool>("health");

            // Assert
            result.Should().BeTrue("health check should always return true");
            _output.WriteLine("Health check passed");
        }

        [Fact]
        public async Task Initialize_ShouldAcceptValidParameters_AndReturnTrue()
        {
            // Arrange
            var initParams = new
            {
                role = "reader",
                platform = "python",
                scenario = "Test1_1", 
                testRunId = Guid.NewGuid().ToString(),
                hostPid = Process.GetCurrentProcess().Id,
                featureId = 123
            };

            // Act
            var result = await _jsonRpc!.InvokeAsync<bool>("initialize", initParams);

            // Assert
            result.Should().BeTrue("initialization should succeed with valid parameters");
            _output.WriteLine($"Initialization succeeded with role={initParams.role}, platform={initParams.platform}");
        }

        public class DiscoverResponse
        {
            public List<StepInfo> Steps { get; set; } = new();
        }

        public class StepInfo
        {
            public string Type { get; set; } = string.Empty;
            public string Pattern { get; set; } = string.Empty;
        }
        [Fact]
        public async Task Discover_ShouldReturnStepDefinitions()
        {
            // Wait a bit more for the process to fully initialize
            await Task.Delay(1000);
            
            // Act
            var result = await _jsonRpc!.InvokeAsync<DiscoverResponse>("discover");

            
            
            
            // Log discovered steps
            _output.WriteLine($"Discovered {result.Steps!.Count} step definitions:");
            if (result.Steps.Count == 0)
            {
                _output.WriteLine("  ERROR: No steps found! Step discovery is broken.");
                
                // Output Python errors if any
                if (_pythonErrors.Any())
                {
                    _output.WriteLine("Python stderr during discovery:");
                    foreach (var error in _pythonErrors.TakeLast(10))
                    {
                        _output.WriteLine($"  {error}");
                    }
                }
            }
            else
            {
                // Log a sample of discovered steps (first 10)
                
                foreach (var step in result.Steps)
                {
                    _output.WriteLine($"{step.Type} {step.Pattern}");
                }
            }

            // Assert that steps were discovered
            _output.WriteLine($"ASSERTION: Checking if {result.Steps.Count} > 0");
            result.Steps.Count.Should().BeGreaterThan(0, "there should be at least one step definition available");
        }

        [Fact]
        public async Task ExecuteStep_CreateBuffer_ShouldSucceed()
        {
            // Arrange - Initialize first
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "reader",
                platform = "python",
                scenario = "TestCreateBuffer",
                testRunId = Guid.NewGuid().ToString()
            });

            // Act - Create buffer
            var result = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                process = "reader",
                stepType = "Given",
                step = "creates buffer 'test-integration-buffer' with metadata size '1024' and payload size '10240'",
                originalStep = "the reader process creates buffer 'test-integration-buffer' with metadata size '1024' and payload size '10240'",
                parameters = new { }
            });

            // Assert and log
            result.Should().NotBeNull();
            LogStepResult(result, "create buffer");
            result.Success.Should().BeTrue("create buffer step should succeed");
        }

        [Fact]
        public async Task ExecuteStep_ConnectAndWriteMetadata_ShouldSucceed()
        {
            // Arrange - Initialize and create buffer first
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "writer",
                platform = "python", 
                scenario = "TestWriteMetadata",
                testRunId = Guid.NewGuid().ToString()
            });

            // First create a buffer as reader
            var createResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                process = "reader",
                stepType = "Given", 
                step = "creates buffer 'test-metadata-buffer' with metadata size '1024' and payload size '10240'",
                originalStep = "the reader process creates buffer 'test-metadata-buffer' with metadata size '1024' and payload size '10240'",
                parameters = new { }
            });
            LogStepResult(createResult, "create buffer for metadata test");

            // Act - Connect as writer
            var connectResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                process = "writer",
                stepType = "When",
                step = "connects to buffer 'test-metadata-buffer'",
                originalStep = "the writer process connects to buffer 'test-metadata-buffer'",
                parameters = new { }
            });

            // Assert and log connect
            connectResult.Should().NotBeNull();
            LogStepResult(connectResult, "connect to buffer");
            connectResult.Success.Should().BeTrue("connect to buffer should succeed");

            // Act - Write metadata
            var writeResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                process = "writer",
                stepType = "When",
                step = "writes metadata with size '100'",
                originalStep = "the writer process writes metadata with size '100'",
                parameters = new { }
            });

            // Assert and log write
            writeResult.Should().NotBeNull();
            LogStepResult(writeResult, "write metadata");
            writeResult.Success.Should().BeTrue("write metadata should succeed");
        }

        [Fact]
        public async Task ExecuteStep_InvalidStep_ShouldReturnError()
        {
            // Arrange
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "reader",
                platform = "python",
                scenario = "TestInvalidStep",
                testRunId = Guid.NewGuid().ToString()
            });

            // Act - Try to execute non-existent step
            var result = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                process = "reader",
                stepType = "Action",
                step = "this_step_does_not_exist",
                parameters = new { }
            });

            // Assert and log
            result.Should().NotBeNull();
            LogStepResult(result, "invalid step (expected to fail)");
            result.Success.Should().BeFalse("invalid step should fail");
            result.Error.Should().NotBeNull("error message should be provided");

            var errorMessage = result.Error;
            errorMessage.Should().Contain("No matching step", "error should indicate step not found");
        }
    }
    public class ExecutionResult
    {
        public bool Success { get; init; }
        public TimeSpan Duration { get; init; }
        public string? Error { get; init; }
        public Exception? Exception { get; init; }
        public List<LogEntry> Logs { get; init; } = new();
    }

    [DebuggerDisplay("{Process} {Level} {Message}")]
    public class LogEntry
    {
        public DateTime Timestamp { get; init; }
        public string Process { get; init; } = "";
        public string Platform { get; init; } = "";
        public string Level { get; init; } = "INFO";
        public string Message { get; init; } = "";
    }
}
