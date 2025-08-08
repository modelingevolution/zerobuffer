# ZeroBuffer C++ Testing Guide

## Overview
This directory contains the testing infrastructure for ZeroBuffer C++, including:
- Generated tests from Gherkin feature files
- Cross-platform test executables
- Step definitions for BDD-style testing

## Test Structure

### Generated Tests (`tests/generated/`)
- Auto-generated from feature files using `harmony-cpp-gen` tool
- Google Test based test files
- One test file per feature (e.g., `test_basic_communication.cpp`)

### Step Definitions (`step_definitions/`)
- `step_registry.cpp` - Central registry for all step definitions
- `test_context.cpp` - Test context management
- `basic_communication_steps.cpp` - Steps for basic communication scenarios
- Additional step files to be added as features are implemented

### Cross-Platform Tests (`tests/cross-platform/`)
- `test_reader.cpp` - Standalone reader executable for cross-platform testing
- `test_writer.cpp` - Standalone writer executable for cross-platform testing

## Step Definition Implementation

### Process Parameters
- Steps with `the '([^']+)' process` must accept but ignore the process parameter
- 'And' steps inherit context from previous step (converted to Given/When/Then by build)
- Exception: 'And' steps with explicit process switch

```cpp
// In step_definitions/basic_communication_steps.cpp

void registerBasicCommunicationSteps(StepRegistry& registry) {
    // Process parameter - accept but ignore
    registry.registerStep(
        "Given the {word} process creates buffer {string} with default configuration",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];  // Ignored in single-process test
            const std::string& bufferName = params[1];
            
            BufferConfig config(4096, 65536);  // Default config
            ctx.createReader(process, bufferName, config);
        }
    );
    
    // Writer connects to buffer
    registry.registerStep(
        "When the {word} process connects to buffer {string}",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const std::string& bufferName = params[1];
            
            ctx.createWriter(process, bufferName);
        }
    );
}
```

### State Management
```cpp
// TestContext manages state across steps
class TestContext {
    std::map<std::string, std::unique_ptr<zerobuffer::Reader>> readers_;
    std::map<std::string, std::unique_ptr<zerobuffer::Writer>> writers_;
    std::map<std::string, json> properties_;  // For test data
    std::exception_ptr lastException_;  // For expected failures
    std::string currentBuffer_;
};
```

### Error Handling
- Expected failures: capture exception, don't throw
- Store in `lastException_` for Then validation

```cpp
registry.registerStep(
    "When the {word} process writes oversized frame",
    [](TestContext& ctx, const std::vector<std::string>& params) {
        try {
            auto* writer = ctx.getWriter(params[0]);
            std::vector<uint8_t> oversizedData(1000000);  // Too large
            writer->write_frame(oversizedData.data(), oversizedData.size());
        } catch (const std::exception& e) {
            ctx.setLastException(std::current_exception());
        }
    }
);
```

## Implementation Rules

1. **Every step must have real implementation** - no empty logging steps
2. **Match feature file exactly** - pattern must match feature file text
3. **No unused steps** - delete if not in feature file
4. **No process-specific logic** - treat all processes the same (reader/writer only)

## Running Tests

### Local Testing (Google Test)
```bash
# From cpp directory
./test.sh            # Run all unit tests (default)
./test.sh 1.1        # Run specific test by number
./test.sh benchmark  # Run benchmarks
```

### Generating Tests
```bash
# Generate tests from feature files
harmony-cpp-gen --input ../ZeroBuffer.Harmony.Tests/Features --output tests/generated

# Or use the helper script
./generate_tests.sh
```

### Building
```bash
./build.sh           # Build with tests enabled (default)
./build.sh Debug     # Debug build
./build.sh Release clean  # Clean rebuild
```

### Harmony Integration Testing
```bash
# From zerobuffer root directory
cd ..
./test.sh cpp 1.1    # Run specific test with C++ implementation
./test.sh cpp        # Run all C++ tests
```

## Logging Control

The test infrastructure supports environment variable control:
```bash
ZEROBUFFER_LOG_LEVEL=DEBUG ./test.sh 1.1  # Show debug output
ZEROBUFFER_LOG_LEVEL=ERROR ./test.sh      # Only show errors
```

Available log levels: TRACE, DEBUG, INFO, WARNING, ERROR, FATAL

### Troubleshooting

1. **test.sh scripts** - If they don't work, fix them! Don't work around them.
2. **Advanced scenarios** - Some might be too complex for Harmony. Ask for help immediately.
3. **Don't create workarounds** - Follow the process or ask for help.
4. **Memory management** - Use smart pointers and RAII consistently.

## Common Patterns

### Buffer Creation
```cpp
// Reader creates buffer
auto reader = std::make_unique<zerobuffer::Reader>(bufferName, config);
ctx.setReader("reader", std::move(reader));

// Writer connects to existing buffer
auto writer = std::make_unique<zerobuffer::Writer>(bufferName);
ctx.setWriter("writer", std::move(writer));
```

### Write Operations
```cpp
auto* writer = ctx.getWriter("writer");
std::vector<uint8_t> data = {'H', 'e', 'l', 'l', 'o'};
writer->write_frame(data.data(), data.size());
```

### Read Operations
```cpp
auto* reader = ctx.getReader("reader");
auto frame = reader->read_frame(std::chrono::milliseconds(5000));
if (!frame.valid()) {
    throw std::runtime_error("Read timeout");
}
// Process frame data
reader->release_frame(frame);
```

### Expected Failures
```cpp
try {
    writer->write_frame(oversizedData.data(), oversizedData.size());
} catch (const zerobuffer::InvalidFrameSizeException& e) {
    ctx.setLastException(std::current_exception());
}

// In Then step:
auto ex = ctx.getLastException();
ASSERT_TRUE(ex != nullptr);
```

## Adding New Scenarios

1. **Create/update step definition file** in `step_definitions/`
2. **Register steps** in the file's register function
3. **Include in main** registration (if new file)
4. **Test locally first** before Harmony
5. **Document any special patterns** discovered

## Example: Implementing a New Scenario

For Test 1.2 - Multiple Frames Sequential:

```cpp
// In basic_communication_steps.cpp

registry.registerStep(
    "When the {word} process writes {int} frames of size {int}",
    [](TestContext& ctx, const std::vector<std::string>& params) {
        const std::string& process = params[0];
        int frameCount = std::stoi(params[1]);
        size_t frameSize = std::stoull(params[2]);
        
        auto* writer = ctx.getWriter(process);
        for (int i = 0; i < frameCount; i++) {
            std::vector<uint8_t> data(frameSize, static_cast<uint8_t>('A' + i));
            writer->write_frame(data.data(), data.size());
        }
        
        ctx.setProperty("expected_frame_count", frameCount);
        ctx.setProperty("expected_frame_size", frameSize);
    }
);
```

## Notes

- **RAII is critical** - Always use smart pointers for resource management
- **Pattern matching** - The StepRegistry handles {word}, {string}, {int} conversions
- **State isolation** - Each test scenario gets a fresh TestContext
- **Logging** - Use DualLogger for both stderr and captured logs