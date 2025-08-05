# Step Handler Design - Attribute-Based Approach

## Overview

The C# test service uses an attribute-based approach to map natural language steps from Gherkin scenarios to executable code. This design provides a clean, declarative way to implement test steps that can be discovered and executed dynamically.

## Architecture

### 1. Step Pattern Attribute

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class StepPatternAttribute : Attribute
{
    public string Pattern { get; }
    public string Description { get; set; }
    
    public StepPatternAttribute(string pattern)
    {
        Pattern = pattern;
    }
}
```

### 2. Step Handler Classes

Test steps are implemented as methods decorated with `[StepPattern]` attributes:

```csharp
public class BufferStepHandlers
{
    private readonly TestState _state;
    
    public BufferStepHandlers(TestState state)
    {
        _state = state;
    }
    
    [StepPattern(@"^create buffer '(.+)' with size '(\d+)'$")]
    public StepResult CreateBuffer(string bufferName, int size)
    {
        var config = new BufferConfig(1024, size);
        var reader = new Reader(bufferName, config);
        
        _state.SetReader(reader);
        _state.SetBufferName(bufferName);
        
        return StepResult.Success(new { created = true, bufferName, size });
    }
    
    [StepPattern(@"^create buffer '(.+)' with metadata size '(\d+)' and payload size '(\d+)'$")]
    public StepResult CreateBufferFull(string bufferName, int metadataSize, int payloadSize)
    {
        var config = new BufferConfig(metadataSize, payloadSize);
        var reader = new Reader(bufferName, config);
        
        _state.SetReader(reader);
        _state.SetBufferName(bufferName);
        
        return StepResult.Success(new { created = true, bufferName, metadataSize, payloadSize });
    }
    
    [StepPattern(@"^connect to buffer '(.+)'$")]
    public StepResult ConnectToBuffer(string bufferName)
    {
        var writer = new Writer(bufferName);
        _state.SetWriter(writer);
        
        return StepResult.Success(new { connected = true, bufferName });
    }
}

public class DataStepHandlers
{
    private readonly TestState _state;
    
    public DataStepHandlers(TestState state)
    {
        _state = state;
    }
    
    [StepPattern(@"^write metadata with size '(\d+)'$")]
    public StepResult WriteMetadata(int size)
    {
        var writer = _state.GetWriter();
        var data = TestDataGenerator.GenerateBytes(size);
        writer.SetMetadata(data);
        
        return StepResult.Success(new { written = true, size });
    }
    
    [StepPattern(@"^write frame with data '(.+)'$")]
    public StepResult WriteFrameWithData(string data)
    {
        var writer = _state.GetWriter();
        writer.WriteFrame(Encoding.UTF8.GetBytes(data));
        
        return StepResult.Success(new { written = true, data });
    }
    
    [StepPattern(@"^write frame with size '(\d+)' and sequence '(\d+)'$")]
    public StepResult WriteFrame(int size, ulong sequence)
    {
        var writer = _state.GetWriter();
        var data = TestDataGenerator.GenerateBytes(size);
        writer.WriteFrame(data); // Note: sequence is managed by ZeroBuffer
        
        return StepResult.Success(new { written = true, size, sequence });
    }
}

public class AssertionStepHandlers
{
    private readonly TestState _state;
    
    public AssertionStepHandlers(TestState state)
    {
        _state = state;
    }
    
    [StepPattern(@"^read frame should return '(.+)'$")]
    public StepResult AssertFrameData(string expectedData)
    {
        var reader = _state.GetReader();
        var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
        
        if (!frame.IsValid)
            return StepResult.Error("No frame available");
        
        var actualData = Encoding.UTF8.GetString(frame.ToArray());
        if (actualData != expectedData)
            return StepResult.Error($"Expected '{expectedData}' but got '{actualData}'");
        
        return StepResult.Success(new { verified = true, data = actualData });
    }
    
    [StepPattern(@"^read frame should have sequence '(\d+)' and size '(\d+)'$")]
    public StepResult AssertFrameProperties(ulong expectedSequence, int expectedSize)
    {
        var reader = _state.GetReader();
        var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
        
        if (!frame.IsValid)
            return StepResult.Error("No frame available");
        
        if (frame.Sequence != expectedSequence)
            return StepResult.Error($"Expected sequence {expectedSequence} but got {frame.Sequence}");
        
        if (frame.Size != expectedSize)
            return StepResult.Error($"Expected size {expectedSize} but got {frame.Size}");
        
        return StepResult.Success(new { verified = true, sequence = frame.Sequence, size = frame.Size });
    }
    
    [StepPattern(@"^metadata should have size '(\d+)'$")]
    public StepResult AssertMetadataSize(int expectedSize)
    {
        var reader = _state.GetReader();
        var metadata = reader.GetMetadata();
        
        if (metadata.Length != expectedSize)
            return StepResult.Error($"Expected metadata size {expectedSize} but got {metadata.Length}");
        
        return StepResult.Success(new { verified = true, size = metadata.Length });
    }
}

public class ProcessStepHandlers
{
    private readonly TestState _state;
    
    public ProcessStepHandlers(TestState state)
    {
        _state = state;
    }
    
    [StepPattern(@"^simulate crash$")]
    public StepResult SimulateCrash()
    {
        // In real implementation, this would terminate the process
        // For testing, we just dispose resources abruptly
        _state.DisposeAll();
        
        return StepResult.Success(new { crashed = true });
    }
    
    [StepPattern(@"^wait for '(\d+)' seconds$")]
    public async Task<StepResult> Wait(int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        return StepResult.Success(new { waited = seconds });
    }
    
    [StepPattern(@"^writer should be disconnected$")]
    public StepResult AssertWriterDisconnected()
    {
        var reader = _state.GetReader();
        
        if (reader.IsWriterConnected(0))
            return StepResult.Error("Writer is still connected");
        
        return StepResult.Success(new { disconnected = true });
    }
}
```

### 3. Step Handler Registry

The registry discovers and manages all step handlers:

```csharp
public class StepHandlerRegistry
{
    private readonly List<StepHandlerEntry> _handlers = new();
    private readonly TestState _state = new();
    
    public StepHandlerRegistry()
    {
        // Auto-discover all handlers in the assembly
        DiscoverHandlers();
    }
    
    private void DiscoverHandlers()
    {
        var handlerTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.Name.EndsWith("StepHandlers"))
            .ToList();
        
        foreach (var type in handlerTypes)
        {
            RegisterHandlerType(type);
        }
    }
    
    private void RegisterHandlerType(Type type)
    {
        var instance = Activator.CreateInstance(type, _state);
        
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = method.GetCustomAttribute<StepPatternAttribute>();
            if (attribute != null)
            {
                var entry = new StepHandlerEntry
                {
                    Pattern = new Regex(attribute.Pattern, RegexOptions.IgnoreCase),
                    Method = method,
                    Instance = instance,
                    Description = attribute.Description
                };
                
                _handlers.Add(entry);
            }
        }
    }
    
    public async Task<StepResult> ExecuteStep(string stepType, string step)
    {
        foreach (var handler in _handlers)
        {
            var match = handler.Pattern.Match(step);
            if (match.Success)
            {
                try
                {
                    var parameters = ExtractParameters(match, handler.Method);
                    var result = handler.Method.Invoke(handler.Instance, parameters);
                    
                    // Handle async methods
                    if (result is Task<StepResult> taskResult)
                    {
                        return await taskResult;
                    }
                    
                    return (StepResult)result;
                }
                catch (Exception ex)
                {
                    return StepResult.Error($"Step execution failed: {ex.Message}");
                }
            }
        }
        
        return StepResult.Error($"No handler found for step: {step}");
    }
    
    private object[] ExtractParameters(Match match, MethodInfo method)
    {
        var parameterInfos = method.GetParameters();
        var parameters = new object[parameterInfos.Length];
        
        // Groups[0] is the full match, so we start from 1
        for (int i = 0; i < parameterInfos.Length; i++)
        {
            var groupValue = match.Groups[i + 1].Value;
            parameters[i] = ConvertParameter(groupValue, parameterInfos[i].ParameterType);
        }
        
        return parameters;
    }
    
    private object ConvertParameter(string value, Type targetType)
    {
        if (targetType == typeof(string))
            return value;
        
        if (targetType == typeof(int))
            return int.Parse(value);
        
        if (targetType == typeof(ulong))
            return ulong.Parse(value);
        
        if (targetType == typeof(bool))
            return bool.Parse(value);
        
        throw new NotSupportedException($"Cannot convert to type {targetType}");
    }
}
```

### 4. Test State Management

```csharp
public class TestState : IDisposable
{
    private readonly Dictionary<string, object> _state = new();
    private Reader? _currentReader;
    private Writer? _currentWriter;
    
    public void SetReader(Reader reader)
    {
        _currentReader?.Dispose();
        _currentReader = reader;
        _state["role"] = "reader";
    }
    
    public Reader GetReader()
    {
        if (_currentReader == null)
            throw new InvalidOperationException("No reader created");
        return _currentReader;
    }
    
    public void SetWriter(Writer writer)
    {
        _currentWriter?.Dispose();
        _currentWriter = writer;
        _state["role"] = "writer";
    }
    
    public Writer GetWriter()
    {
        if (_currentWriter == null)
            throw new InvalidOperationException("No writer created");
        return _currentWriter;
    }
    
    public void SetBufferName(string bufferName)
    {
        _state["bufferName"] = bufferName;
    }
    
    public T Get<T>(string key)
    {
        if (_state.TryGetValue(key, out var value))
            return (T)value;
        throw new KeyNotFoundException($"State key '{key}' not found");
    }
    
    public void Set(string key, object value)
    {
        _state[key] = value;
    }
    
    public void DisposeAll()
    {
        _currentReader?.Dispose();
        _currentWriter?.Dispose();
        _currentReader = null;
        _currentWriter = null;
    }
    
    public void Dispose()
    {
        DisposeAll();
    }
}
```

### 5. JSON-RPC Service Integration

```csharp
public class GenericTestService
{
    private readonly StepHandlerRegistry _registry = new();
    
    public async Task<ExecuteStepResponse> ExecuteStep(ExecuteStepRequest request)
    {
        try
        {
            var result = await _registry.ExecuteStep(request.StepType, request.Step);
            
            return new ExecuteStepResponse
            {
                Success = result.Success,
                Data = result.Data,
                Error = result.Error,
                Context = result.Context
            };
        }
        catch (Exception ex)
        {
            return new ExecuteStepResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}
```

## Usage Example

When the test runner sends:
```json
{
  "method": "executeStep",
  "params": {
    "stepType": "given",
    "step": "create buffer 'test-101' with size '10240'"
  }
}
```

The service:
1. Searches all registered patterns
2. Finds `@"^create buffer '(.+)' with size '(\d+)'$"`
3. Extracts parameters: `["test-101", "10240"]`
4. Converts to proper types: `[string, int]`
5. Invokes `CreateBuffer("test-101", 10240)`
6. Returns the result

## Benefits

1. **Declarative**: Steps are defined with simple attributes
2. **Discoverable**: All handlers are auto-discovered at startup
3. **Type-Safe**: Parameters are automatically converted to the correct types
4. **Extensible**: Add new steps by adding methods with patterns
5. **Maintainable**: Steps are organized in logical handler classes
6. **Testable**: Each handler method can be unit tested independently

## Adding New Steps

To add a new step:

1. Add a method to an existing handler class or create a new one:
```csharp
[StepPattern(@"^verify buffer exists '(.+)'$")]
public StepResult VerifyBufferExists(string bufferName)
{
    // Implementation
}
```

2. The step is automatically available in feature files:
```gherkin
Then verify buffer exists 'test-101'
```

No registration code needed - the attribute-based discovery handles everything!