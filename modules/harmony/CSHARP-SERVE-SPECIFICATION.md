# C# Serve Process Specification

## Overview

The C# serve process acts as a bridge between the Harmony test orchestrator and the actual ZeroBuffer test implementation. It receives JSON-RPC requests over stdin/stdout and executes the corresponding SpecFlow steps.

## Architecture

### Project Structure

```
zerobuffer/
├── csharp/
│   ├── ZeroBuffer/                           # Main library
│   │   └── ZeroBuffer.csproj
│   │
│   ├── ZeroBuffer.Tests.Steps/               # SpecFlow xUnit project
│   │   ├── ZeroBuffer.Tests.Steps.csproj
│   │   ├── Features/                         # Copy of feature files
│   │   │   ├── BasicCommunication.feature
│   │   │   ├── Benchmarks.feature
│   │   │   └── ...
│   │   ├── StepDefinitions/
│   │   │   ├── BasicCommunicationSteps.cs
│   │   │   ├── InitializationSteps.cs
│   │   │   └── ...
│   │   └── Support/
│   │       └── TestContext.cs
│   │
│   └── ZeroBuffer.Harmony.Server/            # JSON-RPC serve console app
│       ├── ZeroBuffer.Harmony.Server.csproj
│       ├── Program.cs
│       ├── JsonRpcServer.cs
│       ├── StepExecutor.cs
│       └── StepRegistry.cs
```

## JSON-RPC Protocol

### Request Format

```json
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "process": "reader",
        "stepType": "given",
        "step": "the 'reader' process creates buffer 'test' with size '10240'",
        "originalStep": "the 'reader' process creates buffer 'test' with size '10240'",
        "parameters": {
            "process": "reader",
            "buffer_name": "test",
            "size": "10240"
        },
        "table": {  // Optional, for steps with tables
            "headers": ["write_pos", "read_pos", "expected"],
            "rows": [
                {"write_pos": "100", "read_pos": "50", "expected": "50"},
                {"write_pos": "200", "read_pos": "150", "expected": "50"}
            ]
        }
    },
    "id": "1"
}
```

### Response Format

```json
{
    "jsonrpc": "2.0",
    "result": {
        "success": true,
        "error": null,
        "data": {
            "buffer_name": "test",
            "actual_size": 10240
        },
        "logs": [
            {
                "level": "INFO",
                "message": "Buffer 'test' created successfully"
            }
        ]
    },
    "id": "1"
}
```

## Step Discovery and Execution

### Step Registration Using Reflection

```csharp
public class StepRegistry
{
    private readonly Dictionary<StepDefinitionKey, StepDefinitionInfo> _steps;
    
    public void DiscoverSteps(Assembly assembly)
    {
        var stepTypes = assembly.GetTypes()
            .Where(t => t.GetMethods()
                .Any(m => m.GetCustomAttributes<GivenAttribute>().Any() ||
                         m.GetCustomAttributes<WhenAttribute>().Any() ||
                         m.GetCustomAttributes<ThenAttribute>().Any()));
        
        foreach (var type in stepTypes)
        {
            RegisterStepsFromType(type);
        }
    }
    
    private void RegisterStepsFromType(Type type)
    {
        foreach (var method in type.GetMethods())
        {
            // Extract Given/When/Then attributes
            var givenAttr = method.GetCustomAttribute<GivenAttribute>();
            if (givenAttr != null)
            {
                RegisterStep(StepType.Given, givenAttr.Regex, type, method);
            }
            // Similar for When and Then...
        }
    }
}
```

### Step Execution

```csharp
public class StepExecutor
{
    private readonly StepRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    
    public async Task<StepResult> ExecuteStep(StepRequest request)
    {
        // 1. Find matching step definition
        var stepDef = _registry.FindStep(request.StepType, request.Step);
        
        // 2. Create instance of step class
        var instance = ActivatorUtilities.CreateInstance(
            _serviceProvider, 
            stepDef.DeclaringType);
        
        // 3. Extract parameters from regex match
        var parameters = ExtractParameters(stepDef.Regex, request.Step);
        
        // 4. Convert parameters to correct types
        var methodParams = ConvertParameters(
            stepDef.Method.GetParameters(), 
            parameters,
            request.Table);
        
        // 5. Execute the step
        var result = stepDef.Method.Invoke(instance, methodParams);
        
        // 6. Handle async methods
        if (result is Task task)
        {
            await task;
        }
        
        return new StepResult { Success = true };
    }
}
```

## Step Definition Translation

### SpecFlow Step Example

```csharp
[Binding]
public class BasicCommunicationSteps
{
    private readonly TestContext _context;
    
    public BasicCommunicationSteps(TestContext context)
    {
        _context = context;
    }
    
    // Steps must accept process parameter even if ignored
    [Given(@"the '([^']+)' process creates buffer '([^']+)' with size '(\d+)'")]
    public void GivenProcessCreatesBuffer(string process, string bufferName, int size)
    {
        // Process parameter accepted but not used - implementation is process-agnostic
        _context.CreateBuffer(bufferName, size);
    }
    
    [When(@"the '([^']+)' process connects to buffer '([^']+)'")]
    public void WhenProcessConnectsToBuffer(string process, string bufferName)
    {
        // Process parameter accepted but not used
        var buffer = ZeroBuffer.OpenExisting(bufferName);
        _context.SetBuffer(bufferName, buffer);
    }
    
    [When(@"the '([^']+)' process writes '([^']+)' to buffer '([^']+)'")]
    public void WhenProcessWritesToBuffer(string process, string data, string bufferName)
    {
        // Process parameter accepted but not used
        var buffer = _context.GetBuffer(bufferName);
        buffer.Write(Encoding.UTF8.GetBytes(data));
    }
    
    [Then(@"the '([^']+)' process should read '([^']+)' from buffer '([^']+)'")]
    public void ThenProcessShouldReadFromBuffer(string process, string expectedData, string bufferName)
    {
        // Process parameter accepted but not used
        var buffer = _context.GetBuffer(bufferName);
        var data = buffer.Read();
        var actualData = Encoding.UTF8.GetString(data);
        
        actualData.Should().Be(expectedData);
    }
    
    // And steps without process parameter
    [And(@"writes frame with size '(\d+)'")]
    public void AndWritesFrameWithSize(int size)
    {
        var buffer = _context.GetCurrentBuffer();
        buffer.WriteFrame(GenerateData(size));
    }
}
```

## Context Management

### Test Context

```csharp
public class TestContext
{
    private readonly Dictionary<string, IZeroBuffer> _buffers = new();
    private readonly Dictionary<string, object> _testData = new();
    
    // Set during initialization by the serve process
    public string Role { get; set; }        // "reader" or "writer"
    public string Platform { get; set; }    // "csharp"
    public string ScenarioName { get; set; }
    
    public void CreateBuffer(string name, int size)
    {
        var buffer = new ZeroBuffer(name, size);
        _buffers[name] = buffer;
    }
    
    public void SetBuffer(string name, IZeroBuffer buffer)
    {
        _buffers[name] = buffer;
    }
    
    public IZeroBuffer GetBuffer(string name)
    {
        if (!_buffers.ContainsKey(name))
            throw new KeyNotFoundException($"Buffer '{name}' not found");
        return _buffers[name];
    }
    
    public void SetData(string key, object value)
    {
        _testData[key] = value;
    }
    
    public T GetData<T>(string key)
    {
        return (T)_testData[key];
    }
    
    public void Reset()
    {
        foreach (var buffer in _buffers.Values)
        {
            buffer.Dispose();
        }
        _buffers.Clear();
        _testData.Clear();
    }
}
```

## Main Program

```csharp
public class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "serve")
        {
            // Configure services
            var services = new ServiceCollection();
            services.AddSingleton<TestContext>();
            services.AddTransient<BasicCommunicationSteps>();
            // Add other step classes...
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Create JSON-RPC server
            var server = new JsonRpcServer(
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(),
                serviceProvider);
            
            // Start listening
            await server.ListenAsync();
        }
    }
}
```

## Key Design Decisions

1. **Smart Routing**: Orchestrator routes steps only to the appropriate process
2. **Shared State**: TestContext manages state within each process (buffers, connections, etc.)
3. **Parameter Conversion**: Automatic conversion from string parameters to typed parameters
4. **Table Support**: Steps can receive Gherkin tables as structured data
5. **Async Support**: Steps can be async for operations like waiting on semaphores
6. **Logging**: All operations are logged and returned in the JSON-RPC response
7. **Process Initialization**: Each process receives its role and configuration at startup

## Benefits

1. **Reusable Tests**: Same feature files and step definitions used for both unit tests and cross-platform tests
2. **Type Safety**: Full C# type safety in step implementations
3. **Testability**: Step definitions can be unit tested independently
4. **Flexibility**: Can use any DI container or testing framework features
5. **Debugging**: Can attach debugger to the serve process for troubleshooting

## Implementation Notes

- Use StreamJsonRpc for robust JSON-RPC implementation
- Consider using Microsoft.Extensions.DependencyInjection for DI
- Log all operations for debugging multi-process scenarios
- Handle process cleanup gracefully on shutdown
- Support both synchronous and asynchronous step methods