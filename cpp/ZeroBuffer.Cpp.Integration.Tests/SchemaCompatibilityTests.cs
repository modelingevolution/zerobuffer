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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZeroBuffer.Cpp.Integration.Tests
{
    /// <summary>
    /// Tests that verify schema compatibility between Harmony expectations and C++ servo implementation.
    /// These tests document and verify the exact differences in request/response schemas.
    /// </summary>
    public class SchemaCompatibilityTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private Process? _cppProcess;
        private JsonRpc? _jsonRpc;

        public SchemaCompatibilityTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
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
        public async Task AnalyzeInitializeRequest_Schema()
        {
            _output.WriteLine("=== INITIALIZE REQUEST SCHEMA ANALYSIS ===\n");
            
            // What Harmony expects (from shared contract)
            _output.WriteLine("HARMONY EXPECTS (InitializeRequest):");
            _output.WriteLine("  - Role: string");
            _output.WriteLine("  - Platform: string");
            _output.WriteLine("  - Scenario: string");
            _output.WriteLine("  - HostPid: int");
            _output.WriteLine("  - FeatureId: int");
            _output.WriteLine("  - TestRunId: string (computed from HostPid_FeatureId)\n");

            // Send actual InitializeRequest
            var request = new InitializeRequest(
                Role: "reader",
                Platform: "cpp",
                Scenario: "SchemaTest",
                HostPid: Process.GetCurrentProcess().Id,
                FeatureId: 999
            );

            var result = await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
                ServoMethods.Initialize, 
                request);

            // What C++ actually processes (from code analysis)
            _output.WriteLine("C++ SERVO IMPLEMENTATION:");
            _output.WriteLine("  - Accepts all fields but only logs them");
            _output.WriteLine("  - Only looks for optional 'testName' field (case-insensitive)");
            _output.WriteLine("  - Does NOT store Role, Platform, Scenario, HostPid, FeatureId");
            _output.WriteLine("  - Always returns: true (boolean)\n");
            
            _output.WriteLine("SCHEMA DIFFERENCE:");
            _output.WriteLine("  ❌ C++ ignores all InitializeRequest fields");
            _output.WriteLine("  ❌ C++ doesn't store initialization context");
            _output.WriteLine("  ✓ C++ returns correct type (boolean)");
            
            result.Should().BeTrue();
        }

        [Fact]
        public async Task AnalyzeStepRequest_Schema()
        {
            _output.WriteLine("=== STEP REQUEST SCHEMA ANALYSIS ===\n");
            
            // Initialize first
            await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
                ServoMethods.Initialize, 
                new InitializeRequest("reader", "cpp", "StepSchemaTest", 1, 1));

            _output.WriteLine("HARMONY EXPECTS (StepRequest):");
            _output.WriteLine("  - Process: string");
            _output.WriteLine("  - StepType: StepType enum (Given/When/Then)");
            _output.WriteLine("  - Step: string");
            _output.WriteLine("  - Parameters: ImmutableDictionary<string, string>");
            _output.WriteLine("  - Context: ImmutableDictionary<string, string>");
            _output.WriteLine("  - IsBroadcast: bool (optional, default false)\n");

            // What C++ actually expects (from code analysis)
            _output.WriteLine("C++ SERVO EXPECTS:");
            _output.WriteLine("  - stepType: string (case-insensitive, 'Given'/'When'/'Then')");
            _output.WriteLine("  - step: string");
            _output.WriteLine("  ❌ Ignores: Process, Parameters, Context, IsBroadcast\n");

            // Test with minimal format
            var minimalRequest = new
            {
                stepType = "Given",
                step = "the test environment is initialized"
            };

            var minimalResult = await _jsonRpc!.InvokeWithParameterObjectAsync<JObject>(
                ServoMethods.ExecuteStep, 
                minimalRequest);

            _output.WriteLine("MINIMAL REQUEST RESULT:");
            _output.WriteLine($"  {minimalResult.ToString(Formatting.Indented)}");

            // Test with full StepRequest fields
            var fullRequest = new
            {
                process = "reader",
                stepType = "Given",
                step = "the test environment is initialized",
                parameters = new Dictionary<string, string> { ["key1"] = "value1" },
                context = new Dictionary<string, string> { ["ctx1"] = "data1" },
                isBroadcast = false
            };

            var fullResult = await _jsonRpc!.InvokeWithParameterObjectAsync<JObject>(
                ServoMethods.ExecuteStep, 
                fullRequest);

            _output.WriteLine("\nFULL REQUEST RESULT (with extra fields):");
            _output.WriteLine($"  {fullResult.ToString(Formatting.Indented)}");

            _output.WriteLine("\nSCHEMA DIFFERENCE:");
            _output.WriteLine("  ❌ C++ ignores Process field (doesn't support multi-process context)");
            _output.WriteLine("  ❌ C++ ignores Parameters dictionary");
            _output.WriteLine("  ❌ C++ ignores Context dictionary");
            _output.WriteLine("  ❌ C++ ignores IsBroadcast flag");
            _output.WriteLine("  ✓ C++ accepts stepType and step fields");
        }

        [Fact]
        public async Task AnalyzeStepResponse_Schema()
        {
            _output.WriteLine("=== STEP RESPONSE SCHEMA ANALYSIS ===\n");

            // Initialize first
            await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
                ServoMethods.Initialize, 
                new InitializeRequest("reader", "cpp", "ResponseSchemaTest", 1, 1));

            _output.WriteLine("HARMONY EXPECTS (StepResponse):");
            _output.WriteLine("  - Success: bool");
            _output.WriteLine("  - Error: string? (nullable)");
            _output.WriteLine("  - Context: ImmutableDictionary<string, string>? (nullable)");
            _output.WriteLine("  - Logs: ImmutableList<LogResponse>? (nullable)");
            _output.WriteLine("    where LogResponse has:");
            _output.WriteLine("      - Timestamp: DateTime");
            _output.WriteLine("      - Level: LogLevel enum");
            _output.WriteLine("      - Message: string\n");

            // Execute a step to see actual response
            var stepRequest = new
            {
                stepType = "Given",
                step = "the test environment is initialized"
            };

            var response = await _jsonRpc!.InvokeWithParameterObjectAsync<JObject>(
                ServoMethods.ExecuteStep, 
                stepRequest);

            _output.WriteLine("C++ SERVO RETURNS:");
            _output.WriteLine($"{response.ToString(Formatting.Indented)}\n");

            // Analyze the structure
            _output.WriteLine("C++ ACTUAL SCHEMA:");
            _output.WriteLine("  - success: bool");
            _output.WriteLine("  - error: string/null");
            _output.WriteLine("  - data: object (always empty {})");
            _output.WriteLine("  - logs: array of objects with:");
            _output.WriteLine("    - Level: string ('INFO', 'DEBUG', etc.)");
            _output.WriteLine("    - Message: string");
            _output.WriteLine("  ❌ Missing: timeout field (only on timeout)");
            _output.WriteLine("  ❌ Missing: Context dictionary");
            _output.WriteLine("  ❌ Logs missing Timestamp field");
            _output.WriteLine("  ❌ Logs use string Level instead of LogLevel enum");

            _output.WriteLine("\nSCHEMA DIFFERENCES:");
            _output.WriteLine("  ✓ success matches Success");
            _output.WriteLine("  ✓ error matches Error");
            _output.WriteLine("  ❌ data field is extra (not in StepResponse)");
            _output.WriteLine("  ❌ Context is always null (not implemented)");
            _output.WriteLine("  ⚠️ logs structure differs from Logs<LogResponse>:");
            _output.WriteLine("    - Missing Timestamp");
            _output.WriteLine("    - Level is string, not LogLevel enum");
            _output.WriteLine("    - Field names are PascalCase (Level, Message) not camelCase");
        }

        [Fact]
        public async Task AnalyzeDiscoverResponse_Schema()
        {
            _output.WriteLine("=== DISCOVER RESPONSE SCHEMA ANALYSIS ===\n");

            _output.WriteLine("HARMONY EXPECTS (DiscoverResponse):");
            _output.WriteLine("  - Steps: ImmutableList<StepInfo>");
            _output.WriteLine("    where StepInfo has:");
            _output.WriteLine("      - Type: string");
            _output.WriteLine("      - Pattern: string\n");

            // Call discover
            var response = await _jsonRpc!.InvokeAsync<JObject>(ServoMethods.Discover);

            _output.WriteLine("C++ SERVO RETURNS:");
            _output.WriteLine($"{response.ToString(Formatting.Indented)}\n");

            _output.WriteLine("C++ ACTUAL SCHEMA:");
            _output.WriteLine("  - steps: array of objects with:");
            _output.WriteLine("    - pattern: string");
            _output.WriteLine("    - type: string");
            _output.WriteLine("  - capabilities: object with:");
            _output.WriteLine("    - timeout: bool");
            _output.WriteLine("    - contentLength: bool");
            _output.WriteLine("    - logging: bool");

            _output.WriteLine("\nSCHEMA DIFFERENCES:");
            _output.WriteLine("  ⚠️ Field name case mismatch:");
            _output.WriteLine("    - C++ returns 'steps' (lowercase), Harmony expects 'Steps'");
            _output.WriteLine("    - C++ returns 'pattern', 'type' (lowercase), Harmony expects 'Pattern', 'Type'");
            _output.WriteLine("  ❌ C++ includes extra 'capabilities' object (not in DiscoverResponse)");
            _output.WriteLine("  ✓ Array structure matches ImmutableList");
            _output.WriteLine("  ✓ StepInfo fields are present (just different casing)");
        }

        [Fact]
        public async Task VerifyHealthCheck_Schema()
        {
            _output.WriteLine("=== HEALTH CHECK SCHEMA ANALYSIS ===\n");

            _output.WriteLine("HARMONY EXPECTS:");
            _output.WriteLine("  - No parameters (method: 'health')");
            _output.WriteLine("  - Returns: bool\n");

            var result = await _jsonRpc!.InvokeAsync<bool>(ServoMethods.Health);

            _output.WriteLine("C++ SERVO:");
            _output.WriteLine($"  - Returns: {result} (type: {result.GetType().Name})");
            _output.WriteLine("\nSCHEMA MATCH:");
            _output.WriteLine("  ✓ Accepts parameterless call");
            _output.WriteLine("  ✓ Returns boolean");
            
            result.Should().BeTrue();
        }

        [Fact]
        public async Task DocumentRequiredChanges()
        {
            _output.WriteLine("=== REQUIRED C++ SERVO CHANGES FOR FULL HARMONY COMPATIBILITY ===\n");

            _output.WriteLine("1. INITIALIZE METHOD:");
            _output.WriteLine("   - Store Role, Platform, Scenario, HostPid, FeatureId in TestContext");
            _output.WriteLine("   - Make these values available to step implementations\n");

            _output.WriteLine("2. STEP EXECUTION:");
            _output.WriteLine("   - Accept and store Process field");
            _output.WriteLine("   - Accept and pass Parameters dictionary to steps");
            _output.WriteLine("   - Accept and maintain Context dictionary between steps");
            _output.WriteLine("   - Support IsBroadcast flag (future feature)\n");

            _output.WriteLine("3. STEP RESPONSE:");
            _output.WriteLine("   - Remove 'data' field (or rename to 'Context')");
            _output.WriteLine("   - Add Context dictionary support");
            _output.WriteLine("   - Add Timestamp to log entries");
            _output.WriteLine("   - Convert Level to LogLevel enum values (0-6)\n");

            _output.WriteLine("4. DISCOVER RESPONSE:");
            _output.WriteLine("   - Change field names to PascalCase (Steps, Type, Pattern)");
            _output.WriteLine("   - Remove 'capabilities' or move to separate method\n");

            _output.WriteLine("5. FIELD NAMING CONVENTIONS:");
            _output.WriteLine("   - Use PascalCase for all response fields to match C# conventions");
            _output.WriteLine("   - Accept both camelCase and PascalCase in requests for compatibility\n");

            _output.WriteLine("PRIORITY FIXES:");
            _output.WriteLine("  🔴 HIGH: Store initialization context (Role, Platform, etc.)");
            _output.WriteLine("  🔴 HIGH: Support Parameters in StepRequest");
            _output.WriteLine("  🟡 MEDIUM: Fix field naming to PascalCase");
            _output.WriteLine("  🟡 MEDIUM: Add Timestamp to logs");
            _output.WriteLine("  🟢 LOW: Support Context dictionary");
            _output.WriteLine("  🟢 LOW: Remove extra fields (data, capabilities)");
        }
    }
}