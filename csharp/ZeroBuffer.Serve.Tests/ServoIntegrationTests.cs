using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Shared;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace ZeroBuffer.Serve.Tests
{
    /// <summary>
    /// Integration tests for C# ZeroBuffer.Serve using the Harmony.Shared library.
    /// Verifies all IServoClient methods work correctly.
    /// </summary>
    public class ServoIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _serveProcess;
        private JsonRpc? _jsonRpc;
        private IServoClient? _servoClient;
        private readonly List<string> _serveErrors = new();

        public ServoIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _output.WriteLine("Starting C# ZeroBuffer.Serve process...");
            
            // Find the serve executable
            var serveRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ZeroBuffer.Serve"));
            var servePath = Path.Combine(serveRoot, "bin", "Debug", "net9.0", "ZeroBuffer.Serve");
            
            if (!File.Exists(servePath))
            {
                _output.WriteLine($"Serve executable not found at: {servePath}");
                _output.WriteLine("Building serve project...");
                
                var buildProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{Path.Combine(serveRoot, "ZeroBuffer.Serve.csproj")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                
                await buildProcess!.WaitForExitAsync();
                
                if (buildProcess.ExitCode != 0)
                {
                    var error = await buildProcess.StandardError.ReadToEndAsync();
                    throw new Exception($"Failed to build serve: {error}");
                }
            }
            
            _output.WriteLine($"Serve executable: {servePath}");
            
            // Start serve process
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

            _serveProcess = Process.Start(psi);
            if (_serveProcess == null)
            {
                throw new InvalidOperationException("Failed to start serve process");
            }

            // Capture stderr in background
            _ = Task.Run(async () =>
            {
                var reader = _serveProcess.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    _serveErrors.Add(line);
                    if (line.Contains("ERROR") || line.Contains("FATAL"))
                    {
                        _output.WriteLine($"[SERVE ERROR] {line}");
                    }
                }
            });

            // Create JSON-RPC with Newtonsoft.Json formatter (compatible with Harmony.Shared)
            // NOTE: HeaderDelimitedMessageHandler constructor takes (sendingStream, receivingStream)
            // So we need to pass StandardInput first (for sending), then StandardOutput (for receiving)
            var formatter = new JsonMessageFormatter();
            var handler = new HeaderDelimitedMessageHandler(
                _serveProcess.StandardInput.BaseStream,   // sending stream
                _serveProcess.StandardOutput.BaseStream,  // receiving stream
                formatter);
            
            _jsonRpc = new JsonRpc(handler);
            _jsonRpc.StartListening();
            
            // Create strongly-typed servo client using Harmony.Shared
            _servoClient = new JsonRpcServoClient(_jsonRpc, "csharp-serve");
            
            // Give the process time to initialize
            await Task.Delay(500);
            _output.WriteLine("C# serve process started successfully");
        }

        public async Task DisposeAsync()
        {
            try
            {
                // Try graceful shutdown first
                if (_servoClient != null && _serveProcess != null && !_serveProcess.HasExited)
                {
                    try
                    {
                        await _servoClient.ShutdownAsync();
                        await Task.Delay(100);
                    }
                    catch
                    {
                        // Ignore shutdown errors
                    }
                }
            }
            finally
            {
                if (_servoClient is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                else if (_jsonRpc != null)
                {
                    _jsonRpc.Dispose();
                }

                if (_serveProcess != null && !_serveProcess.HasExited)
                {
                    _serveProcess.Kill();
                    await _serveProcess.WaitForExitAsync();
                    _serveProcess.Dispose();
                }

                // Output serve errors for debugging
                if (_serveErrors.Any())
                {
                    _output.WriteLine("Serve stderr output:");
                    foreach (var error in _serveErrors.TakeLast(20))
                    {
                        _output.WriteLine($"  {error}");
                    }
                }
            }
        }

        private void LogStepResult(StepResponse result, string stepName)
        {
            _output.WriteLine($"Step '{stepName}' result: {(result.Success ? "SUCCESS" : "FAILED")}");
            
            if (!string.IsNullOrEmpty(result.Error))
            {
                _output.WriteLine($"  Error: {result.Error}");
            }
            
            // Log execution logs
            if (result.Logs != null && result.Logs.Count > 0)
            {
                _output.WriteLine($"  Execution logs ({result.Logs.Count} entries):");
                foreach (var log in result.Logs)
                {
                    _output.WriteLine($"    [{log.Level}] {log.Message}");
                }
            }
            
            // Log context data if present
            if (result.Context != null && result.Context.Count > 0)
            {
                _output.WriteLine($"  Context data:");
                foreach (var kvp in result.Context)
                {
                    _output.WriteLine($"    {kvp.Key}: {kvp.Value}");
                }
            }
        }

        [Fact]
        public async Task HealthCheck_ShouldReturnTrue()
        {
            // Act
            var result = await _servoClient!.HealthAsync();

            // Assert
            result.Should().BeTrue("health check should always return true");
            _output.WriteLine("✓ Health check passed");
        }

        [Fact]
        public async Task Initialize_ShouldAcceptValidParameters_AndReturnTrue()
        {
            // Arrange
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "csharp",
                Scenario: "Test_1_1_SimpleWriteRead",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 42
            );

            // Act
            var result = await _servoClient!.InitializeAsync(initRequest);

            // Assert
            result.Should().BeTrue("initialization should succeed with valid parameters");
            _output.WriteLine($"✓ Initialization succeeded - Role: {initRequest.Role}, Platform: {initRequest.Platform}");
            _output.WriteLine($"  TestRunId: {initRequest.TestRunId}");
        }

        [Fact]
        public async Task Discover_ShouldReturnStepDefinitions()
        {
            // Act
            var result = await _servoClient!.DiscoverAsync();

            // Assert
            result.Should().NotBeNull();
            result.Steps.Should().NotBeNull();
            
            _output.WriteLine($"✓ Discovered {result.Steps.Count} step definitions:");
            
            if (result.Steps.Count == 0)
            {
                _output.WriteLine("  WARNING: No steps found! Step registration may be broken.");
            }
            else
            {
                // Log first 10 steps as sample
                foreach (var step in result.Steps)
                {
                    _output.WriteLine($"  [{step.Type}] {step.Pattern}");
                }
               
            }

            // Verify we have expected step types
            result.Steps.Should().Contain(s => s.Type == "Given", "should have Given steps");
            result.Steps.Should().Contain(s => s.Type == "When", "should have When steps");
            result.Steps.Should().Contain(s => s.Type == "Then", "should have Then steps");
        }

        [Fact]
        public async Task ExecuteStep_CreateBuffer_ShouldSucceed()
        {
            // Arrange - Initialize first
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "csharp",
                Scenario: "TestCreateBuffer",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 100
            );
            await _servoClient!.InitializeAsync(initRequest);

            // Act - Create buffer using Harmony.Shared types
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'test-buffer' with metadata size '1024' and payload size '10240'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "reader")
                    .Add("buffer_name", "test-buffer")
                    .Add("metadata_size", "1024")
                    .Add("payload_size", "10240"),
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );

            var result = await _servoClient.ExecuteStepAsync(stepRequest);

            // Assert and log
            result.Should().NotBeNull();
            LogStepResult(result, "create buffer");
            result.Success.Should().BeTrue($"create buffer step should succeed. Error: {result.Error}");
        }

        [Fact]
        public async Task ExecuteStep_CompleteTest11Scenario_ShouldSucceed()
        {
            // This tests the complete Test 1.1 scenario
            
            // Initialize
            var initRequest = new InitializeRequest(
                Role: "both",
                Platform: "csharp",
                Scenario: "Test_1_1_SimpleWriteRead",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 11
            );
            var initResult = await _servoClient!.InitializeAsync(initRequest);
            initResult.Should().BeTrue("initialization should succeed");
            _output.WriteLine("✓ Initialized for Test 1.1 scenario");

            // Step 1: Create buffer
            var createBufferRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'test-1-1' with metadata size '1024' and payload size '10240'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "reader")
                    .Add("buffer_name", "test-1-1")
                    .Add("metadata_size", "1024")
                    .Add("payload_size", "10240"),
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            var createResult = await _servoClient.ExecuteStepAsync(createBufferRequest);
            LogStepResult(createResult, "create buffer");
            createResult.Success.Should().BeTrue("create buffer should succeed");

            // Step 2: Connect writer
            var connectRequest = new StepRequest(
                Process: "writer",
                StepType: StepType.When,
                Step: "the 'writer' process connects to buffer 'test-1-1'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "writer")
                    .Add("buffer_name", "test-1-1"),
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            var connectResult = await _servoClient.ExecuteStepAsync(connectRequest);
            LogStepResult(connectResult, "connect writer");
            connectResult.Success.Should().BeTrue("connect writer should succeed");

            // Step 3: Write metadata
            var writeMetadataRequest = new StepRequest(
                Process: "writer",
                StepType: StepType.When,
                Step: "the 'writer' process writes metadata with size '100'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "writer")
                    .Add("size", "100"),
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            var writeMetadataResult = await _servoClient.ExecuteStepAsync(writeMetadataRequest);
            LogStepResult(writeMetadataResult, "write metadata");
            writeMetadataResult.Success.Should().BeTrue("write metadata should succeed");

            // Step 4: Write frame
            var writeFrameRequest = new StepRequest(
                Process: "writer",
                StepType: StepType.When,
                Step: "the 'writer' process writes frame with size '1024' and sequence '1'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "writer")
                    .Add("size", "1024")
                    .Add("sequence", "1"),
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            var writeFrameResult = await _servoClient.ExecuteStepAsync(writeFrameRequest);
            LogStepResult(writeFrameResult, "write frame");
            writeFrameResult.Success.Should().BeTrue("write frame should succeed");

            // Step 5: Read frame
            var readFrameRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Then,
                Step: "the 'reader' process should read frame with sequence '1' and size '1024'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "reader")
                    .Add("sequence", "1")
                    .Add("size", "1024"),
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            var readFrameResult = await _servoClient.ExecuteStepAsync(readFrameRequest);
            LogStepResult(readFrameResult, "read frame");
            readFrameResult.Success.Should().BeTrue("read frame should succeed");

            _output.WriteLine("✓ Complete Test 1.1 scenario executed successfully!");
        }

        [Fact]
        public async Task ExecuteStep_InvalidStep_ShouldReturnError()
        {
            // Arrange
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "csharp",
                Scenario: "TestInvalidStep",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 999
            );
            await _servoClient!.InitializeAsync(initRequest);

            // Act - Try to execute non-existent step
            var invalidStepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process does something that definitely does not exist",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            
            var result = await _servoClient.ExecuteStepAsync(invalidStepRequest);

            // Assert and log
            result.Should().NotBeNull();
            LogStepResult(result, "invalid step (expected to fail)");
            result.Success.Should().BeFalse("invalid step should fail");
            result.Error.Should().NotBeNullOrEmpty("error message should be provided");
            result.Error.Should().Contain("No matching step definition found", "error should indicate step not found");
        }

        [Fact]
        public async Task Cleanup_AfterOperations_ShouldSucceed()
        {
            // Arrange - Initialize and execute some steps
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "csharp",
                Scenario: "TestCleanup",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 200
            );
            await _servoClient!.InitializeAsync(initRequest);

            // Create a buffer
            var createRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'cleanup-test' with metadata size '1024' and payload size '10240'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "reader")
                    .Add("buffer_name", "cleanup-test")
                    .Add("metadata_size", "1024")
                    .Add("payload_size", "10240"),
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            await _servoClient.ExecuteStepAsync(createRequest);

            // Act - Call cleanup
            await _servoClient.CleanupAsync();

            // Assert - Should not throw
            _output.WriteLine("✓ Cleanup completed successfully");

            // Verify we can still use the servo after cleanup
            var healthAfterCleanup = await _servoClient.HealthAsync();
            healthAfterCleanup.Should().BeTrue("servo should still be healthy after cleanup");
            _output.WriteLine("✓ Servo still healthy after cleanup");
        }

        [Fact]
        public async Task MultipleInitializations_ShouldSucceed()
        {
            // Test multiple initializations with different scenarios
            
            for (int i = 1; i <= 3; i++)
            {
                var initRequest = new InitializeRequest(
                    Role: i % 2 == 0 ? "writer" : "reader",
                    Platform: "csharp",
                    Scenario: $"Test_Scenario_{i}",
                    HostPid: Process.GetCurrentProcess().Id,
                    FeatureId: i * 10
                );
                
                var result = await _servoClient!.InitializeAsync(initRequest);
                
                result.Should().BeTrue($"initialization #{i} should succeed");
                _output.WriteLine($"✓ Initialize #{i} succeeded - Role: {initRequest.Role}, Scenario: {initRequest.Scenario}");
                
                // Execute a simple step to verify initialization worked
                var stepRequest = new StepRequest(
                    Process: initRequest.Role,
                    StepType: StepType.Given,
                    Step: $"the '{initRequest.Role}' process creates buffer 'test-{i}' with metadata size '1024' and payload size '{10240 * i}'",
                    Parameters: ImmutableDictionary<string, string>.Empty
                        .Add("process", initRequest.Role)
                        .Add("buffer_name", $"test-{i}")
                        .Add("metadata_size", "1024")
                        .Add("payload_size", $"{10240 * i}"),
                    Context: ImmutableDictionary<string, string>.Empty,
                    IsBroadcast: false
                );
                
                var stepResult = await _servoClient.ExecuteStepAsync(stepRequest);
                stepResult.Success.Should().BeTrue($"step after initialization #{i} should succeed");
                
                // Cleanup between iterations
                await _servoClient.CleanupAsync();
            }
            
            _output.WriteLine("✓ All multiple initializations succeeded");
        }

        [Fact]
        public async Task ExecuteStep_WithContext_ShouldPassContextToStep()
        {
            // Arrange
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "csharp",
                Scenario: "TestContext",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 300
            );
            await _servoClient!.InitializeAsync(initRequest);

            // Act - Execute step with context
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'context-test' with metadata size '2048' and payload size '20480'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "reader")
                    .Add("buffer_name", "context-test")
                    .Add("metadata_size", "2048")
                    .Add("payload_size", "20480"),
                Context: ImmutableDictionary<string, string>.Empty
                    .Add("test_id", "context-123")
                    .Add("iteration", "5")
                    .Add("timestamp", DateTime.UtcNow.ToString("O")),
                IsBroadcast: false
            );

            var result = await _servoClient.ExecuteStepAsync(stepRequest);

            // Assert
            result.Should().NotBeNull();
            LogStepResult(result, "step with context");
            result.Success.Should().BeTrue("step with context should succeed");
            
            // The step might return context data
            if (result.Context != null && result.Context.Count > 0)
            {
                _output.WriteLine("✓ Step returned context data");
            }
        }

        [Fact]
        public async Task CompleteLifecycle_AllMethods_ShouldWork()
        {
            // Test complete lifecycle of all IServoClient methods
            
            // 1. Health check
            var health1 = await _servoClient!.HealthAsync();
            health1.Should().BeTrue();
            _output.WriteLine("✓ 1. Initial health check passed");

            // 2. Initialize
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "csharp",
                Scenario: "LifecycleTest",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 500
            );
            var initResult = await _servoClient.InitializeAsync(initRequest);
            initResult.Should().BeTrue();
            _output.WriteLine("✓ 2. Initialize succeeded");

            // 3. Discover
            var discoverResult = await _servoClient.DiscoverAsync();
            discoverResult.Steps.Count.Should().BeGreaterThan(0);
            _output.WriteLine($"✓ 3. Discover found {discoverResult.Steps.Count} steps");

            // 4. Execute multiple steps
            for (int i = 1; i <= 3; i++)
            {
                var stepRequest = new StepRequest(
                    Process: "reader",
                    StepType: StepType.Given,
                    Step: $"the 'reader' process creates buffer 'lifecycle-{i}' with metadata size '512' and payload size '{512 * i}'",
                    Parameters: ImmutableDictionary<string, string>.Empty
                        .Add("process", "reader")
                        .Add("buffer_name", $"lifecycle-{i}")
                        .Add("metadata_size", "512")
                        .Add("payload_size", $"{512 * i}"),
                    Context: ImmutableDictionary<string, string>.Empty,
                    IsBroadcast: false
                );
                
                var stepResult = await _servoClient.ExecuteStepAsync(stepRequest);
                stepResult.Success.Should().BeTrue();
                _output.WriteLine($"✓ 4.{i}. Execute step {i} succeeded");
            }

            // 5. Health check after operations
            var health2 = await _servoClient.HealthAsync();
            health2.Should().BeTrue();
            _output.WriteLine("✓ 5. Health check after operations passed");

            // 6. Cleanup
            await _servoClient.CleanupAsync();
            _output.WriteLine("✓ 6. Cleanup succeeded");

            // 7. Health check after cleanup
            var health3 = await _servoClient.HealthAsync();
            health3.Should().BeTrue();
            _output.WriteLine("✓ 7. Health check after cleanup passed");

            // 8. Can still execute steps after cleanup
            var postCleanupRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'post-cleanup' with metadata size '256' and payload size '2560'",
                Parameters: ImmutableDictionary<string, string>.Empty
                    .Add("process", "reader")
                    .Add("buffer_name", "post-cleanup")
                    .Add("metadata_size", "256")
                    .Add("payload_size", "2560"),
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            var postCleanupResult = await _servoClient.ExecuteStepAsync(postCleanupRequest);
            postCleanupResult.Success.Should().BeTrue();
            _output.WriteLine("✓ 8. Can execute steps after cleanup");

            // 9. Shutdown will be called in DisposeAsync
            _output.WriteLine("✓ Complete lifecycle test passed!");
        }
    }
}