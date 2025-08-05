# JSON-RPC Communication Examples

## Example Test Execution

For test: `csharp/python | Test 1.1 - Simple Write-Read Cycle`

### Process Initialization

Before any steps are executed, each process receives initialization:

```json
{
    "jsonrpc": "2.0",
    "method": "initialize",
    "params": {
        "role": "reader",
        "platform": "csharp",
        "scenario": "Test 1.1 - Simple Write-Read Cycle",
        "testRunId": "run-12345"
    },
    "id": "init"
}
```

### Background Steps

#### Step 1: Test Mode Configuration
```json
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "stepType": "given",
        "step": "the test mode is configured",
        "parameters": {}
    },
    "id": "1"
}
```

Note: Background steps without process context might be sent to all processes or handled by the orchestrator.

Response:
```json
{
    "jsonrpc": "2.0",
    "result": {
        "success": true,
        "error": null,
        "data": {},
        "logs": [
            {"level": "INFO", "message": "Test mode configured"}
        ]
    },
    "id": "1"
}
```

### Scenario Steps

#### Step 2: Reader Creates Buffer (sent ONLY to C# process)
```json
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "stepType": "given",
        "step": "creates buffer 'simple-test' with default configuration",
        "parameters": {
            "buffer_name": "simple-test"
        }
    },
    "id": "2"
}
```

Note: Process context already extracted by orchestrator. Step routed only to reader (C#).

#### Step 3: Writer Connects (sent ONLY to Python process)
```json
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "stepType": "when",
        "step": "connects to buffer 'simple-test'",
        "parameters": {
            "buffer_name": "simple-test"
        }
    },
    "id": "3"
}
```

Note: Process context already extracted by orchestrator. Step routed only to writer (Python).

#### Step 4: Writer Writes Data
```json
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "process": "writer",
        "stepType": "when",
        "step": "writes 'Hello, ZeroBuffer!' to the buffer",
        "originalStep": "And the 'writer' process writes 'Hello, ZeroBuffer!' to the buffer",
        "parameters": {
            "data": "Hello, ZeroBuffer!"
        }
    },
    "id": "4"
}
```

#### Step 5: Reader Reads Data
```json
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "process": "reader",
        "stepType": "then",
        "step": "should read 'Hello, ZeroBuffer!' from the buffer",
        "originalStep": "Then the 'reader' process should read 'Hello, ZeroBuffer!' from the buffer",
        "parameters": {
            "expected_data": "Hello, ZeroBuffer!"
        }
    },
    "id": "5"
}
```

## Example with Table Data

For a step with table data from EdgeCases.feature:

```json
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "process": "reader",
        "stepType": "when",
        "step": "test continuous_free_bytes calculation with:",
        "originalStep": "When test continuous_free_bytes calculation with:",
        "parameters": {},
        "table": {
            "headers": ["write_pos", "read_pos", "expected_result", "scenario"],
            "rows": [
                {
                    "write_pos": "5000",
                    "read_pos": "2000",
                    "expected_result": "calculated",
                    "scenario": "write_pos > read_pos"
                },
                {
                    "write_pos": "2000",
                    "read_pos": "5000",
                    "expected_result": "calculated",
                    "scenario": "write_pos < read_pos"
                }
            ]
        }
    },
    "id": "6"
}
```

## Test Name Examples

The generated test names follow the pattern: `{platforms} | {scenario name}`

Examples:
- `csharp/python | Test 1.1 - Simple Write-Read Cycle`
- `python/cpp | Test 1.2 - Multiple Frames Sequential`
- `cpp/csharp | Test 1.3 - Buffer Full Handling`

Test IDs follow the pattern: `{platform1}-{platform2}-{scenario-name-kebab-case}`

Examples:
- `csharp-python-test-1-1---simple-write-read-cycle`
- `python-cpp-test-1-2---multiple-frames-sequential`
- `cpp-csharp-test-1-3---buffer-full-handling`