# Harmony Tests

This project contains unit tests for the Harmony multiprocess test runner framework.

## Test Approach

### Mock Process Management
Tests use mocked implementations of `IProcessManager` and `IProcessConnection` to avoid starting real processes during unit tests. This ensures:
- Tests run quickly and reliably
- No dependency on external executables
- Predictable test behavior

### Test Configuration
Tests use a separate configuration (`harmony-config.test.json`) that specifies mock executables. The `TestBase` class provides a consistent test configuration for all tests.

### Key Test Classes

- **MultiprocessTests** - Tests the main scenario execution flow with mocked processes
- **GherkinParsingTests** - Tests feature file parsing and scenario extraction
- **ProcessContextExtractorTests** - Tests extraction of process context from Gherkin steps
- **TableHandlingTests** - Tests handling of Gherkin tables (pending full implementation)
- **JsonRpcCommunicationTests** - Demonstrates the JSON-RPC protocol used for process communication

## Running Tests

```bash
dotnet test
```

## Test Data

Feature files are copied from the main project and represent real test scenarios from the ZeroBuffer project.