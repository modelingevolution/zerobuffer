# Harmony - Cross-Platform Process Orchestration Framework

Harmony is a test orchestration framework that enables cross-platform, multi-process testing scenarios. It allows you to write platform-independent tests in Gherkin syntax and automatically generates all possible platform combinations for thorough integration testing.

## Overview

Harmony orchestrates test execution across multiple processes written in different languages (C#, Python, C++), ensuring they work together harmoniously. It uses JSON-RPC for inter-process communication and xUnit Theory tests to generate all platform combinations automatically.

## Key Features

- **Platform-Independent Tests**: Write tests once in Gherkin, run across all platform combinations
- **Automatic Combination Generation**: For N processes and M platforms, generates M^N test combinations
- **Process Context Detection**: Automatically routes test steps to the correct process
- **Clean Architecture**: Well-separated concerns with modular design
- **JSON-RPC Communication**: Reliable cross-process communication protocol

## Architecture

```
ModelingEvolution.Harmony/
├── Configuration/          # Configuration loading and models
├── Core/                   # Core domain models (ScenarioExecution, etc.)
├── Execution/             # Test execution and scenario generation
├── Gherkin/               # Gherkin parsing and processing
├── ProcessManagement/     # Process lifecycle management
└── Features/              # Gherkin feature files
```

## Usage

### Writing Tests

Create Gherkin feature files that use process-based steps:

```gherkin
Feature: Basic Communication
  
  Scenario: Simple write and read
    Given the 'writer' process creates a buffer named "test"
    When the 'writer' writes "Hello, World!"
    And the 'reader' process connects to buffer "test"
    Then the 'reader' should read "Hello, World!"
```

### Configuration

Configure platform executables in `harmony-config.json`:

```json
{
  "platforms": {
    "csharp": {
      "executable": "dotnet",
      "arguments": "run --project path/to/server.csproj",
      "workingDirectory": "."
    },
    "python": {
      "executable": "python",
      "arguments": "test_server.py",
      "workingDirectory": "./python"
    }
  },
  "featuresPath": "Features",
  "defaultTimeoutMs": 30000
}
```

### Running Tests

Tests are executed through xUnit:

```bash
dotnet test
```

## How It Works

1. **Gherkin Parsing**: Harmony parses feature files to extract scenarios and steps
2. **Process Detection**: Identifies which process should execute each step (e.g., "the 'writer' process")
3. **Combination Generation**: Creates all platform combinations (e.g., writer=C#/reader=Python, writer=Python/reader=C#, etc.)
4. **Process Management**: Starts required processes with JSON-RPC servers
5. **Step Routing**: Routes each step to the appropriate process via JSON-RPC
6. **Result Aggregation**: Collects results and reports test outcomes

## Feature File Management

The source of truth for feature files is located in `ZeroBuffer.Harmony.Tests/Features/`. These files are copied to platform-specific test projects:

| Platform | Location | Copy Method | Framework | Notes |
|----------|----------|-------------|-----------|-------|
| **C#** | `csharp/ZeroBuffer.Tests/Features/` | MSBuild target (automatic) | SpecFlow | Copies at build via `CopyFeatureFiles` target in .csproj |
| **Python** | `python/features/` | `copy_features.sh` script | pytest-bdd | Converts "And" steps to Given/When/Then |
| **C++** | `cpp/features/` | CMake (planned) | Google Test | Will generate GTest files via C# generator |

### Platform-Specific Adaptations

- **C# (SpecFlow)**: 
  - MSBuild `CopyFeatureFiles` target copies features before build
  - SpecFlow automatically generates .cs test files from .feature files
  - No manual intervention needed

- **Python (pytest-bdd)**: 
  - Run `./copy_features.sh` to copy and adapt features
  - Script converts "And" steps since pytest-bdd doesn't support @and decorator
  - Must be run manually or as part of CI

- **C++ (Google Test)**: 
  - CMake will copy features during configuration (planned)
  - C# generator tool will create GTest files for native testing
  - Enables dual-mode testing (native GTest + Harmony JSON-RPC)

## Serve Implementation Requirements

All platform serve implementations (C#, Python, C++) MUST follow these requirements:

### Timeout Handling (CRITICAL)

**The serve implementation MUST enforce a 30-second default timeout for step execution. Harmony does NOT implement timeouts.**

- Default timeout: 30 seconds (30000ms) per step
- Harmony currently does NOT send a `timeoutMs` parameter - serves must use the default
- Timeout responses must be structured (not exceptions):

```json
{
    "jsonrpc": "2.0",
    "result": {
        "success": false,
        "error": "Step execution timeout: The step execution time limit of 30000ms was reached...",
        "data": {
            "timeoutType": "STEP_EXECUTION_TIMEOUT",
            "timeoutMs": 30000,
            "elapsedMs": 30005
        },
        "logs": [...]
    },
    "id": 4
}
```

### Exception Handling

**All exceptions MUST be caught and returned as structured responses:**

- Never let exceptions crash the serve process
- Return `success: false` with exception details
- Include exception type, message, and optional stack trace:

```json
{
    "jsonrpc": "2.0",
    "result": {
        "success": false,
        "error": "Exception in step execution: BufferFullException...",
        "data": {
            "exceptionType": "BufferFullException",
            "exceptionMessage": "Cannot write frame, buffer is full",
            "stepText": "the writer process writes data"
        },
        "logs": [...]
    },
    "id": 5
}
```

### JSON-RPC Protocol

Serves must implement these methods with LSP-style Content-Length headers:

1. **health** - Health check
2. **initialize** - Process initialization
3. **discover** - Return available step definitions
4. **executeStep** - Execute a step (with 30s timeout)
5. **cleanup** - Clean up resources
6. **shutdown** - Graceful shutdown

Message format:
```
Content-Length: 123\r\n
\r\n
{"jsonrpc":"2.0","method":"executeStep","params":{...},"id":1}
```

### Implementation Status

| Platform | Component | Timeout (30s) | Exception Handling | Status |
|----------|-----------|---------------|-------------------|---------|
| **C#** | ZeroBuffer.Serve | ❌ TODO | ⚠️ Partial | Needs timeout |
| **Python** | zerobuffer_serve | ❌ TODO | ⚠️ Partial | Needs timeout |
| **C++** | zerobuffer-serve | 📝 Design | 📝 Design | In design phase |

### Action Items

1. **C# (ZeroBuffer.Serve)**: Add 30-second timeout in `ExecuteStepAsync` using `CancellationTokenSource`
2. **Python (zerobuffer_serve)**: Add 30-second timeout in `_handle_execute_step` using `asyncio.wait_for`
3. **C++**: Implement timeout from the start using `std::async` with `wait_for`
4. **All platforms**: Ensure exceptions are caught and returned as structured responses

## Future Enhancements

- Event-based communication. Invokes sends command, in response we have stream of events.
- Extracting common servo logic to proper libraries to make it easier to write a servo. 

## Contributing

Harmony is designed with clean architecture principles. When contributing:
- Maintain separation of concerns
- Add unit tests for new functionality
- Follow existing patterns and conventions
- Document new features

## License

Part of the ZeroBuffer project.