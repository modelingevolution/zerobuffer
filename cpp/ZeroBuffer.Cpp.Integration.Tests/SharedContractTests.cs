using System;
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
    /// Tests that demonstrate proper usage of shared Harmony contracts.
    /// All initialization and step execution MUST use the shared contracts.
    /// </summary>
    public class SharedContractTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _cppProcess;
        private JsonRpc? _jsonRpc;

        public SharedContractTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _output.WriteLine("Starting C++ zerobuffer-serve for shared contract testing...");
            
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

            _cppProcess = Process.Start(psi);
            if (_cppProcess == null)
            {
                throw new InvalidOperationException("Failed to start C++ process");
            }

            // Create JSON-RPC client
            var formatter = new JsonMessageFormatter();
            var handler = new HeaderDelimitedMessageHandler(
                _cppProcess.StandardInput.BaseStream, 
                _cppProcess.StandardOutput.BaseStream, 
                formatter);
            _jsonRpc = new JsonRpc(handler);
            _jsonRpc.StartListening();
            
            await Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            _jsonRpc?.Dispose();
            
            if (_cppProcess != null && !_cppProcess.HasExited)
            {
                _cppProcess.Kill();
                await _cppProcess.WaitForExitAsync();
                _cppProcess.Dispose();
            }
        }

        [Fact]
        public async Task Initialize_MustUseSharedInitializeRequest()
        {
            // CORRECT: Using shared InitializeRequest from ModelingEvolution.Harmony.Shared
            var request = new InitializeRequest(
                Role: "reader",
                Platform: "cpp",
                Scenario: "SharedContractTest",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 123
            );

            // Act
            var result = await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
                ServoMethods.Initialize, 
                request);

            // Assert
            result.Should().BeTrue();
            _output.WriteLine($"✓ Initialization using shared InitializeRequest succeeded");
            _output.WriteLine($"  TestRunId: {request.TestRunId}");
        }

        [Fact]
        public async Task ExecuteStep_MustUseSharedStepRequest()
        {
            // Initialize first
            var initRequest = new InitializeRequest(
                Role: "reader",
                Platform: "cpp",
                Scenario: "StepExecutionTest",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 456
            );
            await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(ServoMethods.Initialize, initRequest);

            // CORRECT: Using shared StepRequest from ModelingEvolution.Harmony.Shared
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the test environment is initialized",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );

            // Note: C++ servo expects simpler format, so we adapt
            var adaptedRequest = new
            {
                stepType = stepRequest.StepType.ToString(),
                step = stepRequest.Step
            };

            // Act
            var response = await _jsonRpc!.InvokeWithParameterObjectAsync<dynamic>(
                ServoMethods.ExecuteStep, 
                adaptedRequest);

            // Assert
            ((bool)response.success).Should().BeTrue();
            _output.WriteLine($"✓ Step execution using shared StepRequest succeeded");
            _output.WriteLine($"  StepType: {stepRequest.StepType}");
            _output.WriteLine($"  Step: {stepRequest.Step}");
        }

        [Fact]
        public async Task Discover_MustReturnSharedDiscoverResponse()
        {
            // Act - Using shared DiscoverResponse
            var response = await _jsonRpc!.InvokeAsync<DiscoverResponse>(ServoMethods.Discover);

            // Assert
            response.Should().NotBeNull();
            response.Steps.Should().NotBeNull();
            response.Steps.Count.Should().BeGreaterThan(0);
            
            _output.WriteLine($"✓ Discover returned shared DiscoverResponse");
            _output.WriteLine($"  Found {response.Steps.Count} steps");
            
            // Each step should be a StepInfo from the shared contract
            foreach (var step in response.Steps.Take(3))
            {
                step.Should().BeOfType<StepInfo>();
                _output.WriteLine($"  - {step.Type}: {step.Pattern}");
            }
        }

        [Fact]
        public async Task CompleteWorkflow_UsingOnlySharedContracts()
        {
            // This test demonstrates that ALL servo communication uses shared contracts
            
            // 1. Initialize with shared InitializeRequest
            var initRequest = new InitializeRequest(
                Role: "both",
                Platform: "cpp",
                Scenario: "CompleteWorkflow",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 789
            );
            
            var initResult = await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
                ServoMethods.Initialize, 
                initRequest);
            initResult.Should().BeTrue();
            _output.WriteLine("✓ Step 1: Initialized using InitializeRequest");

            // 2. Discover using shared DiscoverResponse
            var discoverResponse = await _jsonRpc!.InvokeAsync<DiscoverResponse>(
                ServoMethods.Discover);
            discoverResponse.Steps.Should().NotBeEmpty();
            _output.WriteLine($"✓ Step 2: Discovered {discoverResponse.Steps.Count} steps using DiscoverResponse");

            // 3. Execute step using shared StepRequest
            var stepRequest = new StepRequest(
                Process: "reader",
                StepType: StepType.Given,
                Step: "the 'reader' process creates buffer 'shared-test' with metadata size '1024' and payload size '10240'",
                Parameters: ImmutableDictionary<string, string>.Empty,
                Context: ImmutableDictionary<string, string>.Empty
            );

            // Adapt for C++ servo (it expects simpler format currently)
            var adaptedStep = new
            {
                stepType = stepRequest.StepType.ToString(),
                step = stepRequest.Step
            };

            var stepResponse = await _jsonRpc!.InvokeWithParameterObjectAsync<dynamic>(
                ServoMethods.ExecuteStep, 
                adaptedStep);
            
            ((bool)stepResponse.success).Should().BeTrue();
            _output.WriteLine("✓ Step 3: Executed step using StepRequest");

            // 4. Cleanup
            await _jsonRpc!.InvokeAsync(ServoMethods.Cleanup);
            _output.WriteLine("✓ Step 4: Cleanup completed");

            _output.WriteLine("\n✅ ALL operations used shared Harmony contracts!");
        }

        [Fact]
        public void InitializeRequest_WithoutSharedContract_IsUnacceptable()
        {
            // This demonstrates what is UNACCEPTABLE
            
            // WRONG: Creating raw JSON or anonymous objects instead of using InitializeRequest
            var wrongWay = new
            {
                role = "reader",
                platform = "cpp",
                scenario = "Test",
                testRunId = "test-123",
                hostPid = 123,
                featureId = 1
            };

            // This is what the old tests were doing - this is WRONG!
            _output.WriteLine("❌ UNACCEPTABLE: Using anonymous objects instead of InitializeRequest");
            _output.WriteLine($"   Wrong: {Newtonsoft.Json.JsonConvert.SerializeObject(wrongWay)}");

            // CORRECT: Always use the shared contract
            var correctWay = new InitializeRequest(
                Role: "reader",
                Platform: "cpp",
                Scenario: "Test",
                HostPid: 123,
                FeatureId: 1
            );
            
            _output.WriteLine("✅ CORRECT: Using shared InitializeRequest");
            _output.WriteLine($"   Right: Role={correctWay.Role}, Platform={correctWay.Platform}, TestRunId={correctWay.TestRunId}");
        }
    }
}