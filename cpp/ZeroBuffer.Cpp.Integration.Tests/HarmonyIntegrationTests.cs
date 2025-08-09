using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using ModelingEvolution.Harmony.Shared;
using Microsoft.Extensions.Logging;

namespace ZeroBuffer.Cpp.Integration.Tests
{
    /// <summary>
    /// Integration tests for C++ servo using the shared Harmony contracts.
    /// This demonstrates that the C++ servo is compatible with the Harmony framework.
    /// </summary>
    public class HarmonyIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _cppProcess;
        private JsonRpc? _jsonRpc;
        private readonly List<string> _cppErrors = new();

        public HarmonyIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _output.WriteLine("Starting C++ zerobuffer-serve process for Harmony contract testing...");
            
            var cppRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var servePath = Path.Combine(cppRoot, "build", "serve", "zerobuffer-serve");
            servePath = Path.GetFullPath(servePath);

            _output.WriteLine($"Serve executable: {servePath}");
            
            if (!File.Exists(servePath))
            {
                throw new FileNotFoundException($"C++ zerobuffer-serve not found at: {servePath}. Please build the C++ project first.");
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

            _cppProcess = Process.Start(psi);
            if (_cppProcess == null)
            {
                throw new InvalidOperationException("Failed to start C++ process");
            }

            // Capture stderr in background
            _ = Task.Run(async () =>
            {
                var reader = _cppProcess.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    _cppErrors.Add(line);
                    if (line.Contains("ERROR") || line.Contains("FATAL"))
                    {
                        _output.WriteLine($"[C++ ERROR] {line}");
                    }
                }
            });

            // Create JSON-RPC client with HeaderDelimitedMessageHandler for LSP-style communication
            var formatter = new JsonMessageFormatter();
            var handler = new HeaderDelimitedMessageHandler(
                _cppProcess.StandardInput.BaseStream, 
                _cppProcess.StandardOutput.BaseStream, 
                formatter);
            _jsonRpc = new JsonRpc(handler);
            _jsonRpc.StartListening();
            
            _output.WriteLine("C++ process started successfully");
            await Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            try
            {
                if (_jsonRpc != null && !_cppProcess!.HasExited)
                {
                    try
                    {
                        await _jsonRpc.InvokeAsync("shutdown");
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
                _jsonRpc?.Dispose();

                if (_cppProcess != null && !_cppProcess.HasExited)
                {
                    _cppProcess.Kill();
                    await _cppProcess.WaitForExitAsync();
                    _cppProcess.Dispose();
                }

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

        [Fact]
        public async Task Health_UsingSharedContract_ShouldReturnTrue()
        {
            // Act - Using method name directly (health has no parameters)
            var result = await _jsonRpc!.InvokeAsync<bool>(ServoMethods.Health);

            // Assert
            result.Should().BeTrue("health check should always return true");
            _output.WriteLine("Health check using shared contract passed");
        }

        [Fact]
        public async Task Initialize_UsingSharedContract_ShouldSucceed()
        {
            // Arrange - Use the shared InitializeRequest
            var request = new InitializeRequest(
                Role: "reader",
                Platform: "cpp",
                Scenario: "HarmonyTest1",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 42
            );

            // Act - The C++ servo expects the params directly, not wrapped
            var result = await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
                ServoMethods.Initialize, 
                request);

            // Assert
            result.Should().BeTrue("initialization should succeed");
            _output.WriteLine($"Initialization with shared contract succeeded for {request.Role}/{request.Platform}");
        }

        [Fact]
        public async Task Discover_UsingSharedContract_ShouldReturnSteps()
        {
            // Act - Using shared contract's DiscoverResponse
            var response = await _jsonRpc!.InvokeAsync<DiscoverResponse>(ServoMethods.Discover);

            // Assert
            response.Should().NotBeNull();
            response.Steps.Should().NotBeNull();
            response.Steps.Count.Should().BeGreaterThan(0, "should discover step definitions");

            _output.WriteLine($"Discovered {response.Steps.Count} steps using shared contract:");
            foreach (var step in response.Steps.Take(5))
            {
                _output.WriteLine($"  {step.Type}: {step.Pattern}");
            }
        }

        [Fact]
        public async Task ExecuteStep_UsingSharedContract_ShouldWork()
        {
            // Arrange - Initialize first
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "cpp",
                Scenario: "HarmonyStepTest",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 100
            );
            await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(ServoMethods.Initialize, initRequest);

            // Create a StepRequest using the shared contract
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the test environment is initialized",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );

            // Act - Execute step using shared StepRequest/StepResponse
            // Note: The C++ servo expects certain fields, we need to adapt
            var adaptedRequest = new
            {
                stepType = stepRequest.StepType,
                step = stepRequest.Step,
                process = stepRequest.Process,
                parameters = stepRequest.Parameters,
                context = stepRequest.Context
            };

            var jsonResponse = await _jsonRpc!.InvokeWithParameterObjectAsync<Dictionary<string, object>>(
                ServoMethods.ExecuteStep, 
                adaptedRequest);

            // Convert the JSON response to StepResponse
            var response = new StepResponse(
                Success: jsonResponse.ContainsKey("success") && (bool)jsonResponse["success"],
                Error: jsonResponse.ContainsKey("error") ? jsonResponse["error"]?.ToString() : null,
                Context: null,  // C++ doesn't return context
                Logs: null      // Would need to convert logs format
            );

            // Assert
            response.Success.Should().BeTrue("step execution should succeed");
            _output.WriteLine($"Step executed successfully using shared contract: {stepRequest.Step}");
        }

        [Fact]
        public async Task CompleteScenario_UsingSharedContracts_ShouldSucceed()
        {
            // This test demonstrates a complete scenario using shared Harmony contracts

            // 1. Initialize
            var initRequest = new InitializeRequest(
                Role: "both",
                Platform: "cpp",
                Scenario: "CompleteHarmonyScenario",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 200
            );
            
            var initResult = await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
                ServoMethods.Initialize, 
                initRequest);
            initResult.Should().BeTrue();
            _output.WriteLine("✓ Initialized using shared contract");

            // 2. Discover available steps
            var discoverResponse = await _jsonRpc!.InvokeAsync<DiscoverResponse>(ServoMethods.Discover);
            discoverResponse.Steps.Count.Should().BeGreaterThan(0);
            _output.WriteLine($"✓ Discovered {discoverResponse.Steps.Count} steps");

            // 3. Execute a step
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'harmony-test' with metadata size '1024' and payload size '10240'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );

            // Adapt to what C++ expects
            var adaptedStep = new
            {
                stepType = stepRequest.StepType,
                step = stepRequest.Step
            };

            var stepResult = await _jsonRpc!.InvokeWithParameterObjectAsync<Dictionary<string, object>>(
                ServoMethods.ExecuteStep, 
                adaptedStep);
            
            var success = stepResult.ContainsKey("success") && (bool)stepResult["success"];
            success.Should().BeTrue();
            _output.WriteLine("✓ Executed step using shared contract");

            // 4. Cleanup
            await _jsonRpc!.InvokeAsync(ServoMethods.Cleanup);
            _output.WriteLine("✓ Cleanup completed");

            _output.WriteLine("\n✅ Complete scenario using Harmony shared contracts succeeded!");
        }

        [Fact]
        public async Task VerifyContractCompatibility_StepRequest_Format()
        {
            // This test verifies that the C++ servo can handle StepRequest fields
            
            await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(ServoMethods.Initialize, 
                new InitializeRequest("reader", "cpp", "CompatTest", Process.GetCurrentProcess().Id, 300));

            // Test 1: Minimal format (what C++ currently expects)
            var minimalRequest = new { stepType = "Given", step = "the test environment is initialized" };
            var minimalResult = await _jsonRpc!.InvokeWithParameterObjectAsync<Dictionary<string, object>>(
                "executeStep", minimalRequest);
            
            _output.WriteLine($"Minimal format: Success = {minimalResult["success"]}");
            ((bool)minimalResult["success"]).Should().BeTrue("C++ should handle minimal format");

            // Test 2: Full StepRequest format (with all Harmony fields)
            var fullRequest = new
            {
                stepType = "Given",
                step = "the test environment is initialized",
                process = "reader",
                parameters = new Dictionary<string, string>(),
                context = new Dictionary<string, string>(),
                isBroadcast = false
            };
            
            var fullResult = await _jsonRpc!.InvokeWithParameterObjectAsync<Dictionary<string, object>>(
                "executeStep", fullRequest);
            
            _output.WriteLine($"Full format: Success = {fullResult["success"]}");
            ((bool)fullResult["success"]).Should().BeTrue("C++ should handle full Harmony format");

            _output.WriteLine("✓ C++ servo is compatible with both minimal and full StepRequest formats");
        }
    }
}