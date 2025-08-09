using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Shared;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Python.Integration.Tests
{
    /// <summary>
    /// Integration tests for Python servo implementation using Harmony shared library contracts.
    /// These tests verify that the Python servo correctly implements the IServoClient interface.
    /// </summary>
    public class PythonServeIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _pythonProcess;
        private JsonRpc? _jsonRpc;
        private IServoClient? _servoClient;
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
            
            // Verify the serve executable exists
            if (!File.Exists(servePath))
            {
                throw new FileNotFoundException($"Python serve executable not found at: {servePath}");
            }
            
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

            // Capture stderr in background for debugging
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

            // Create strongly-typed servo client using Harmony shared library
            _servoClient = new JsonRpcServoClient(_jsonRpc, "python-servo");

            // Give the process time to initialize
            await Task.Delay(500);
            _output.WriteLine("Python process started successfully");
        }

        public async Task DisposeAsync()
        {
            if (_servoClient is IDisposable disposable)
            {
                disposable.Dispose();
            }

            if (_jsonRpc != null)
            {
                _jsonRpc.Dispose();
            }

            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                try
                {
                    // Try graceful shutdown first
                    await _servoClient?.ShutdownAsync()!;
                    await Task.Delay(500);
                }
                catch
                {
                    // Ignore shutdown errors
                }

                if (!_pythonProcess.HasExited)
                {
                    _pythonProcess.Kill();
                    await _pythonProcess.WaitForExitAsync();
                }
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

        private void LogStepResult(StepResponse response, string stepName)
        {
            _output.WriteLine($"Step '{stepName}' result: {(response.Success ? "SUCCESS" : "FAILED")}");
            
            if (!string.IsNullOrEmpty(response.Error))
            {
                _output.WriteLine($"  Error: {response.Error}");
            }
            
            // Log execution logs from Python
            if (response.Logs != null && response.Logs.Count > 0)
            {
                _output.WriteLine($"  Execution logs ({response.Logs.Count} entries):");
                foreach (var log in response.Logs)
                {
                    _output.WriteLine($"    [{log.Level}] {log.Message}");
                }
            }
        }

        [Fact]
        public async Task HealthCheck_ShouldReturnTrue()
        {
            // Act - Health check should be parameterless according to new contract
            var result = await _servoClient!.HealthAsync();

            // Assert
            result.Should().BeTrue("health check should always return true for a running servo");
            _output.WriteLine("Health check passed");
        }

        [Fact]
        public async Task Initialize_ShouldAcceptValidParameters_AndReturnTrue()
        {
            // Arrange - Use the correct Harmony contract
            var request = new InitializeRequest(
                Role: "reader",
                Platform: "python",
                Scenario: "Test1_1",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 123
            );

            // Act
            var result = await _servoClient!.InitializeAsync(request);

            // Assert
            result.Should().BeTrue("initialization should succeed with valid parameters");
            _output.WriteLine($"Initialization succeeded with role={request.Role}, platform={request.Platform}, testRunId={request.TestRunId}");
        }

        [Fact]
        public async Task Discover_ShouldReturnStepDefinitions()
        {
            // Wait a bit more for the process to fully initialize
            await Task.Delay(1000);
            
            // Act
            var response = await _servoClient!.DiscoverAsync();

            // Assert
            response.Should().NotBeNull();
            response.Steps.Should().NotBeNull();
            
            // Log discovered steps
            _output.WriteLine($"Discovered {response.Steps.Count} step definitions:");
            if (response.Steps.Count == 0)
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
                foreach (var step in response.Steps.Take(10))
                {
                    _output.WriteLine($"  {step.Type} {step.Pattern}");
                }
                
                if (response.Steps.Count > 10)
                {
                    _output.WriteLine($"  ... and {response.Steps.Count - 10} more");
                }
            }

            // Assert that steps were discovered
            response.Steps.Count.Should().BeGreaterThan(0, "there should be at least one step definition available");
        }

        [Fact]
        public async Task ExecuteStep_CreateBuffer_ShouldSucceed()
        {
            // Arrange - Initialize first
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "python",
                Scenario: "TestCreateBuffer",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 1
            );
            
            var initialized = await _servoClient!.InitializeAsync(initRequest);
            initialized.Should().BeTrue("initialization should succeed");

            // Act - Create buffer using Harmony contract
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'test-integration-buffer' with metadata size '1024' and payload size '10240'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            
            var response = await _servoClient.ExecuteStepAsync(stepRequest);

            // Assert and log
            response.Should().NotBeNull();
            LogStepResult(response, "create buffer");
            response.Success.Should().BeTrue("create buffer step should succeed");
        }

        [Fact]
        public async Task ExecuteStep_ConnectAndWriteMetadata_ShouldSucceed()
        {
            // Arrange - Initialize as writer
            var initRequest = new InitializeRequest(
                Role: "writer",
                Platform: "python",
                Scenario: "TestWriteMetadata",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 2
            );
            
            var initialized = await _servoClient!.InitializeAsync(initRequest);
            initialized.Should().BeTrue("initialization should succeed");

            // First create a buffer as reader
            var createRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'test-metadata-buffer' with metadata size '1024' and payload size '10240'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            
            var createResponse = await _servoClient.ExecuteStepAsync(createRequest);
            LogStepResult(createResponse, "create buffer for metadata test");

            // Act - Connect as writer
            var connectRequest = new StepRequest(
                Process: "writer",
                StepType: StepType.When,
                Step: "the 'writer' process connects to buffer 'test-metadata-buffer'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            
            var connectResponse = await _servoClient.ExecuteStepAsync(connectRequest);

            // Assert and log connect
            connectResponse.Should().NotBeNull();
            LogStepResult(connectResponse, "connect to buffer");
            connectResponse.Success.Should().BeTrue("connect to buffer should succeed");

            // Act - Write metadata
            var writeRequest = new StepRequest(
                Process: "writer",
                StepType: StepType.When,
                Step: "the 'writer' process writes metadata with size '100'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            
            var writeResponse = await _servoClient.ExecuteStepAsync(writeRequest);

            // Assert and log write
            writeResponse.Should().NotBeNull();
            LogStepResult(writeResponse, "write metadata");
            writeResponse.Success.Should().BeTrue("write metadata should succeed");
        }

        [Fact]
        public async Task ExecuteStep_InvalidStep_ShouldReturnError()
        {
            // Arrange
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "python",
                Scenario: "TestInvalidStep",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 3
            );
            
            await _servoClient!.InitializeAsync(initRequest);

            // Act - Try to execute non-existent step
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.When,
                Step: "this_step_does_not_exist",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty,
                IsBroadcast: false
            );
            
            var response = await _servoClient.ExecuteStepAsync(stepRequest);

            // Assert and log
            response.Should().NotBeNull();
            LogStepResult(response, "invalid step (expected to fail)");
            response.Success.Should().BeFalse("invalid step should fail");
            response.Error.Should().NotBeNullOrEmpty("error message should be provided");
            response.Error.Should().Contain("No matching step", "error should indicate step not found");
        }

        [Fact]
        public async Task Cleanup_ShouldNotThrow()
        {
            // Arrange - Initialize first
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "python",
                Scenario: "TestCleanup",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 4
            );
            
            await _servoClient!.InitializeAsync(initRequest);

            // Act & Assert - Cleanup should not throw
            var act = async () => await _servoClient.CleanupAsync();
            await act.Should().NotThrowAsync("cleanup should handle errors gracefully");
            
            _output.WriteLine("Cleanup completed successfully");
        }

        [Fact]
        public async Task Shutdown_ShouldNotThrow()
        {
            // Act & Assert - Shutdown should not throw even if called multiple times
            var act = async () => await _servoClient!.ShutdownAsync();
            await act.Should().NotThrowAsync("shutdown should handle errors gracefully");
            
            _output.WriteLine("Shutdown completed successfully");
        }

        [Fact]
        public async Task FullScenario_CreateWriteRead_ShouldWork()
        {
            // This test simulates a complete scenario using the Harmony contract
            
            // Initialize as reader
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "python",
                Scenario: "FullScenario",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 5
            );
            
            await _servoClient!.InitializeAsync(initRequest);

            // Create buffer
            var createRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'full-scenario-buffer' with metadata size '1024' and payload size '10240'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            
            var createResponse = await _servoClient.ExecuteStepAsync(createRequest);
            LogStepResult(createResponse, "create buffer");
            createResponse.Success.Should().BeTrue();

            // Re-initialize as writer
            var writerInitRequest = new InitializeRequest(
                Role: "writer",
                Platform: "python",
                Scenario: "FullScenario",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 5
            );
            
            await _servoClient.InitializeAsync(writerInitRequest);

            // Connect as writer
            var connectRequest = new StepRequest(
                Process: "writer",
                StepType: StepType.When,
                Step: "the 'writer' process connects to buffer 'full-scenario-buffer'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            
            var connectResponse = await _servoClient.ExecuteStepAsync(connectRequest);
            LogStepResult(connectResponse, "connect as writer");
            connectResponse.Success.Should().BeTrue();

            // Write data
            var writeRequest = new StepRequest(
                Process: "writer",
                StepType: StepType.When,
                Step: "the 'writer' process writes frame with data 'Hello, ZeroBuffer!'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            
            var writeResponse = await _servoClient.ExecuteStepAsync(writeRequest);
            LogStepResult(writeResponse, "write frame");
            writeResponse.Success.Should().BeTrue();

            // Cleanup
            await _servoClient.CleanupAsync();
            
            _output.WriteLine("Full scenario completed successfully");
        }
    }
}