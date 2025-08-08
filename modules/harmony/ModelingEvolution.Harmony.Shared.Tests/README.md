# ModelingEvolution.Harmony.Shared Serialization Examples

This test project demonstrates how the shared contract models are serialized for JSON-RPC communication between different platforms (C#, C++, Python).

## Serialization Format

The contracts use **PascalCase** property naming by default, which is the standard for StreamJsonRpc in C#. This means:
- Property names start with capital letters (e.g., `Process`, `StepType`, `Message`)
- Enum values are serialized as integers (e.g., `LogLevel.Info` → `2`)
- Collections use immutable types for thread safety

## Example JSON Formats

### StepRequest
```json
{
  "Process": "writer",
  "StepType": "When",
  "Step": "the writer writes a frame with size 1024",
  "Parameters": {
    "size": "1024",
    "pattern": "test_data"
  },
  "IsBroadcast": false
}
```

### StepResponse
```json
{
  "Success": true,
  "Error": null,
  "Data": {
    "frameId": "frame_001",
    "bytesWritten": 1024
  },
  "Logs": [
    {
      "Timestamp": "2025-08-08T10:00:00Z",
      "Level": 2,
      "Message": "Frame written successfully"
    }
  ]
}
```

### LogLevel Enum Values
- `Trace` = 0
- `Debug` = 1
- `Info` = 2
- `Warning` = 3
- `Error` = 4
- `Fatal` = 5

## Platform Compatibility

### C# (StreamJsonRpc)
Uses default settings with PascalCase naming:
```csharp
var jsonRpc = new JsonRpc(inputStream, outputStream);
```

### C++ (nlohmann/json)
The C++ servo handles case-insensitive field matching to work with both PascalCase and camelCase:
```cpp
// Accepts both "Process" and "process"
std::string process = getJsonStringCaseInsensitive(params, "Process");
```

### Python
Python servos should handle PascalCase fields when deserializing from C#/Harmony.

## Running the Tests

```bash
dotnet test
```

The tests demonstrate:
- Serialization of all contract types
- Handling of null/empty collections
- Complex data types in dictionaries
- Enum serialization as integers
- Round-trip serialization/deserialization

## Integration with StreamJsonRpc

When using these contracts with StreamJsonRpc:

1. **Request/Response Types**: Always use strongly-typed contracts
   ```csharp
   var response = await jsonRpc.InvokeAsync<StepResponse>("executeStep", request);
   ```

2. **Immutable Collections**: The contracts use `ImmutableDictionary` and `ImmutableList` for thread safety

3. **Nullable Fields**: Optional fields are properly marked as nullable (`string?`, `ImmutableList<T>?`)

4. **Duration Tracking**: `StepExecutionResult` includes a `TimeSpan Duration` for performance monitoring