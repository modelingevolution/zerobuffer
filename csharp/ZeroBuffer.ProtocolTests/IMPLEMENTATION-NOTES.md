# Implementation Notes for Multiprocess Test Runner

## Core Design Decisions

### 1. Health Check
- Simple ping/health endpoint in JSON-RPC servers
- No complex synchronization needed

### 2. Resource Cleanup
- **Platform Server Responsibility**: Each platform server must clean up its own resources
- **Process Lifecycle**: Multiprocess runner handles process start/stop only
- Built-in Gherkin steps for process management:
  ```gherkin
  Given the 'writer' process starts
  ...
  Then the 'writer' process stops
  ```

### 3. Platform Capabilities
- Keep it simple for now
- Deal with platform limitations (like Python zero-copy) when needed
- No pre-emptive capability system

### 4. Debugging and Logging
- **Step-Level Logging**: Each JSON-RPC response includes execution logs
- Response format:
  ```json
  {
    "success": true,
    "data": { ... },
    "logs": [
      "[DEBUG] Creating buffer 'test' with size 1024",
      "[INFO] Buffer created successfully",
      "[DEBUG] Buffer ID: test, Handle: 0x1234"
    ]
  }
  ```
- Multiprocess runner aggregates logs with process/platform context

### 5. Performance
- Not a primary concern
- Focus on correctness and clarity
- Can optimize later if needed

### 6. State Management Clarification
- Each process maintains its own state
- No shared state service needed
- State coordination happens through IPC (the thing we're testing!)

### 7. Timeouts
- Add timeout support at step and scenario level
- Default timeout: 30 seconds per step
- Override via tags:
  ```gherkin
  @timeout:5000
  Scenario: Quick test
  ```

### 8. Platform Variants
- Support conditional execution based on platform
- Use tags to mark platform-specific tests:
  ```gherkin
  @platform:windows
  Scenario: Windows named pipes test
  
  @platform:linux,macos
  Scenario: Unix domain socket test
  ```

## What We're NOT Doing

1. **No Performance Metrics** (in v1)
2. **No Recovery Actions** - tests should be deterministic
3. **No Data Generators** - explicit test data only
4. **No Complex State Service** - processes coordinate via IPC
5. **No Capability Discovery** - handle limitations as they arise

## JSON-RPC Protocol Update

### Request
```json
{
  "jsonrpc": "2.0",
  "method": "executeStep",
  "params": {
    "process": "writer",
    "stepType": "when",
    "step": "writes frame with size 1024",
    "context": {
      "scenarioId": "test-1-1",
      "platform": "csharp"
    }
  },
  "id": 1
}
```

### Response with Logs
```json
{
  "jsonrpc": "2.0",
  "result": {
    "success": true,
    "data": {
      "frameId": 123,
      "actualSize": 1024
    },
    "logs": [
      "[2024-01-01 10:00:00.123] Writing frame size=1024",
      "[2024-01-01 10:00:00.125] Frame written successfully, id=123"
    ]
  },
  "id": 1
}
```

## Process Management in Gherkin

### Implicit Process Lifecycle
Processes start when first referenced and stop after scenario:
```gherkin
Scenario: Auto lifecycle
  When the 'writer' process writes data  # Starts writer if needed
  Then the 'reader' process reads data   # Starts reader if needed
  # Both processes stopped after scenario
```

### Explicit Process Control
For scenarios testing process crashes/restarts:
```gherkin
Scenario: Process restart
  Given the 'writer' process writes data
  When the 'writer' process stops
  And the 'writer' process starts
  Then the 'writer' process can write again
```

## Platform Server Requirements

Each platform server must:

1. **Implement JSON-RPC over stdin/stdout**
2. **Clean up all resources on shutdown**
3. **Return logs with each step execution**
4. **Handle timeout gracefully**
5. **Support health check endpoint**

## Multiprocess Runner Responsibilities

1. **Parse Gherkin files**
2. **Generate platform combinations**
3. **Start/stop platform processes**
4. **Route steps to correct process**
5. **Aggregate logs with context**
6. **Report test results**
7. **Kill processes on timeout**

## Simple and Focused

The key insight is to keep the test infrastructure simple and let the complexity live in the actual IPC tests. The multiprocess runner is just a coordinator, not a complex state machine.