# Scenario Development Guide

## Prerequisites
1. Read ZeroBuffer protocol documentation (all markdown files)
2. Read harmony documentation. (all markdown files)

## File Locations
- **ZeroBuffer Library**: `ZeroBuffer/` (core C# implementation)
- **Feature Files**: `ZeroBuffer.Tests/Features/` (auto-copied from `../../ZeroBuffer.Harmony.Tests/Features/`)
- **Step Definitions**: `ZeroBuffer.Tests/StepDefinitions/` (create as needed for each feature)

## Step Definition Implementation

### Process Parameters
- Steps with `the '([^']+)' process` must accept but ignore the process parameter
- 'And' steps MUST NOT have process parameters (inherit context from previous step)
- Exception: 'And' steps with explicit process switch

```csharp
// Process parameter - accept but ignore
[Given(@"the '([^']+)' process creates buffer '([^']+)'")]
public void GivenProcessCreatesBuffer(string process, string bufferName)
{
    var buffer = new ZeroBuffer(bufferName);
    _buffers[bufferName] = buffer;
}

// And step - no process parameter
[And(@"writes frame with size '(\d+)'")]
public void AndWritesFrameWithSize(int size)
{
    var buffer = GetCurrentBuffer();
    buffer.WriteFrame(GenerateData(size));
}
```

### State Management
```csharp
private readonly Dictionary<string, ZeroBuffer> _buffers = new();
private readonly Dictionary<string, object> _testData = new();
private Exception _lastException;
private string _currentBuffer;
```

### Error Handling
- Expected failures: capture exception, don't throw
- Store in `_lastException` for Then validation

## Implementation Rules

1. **Every step must have real implementation** - no empty logging steps
2. **Match feature file exactly** - regex must match feature file text
3. **No unused steps** - delete if not in feature file
4. **No process-specific logic** - treat all processes the same, the process might only refer to specific zero-buffer client (reader/writer).

## Development Process - YOUR TODO LIST

0. READ THE PREREQUISITES if you haven't!
1. Read scenario in feature file; Try to understand it and assess if it make sense. If it doesn't make sense, ask for clarification.
2. Identify required step definitions
3. Analize if we need to exchange data betwen the processes, or we can simply relay on patterns. If we need to exchange data - stop working and tell me that.
4. Implement each step with actual ZeroBuffer operations
5. Run single test: `csharp/test.sh [test-number]`; DO NOT RUN ALL TESTS AT ONCE!
6. Fix issues; If the test fails, it can mean that the implementation is incorrect. Investigate the failure, read the protocol documentaiton and try to fix the implementation. 
7. Only if GREEN, run again with Harmony: `../../test.sh csharp [test-number]`; Fix any issues

### Troubleshooting

1. test.sh scripts may fail do to wrong implementation of those scripts. If they do not work, fix them. With them you should be able to run single tests easily. If this is not possible do not look for workaround! Fix the script or ask for help.
   - In-process tests: `csharp/test.sh`
   - Harmony cross-process tests: `../../test.sh` (in ZeroBuffer.Harmony.Tests directory)
2. Some scenarios might be too advanced for harmony. This is very important discovery. We need to know why. Ask immedietely for help if you think the step isn't feasible to implement. (Actually I'm sure, some will)
3. Do not work on workarounds the Development Process. If you cannot proceed with next points from the Development Process, ask for help and stop working.
4. **Frame vs FrameRef confusion**: `Frame` is a stack-only struct that cannot be stored in fields or collections. For testing purposes, use `FrameRef` which can be stored in fields and compared later. 

## Common Patterns

### Buffer Naming Service
Use `IBufferNamingService` to ensure unique buffer names across processes:
```csharp
var actualName = _bufferNaming.GetBufferName("test-buffer");
var reader = new Reader(actualName, config);
```

### Test Data Patterns
Use `TestDataPatterns` for consistent data generation and assertions across processes:
```csharp
// Writer process: Generate frame data
var data = TestDataPatterns.GenerateFrameData(size: 1024, sequence: 1);
writer.WriteFrame(data);

// Reader process: Generate expected data for assertion
var expectedData = TestDataPatterns.GenerateFrameData(frameData.Length, frameSequence);
Assert.Equal(expectedData, frameData);
```

### Buffer Creation
```csharp
var buffer = new ZeroBuffer(name, metadataSize, payloadSize);
_buffers[name] = buffer;
_currentBuffer = name;
```

### Write Operations
```csharp
var buffer = _buffers[_currentBuffer];
buffer.WriteFrame(data);
```

### Read Operations
```csharp
var frame = buffer.ReadFrame();
Assert.Equal(expectedSize, frame.Length);
```

### Expected Failures
```csharp
try 
{
    buffer.WriteFrame(oversizedData);
}
catch (Exception ex)
{
    _lastException = ex;
}
```
