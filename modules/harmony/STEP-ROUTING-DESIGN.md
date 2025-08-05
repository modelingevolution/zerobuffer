# Corrected Step Routing Design

## The Problem with Broadcasting

Broadcasting all steps to all processes is inefficient because:
1. **Wasted Processing**: Each process has to parse and check every step
2. **Wasted Network**: Sending unnecessary JSON-RPC calls
3. **Complexity**: Each step implementation needs filtering logic
4. **Confusion**: Processes receive steps they'll never execute

## The Correct Approach: Smart Routing

### Step Routing in Harmony Orchestrator

```csharp
public class StepExecutor : IStepExecutor
{
    private readonly IProcessManager _processManager;
    
    public async Task<StepResult> ExecuteStepAsync(
        StepDefinition step, 
        PlatformCombination platforms,
        CancellationToken cancellationToken)
    {
        // Determine which process should execute this step
        if (step.Process == null)
        {
            // Background steps or coordination steps
            // These might need to go to all processes or a coordinator
            return await ExecuteCoordinationStep(step, platforms, cancellationToken);
        }
        
        // Route to specific process
        var targetProcess = step.Process; // "reader" or "writer"
        var connection = _processManager.GetConnection(targetProcess);
        
        // Send ONLY to the target process
        var result = await connection.InvokeAsync<StepResponse>(
            "executeStep",
            new StepRequest
            {
                StepType = step.Type.ToString().ToLower(),
                Step = step.ProcessedText, // Already stripped of process context
                Parameters = step.Parameters,
                Table = step.Table
            },
            cancellationToken);
        
        return ConvertToStepResult(result);
    }
}
```

### Process Context Extraction

The Harmony orchestrator already extracts process context:

```csharp
// Original step: "Given the 'reader' process creates buffer 'test' with size '1024'"
// After extraction:
var stepDefinition = new StepDefinition
{
    Process = "reader",                                    // Extracted
    ProcessedText = "creates buffer 'test' with size '1024'", // Stripped
    Parameters = new Dictionary<string, object>
    {
        ["buffer_name"] = "test",
        ["size"] = "1024"
    }
};
```

### Simplified Step Implementation

With smart routing, step implementations become much simpler:

```csharp
[Binding]
public class BasicCommunicationSteps
{
    private readonly TestContext _context;
    
    [Given(@"creates buffer '(.*)' with size '(.*)'")]
    public void CreateBuffer(string bufferName, int size)
    {
        // No need to check process - we only receive our steps!
        _context.CreateBuffer(bufferName, size);
    }
    
    [When(@"writes '(.*)' to the buffer")]
    public void WriteToBuffer(string data)
    {
        // Direct implementation, no filtering
        var buffer = _context.GetCurrentBuffer();
        buffer.Write(Encoding.UTF8.GetBytes(data));
    }
}
```

## Special Cases

### 1. Background Steps (No Process Specified)

```gherkin
Background:
    Given the test mode is configured
```

Options:
- Send to all processes (if they all need configuration)
- Send to a coordinator process
- Handle in orchestrator without sending to processes

### 2. Cross-Process Coordination

```gherkin
When the writer notifies the reader
```

This might need special handling:
- Orchestrator manages the coordination
- Or one process acts as coordinator

### 3. Process Initialization

The orchestrator can send initialization info when starting a process:

```json
{
    "jsonrpc": "2.0",
    "method": "initialize",
    "params": {
        "role": "reader",
        "platform": "csharp",
        "scenario": "Test 1.1 - Simple Write-Read Cycle",
        "sharedConfig": {
            "bufferNamingPrefix": "test-run-12345"
        }
    },
    "id": "init"
}
```

## Benefits of Smart Routing

1. **Efficiency**: Each process only receives relevant steps
2. **Simplicity**: No filtering logic in step implementations
3. **Performance**: Fewer JSON-RPC calls
4. **Clarity**: Each process has a clear, focused role
5. **Debugging**: Easier to trace what each process is doing

## Example Flow

For scenario: `csharp/python | Test 1.1 - Simple Write-Read Cycle`

```
Step: "Given the 'reader' process creates buffer 'test'"
  ├─ Orchestrator extracts: process="reader"
  ├─ Finds reader is C# process
  └─ Sends ONLY to C# process: "creates buffer 'test'"

Step: "And sets buffer size to '1024'"
  ├─ Orchestrator detects And keyword
  ├─ Inherits process="reader" from previous step
  └─ Sends ONLY to C# process: "sets buffer size to '1024'"

Step: "When the 'writer' process connects to buffer 'test'"
  ├─ Orchestrator extracts: process="writer"
  ├─ Finds writer is Python process
  └─ Sends ONLY to Python process: "connects to buffer 'test'"

Step: "And writes 'Hello' to the buffer"
  ├─ Orchestrator detects And keyword
  ├─ Inherits process="writer" from previous step
  └─ Sends ONLY to Python process: "writes 'Hello' to the buffer"

Step: "Then the 'reader' process should read 'Hello'"
  ├─ Orchestrator extracts: process="reader"
  ├─ Finds reader is C# process
  └─ Sends ONLY to C# process: "should read 'Hello'"
```

## Handling And/But Keywords

The orchestrator must handle Gherkin's And/But keywords which inherit context from the previous step:

```csharp
public class StepRouter
{
    private string? _lastProcess;
    private StepType _lastStepType = StepType.Given;
    
    public async Task<StepResult> RouteStep(
        StepDefinition step,
        PlatformCombination platforms)
    {
        // Handle And/But inheritance
        var effectiveStepType = step.Type;
        var effectiveProcess = step.Process;
        
        if (step.Type == StepType.And || step.Type == StepType.But)
        {
            effectiveStepType = _lastStepType;
            effectiveProcess = effectiveProcess ?? _lastProcess;
        }
        else
        {
            _lastStepType = step.Type;
        }
        
        if (effectiveProcess != null)
        {
            _lastProcess = effectiveProcess;
            
            // Route to specific process
            var platform = platforms.GetPlatform(effectiveProcess);
            var connection = _processManager.GetConnection(effectiveProcess);
            
            return await connection.InvokeAsync<StepResult>(
                "executeStep",
                new
                {
                    stepType = effectiveStepType.ToString().ToLower(),
                    step = step.ProcessedText,
                    parameters = step.Parameters,
                    table = step.Table
                });
        }
        else
        {
            // No process specified - coordination step
            return await HandleCoordinationStep(step);
        }
    }
}
```

## Updated TestContext

Since each process only receives its own steps:

```csharp
public class TestContext
{
    // No need for CurrentProcess - we know who we are from initialization
    public string Role { get; set; } // Set during initialization
    public string Platform { get; set; } // Set during initialization
    
    // Shared state remains the same
    private readonly Dictionary<string, IZeroBuffer> _buffers = new();
    
    // Simpler API - no process checking needed
    public void CreateBuffer(string name, int size)
    {
        var buffer = new ZeroBuffer(name, size);
        _buffers[name] = buffer;
    }
}
```

This is much cleaner and more efficient!