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

## Future Enhancements

- Support for more platforms (Rust, Go, etc.)
- Parallel test execution
- Performance benchmarking
- Visual test result reporting
- Integration with CI/CD pipelines

## Contributing

Harmony is designed with clean architecture principles. When contributing:
- Maintain separation of concerns
- Add unit tests for new functionality
- Follow existing patterns and conventions
- Document new features

## License

Part of the ZeroBuffer project.