# Process Context and Step Execution Design

## Overview

This document describes how the Multiprocess Test Runner handles process context switching and step execution across different platform implementations.

## Process Context Detection

### Patterns

The runner detects process context by looking for these patterns in Gherkin steps:

1. **Direct process reference**: `the '{name}' process`
   ```gherkin
   Given the 'writer' process creates a buffer
   When the 'reader' process connects
   ```

2. **Process as subject**: `'{name}' {action}`
   ```gherkin
   Given 'writer' is configured with default settings
   When 'reader' attempts to connect
   ```

3. **Contextual process reference**: `the {role} process`
   ```gherkin
   Given the writer process starts
   When the reader process connects
   ```

### Context Extraction

```csharp
public class ProcessContextExtractor
{
    private static readonly Regex ProcessPatterns = new Regex(
        @"(?:the\s+)?'(?<process>\w+)'(?:\s+process)?|" +
        @"the\s+(?<process>\w+)\s+process",
        RegexOptions.IgnoreCase
    );
    
    public (string? process, string step) ExtractContext(string fullStep)
    {
        var match = ProcessPatterns.Match(fullStep);
        if (match.Success)
        {
            var process = match.Groups["process"].Value;
            var step = fullStep.Replace(match.Value, "").Trim();
            return (process, step);
        }
        return (null, fullStep);
    }
}
```

## Step Routing

### Step Execution Flow

1. **Parse Step**: Extract process context from Gherkin step
2. **Map to Platform**: Use current test's platform combination
3. **Route to Server**: Send to appropriate platform server
4. **Execute**: Platform server performs actual operation
5. **Return Result**: Propagate result back to test runner

### Example Flow

```
Scenario: csharp/python | Test 1.1
Step: "Given the 'writer' process creates buffer 'test'"

1. Extract: process="writer", step="creates buffer 'test'"
2. Map: writer -> csharp (from test combination)
3. Route: Send to C# server via JSON-RPC
4. Execute: C# server creates ZeroBuffer
5. Result: { success: true, bufferId: "test" }
```

## Shared Context Management

### Cross-Process Data Sharing

Scenarios often require sharing data between processes:

```gherkin
Given the 'writer' process creates buffer 'test' with size 1024
When the 'reader' process connects to buffer 'test'
Then the 'reader' process should see buffer size 1024
```

### Context Propagation

```csharp
public class ScenarioContext
{
    private readonly Dictionary<string, object> _sharedData = new();
    private readonly Dictionary<string, ProcessInfo> _processes = new();
    
    public void SetProcessData(string process, string key, object value)
    {
        var processKey = $"{process}.{key}";
        _sharedData[processKey] = value;
    }
    
    public T? GetProcessData<T>(string process, string key)
    {
        var processKey = $"{process}.{key}";
        return _sharedData.TryGetValue(processKey, out var value) 
            ? (T)value 
            : default;
    }
    
    public object GetSharedContext()
    {
        return new
        {
            processes = _processes.Keys,
            data = _sharedData
        };
    }
}
```

## Platform Server Implementation

### Server Interface

Each platform must implement:

```csharp
public interface ITestServer
{
    Task<StepResult> ExecuteStepAsync(StepRequest request);
    Task<bool> InitializeAsync();
    Task ShutdownAsync();
}

public class StepRequest
{
    public string Process { get; set; }
    public string StepType { get; set; }  // given, when, then
    public string Step { get; set; }
    public Dictionary<string, object> Context { get; set; }
}

public class StepResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}
```

### Platform-Specific Handlers

Example C# implementation:

```csharp
public class CSharpTestServer : ITestServer
{
    private readonly Dictionary<string, IProcessHandler> _handlers = new();
    
    public async Task<StepResult> ExecuteStepAsync(StepRequest request)
    {
        if (!_handlers.TryGetValue(request.Process, out var handler))
        {
            handler = CreateProcessHandler(request.Process);
            _handlers[request.Process] = handler;
        }
        
        return request.StepType.ToLower() switch
        {
            "given" => await handler.HandleGivenAsync(request.Step, request.Context),
            "when" => await handler.HandleWhenAsync(request.Step, request.Context),
            "then" => await handler.HandleThenAsync(request.Step, request.Context),
            _ => new StepResult { Success = false, Error = "Unknown step type" }
        };
    }
}
```

## Step Parsing and Execution

### Pattern-Based Step Handlers

```csharp
public class WriterProcessHandler : IProcessHandler
{
    private readonly Dictionary<Regex, Func<Match, Context, Task<StepResult>>> _handlers;
    
    public WriterProcessHandler()
    {
        _handlers = new()
        {
            [new Regex(@"creates buffer '(.+)' with size (\d+)")] = CreateBufferAsync,
            [new Regex(@"writes (\d+) frames")] = WriteFramesAsync,
            [new Regex(@"disconnects")] = DisconnectAsync
        };
    }
    
    public async Task<StepResult> HandleGivenAsync(string step, Context context)
    {
        foreach (var (pattern, handler) in _handlers)
        {
            var match = pattern.Match(step);
            if (match.Success)
            {
                return await handler(match, context);
            }
        }
        
        return new StepResult 
        { 
            Success = false, 
            Error = $"No handler for step: {step}" 
        };
    }
}
```

## Background and Examples Support

### Background Execution

Background steps run before each scenario:

```csharp
public class ScenarioExecutor
{
    public async Task ExecuteScenarioAsync(
        Scenario scenario, 
        Dictionary<string, string> platformMapping)
    {
        // Execute background steps
        if (scenario.Background != null)
        {
            foreach (var step in scenario.Background.Steps)
            {
                await ExecuteStepAsync(step, platformMapping);
            }
        }
        
        // Execute scenario steps
        foreach (var step in scenario.Steps)
        {
            await ExecuteStepAsync(step, platformMapping);
        }
    }
}
```

### Examples Expansion

Scenario outlines are expanded into individual tests:

```csharp
public IEnumerable<ScenarioInstance> ExpandScenarioOutline(ScenarioOutline outline)
{
    foreach (var exampleRow in outline.Examples.Rows)
    {
        var scenario = new ScenarioInstance
        {
            Name = $"{outline.Name} - {exampleRow}",
            Steps = outline.Steps.Select(s => 
                SubstituteParameters(s, exampleRow)).ToList()
        };
        
        yield return scenario;
    }
}
```

## Error Handling and Reporting

### Detailed Error Context

```csharp
public class ProcessStepException : Exception
{
    public string Process { get; }
    public string Platform { get; }
    public string Step { get; }
    public string StepType { get; }
    public Dictionary<string, object> Context { get; }
    
    public override string ToString()
    {
        return $"""
            Process Step Execution Failed:
            Platform: {Platform}
            Process: {Process}
            Step Type: {StepType}
            Step: {Step}
            Error: {Message}
            Context: {JsonSerializer.Serialize(Context)}
            """;
    }
}
```

## Benefits of Process-Based Approach

1. **Clear Ownership**: Each step clearly belongs to a specific process
2. **Realistic Testing**: Mimics actual multi-process scenarios
3. **Platform Independence**: Same scenarios work across all platforms
4. **Easy Debugging**: Clear process/platform context in errors
5. **Natural Language**: Gherkin remains readable and maintainable