# ZeroBuffer Protocol Tests

This project implements the numbered test scenarios from TEST_SCENARIOS.md in C#.

## Architecture

The tests follow a numbered scheme where:
- Test 1.1 = Test ID 101
- Test 1.2 = Test ID 102
- Test 14.1 = Test ID 1401
- etc.

Each test implements both reader and writer sides that can be run:
1. **Same-process mode**: Both sides in one process (different threads)
2. **Separate-process mode**: Reader and writer in different processes
3. **Cross-platform mode**: For testing interoperability between languages

## Usage

### List all tests:
```bash
dotnet run -- list
```

### Run a test in same-process mode:
```bash
dotnet run -- run -m same-process -t 101 -r both
```

### Run a test in separate-process mode:
```bash
# The runner will spawn both processes
dotnet run -- run -m separate-process -t 101 -r both

# Or run each side manually:
dotnet run -- run -m separate-process -t 101 -r reader -b test-buffer-name &
dotnet run -- run -m separate-process -t 101 -r writer -b test-buffer-name
```

### Run for cross-platform testing:
```bash
# C# reader
dotnet run -- run -m cross-platform -t 101 -r reader -b test-buffer-name

# In another terminal, run writer in different language
./cpp_test_runner cross-platform 101 writer test-buffer-name
```

## Implemented Tests

### Basic Communication (1.x)
- ✅ 101: Simple Write-Read Cycle
- ✅ 102: Multiple Frames Sequential  
- ✅ 103: Buffer Full Handling

### Process Lifecycle (2.x)
- ✅ 201: Writer Crash Detection
- ⚠️  202: Reader Crash Detection (stub)
- ⚠️  203: Reader Replacement After Crash (stub)

### Duplex Channel (14.x)
- ✅ 1401: Basic Request-Response
- ⚠️  1402-1410: Various duplex scenarios (stubs)

## Implementation Status

- ✅ Test infrastructure and runner
- ✅ Basic communication tests (3/3)
- ⚠️  Process lifecycle tests (1/3 implemented)
- ⚠️  Duplex channel tests (1/10 implemented)
- ❌ Other test categories not yet implemented

## Adding New Tests

1. Create a class inheriting from `BaseProtocolTest`
2. Implement `RunReaderAsync` and `RunWriterAsync` methods
3. Register in `TestRegistry.Initialize()`

Example:
```csharp
public class Test_501_NewScenario : BaseProtocolTest
{
    public override int TestId => 501;
    public override string Description => "New Test Scenario";
    
    public override async Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken)
    {
        // Reader implementation
    }
    
    public override async Task<int> RunWriterAsync(string bufferName, CancellationToken cancellationToken)
    {
        // Writer implementation
    }
}
```

## Notes

- Tests use a shared buffer name to coordinate reader/writer
- Each test returns 0 for success, non-zero for failure
- Logging is built into the base class via `Log()` and `LogError()`
- Assertions use `AssertEquals()`, `AssertTrue()`, etc.