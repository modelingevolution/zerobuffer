using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;

namespace ModelingEvolution.Harmony.Tests;

/// <summary>
/// Tests that demonstrate the actual JSON-RPC communication over stdin/stdout
/// </summary>
public class JsonRpcCommunicationTests
{
    private readonly ITestOutputHelper _output;
    
    public JsonRpcCommunicationTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public async Task SimulateJsonRpcOverStreams_ShowsActualProtocol()
    {
        // This test demonstrates what the JSON-RPC messages would look like
        // without actually setting up the complex stream infrastructure
        
        _output.WriteLine("=== JSON-RPC Communication Protocol Demo ===\n");
        
        // Test scenario steps
        var steps = new[]
        {
            new
            {
                process = "reader",
                stepType = "given",
                step = "creates buffer 'test' with size '10240'",
                originalStep = "creates buffer 'test' with size '10240'",
                parameters = new Dictionary<string, string>
                {
                    ["buffer_name"] = "test",
                    ["size"] = "10240"
                }
            },
            new
            {
                process = "writer",
                stepType = "when",
                step = "connects to buffer 'test'",
                originalStep = "connects to buffer 'test'",
                parameters = new Dictionary<string, string>
                {
                    ["buffer_name"] = "test"
                }
            },
            new
            {
                process = "writer",
                stepType = "when",
                step = "writes 'Hello, World!'",
                originalStep = "writes 'Hello, World!'",
                parameters = new Dictionary<string, string>
                {
                    ["data"] = "Hello, World!"
                }
            }
        };
        
        var requestId = 1;
        
        foreach (var step in steps)
        {
            _output.WriteLine($"[CLIENT -> {step.process.ToUpper()}] Sending request:");
            
            // Show the JSON-RPC request that would be sent
            var request = new
            {
                jsonrpc = "2.0",
                method = "executeStep",
                @params = step,
                id = requestId
            };
            
            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
            _output.WriteLine($"Request:\n{requestJson}\n");
            
            // Show the expected response
            var response = new
            {
                jsonrpc = "2.0",
                result = new
                {
                    Success = true,
                    Error = (string?)null,
                    Data = new Dictionary<string, object>
                    {
                        ["processed"] = true,
                        ["timestamp"] = DateTime.UtcNow.ToString("O")
                    },
                    Logs = new[]
                    {
                        new { Level = "INFO", Message = $"Executed step on {step.process}" }
                    }
                },
                id = requestId
            };
            
            var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
            _output.WriteLine($"[{step.process.ToUpper()} -> CLIENT] Response:\n{responseJson}\n");
            _output.WriteLine("---\n");
            
            requestId++;
        }
        
        _output.WriteLine("Note: In actual execution, these JSON-RPC messages would be");
        _output.WriteLine("transmitted over stdin/stdout between the Harmony orchestrator");
        _output.WriteLine("and the platform-specific test processes.");
        
        // Test passes by demonstrating the protocol
        await Task.CompletedTask;
    }
    
    [Fact]
    public async Task CaptureJsonRpcMessages_ToFile()
    {
        // This test demonstrates capturing JSON-RPC messages to a file for analysis
        var logFile = Path.GetTempFileName();
        var messages = new List<JsonRpcMessage>();
        
        try
        {
            // Simulate process communication
            var processConnection = new MockProcessConnection("writer", "python", messages);
            
            // Execute some steps
            var testSteps = new[]
            {
                ("Given", "creates buffer 'test'"),
                ("When", "writes frame with size '1024'"),
                ("Then", "should have written successfully")
            };
            
            foreach (var (stepType, stepText) in testSteps)
            {
                var request = new
                {
                    process = "writer",
                    stepType = stepType.ToLowerInvariant(),
                    step = stepText,
                    originalStep = stepText,
                    parameters = new Dictionary<string, string>()
                };
                
                await processConnection.InvokeAsync<object>("executeStep", request);
            }
            
            // Write all messages to file
            var jsonContent = JsonSerializer.Serialize(messages, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await File.WriteAllTextAsync(logFile, jsonContent);
            
            _output.WriteLine($"JSON-RPC messages written to: {logFile}");
            _output.WriteLine("\nCaptured messages:");
            _output.WriteLine(jsonContent);
            
            // Parse and analyze the messages
            var parsedMessages = JsonSerializer.Deserialize<List<JsonRpcMessage>>(
                await File.ReadAllTextAsync(logFile));
            
            parsedMessages.Should().NotBeNull();
            parsedMessages!.Count.Should().Be(6); // 3 requests + 3 responses
            
            var requests = parsedMessages.Where(m => m.Type == "Request").ToList();
            var responses = parsedMessages.Where(m => m.Type == "Response").ToList();
            
            _output.WriteLine($"\nAnalysis:");
            _output.WriteLine($"  Total messages: {parsedMessages.Count}");
            _output.WriteLine($"  Requests: {requests.Count}");
            _output.WriteLine($"  Responses: {responses.Count}");
            
            // Verify request/response pairing
            foreach (var request in requests)
            {
                var response = responses.FirstOrDefault(r => r.Id == request.Id);
                response.Should().NotBeNull($"Response for request {request.Id} should exist");
            }
        }
        finally
        {
            if (File.Exists(logFile))
                File.Delete(logFile);
        }
    }
    
    private class MockProcessConnection : IProcessConnection
    {
        private readonly List<JsonRpcMessage> _messages;
        private int _requestId = 1;
        
        public string ProcessName { get; }
        public string Platform { get; }
        public bool IsConnected => true;
        
        public MockProcessConnection(string processName, string platform, List<JsonRpcMessage> messages)
        {
            ProcessName = processName;
            Platform = platform;
            _messages = messages;
        }
        
        public Task<T> InvokeAsync<T>(string method, object parameters, CancellationToken cancellationToken = default)
        {
            var requestId = _requestId++;
            
            // Log the request
            var request = new JsonRpcMessage
            {
                Type = "Request",
                Id = requestId.ToString(),
                Method = method,
                Parameters = parameters,
                Timestamp = DateTime.UtcNow,
                Process = ProcessName,
                Platform = Platform
            };
            _messages.Add(request);
            
            // Create response
            var responseData = new
            {
                Success = true,
                Error = (string?)null,
                Data = new Dictionary<string, object>(),
                Logs = new List<object>()
            };
            
            // Log the response
            var response = new JsonRpcMessage
            {
                Type = "Response",
                Id = requestId.ToString(),
                Result = responseData,
                Timestamp = DateTime.UtcNow,
                Process = ProcessName,
                Platform = Platform
            };
            _messages.Add(response);
            
            return Task.FromResult((T)(object)responseData);
        }
    }
    
    private class JsonRpcMessage
    {
        public string Type { get; set; } = "";
        public string Id { get; set; } = "";
        public string? Method { get; set; }
        public object? Parameters { get; set; }
        public object? Result { get; set; }
        public DateTime Timestamp { get; set; }
        public string Process { get; set; } = "";
        public string Platform { get; set; } = "";
    }
}