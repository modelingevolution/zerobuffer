# C++ Scenario Development Guide

## Prerequisites
1. Read ZeroBuffer protocol documentation (all markdown files)
2. Read harmony documentation (all markdown files)
3. Understand the dual-mode testing approach (DESIGN.md)

## File Locations
- **ZeroBuffer Library**: `cpp/include/zerobuffer/` and `cpp/src/`
- **Feature Files**: `cpp/build/features/` (auto-copied from `../../ZeroBuffer.Harmony.Tests/Features/`)
- **Step Definitions**: `cpp/step_definitions/` (create as needed for each feature)
- **Test Infrastructure**: `cpp/tests/` (StepRegistry, TestContext, etc.)

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

## Development Process - YOUR TODO LIST

0. **READ THE PREREQUISITES** if you haven't!
1. **Read scenario** in feature file; Try to understand it and assess if it makes sense
2. **Identify required step definitions**
3. **Analyze data exchange** - If processes need to exchange data, stop and ask for help
4. **Implement each step** with actual ZeroBuffer operations
5. **Run single test locally**: `cpp/test.sh 1.1` (runs via Google Test)
6. **Fix issues** - Read protocol docs if test fails
7. **Only if GREEN**, run with Harmony: `../../test.sh cpp 1.1`

### Running Tests

#### Local Testing (Google Test)
```bash
cd cpp
./test.sh 1.1        # Run Test 1.1
./test.sh 1.2        # Run Test 1.2
./test.sh basic      # Run all BasicCommunication tests
```

#### Harmony Testing (Cross-platform)
```bash
cd ../..  # Go to zerobuffer root
./test.sh cpp 1.1    # Run Test 1.1 with C++/C++
./test.sh cpp        # Run all C++ tests
```

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