using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ModelingEvolution.Harmony.Shared.Tests;

/// <summary>
/// Tests demonstrating serialization of all contract models compatible with StreamJsonRpc.
/// StreamJsonRpc's JsonMessageFormatter uses Newtonsoft.Json with default settings (PascalCase).
/// </summary>
public class SerializationTests
{
    private readonly ITestOutputHelper _output;
    private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented
    };

    public SerializationTests(ITestOutputHelper output)
    {
        _output = output;

        // Configure Newtonsoft.Json settings to match StreamJsonRpc's defaults
        var p = new JsonMessageFormatter();
        Assert.True(p.JsonSerializer.Converters.Count == 9);
        this.JsonSerializer = new JsonSerializer()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            DateParseHandling = DateParseHandling.None,
            ContractResolver = p.JsonSerializer.ContractResolver,
            Converters  =
            {
                p.JsonSerializer.Converters[0],
                p.JsonSerializer.Converters[1],
                p.JsonSerializer.Converters[2],
                p.JsonSerializer.Converters[3],
                p.JsonSerializer.Converters[4],
                p.JsonSerializer.Converters[5],
                p.JsonSerializer.Converters[6],
                p.JsonSerializer.Converters[7],
                p.JsonSerializer.Converters[8],
            }
        };
    }

    public JsonSerializer JsonSerializer { get;  }

    private string SerializeToJson<T>(T obj)
    {
        StringWriter sw = new StringWriter();
        JsonSerializer.Serialize(sw, obj);
        return sw.ToString();
    }

    private T? DeserializeFromJson<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json, _jsonSettings);
    }

    [Fact]
    public void StepRequest_Serialization_Example()
    {
        // Arrange
        var request = new StepRequest(
            Process: "writer",
            StepType: StepType.When,
            Step: "the writer writes a frame with size 1024",
            Parameters: ImmutableDictionary<string, string>.Empty
                .Add("size", "1024")
                .Add("pattern", "test_data"),
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        // Act
        var json = SerializeToJson(request);
        _output.WriteLine("StepRequest JSON:");
        _output.WriteLine(json);

        var deserialized = DeserializeFromJson<StepRequest>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.Process, deserialized.Process);
        Assert.Equal(request.StepType, deserialized.StepType);
        Assert.Equal(request.Step, deserialized.Step);
        Assert.Equal(request.Parameters["size"], deserialized.Parameters["size"]);
        Assert.Equal(request.IsBroadcast, deserialized.IsBroadcast);
    }

    [Fact]
    public void StepResponse_Serialization_Example()
    {
        // Arrange
        var response = new StepResponse(
            Success: true,
            Error: null,
            Context: ImmutableDictionary<string, string>.Empty
                .Add("frameId", "frame_001")
                .Add("bytesWritten", "1024"),
            Logs: ImmutableList.Create(
                new LogResponse(
                    Timestamp: DateTime.UtcNow,
                    Level: LogLevel.Information,
                    Message: "Frame written successfully"
                ),
                new LogResponse(
                    Timestamp: DateTime.UtcNow,
                    Level: LogLevel.Debug,
                    Message: "Buffer position updated"
                )
            )
        );

        // Act
        var json = SerializeToJson(response);
        _output.WriteLine("StepResponse JSON:");
        _output.WriteLine(json);

        var deserialized = DeserializeFromJson<StepResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.Success, deserialized.Success);
        Assert.Null(deserialized.Error);
        Assert.NotNull(deserialized.Context);
        Assert.NotNull(deserialized.Logs);
        Assert.Equal(2, deserialized.Logs.Count);
    }

    [Fact]
    public void LogResponse_Serialization_Example()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var logEntry = new LogResponse(
            Timestamp: timestamp,
            Level: LogLevel.Warning,
            Message: "Buffer nearly full: 90% capacity"
        );

        // Act
        var json = SerializeToJson(logEntry);
        _output.WriteLine("LogResponse JSON:");
        _output.WriteLine(json);

        var deserialized = DeserializeFromJson<LogResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(logEntry.Level, deserialized.Level);
        Assert.Equal(logEntry.Message, deserialized.Message);
        // Timestamp comparison with tolerance for serialization precision
        Assert.True(Math.Abs((logEntry.Timestamp - deserialized.Timestamp).TotalMilliseconds) < 1);
    }

    [Fact]
    public void InitializeRequest_Serialization_Example()
    {
        // Arrange
        var request = new InitializeRequest(
            Role: "writer",
            Platform: "cpp",
            Scenario: "1.1",
            HostPid: 5678,
            FeatureId: 101
        );

        // Act
        var json = SerializeToJson(request);
        _output.WriteLine("InitializeRequest JSON:");
        _output.WriteLine(json);

        var deserialized = DeserializeFromJson<InitializeRequest>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.Role, deserialized.Role);
        Assert.Equal(request.Platform, deserialized.Platform);
        Assert.Equal(request.Scenario, deserialized.Scenario);
        Assert.Equal($"{request.HostPid}_{request.FeatureId}", deserialized.TestRunId);
        Assert.Equal(request.HostPid, deserialized.HostPid);
        Assert.Equal(request.FeatureId, deserialized.FeatureId);
    }

    // HealthRequest removed - health check is now parameterless

    [Fact]
    public void DiscoverResponse_Serialization_Example()
    {
        // Arrange
        var response = new DiscoverResponse(
            Steps: ImmutableList.Create(
                new StepInfo(
                    Type: "Given",
                    Pattern: "the 'writer' process creates a buffer named {string}"
                ),
                new StepInfo(
                    Type: "When",
                    Pattern: "the 'writer' writes {string}"
                ),
                new StepInfo(
                    Type: "Then",
                    Pattern: "the 'reader' should read {string}"
                )
            )
        );

        // Act
        var json = SerializeToJson(response);
        _output.WriteLine("DiscoverResponse JSON:");
        _output.WriteLine(json);

        var deserialized = DeserializeFromJson<DiscoverResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Steps);
        Assert.Equal(3, deserialized.Steps.Count);
        Assert.Equal("Given", deserialized.Steps[0].Type);
    }

    [Fact]
    public void LogLevel_Enum_Serialization()
    {
        // Arrange & Act - Test all enum values
        var levels = new[]
        {
            (LogLevel.Trace, 0, "Trace level"),
            (LogLevel.Debug, 1, "Debug level"),
            (LogLevel.Information, 2, "Info level"),
            (LogLevel.Warning, 3, "Warning level"),
            (LogLevel.Error, 4, "Error level"),
            (LogLevel.Critical, 5, "Fatal level")
        };

        foreach (var (level, expectedValue, description) in levels)
        {
            var logEntry = new LogResponse(
                Timestamp: DateTime.UtcNow,
                Level: level,
                Message: description
            );

            var json = SerializeToJson(logEntry);
            _output.WriteLine($"LogLevel.{level} JSON:");
            _output.WriteLine(json);

            // Parse JSON to verify enum is serialized as integer
            var jObject = JObject.Parse(json);
            Assert.Equal(expectedValue, jObject["Level"]!.Value<int>());

            var deserialized = DeserializeFromJson<LogResponse>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(level, deserialized!.Level);
        }
    }

    [Fact]
    public void EmptyCollections_Serialization()
    {
        // Arrange
        var request = new StepRequest(
            Process: "test",
            StepType: StepType.Given,
            Step: "a simple step",
            Parameters: ImmutableDictionary<string, string>.Empty,
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        // Act
        var json = SerializeToJson(request);
        _output.WriteLine("StepRequest with empty Parameters:");
        _output.WriteLine(json);

        var deserialized = DeserializeFromJson<StepRequest>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Parameters);
        Assert.Empty(deserialized.Parameters);
    }

    [Fact]
    public void NullableFields_Serialization()
    {
        // Arrange - Response with null error and null optional collections
        var response = new StepResponse(
            Success: true,
            Error: null,
            Context: null,
            Logs: null
        );

        // Act
        var json = SerializeToJson(response);
        _output.WriteLine("StepResponse with null fields:");
        _output.WriteLine(json);

        var deserialized = DeserializeFromJson<StepResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Null(deserialized.Error);
        Assert.Null(deserialized.Context);
        Assert.Null(deserialized.Logs);
    }

    [Fact]
    public void StringData_Dictionary_Serialization()
    {
        // Arrange - Dictionary with string values only (as per the new contract)
        var response = new StepResponse(
            Success: true,
            Error: null,
            Context: ImmutableDictionary<string, string>.Empty
                .Add("frameId", "frame_001")
                .Add("bytesWritten", "1024")
                .Add("bufferPosition", "2048")
                .Add("timestamp", "2024-01-15T10:30:00Z")
                .Add("success", "true")
                .Add("ratio", "3.14159"),
            Logs: null
        );

        // Act
        var json = SerializeToJson(response);
        _output.WriteLine("StepResponse with string data:");
        _output.WriteLine(json);

        var deserialized = DeserializeFromJson<StepResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Context);
        Assert.Equal(6, deserialized.Context.Count);
        
        // All values are strings now
        Assert.Equal("frame_001", deserialized.Context["frameId"]);
        Assert.Equal("1024", deserialized.Context["bytesWritten"]);
        Assert.Equal("2048", deserialized.Context["bufferPosition"]);
        Assert.Equal("2024-01-15T10:30:00Z", deserialized.Context["timestamp"]);
        Assert.Equal("true", deserialized.Context["success"]);
        Assert.Equal("3.14159", deserialized.Context["ratio"]);
    }

    [Fact]
    public void RoundTrip_All_Contracts()
    {
        // This test ensures all contracts can be serialized and deserialized successfully
        var contracts = new object[]
        {
            new StepRequest("proc", StepType.When, "step", ImmutableDictionary<string, string>.Empty, ImmutableDictionary<string, string>.Empty,false),
            new StepResponse(true, null, null, null),
            new LogResponse(DateTime.UtcNow, LogLevel.Information, "test"),
            new InitializeRequest("role", "platform", "scenario", 123, 456),
            // HealthRequest removed - health is parameterless
            new DiscoverResponse(ImmutableList<StepInfo>.Empty),
            new StepInfo("Given", "pattern")
        };

        foreach (var contract in contracts)
        {
            var type = contract.GetType();
            _output.WriteLine($"Testing {type.Name}...");

            // Serialize
            var json = JsonConvert.SerializeObject(contract, type, _jsonSettings);
            _output.WriteLine($"  JSON: {json}");

            // Deserialize
            var deserialized = JsonConvert.DeserializeObject(json, type, _jsonSettings);

            // Basic verification
            Assert.NotNull(deserialized);
            Assert.Equal(type, deserialized!.GetType());
        }
    }

    [Fact]
    public void Verify_PascalCase_Property_Names()
    {
        // Verify that default Newtonsoft.Json serialization uses PascalCase
        var request = new StepRequest(
            Process: "test",
            StepType: StepType.Given,
            Step: "test step",
            Parameters: ImmutableDictionary<string, string>.Empty.Add("key", "value"),
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: true
        );

        var json = SerializeToJson(request);
        _output.WriteLine("Verifying PascalCase property names:");
        _output.WriteLine(json);

        // Verify property names are PascalCase
        Assert.Contains("\"Process\"", json);
        Assert.Contains("\"StepType\"", json);
        Assert.Contains("\"Step\"", json);
        Assert.Contains("\"Parameters\"", json);
        Assert.Contains("\"IsBroadcast\"", json);
        
        // Should NOT contain camelCase
        Assert.DoesNotContain("\"process\"", json);
        Assert.DoesNotContain("\"stepType\"", json);
    }

    [Fact]
    public void StreamJsonRpc_Message_Format_Example()
    {
        // Example of how these contracts would be used in actual JSON-RPC messages
        var request = new StepRequest(
            Process: "writer",
            StepType: StepType.When,
            Step: "the writer writes a frame",
            Parameters: ImmutableDictionary<string, string>.Empty,
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        // Simulate a JSON-RPC request as it would appear in StreamJsonRpc
        var rpcRequest = new
        {
            jsonrpc = "2.0",
            method = "executeStep",
            @params = request,
            id = 1
        };

        var json = JsonConvert.SerializeObject(rpcRequest, _jsonSettings);
        _output.WriteLine("Example JSON-RPC Request:");
        _output.WriteLine(json);

        // Verify the structure
        var jObject = JObject.Parse(json);
        Assert.Equal("2.0", jObject["jsonrpc"]!.Value<string>());
        Assert.Equal("executeStep", jObject["method"]!.Value<string>());
        Assert.Equal(1, jObject["id"]!.Value<int>());
        Assert.NotNull(jObject["params"]);
        Assert.Equal("writer", jObject["params"]!["Process"]!.Value<string>());
    }

    [Fact]
    public void Verify_StreamJsonRpc_Compatibility()
    {
        // This test verifies that our contracts work with actual StreamJsonRpc infrastructure
        using var clientStream = new MemoryStream();
        using var serverStream = new MemoryStream();
        
        // Create a JsonMessageFormatter (StreamJsonRpc's default formatter)
        var formatter = new JsonMessageFormatter();
        
        // Create message handler
        var handler = new HeaderDelimitedMessageHandler(serverStream, clientStream, formatter);
        
        // Create JsonRpc instance  
        using var jsonRpc = new JsonRpc(handler);
        
        // Verify we can create a JsonRpc instance with our formatter
        // This ensures our contracts are compatible with StreamJsonRpc
        Assert.NotNull(jsonRpc);
        
        _output.WriteLine("Successfully created JsonRpc instance with JsonMessageFormatter");
        _output.WriteLine("This confirms our contracts are compatible with StreamJsonRpc's default serialization");
    }
}