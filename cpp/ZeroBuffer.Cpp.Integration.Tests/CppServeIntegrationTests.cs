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

namespace ZeroBuffer.Cpp.Integration.Tests
{
    public class CppServeIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _cppProcess;
        private JsonRpc? _jsonRpc;
        private readonly List<string> _cppErrors = new();

        public CppServeIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _output.WriteLine("Starting C++ zerobuffer-serve process...");
            
            // Find the C++ build directory
            // Test is in: cpp/ZeroBuffer.Cpp.Integration.Tests/bin/Debug/net9.0/
            // Serve is in: cpp/build/serve/zerobuffer-serve
            var cppRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var servePath = Path.Combine(cppRoot, "build", "serve", "zerobuffer-serve");
            
            _output.WriteLine($"C++ root: {cppRoot}");
            _output.WriteLine($"Serve executable: {servePath}");
            
            if (!File.Exists(servePath))
            {
                throw new FileNotFoundException($"C++ zerobuffer-serve not found at: {servePath}. Please build the C++ project first.");
            }
            
            // Start C++ serve process
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

            _cppProcess = Process.Start(psi);
            if (_cppProcess == null)
            {
                throw new InvalidOperationException("Failed to start C++ process");
            }

            // Capture stderr in background (C++ logs go to stderr)
            _ = Task.Run(async () =>
            {
                var reader = _cppProcess.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    _cppErrors.Add(line);
                    // Optionally output important logs immediately for debugging
                    if (line.Contains("ERROR") || line.Contains("FATAL"))
                    {
                        _output.WriteLine($"[C++ ERROR] {line}");
                    }
                }
            });

            // Create JSON-RPC client with StreamJsonRpc
            _jsonRpc = new JsonRpc(_cppProcess.StandardInput.BaseStream, _cppProcess.StandardOutput.BaseStream);
            _jsonRpc.StartListening();

            // Give the process time to initialize
            await Task.Delay(500);
            _output.WriteLine("C++ process started successfully");
        }

        public async Task DisposeAsync()
        {
            try
            {
                // Try graceful shutdown first
                if (_jsonRpc != null && !_cppProcess!.HasExited)
                {
                    try
                    {
                        await _jsonRpc.InvokeAsync("shutdown");
                        await Task.Delay(500);
                    }
                    catch
                    {
                        // Ignore shutdown errors
                    }
                }
            }
            finally
            {
                if (_jsonRpc != null)
                {
                    _jsonRpc.Dispose();
                }

                if (_cppProcess != null && !_cppProcess.HasExited)
                {
                    _cppProcess.Kill();
                    await _cppProcess.WaitForExitAsync();
                    _cppProcess.Dispose();
                }

                // Output C++ logs for debugging
                if (_cppErrors.Any())
                {
                    _output.WriteLine("C++ stderr output:");
                    foreach (var error in _cppErrors.TakeLast(20))
                    {
                        _output.WriteLine($"  {error}");
                    }
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
            
            // Log all execution logs from C++
            if (result.Logs != null && result.Logs.Count > 0)
            {
                _output.WriteLine($"  Execution logs ({result.Logs.Count} entries):");
                foreach (var log in result.Logs)
                {
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
            // Arrange - C++ expects slightly different format
            var initParams = new
            {
                role = "reader",
                platform = "cpp",
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
            public Dictionary<string, bool>? Capabilities { get; set; }
        }

        public class StepInfo
        {
            public string Type { get; set; } = string.Empty;
            public string Pattern { get; set; } = string.Empty;
        }

        [Fact]
        public async Task Discover_ShouldReturnStepDefinitions()
        {
            // Wait a bit for the process to fully initialize
            await Task.Delay(500);
            
            // Act
            var result = await _jsonRpc!.InvokeAsync<DiscoverResponse>("discover");

            // Log discovered steps
            _output.WriteLine($"Discovered {result.Steps!.Count} step definitions:");
            if (result.Steps.Count == 0)
            {
                _output.WriteLine("  ERROR: No steps found! Step discovery is broken.");
                
                // Output C++ errors if any
                if (_cppErrors.Any())
                {
                    _output.WriteLine("C++ stderr during discovery:");
                    foreach (var error in _cppErrors.TakeLast(10))
                    {
                        _output.WriteLine($"  {error}");
                    }
                }
            }
            else
            {
                // Log discovered steps
                foreach (var step in result.Steps)
                {
                    _output.WriteLine($"  {step.Type}: {step.Pattern}");
                }
            }

            // Check capabilities
            if (result.Capabilities != null)
            {
                _output.WriteLine("Capabilities:");
                foreach (var cap in result.Capabilities)
                {
                    _output.WriteLine($"  {cap.Key}: {cap.Value}");
                }
            }

            // Assert that steps were discovered
            result.Steps.Count.Should().BeGreaterThan(0, "there should be at least one step definition available");
        }

        [Fact]
        public async Task ExecuteStep_InitializeEnvironment_ShouldSucceed()
        {
            // Arrange - Initialize first
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "reader",
                platform = "cpp",
                scenario = "TestEnvironment",
                testRunId = Guid.NewGuid().ToString()
            });

            // Act - Execute the environment initialization step (which C++ has registered)
            // C++ expects the StreamJsonRpc format: array with single object
            var result = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Given",
                step = "the test environment is initialized"
            });

            // Assert and log
            result.Should().NotBeNull();
            LogStepResult(result, "initialize environment");
            result.Success.Should().BeTrue("initialize environment step should succeed");
        }

        [Fact]
        public async Task ExecuteStep_CreateBuffer_ShouldSucceed()
        {
            // Arrange - Initialize first
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "reader",
                platform = "cpp",
                scenario = "TestCreateBuffer",
                testRunId = Guid.NewGuid().ToString()
            });

            // Initialize environment first
            await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Given",
                step = "the test environment is initialized"
            });

            // Act - Create buffer with C++ step definition format
            var result = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Given",
                step = "the reader process creates buffer 'test-cpp-buffer' with default configuration"
            });

            // Assert and log
            result.Should().NotBeNull();
            LogStepResult(result, "create buffer");
            result.Success.Should().BeTrue("create buffer step should succeed");
        }

        [Fact]
        public async Task ExecuteStep_CompleteTest11Scenario_ShouldSucceed()
        {
            // This tests the complete Test 1.1 scenario that C++ has implemented
            
            // Arrange - Initialize
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "both", // C++ can handle both reader and writer in same process
                platform = "cpp",
                scenario = "Test1_1_SimpleWriteRead",
                testRunId = Guid.NewGuid().ToString()
            });

            // Initialize environment
            var initResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Given",
                step = "the test environment is initialized"
            });
            LogStepResult(initResult, "initialize environment");
            initResult.Success.Should().BeTrue();

            // Step 1: Create buffer (matching Harmony's Test 1.1 format exactly)
            var createResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Given",
                step = "the 'reader' process creates buffer 'simple-test' with metadata size '1024' and payload size '10240'"
            });
            LogStepResult(createResult, "create buffer");
            createResult.Success.Should().BeTrue();

            // Step 2: Connect writer (with quotes around process name)
            var connectResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "When",
                step = "the 'writer' process connects to buffer 'simple-test'"
            });
            LogStepResult(connectResult, "connect writer");
            connectResult.Success.Should().BeTrue();

            // Step 3: Write metadata
            var writeMetadataResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "When",
                step = "the 'writer' process writes metadata with size '100'"
            });
            LogStepResult(writeMetadataResult, "write metadata");
            writeMetadataResult.Success.Should().BeTrue();

            // Step 4: Write frame
            var writeResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "When",
                step = "the 'writer' process writes frame with size '1024' and sequence '1'"
            });
            LogStepResult(writeResult, "write frame");
            writeResult.Success.Should().BeTrue();

            // Step 5: Read and verify frame
            var readResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Then",
                step = "the 'reader' process should read frame with sequence '1' and size '1024'"
            });
            LogStepResult(readResult, "read frame");
            readResult.Success.Should().BeTrue();

            // Step 6: Validate frame data
            var validateResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Then",
                step = "the 'reader' process should validate frame data"
            });
            LogStepResult(validateResult, "validate frame data");
            validateResult.Success.Should().BeTrue();

            // Step 7: Signal space available
            var signalResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Then",
                step = "the 'reader' process signals space available"
            });
            LogStepResult(signalResult, "signal space available");
            signalResult.Success.Should().BeTrue();

            _output.WriteLine("Complete Test 1.1 scenario executed successfully!");
        }

        [Fact]
        public async Task ExecuteStep_InvalidStep_ShouldReturnError()
        {
            // Arrange
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "reader",
                platform = "cpp",
                scenario = "TestInvalidStep",
                testRunId = Guid.NewGuid().ToString()
            });

            // Act - Try to execute non-existent step
            var result = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Given",
                step = "this step definitely does not exist in C++"
            });

            // Assert and log
            result.Should().NotBeNull();
            LogStepResult(result, "invalid step (expected to fail)");
            result.Success.Should().BeFalse("invalid step should fail");
            result.Error.Should().NotBeNull("error message should be provided");
        }

        [Fact]
        public async Task Cleanup_ShouldSucceed()
        {
            // Initialize and run some steps first
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "reader",
                platform = "cpp",
                scenario = "TestCleanup",
                testRunId = Guid.NewGuid().ToString()
            });

            // Act - Call cleanup
            await _jsonRpc!.InvokeAsync("cleanup");

            // Assert - Should not throw
            _output.WriteLine("Cleanup completed successfully");
        }
        
        [Fact]
        public async Task VerifyHarmonyStepFormat_ShouldIncludeFullText()
        {
            // This test verifies what format Harmony actually sends for steps
            // The hypothesis is that Harmony sends the full text, not ProcessedText
            
            // Arrange - Initialize first
            await _jsonRpc!.InvokeAsync<bool>("initialize", new
            {
                role = "reader",
                platform = "cpp",
                scenario = "TestStepFormat",
                testRunId = Guid.NewGuid().ToString()
            });

            // Test 1: What format does Harmony actually use for the first Test 1.1 step?
            // Based on the generated test JSON, the "Text" field contains:
            // "the 'reader' process creates buffer 'test-basic' with metadata size '1024' and payload size '10240'"
            
            // Try the full text format (what we expect Harmony to send)
            var fullTextResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Given",
                step = "the 'reader' process creates buffer 'test-basic' with metadata size '1024' and payload size '10240'"
            });
            
            // Log result
            _output.WriteLine($"Full text format result: Success={fullTextResult.Success}");
            if (!string.IsNullOrEmpty(fullTextResult.Error))
            {
                _output.WriteLine($"Full text error: {fullTextResult.Error}");
            }
            
            // Try the ProcessedText format (without process prefix)
            var processedTextResult = await _jsonRpc!.InvokeAsync<ExecutionResult>("executeStep", new
            {
                stepType = "Given", 
                step = "creates buffer 'test-basic-2' with metadata size '1024' and payload size '10240'"
            });
            
            // Log result
            _output.WriteLine($"ProcessedText format result: Success={processedTextResult.Success}");
            if (!string.IsNullOrEmpty(processedTextResult.Error))
            {
                _output.WriteLine($"ProcessedText error: {processedTextResult.Error}");
            }
            
            // Assert - We expect the full text format to work
            fullTextResult.Success.Should().BeTrue("Harmony should send the full text, not ProcessedText");
            
            // And the ProcessedText format should fail (no matching pattern)
            processedTextResult.Success.Should().BeFalse("ProcessedText format should not match our patterns");
        }
    }

    public class ExecutionResult
    {
        public bool Success { get; init; }
        public TimeSpan Duration { get; init; }
        public string? Error { get; init; }
        public Exception? Exception { get; init; }
        public List<LogEntry> Logs { get; init; } = new();
        public Dictionary<string, object>? Data { get; init; }
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