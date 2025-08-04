# ZeroBuffer Protocol Tests Design

## Overview

The ZeroBuffer Protocol Tests provide a standardized way to test the ZeroBuffer IPC library across different programming languages and execution modes. The architecture uses:

1. **SpecFlow** for test orchestration with natural language scenarios
2. **Generic JSON-RPC** protocol over stdin/stdout for language-agnostic communication
3. **Configuration-driven** target selection for different language implementations

## Architecture Components

### 1. Generic SpecFlow Test Runner

A completely generic test orchestrator that knows nothing about ZeroBuffer. It uses 12 total methods:
- 3 context-setting methods (Given/When/Then)
- 9 generic step methods (0, 1, or 2 parameters for each of Given/When/Then)

```csharp
[Binding]
public class GenericJsonRpcSteps
{
    // Context setters - detect configured targets
    [Given(@"(.*) '(.*)'")] 
    public async Task Given1(string step, string param1)
    {
        if (IsConfiguredTarget(param1) && step.EndsWith(" is"))
        {
            _context["currentGivenTarget"] = GetOrStartProcess(param1);
            return;
        }
        // Normal step execution...
    }
    
    // Generic steps with 0-2 parameters
    [When(@"(.*)")]
    [When(@"(.*) '(.*)'")] 
    [When(@"(.*) '(.*)' and '(.*)'")]
    // etc...
}
```

### 2. Test Service (JSON-RPC over stdio)

Each language implementation exposes a test service that communicates via JSON-RPC over stdin/stdout:

```csharp
// C# implementation
var rpc = new JsonRpc(Console.OpenStandardOutput(), Console.OpenStandardInput());
rpc.AddLocalRpcTarget(new TestService());
rpc.StartListening();
```

### 3. Configuration-Driven Targets

```json
{
  "targets": {
    "csharp": {
      "executable": "dotnet",
      "arguments": "run --project ZeroBuffer.ProtocolTests -- serve"
    },
    "python": {
      "executable": "python",
      "arguments": "protocol_tests.py serve"
    },
    "cpp": {
      "executable": "./zerobuffer_tests",
      "arguments": "serve"
    }
  }
}
```

### 4. JSON-RPC Protocol

#### Single Method: `executeStep`

The generic test service exposes a single method that interprets natural language steps:

Request:
```json
{
  "jsonrpc": "2.0",
  "method": "executeStep",
  "params": {
    "stepType": "given",
    "step": "create buffer 'test-101' with size '10240'",
    "parameters": ["test-101", "10240"],
    "context": {
      "scenarioId": "abc123",
      "previousResults": {}
    }
  },
  "id": 1
}
```

Response (Success):
```json
{
  "jsonrpc": "2.0",
  "result": {
    "success": true,
    "data": {
      "bufferId": "test-101",
      "created": true
    },
    "context": {
      "bufferId": "test-101",
      "role": "reader"
    }
  },
  "id": 1
}
```

Response (Error):
```json
{
  "jsonrpc": "2.0",
  "error": {
    "code": -32000,
    "message": "Buffer already exists",
    "data": {
      "bufferId": "test-101"
    }
  },
  "id": 1
}
```

The test service maintains its own state and interprets the step strings to determine what actions to take.

### 5. Feature Files (Test Definitions)

```gherkin
Feature: Cross-Platform ZeroBuffer Testing

Scenario: C# Reader with Python Writer
    Given the reader is 'csharp'
    And create buffer 'test-101' with size '10240'
    
    When the writer is 'python'
    And connect to buffer 'test-101'
    And write frame with data 'Hello from Python'
    
    Then the reader is 'csharp'
    And read frame should return 'Hello from Python'
```

### 6. Common Test Steps

All language implementations must support these standard test steps:

#### Buffer Operations
- `createBuffer(metadataSize, payloadSize)` - Create a new shared memory buffer
- `connectToBuffer(bufferName)` - Connect to an existing buffer
- `closeBuffer()` - Close buffer connection

#### Write Operations
- `writeMetadata(data)` - Write metadata to buffer
- `writeFrame(data, sequence)` - Write a frame with sequence number
- `writeFrameZeroCopy(size, sequence, fillPattern)` - Write using zero-copy API

#### Read Operations
- `readMetadata()` - Read metadata from buffer
- `readFrame(timeoutMs)` - Read next frame
- `readFrameZeroCopy(timeoutMs)` - Read using zero-copy API

#### Verification Operations
- `verifyMetadata(expected)` - Verify metadata matches expected
- `verifyFrame(frame, expectedData, expectedSequence)` - Verify frame contents
- `verifyBufferState(expectedState)` - Verify buffer state

#### State Queries
- `isWriterConnected()` - Check if writer is connected
- `isReaderConnected()` - Check if reader is connected
- `getBufferStats()` - Get buffer statistics

#### Process Lifecycle
- `simulateCrash()` - Simulate process crash
- `waitForEvent(eventName, timeoutMs)` - Wait for specific event

### 7. Test Numbering Convention

Tests are organized by category with specific numbering:

- **1xx**: Basic Communication Tests
  - 101: Simple Write-Read Cycle
  - 102: Multiple Frames Sequential
  - 103: Buffer Full Handling
  
- **2xx**: Process Lifecycle Tests
  - 201: Writer Crash Detection
  - 202: Reader Crash Detection
  - 203: Reader Replacement After Crash
  
- **14xx**: Duplex Channel Tests
  - 1401: Basic Request-Response
  - 1402: Sequence Number Correlation
  - 1403: Concurrent Client Operations

### 8. Execution Flow

```
1. SpecFlow reads feature file
2. Detects target from step (e.g., "the reader is 'csharp'")
3. Starts/reuses process from configuration
4. Sends step via JSON-RPC: executeStep("given", "create buffer 'test-101' with size '10240'")
5. Target process interprets and executes step
6. Returns result via JSON-RPC
7. SpecFlow continues with next step
```

## Key Design Principles

1. **Generic Test Runner**: The SpecFlow test runner is completely generic and has no knowledge of ZeroBuffer
2. **Configuration-Driven**: New language implementations are added via configuration only
3. **Natural Language**: Tests are written in Gherkin and read like specifications
4. **Context Switching**: The phrases "the reader is", "the writer is", etc. automatically switch execution context
5. **Single Process Per Language**: Each language runs one process that can handle all roles (reader/writer/server/client)

## Implementation Guidelines

### 1. Adding a New Language

1. Create implementation that exposes JSON-RPC on stdin/stdout
2. Implement the `executeStep` method that interprets step strings
3. Add configuration entry:
   ```json
   "rust": {
     "executable": "./zerobuffer_tests_rust",
     "arguments": "serve"
   }
   ```
4. Use in feature files: `Given the reader is 'rust'`

### 2. Writing New Tests

1. Create feature file with scenarios
2. Use natural language that maps to test steps
3. Switch contexts with "the [role] is '[target]'"
4. No code changes needed in test runner

### 3. Step Interpretation

Each language implementation must parse step strings like:
- `"create buffer 'test-101' with size '10240'"` → Create a Reader with buffer
- `"connect to buffer 'test-101'"` → Create a Writer
- `"write frame with data 'Hello'"` → Use Writer to send data
- `"read frame should return 'Hello'"` → Use Reader and assert

## Benefits

1. **Zero Domain Knowledge in Runner**: Test runner doesn't know about ZeroBuffer
2. **Configuration-Only Extensions**: Add languages without code changes
3. **Natural Language Tests**: Non-developers can read and understand tests
4. **Automatic Context Management**: Language switching is handled automatically
5. **Reusable Framework**: Can test any system that implements the protocol

## Example Test Scenarios

### Test 1.1 - Simple Write-Read Cycle
```gherkin
Given the reader is 'csharp'
And create buffer 'test-101' with metadata size '1024' and payload size '10240'

When the writer is 'python'
And connect to buffer 'test-101'
And write metadata with size '100'
And write frame with size '1024' and sequence '1'

Then the reader is 'csharp'
And metadata should have size '100'
And read frame should have sequence '1' and size '1024'
```

### Test 2.1 - Writer Crash Detection
```gherkin
Given the reader is 'csharp'
And create buffer 'test-201' with size '10240'

When the writer is 'python'
And connect to buffer 'test-201'
And write frame with data 'alive'

Then the reader is 'csharp'
And read frame should return 'alive'

When the writer is 'python'
And simulate crash

Then the reader is 'csharp'
And wait for '2' seconds
And writer should be disconnected
```