# Multiprocess Test Runner Design

## Overview

The Multiprocess Test Runner is a cross-platform test execution framework that validates ZeroBuffer IPC functionality across different language implementations (C#, C++, Python). It uses Gherkin scenarios to describe platform-independent tests and generates all possible platform combinations using xUnit Theory tests.

## Architecture

### Core Components

1. **MultiprocessRunner**: The main test runner that:
   - Parses Gherkin feature files
   - Generates platform combinations
   - Manages subprocess lifecycle
   - Routes commands via JSON-RPC

2. **Process Context Management**: 
   - Detects process context switches in Gherkin steps (e.g., "the 'writer' process")
   - Maps logical process names to platform implementations
   - Maintains active process connections

3. **Platform Servers**: 
   - Each platform (C#, C++, Python) implements a JSON-RPC server
   - Receives step execution commands
   - Performs actual ZeroBuffer operations
   - Returns results/errors

## Test Generation

### Platform Combinations

For a scenario with N processes and M platforms, we generate M^N test cases:

```
Scenario: Two process communication
  Given the 'writer' process is configured
  When the 'reader' process connects
  Then communication succeeds

Platforms: [csharp, cpp, python]
Processes: [writer, reader]

Generated Tests:
- csharp/csharp
- csharp/cpp  
- csharp/python
- cpp/csharp
- cpp/cpp
- cpp/python
- python/csharp
- python/cpp
- python/python
```

### xUnit Theory Integration

```csharp
[Theory]
[MemberData(nameof(GetTestCases))]
public async Task RunScenario(ScenarioExecution execution)
{
    // execution.ToString() => "csharp/python | Test 1.1 - Simple Write-Read"
    await execution.RunAsync();
}

public static IEnumerable<object[]> GetTestCases()
{
    var features = GherkinParser.ParseFeatureFiles("Features/*.feature");
    var platforms = new[] { "csharp", "cpp", "python" };
    
    foreach (var feature in features)
    {
        foreach (var scenario in feature.Scenarios)
        {
            var processes = ExtractProcessNames(scenario);
            var combinations = GeneratePlatformCombinations(platforms, processes.Count);
            
            foreach (var combo in combinations)
            {
                yield return new object[] { 
                    new ScenarioExecution(scenario, processes, combo) 
                };
            }
        }
    }
}
```

## Process Context Detection

### Pattern Recognition

The runner detects process context switches by looking for patterns like:
- `the '{process}' process`
- `'{process}' is`
- `when '{process}'`

Example step parsing:
```gherkin
Given the 'writer' process is configured with buffer size 1024
When the 'reader' process connects to buffer 'test'
Then the 'writer' process should see reader connected
```

Maps to:
```json
{
  "process": "writer",
  "step": "is configured with buffer size 1024"
}
```

## Gherkin Support

### Background Steps

Background steps are executed before each scenario:

```gherkin
Background:
  Given the test environment is configured
  And shared memory is available
```

### Scenario Outlines with Examples

Support for data-driven tests:

```gherkin
Scenario Outline: Write and read frames
  Given the 'writer' process creates buffer '<buffer>'
  When the 'writer' process writes <count> frames
  Then the 'reader' process reads <count> frames
  
  Examples:
    | buffer | count |
    | test1  | 10    |
    | test2  | 100   |
```

## JSON-RPC Protocol

### Request Format

```json
{
  "jsonrpc": "2.0",
  "method": "executeStep",
  "params": {
    "process": "writer",
    "stepType": "given",
    "step": "creates buffer 'test' with size 1024",
    "context": {
      "scenarioId": "test-1-1",
      "platformCombo": "csharp/python",
      "sharedData": {}
    }
  },
  "id": 1
}
```

### Response Format

```json
{
  "jsonrpc": "2.0",
  "result": {
    "success": true,
    "data": {
      "bufferId": "test",
      "actualSize": 1024
    }
  },
  "id": 1
}
```

## Subprocess Management

### Lifecycle

1. **Startup**: Launch platform-specific test servers
2. **Initialize**: Wait for JSON-RPC ready signal
3. **Execute**: Send step commands based on process context
4. **Cleanup**: Graceful shutdown with resource cleanup

### Configuration

```json
{
  "platforms": {
    "csharp": {
      "executable": "dotnet",
      "arguments": "run --project ZeroBuffer.ProtocolTests.Server -- serve",
      "workingDirectory": "."
    },
    "cpp": {
      "executable": "./zerobuffer_test_server",
      "arguments": "serve",
      "workingDirectory": "../cpp/build"
    },
    "python": {
      "executable": "python",
      "arguments": "test_server.py serve",
      "workingDirectory": "../python"
    }
  },
  "defaultTimeout": 30000,
  "initializationDelay": 1000
}
```

## Error Handling

- Process startup failures
- JSON-RPC communication errors
- Test timeout handling
- Resource cleanup on failure
- Detailed error reporting with platform context

## Benefits

1. **True Cross-Platform Testing**: Validates interoperability between all language combinations
2. **Maintainable Test Scenarios**: Single Gherkin file describes behavior for all platforms
3. **Scalable**: Easy to add new platforms or process roles
4. **Clear Test Names**: Generated test names clearly show platform combination
5. **Parallel Execution**: Each platform combination can run independently

## Example Test Execution Flow

1. Parse `BasicCommunication.feature`
2. Find scenario "Test 1.1 - Simple Write-Read"
3. Extract processes: ["writer", "reader"]
4. Generate 9 combinations (3^2)
5. For "csharp/python" combination:
   - Start C# server for "writer" role
   - Start Python server for "reader" role
   - Execute steps, routing to appropriate process
   - Collect results
   - Shutdown servers
6. Report as "csharp/python | Test 1.1 - Simple Write-Read"