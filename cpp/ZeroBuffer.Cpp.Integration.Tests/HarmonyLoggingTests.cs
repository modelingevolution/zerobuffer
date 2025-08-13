using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using StreamJsonRpc;
using ModelingEvolution.Harmony.Shared;

namespace ZeroBuffer.Cpp.Integration.Tests
{
    /// <summary>
    /// Tests that verify C++ logging works correctly through the Harmony protocol
    /// These tests use the same logger as production code and verify logs are collected
    /// </summary>
    public class HarmonyLoggingTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _cppProcess;
        private JsonRpc? _jsonRpc;
        private HeaderDelimitedMessageHandler? _messageHandler;
        
        // Note: We use StepRequest and StepResponse from ModelingEvolution.Harmony.Shared

        public HarmonyLoggingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _output.WriteLine("Starting C++ zerobuffer-serve process for Harmony logging test...");
            
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
            
            // Set environment to use DEBUG level logging so we can verify logs
            psi.Environment["ZEROBUFFER_LOG_LEVEL"] = "DEBUG";

            _cppProcess = Process.Start(psi);
            if (_cppProcess == null)
            {
                throw new InvalidOperationException("Failed to start C++ process");
            }

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

            // Setup JSON-RPC communication
            _messageHandler = new HeaderDelimitedMessageHandler(_cppProcess.StandardInput.BaseStream, _cppProcess.StandardOutput.BaseStream);
            _jsonRpc = new JsonRpc(_messageHandler);
            _jsonRpc.StartListening();

            // Wait for process to start
            await Task.Delay(500);
            
            // Verify it's ready
            var health = await _jsonRpc.InvokeAsync<bool>("health");
            health.Should().BeTrue("C++ serve should be healthy");
            
            // Initialize the process for testing
            var initParams = new
            {
                role = "reader",
                platform = "cpp",
                scenario = "Logging Test",
                testRunId = Guid.NewGuid().ToString(),
                hostPid = Process.GetCurrentProcess().Id,
                featureId = 1
            };
            
            var initResult = await _jsonRpc.InvokeAsync<bool>("initialize", initParams);
            initResult.Should().BeTrue("Initialize should succeed");
            
            _output.WriteLine("C++ serve initialized and ready for testing");
        }

        public async Task DisposeAsync()
        {
            if (_jsonRpc != null)
            {
                try
                {
                    await _jsonRpc.InvokeAsync("cleanup");
                }
                catch { }
                
                _jsonRpc.Dispose();
            }
            
            _messageHandler?.Dispose();
            
            if (_cppProcess != null && !_cppProcess.HasExited)
            {
                _cppProcess.Kill();
                await _cppProcess.WaitForExitAsync();
                _cppProcess.Dispose();
            }
        }

        [Fact]
        public async Task ExecuteStep_AllProcessesReady_ShouldReturnDebugLogs()
        {
            // Arrange
            var stepText = "all processes are ready";
            _output.WriteLine($"Executing step: {stepText}");
            var request = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: stepText,
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            
            // Act
            var response = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", request);
            
            // Assert
            _output.WriteLine($"Response: {response}");
            
            // Check success
            Assert.True(response.Success, "Step should execute successfully");
            
            // Verify we have logs
            Assert.NotNull(response.Logs);
            _output.WriteLine($"Found {response.Logs.Count} log messages:");
            foreach (var log in response.Logs)
            {
                _output.WriteLine($"  [{log.Level}] {log.Message}");
            }

            // Should contain the debug log from the step
            response.Logs.Should().Contain(log => log.Message.Contains("All processes ready"), 
                "Step should log 'All processes ready'");
        }

        

        [Fact]
        public async Task ExecuteStep_MultipleSteps_ShouldAccumulateLogsCorrectly()
        {
            // Execute multiple steps and verify logs are properly cleared between calls
            
            // First step
            var request1 = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the test environment is initialized",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            var response1 = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", request1);
            Assert.NotNull(response1.Logs);
            var count1 = response1.Logs.Count;
            _output.WriteLine($"First step produced {count1} logs");
            
            // Second step
            var request2 = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "all processes are ready",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            var response2 = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", request2);
            Assert.NotNull(response2.Logs);
            var count2 = response2.Logs.Count;
            _output.WriteLine($"Second step produced {count2} logs");
            
            // Logs should be independent (not accumulated from previous call)
            count2.Should().BeLessThanOrEqualTo(10, "Second step should not accumulate logs from first step");
            
            // Each step should have its own logs
            var messages2 = response2.Logs.Select(log => log.Message).ToList();
            messages2.Should().Contain(msg => msg.Contains("All processes ready") || msg.Contains("ready"),
                "Second step should have its own specific logs");
        }

        [Fact]
        public async Task ExecuteStep_WithError_ShouldReturnErrorLogs()
        {
            // Arrange - use an invalid step that will cause an error
            var stepText = "this step does not exist and will fail";
            _output.WriteLine($"Executing invalid step: {stepText}");
            
            // Act
            var request = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: stepText,
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            var response = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", request);
            
            // Assert
            _output.WriteLine($"Response: {response}");
            
            // Check failure
            Assert.False(response.Success, "Step should fail");
            
            // Check logs were still returned (error logs)
            Assert.NotNull(response.Logs);
            
            // Should have error message
            response.Error.Should().NotBeNullOrEmpty("Should have error message");
            response.Error.Should().Contain("Step execution failed", "Should indicate step execution failed");
            
            _output.WriteLine($"Error message: {response.Error}");
            
            if (response.Logs.Count > 0)
            {
                _output.WriteLine($"Found {response.Logs.Count} log messages during error:");
                foreach (var log in response.Logs)
                {
                    _output.WriteLine($"  [{log.Level}] {log.Message}");
                }
            }
        }

        [Fact]
        public async Task ExecuteStep_ShouldReturnLogsWithCorrectSeverityLevels()
        {
            // Arrange
            var stepText = "all processes are ready";
            var request = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: stepText,
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );
            
            // Act
            var response = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", request);
            
            // Assert
            Assert.NotNull(response.Logs);
            Assert.NotEmpty(response.Logs);
            
            // Check that logs have proper severity levels (using LogLevel enum)
            foreach (var log in response.Logs)
            {
                // LogLevel is an enum from Microsoft.Extensions.Logging
                log.Level.Should().BeOneOf(
                    LogLevel.Trace,
                    LogLevel.Debug,
                    LogLevel.Information,
                    LogLevel.Warning,
                    LogLevel.Error,
                    LogLevel.Critical);
                
                _output.WriteLine($"[{log.Level}] {log.Message}");
            }
            
            // Should have at least one Debug level log from our step
            var hasDebugLog = response.Logs.Any(log => log.Level == LogLevel.Debug);
            hasDebugLog.Should().BeTrue("Should have at least one DEBUG level log");
        }
    }
}