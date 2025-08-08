# ZeroBuffer C++ Testing Design - Dual Mode with Google Test and Harmony

## Overview

This document outlines the comprehensive testing strategy for C++ ZeroBuffer, enabling tests to run in two modes:
1. **Native Mode**: Using Google Test directly for fast local development
2. **Harmony Mode**: Via JSON-RPC server (`zerobuffer-serve`) for cross-platform integration testing

The key insight: **Share step definitions between both modes** using a common pattern matching system, maintaining a single source of truth.

## Architecture

### Dual-Mode Testing Flow

```
Feature Files (.feature)
    ├── [Build Event] → C# Generator
    │                        ↓
    │              Generated GTest Files
    │                        ↓
    │                   Google Test
    │                   (Local Testing)
    └── [Harmony] → zerobuffer-serve
                    (Cross-platform Testing)
```

### Core Components (Shared Between Both Modes)

1. **Step Registry** - Manages step definitions with pattern matching
2. **Test Context** - Maintains state across step executions
3. **Step Definitions** - Shared implementation for both GTest and Harmony
4. **Pattern Matcher** - Converts `{type}` placeholders to regex

### Harmony-Specific Components

1. **JSON-RPC Server** - Handles stdin/stdout communication with Harmony
2. **Logger** - Dual logging to stderr and in-memory capture

## Technology Choices

### 1. JSON Library
**Choice: nlohmann/json** (already in use)
- Already integrated in the project
- Modern C++ design
- Header-only option available
- Excellent performance and ease of use

### 2. JSON-RPC Implementation
**Choice: Custom implementation using nlohmann/json**
- Simple direct implementation in zerobuffer-serve
- No additional dependencies beyond nlohmann/json
- Full control over Content-Length header handling
- Supports Harmony protocol requirements
- Clean integration with step registry

### 3. Step Pattern Matching Approach

**Choice: Minimal Custom Implementation**

Since we're not actually parsing Gherkin files (Harmony does that), we only need:
1. **Pattern matching** for step text
2. **Parameter extraction** from matched patterns
3. **Step execution** with the extracted parameters

**Implementation Strategy:**
- Use simple string templates with placeholders
- Convert to regex at registration time
- Extract parameters using capture groups
- No need for full Gherkin parser or BDD framework

**Pattern Format Examples:**
```
"the {string} process creates buffer {string} with size {int}"
→ regex: "the \"([^\"]+)\" process creates buffer \"([^\"]+)\" with size (\\d+)"

"the {word} process writes {string}"
→ regex: "the (\\w+) process writes \"([^\"]+)\""
```

This approach:
- Matches C# and Python implementations' simplicity
- No heavy framework dependencies
- Easy to maintain and understand
- Fast to implement

### 4. Threading Model
**Choice: Single-threaded with async I/O**
- Matches Python/C# implementations
- Simpler state management
- Sufficient for test scenarios

## Implementation Structure

```
cpp/
├── step_definitions/              # Shared by both GTest and Harmony
│   ├── step_registry.h/cpp       # Pattern matching and registration
│   ├── test_context.h/cpp        # Test state management
│   ├── basic_communication.cpp   # Step implementations
│   ├── benchmarks.cpp
│   ├── edge_cases.cpp
│   ├── error_handling.cpp
│   ├── process_lifecycle.cpp
│   ├── stress_tests.cpp
│   └── synchronization.cpp
├── serve/                         # Harmony mode only
│   ├── CMakeLists.txt
│   ├── main.cpp                  # zerobuffer-serve entry point
│   ├── json_rpc_server.h/cpp    # JSON-RPC protocol handler
│   └── logger.h/cpp              # Dual logging
├── tests/
│   ├── generated/                # Auto-generated GTest files
│   │   ├── test_BasicCommunication.generated.cpp
│   │   ├── test_EdgeCases.generated.cpp
│   │   └── ...
│   └── main.cpp                  # GTest main entry
├── tools/
│   └── GTestGenerator/           # C# app to generate GTest files
│       ├── Program.cs
│       └── GTestGenerator.csproj
└── features/                      # Copied from Harmony test features
    ├── 01-BasicCommunication.feature
    ├── 02-ProcessLifecycle.feature
    └── ...
```

## Key Classes Design

### 1. JsonRpcServer (with Content-Length Headers)

```cpp
class JsonRpcServer {
public:
    JsonRpcServer(StepRegistry& registry, TestContext& context);
    void run();  // Main loop reading from stdin with Content-Length
    
private:
    std::string readMessage();  // Read Content-Length header, then body
    void sendMessage(const std::string& json);  // Send with Content-Length header
    void handleRequest(const std::string& json_input);
    
    // Harmony-specific method handlers
    json handleInitialize(const json& params);
    json handleDiscover(const json& params);
    json handleExecuteStep(const json& params);  // Must implement 30s timeout
    json handleReset(const json& params);
    json handleCleanupProcess(const json& params);
    json handleShutdown(const json& params);
    
    StepRegistry& step_registry_;
    TestContext& test_context_;
    Logger logger_;
    bool should_exit_ = false;
};

// Implementation detail for stdin/stdout with Content-Length
std::string JsonRpcServer::readMessage() {
    std::string line;
    size_t content_length = 0;
    
    // Read headers until empty line
    while (std::getline(std::cin, line)) {
        if (line == "\r" || line.empty()) break;
        
        // Parse Content-Length header
        if (line.find("Content-Length: ") == 0) {
            content_length = std::stoul(line.substr(16));
        }
    }
    
    // Read the JSON body
    std::string body(content_length, '\0');
    std::cin.read(&body[0], content_length);
    return body;
}

void JsonRpcServer::sendMessage(const std::string& json) {
    std::cout << "Content-Length: " << json.length() << "\r\n";
    std::cout << "\r\n";
    std::cout << json;
    std::cout.flush();
}

// Example timeout implementation for executeStep
json JsonRpcServer::handleExecuteStep(const json& params) {
    // Extract timeout, default to 30 seconds
    int timeoutMs = params.value("timeoutMs", 30000);
    auto step = params["step"].get<std::string>();
    
    // Use std::async with timeout
    auto future = std::async(std::launch::async, [&]() {
        return step_registry_.executeStep(step, test_context_);
    });
    
    auto start = std::chrono::steady_clock::now();
    
    // Wait for step completion or timeout
    if (future.wait_for(std::chrono::milliseconds(timeoutMs)) == std::future_status::timeout) {
        auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start).count();
        
        // Step timed out - return specific timeout response
        return json{
            {"success", false},
            {"error", "Step execution timeout: The step execution time limit of " + 
                     std::to_string(timeoutMs) + "ms was reached. The step '" + 
                     step + "' did not complete within the allowed time."},
            {"data", {
                {"timeoutType", "STEP_EXECUTION_TIMEOUT"},
                {"timeoutMs", timeoutMs},
                {"elapsedMs", elapsed}
            }},
            {"logs", logger_.getCapturedLogs()}
        };
    }
    
    // Step completed within timeout
    bool success = future.get();
    return json{
        {"success", success},
        {"error", nullptr},
        {"data", json::object()},
        {"logs", logger_.getCapturedLogs()}
    };
}
```

### 2. StepRegistry (Singleton with Simple Pattern Conversion)

```cpp
class StepRegistry {
public:
    static StepRegistry& getInstance() {
        static StepRegistry instance;  // Meyers' Singleton
        return instance;
    }
    
    // Registration methods (accepts patterns like "the {word} process creates buffer {string}")
    void registerStep(const std::string& pattern, StepHandler handler);
    
    // Execution
    bool executeStep(const std::string& step, TestContext& context);
    
    // Discovery (for JSON-RPC)
    std::vector<StepInfo> getAllSteps() const;
    
private:
    StepRegistry() = default;
    
    // Convert pattern with {type} placeholders to regex
    std::regex patternToRegex(const std::string& pattern);
    
    struct StepDefinition {
        std::string original_pattern;  // "the {word} process writes {string}"
        std::regex regex_pattern;       // Compiled regex
        StepHandler handler;
        std::vector<std::string> param_types;  // ["word", "string"]
    };
    
    std::vector<StepDefinition> definitions_;
    mutable std::mutex mutex_;  // For thread safety if needed
    
    // Pattern conversion helpers
    static std::string convertPatternToRegex(const std::string& pattern) {
        static const std::map<std::string, std::string> type_patterns = {
            {"{int}", "(\\d+)"},
            {"{float}", "([+-]?\\d*\\.?\\d+)"},
            {"{word}", "(\\w+)"},
            {"{string}", "\"([^\"]+)\""},
            {"{}", "(.*)"}
        };
        
        std::string regex = pattern;
        for (const auto& [placeholder, regex_pattern] : type_patterns) {
            size_t pos = 0;
            while ((pos = regex.find(placeholder, pos)) != std::string::npos) {
                regex.replace(pos, placeholder.length(), regex_pattern);
                pos += regex_pattern.length();
            }
        }
        return "^" + regex + "$";
    }
};

using StepHandler = std::function<void(TestContext&, const std::vector<std::string>&)>;
```

### 3. TestContext

```cpp
class TestContext {
public:
    // Buffer management
    void createReader(const std::string& process_name, 
                     const std::string& buffer_name,
                     const BufferConfig& config);
    void createWriter(const std::string& process_name,
                     const std::string& buffer_name);
    
    zerobuffer::Reader* getReader(const std::string& process_name);
    zerobuffer::Writer* getWriter(const std::string& process_name);
    
    // State management
    void setProperty(const std::string& key, const json& value);
    json getProperty(const std::string& key) const;
    
    // Cleanup
    void reset();
    
private:
    std::map<std::string, std::unique_ptr<zerobuffer::Reader>> readers_;
    std::map<std::string, std::unique_ptr<zerobuffer::Writer>> writers_;
    std::map<std::string, json> properties_;
};
```

### 4. Step Definition Example

#### Simple Registration with Pattern Templates

```cpp
class BasicCommunicationSteps {
public:
    static void registerSteps(StepRegistry& registry) {
        // Given the 'reader' process creates buffer 'test-buffer' with size 1024
        registry.registerStep(
            "Given the {word} process creates buffer {string} with size {int}",
            [](TestContext& ctx, const std::vector<std::string>& params) {
                std::string process = params[0];
                std::string buffer = params[1];
                size_t size = std::stoull(params[2]);
                
                BufferConfig config(4096, size);
                ctx.createReader(process, buffer, config);
            }
        );
        
        // When the 'writer' process writes "Hello, World!"
        registry.registerStep(
            "When the {word} process writes {string}",
            [](TestContext& ctx, const std::vector<std::string>& params) {
                std::string process = params[0];
                std::string data = params[1];
                
                auto* writer = ctx.getWriter(process);
                if (!writer) {
                    throw std::runtime_error("Writer not found: " + process);
                }
                
                writer->write_frame(data.data(), data.size());
            }
        );
        
        // Then the 'reader' should read "Hello, World!"
        registry.registerStep(
            "Then the {word} should read {string}",
            [](TestContext& ctx, const std::vector<std::string>& params) {
                std::string process = params[0];
                std::string expected = params[1];
                
                auto* reader = ctx.getReader(process);
                if (!reader) {
                    throw std::runtime_error("Reader not found: " + process);
                }
                
                auto frame = reader->read_frame(5000);
                if (!frame.valid()) {
                    throw std::runtime_error("Failed to read frame");
                }
                
                std::string actual(frame.data(), frame.data() + frame.size());
                if (actual != expected) {
                    throw std::runtime_error("Data mismatch: expected '" + 
                                           expected + "' but got '" + actual + "'");
                }
            }
        );
    }
};
```

## JSON-RPC Protocol (with Content-Length Headers)

### Message Format
Messages are sent with HTTP-style headers followed by JSON body:
```
Content-Length: 123\r\n
\r\n
{"jsonrpc":"2.0","method":"execute_step","params":{...},"id":1}
```

### Harmony JSON-RPC Methods (Verified from C# and Python implementations)

#### 1. health
Health check to verify the server is responsive.
```json
// Request
{
    "jsonrpc": "2.0",
    "method": "health",
    "params": {
        "hostPid": 12345,
        "featureId": "test-feature-123"
    },
    "id": 1
}

// Response
{
    "jsonrpc": "2.0",
    "result": true,
    "id": 1
}
```

#### 2. initialize
Initialize the test environment with context information.
```json
// Request
{
    "jsonrpc": "2.0",
    "method": "initialize",
    "params": {
        "hostPid": 12345,
        "featureId": "test-feature-123",
        "role": "reader",
        "platform": "cpp",
        "scenario": "Simple write and read",
        "testRunId": "run-456"
    },
    "id": 2
}

// Response
{
    "jsonrpc": "2.0",
    "result": true,
    "id": 2
}
```

#### 3. discover
Returns all available step definitions with their patterns.
```json
// Request
{
    "jsonrpc": "2.0",
    "method": "discover",
    "params": {},
    "id": 3
}

// Response
{
    "jsonrpc": "2.0",
    "result": {
        "steps": [
            "Given the {word} process creates buffer {string} with size {int}",
            "When the {word} process writes {string}",
            "Then the {word} should read {string}"
            // ... all other registered steps
        ]
    },
    "id": 3
}
```

#### 4. executeStep
Execute a specific Gherkin step with timeout handling.

**CRITICAL TIMEOUT REQUIREMENTS**:
- The serve implementation MUST enforce a 30-second default timeout for step execution
- Harmony does NOT implement timeouts - it expects the serve to handle them
- Harmony does NOT send a `timeoutMs` parameter currently (serves must use 30000ms default)
- C# and Python serves also need to be updated to implement this timeout handling

```json
// Request
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "process": "reader",
        "stepType": "given",
        "step": "the reader process creates buffer test with size 1024",
        "originalStep": "the reader process creates buffer test with size 1024",
        "parameters": {},
        "isBroadcast": false,
        "table": null,
        "timeoutMs": 30000  // Optional, defaults to 30000ms (30 seconds)
    },
    "id": 4
}

// Response (success)
{
    "jsonrpc": "2.0",
    "result": {
        "success": true,
        "error": null,
        "data": {},
        "logs": [
            {
                "level": "INFO",
                "message": "Buffer created: test"
            }
        ]
    },
    "id": 4
}

// Response (step execution timeout - NOT an exception)
{
    "jsonrpc": "2.0",
    "result": {
        "success": false,
        "error": "Step execution timeout: The step execution time limit of 30000ms was reached. The step 'the reader process creates buffer test with size 1024' did not complete within the allowed time.",
        "data": {
            "timeoutType": "STEP_EXECUTION_TIMEOUT",
            "timeoutMs": 30000,
            "elapsedMs": 30005
        },
        "logs": [
            {
                "level": "INFO",
                "message": "Starting buffer creation..."
            },
            {
                "level": "WARNING",
                "message": "Step execution timeout reached after 30000ms"
            },
            {
                "level": "ERROR",
                "message": "Step execution was cancelled due to timeout - no exception was thrown"
            }
        ]
    },
    "id": 4
}

// Example with table data (for steps with tables)
{
    "jsonrpc": "2.0",
    "method": "executeStep",
    "params": {
        "process": "reader",
        "stepType": "given",
        "step": "the following configuration",
        "originalStep": "the following configuration",
        "parameters": {},
        "isBroadcast": false,
        "table": {
            "headers": ["key", "value"],
            "rows": [
                {"key": "buffer_size", "value": "1024"},
                {"key": "timeout", "value": "5000"}
            ]
        }
    },
    "id": 5
}
```

#### 5. cleanup
Clean up all test resources (called between test scenarios).
```json
// Request
{
    "jsonrpc": "2.0",
    "method": "cleanup",
    "params": {},
    "id": 5
}

// Response
{
    "jsonrpc": "2.0",
    "result": null,
    "id": 5
}
```

#### 6. shutdown
Graceful shutdown request (server should exit after responding).
```json
// Request
{
    "jsonrpc": "2.0",
    "method": "shutdown",
    "params": {},
    "id": 6
}

// Response (sent before exit)
{
    "jsonrpc": "2.0",
    "result": null,
    "id": 6
}
```

#### 7. crash (Optional - for testing)
Force a crash for testing error recovery.
```json
// Request
{
    "jsonrpc": "2.0",
    "method": "crash",
    "params": {},
    "id": 7
}

// No response - process crashes immediately
```

### Error Format
```json
{
    "jsonrpc": "2.0",
    "error": {
        "code": -32000,
        "message": "Step execution failed",
        "data": {
            "step": "Given unknown step",
            "error": "No matching step definition found"
        }
    },
    "id": 1
}
```

### Protocol Notes
- Uses Language Server Protocol (LSP) style message framing
- Content-Length header specifies the byte length of the JSON body
- Headers are separated from body by empty line (`\r\n\r\n`)
- This matches how C# and Python implementations handle JSON-RPC

## Build Integration

### Feature File Copying

Feature files are the source of truth located in `../ZeroBuffer.Harmony.Tests/Features/`. During the build process:

1. **Copy feature files** from `../ZeroBuffer.Harmony.Tests/Features/` to `cpp/features/`
2. **Run C# generator** to create Google Test files from feature files
3. **Compile tests** including both generated and manual test files

### CMakeLists.txt Addition

```cmake
# In cpp/CMakeLists.txt
option(BUILD_SERVE "Build zerobuffer-serve executable" ON)
option(BUILD_DUAL_MODE_TESTS "Build dual-mode tests (GTest + Harmony)" ON)

# Copy feature files from source of truth
if(BUILD_DUAL_MODE_TESTS)
    file(GLOB FEATURE_FILES "${CMAKE_SOURCE_DIR}/../ZeroBuffer.Harmony.Tests/Features/*.feature")
    file(COPY ${FEATURE_FILES} DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/features)
    
    # Run C# generator to create GTest files
    add_custom_command(
        OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/tests/generated/tests.stamp
        COMMAND dotnet run --project ${CMAKE_SOURCE_DIR}/tools/GTestGenerator/GTestGenerator.csproj
                ${CMAKE_CURRENT_BINARY_DIR}/features
                ${CMAKE_CURRENT_BINARY_DIR}/tests/generated
        COMMAND ${CMAKE_COMMAND} -E touch ${CMAKE_CURRENT_BINARY_DIR}/tests/generated/tests.stamp
        DEPENDS ${FEATURE_FILES}
        COMMENT "Generating Google Test files from feature files"
    )
    
    add_custom_target(generate_tests ALL
        DEPENDS ${CMAKE_CURRENT_BINARY_DIR}/tests/generated/tests.stamp
    )
endif()

if(BUILD_SERVE)
    add_subdirectory(serve)
endif()
```

### serve/CMakeLists.txt

```cmake
cmake_minimum_required(VERSION 3.20)

# Fetch jsonrpcpp
include(FetchContent)
FetchContent_Declare(
    jsonrpcpp
    GIT_REPOSITORY https://github.com/badaix/jsonrpcpp.git
    GIT_TAG v1.4.0
)
FetchContent_MakeAvailable(jsonrpcpp)

# Create executable
add_executable(zerobuffer-serve
    main.cpp
    json_rpc_server.cpp
    step_registry.cpp
    step_executor.cpp
    test_context.cpp
    logger.cpp
    step_definitions/basic_communication.cpp
    step_definitions/benchmarks.cpp
    step_definitions/edge_cases.cpp
    step_definitions/error_handling.cpp
    step_definitions/process_lifecycle.cpp
    step_definitions/stress_tests.cpp
    step_definitions/synchronization.cpp
)

target_link_libraries(zerobuffer-serve
    PRIVATE
    zerobuffer
    nlohmann_json::nlohmann_json
    jsonrpcpp::jsonrpcpp
    ${CMAKE_THREAD_LIBS_INIT}
)

target_compile_features(zerobuffer-serve PRIVATE cxx_std_20)

# Install
install(TARGETS zerobuffer-serve
    RUNTIME DESTINATION bin
)
```

## Dual-Mode Implementation Strategy

### Developer Workflow

```
1. Write/modify feature files
2. Build → Auto-generates GTest files (C# generator)
3. Run Google Test locally (fast feedback)
4. Fix any issues
5. Commit and run Harmony tests (cross-platform validation)
```

### Benefits of Dual-Mode Approach

1. **Fast Local Development**
   - Run tests directly with Google Test
   - No JSON-RPC overhead
   - Immediate feedback during development
   - Use familiar GTest assertions and debugging

2. **Seamless Harmony Integration**
   - Same step definitions work in both modes
   - No duplication of test logic
   - Easy transition from local to cross-platform testing

3. **Single Source of Truth**
   - Feature files define all tests
   - Generated GTest files are disposable
   - Step definitions are shared code

## C# Test Generator

### Purpose
Generate Google Test C++ files from Gherkin feature files during build.

### Implementation

```csharp
// tools/GTestGenerator/Program.cs
public class GTestGenerator
{
    public static void Main(string[] args)
    {
        var featureDir = args[0];
        var outputDir = args[1];
        
        foreach (var featureFile in Directory.GetFiles(featureDir, "*.feature"))
        {
            GenerateGTestFile(featureFile, outputDir);
        }
    }
    
    static void GenerateGTestFile(string featureFile, string outputDir)
    {
        var feature = ParseFeature(File.ReadAllText(featureFile));
        var testName = Path.GetFileNameWithoutExtension(featureFile);
        var output = new StringBuilder();
        
        // Generate TEST macros for each scenario
        foreach (var scenario in feature.Scenarios)
        {
            var testMethodName = SanitizeName(scenario.Name);
            output.AppendLine($"TEST({testName}, {testMethodName})");
            output.AppendLine("{");
            
            foreach (var step in scenario.Steps)
            {
                output.AppendLine($"    ASSERT_TRUE(zerobuffer::steps::StepRegistry::getInstance()");
                output.AppendLine($"        .executeStep(\"{EscapeString(step)}\"))");
                output.AppendLine($"        << \"Failed at step: {EscapeString(step)}\";");
            }
            
            output.AppendLine("}");
        }
        
        File.WriteAllText(
            Path.Combine(outputDir, $"test_{testName}.generated.cpp"),
            output.ToString()
        );
    }
}
```

### Generated Test Example

```cpp
// tests/generated/test_BasicCommunication.generated.cpp
#include <gtest/gtest.h>
#include "step_definitions/step_registry.h"

TEST(BasicCommunication, Simple_write_and_read)
{
    // Scenario: Simple write and read
    ASSERT_TRUE(zerobuffer::steps::StepRegistry::getInstance()
        .executeStep("Given the reader process creates buffer test-buffer with size 1024"))
        << "Failed at step: Given the reader process creates buffer test-buffer with size 1024";
    
    ASSERT_TRUE(zerobuffer::steps::StepRegistry::getInstance()
        .executeStep("When the writer process writes \"Hello, World!\""))
        << "Failed at step: When the writer process writes \"Hello, World!\"";
    
    ASSERT_TRUE(zerobuffer::steps::StepRegistry::getInstance()
        .executeStep("Then the reader should read \"Hello, World!\""))
        << "Failed at step: Then the reader should read \"Hello, World!\"";
}
```

## Implementation Phases

### Phase 1: Shared Infrastructure
1. Implement StepRegistry with pattern matching
2. Create TestContext class
3. Design step definition macros for easy registration

### Phase 2: C# Generator Tool
1. Create feature file parser
2. Implement GTest code generator
3. Add as build event in CMake

### Phase 3: Step Definitions (Shared)
1. Port BasicCommunicationSteps from C#/Python
2. Implement all other step definition classes
3. Ensure they work with both GTest and JSON-RPC

### Phase 4: Native Mode (Google Test)
1. Set up GTest integration
2. Configure CMake for test building
3. Verify all generated tests pass

### Phase 5: Harmony Mode (JSON-RPC Server)
1. Implement JSON-RPC server with stdin/stdout
2. Connect to shared StepRegistry
3. Add logging capabilities
4. Test with Harmony framework

## Testing Strategy

1. **Unit Tests**: Test individual components (StepRegistry, TestContext)
2. **Integration Tests**: Test JSON-RPC communication
3. **Harmony Tests**: Run full cross-platform tests
4. **Performance Tests**: Ensure comparable performance to C#/Python

## Logging Strategy

Implement dual logging similar to C# and Python:
1. **stderr**: Debug and error messages with timestamps
2. **In-memory**: Captured logs for JSON-RPC responses

```cpp
class DualLogger {
public:
    void log(LogLevel level, const std::string& message);
    std::vector<std::string> getCapturedLogs();
    void clearCapturedLogs();
    
private:
    std::vector<std::string> captured_logs_;
    std::mutex mutex_;
};
```

## Error Handling

### Exception Handling (Critical)

**ALL exceptions MUST be caught and handled by the serve implementation:**

1. **No Unhandled Exceptions**: The serve must NEVER crash due to unhandled exceptions
2. **Catch All Exceptions**: Use catch-all blocks to ensure no exception escapes
3. **Return as Failures**: All exceptions should result in `success: false` responses
4. **Preserve Exception Information**: Include exception type and message in the error response

**C++ ZeroBuffer Exception Design:**
The C++ ZeroBuffer implementation SHOULD throw exceptions (not return error codes) for:
- `BufferNotFoundException` - Buffer doesn't exist
- `BufferFullException` - No space for new frames  
- `WriterDeadException` - Writer process has died
- `ReaderDeadException` - Reader process has died
- `FrameTooLargeException` - Frame exceeds buffer capacity
- `SemaphoreTimeoutException` - Semaphore wait timed out
- `BufferCorruptedException` - Shared memory corruption detected

This allows the serve to catch and handle them properly.

**Exception Response Format:**
```json
{
    "jsonrpc": "2.0",
    "result": {
        "success": false,
        "error": "Exception in step execution: BufferFullException: Cannot write frame, buffer is full after 5000ms timeout",
        "data": {
            "exceptionType": "BufferFullException",
            "exceptionMessage": "Cannot write frame, buffer is full after 5000ms timeout",
            "stepText": "the writer process writes data",
            "stackTrace": "..."  // Optional, for debugging
        },
        "logs": [
            {
                "level": "ERROR",
                "message": "Step failed with exception: BufferFullException"
            }
        ]
    },
    "id": 4
}
```

### Timeout Handling (Critical)

**Step Execution Timeout Requirements:**
1. **Default timeout**: 30 seconds per step (30000ms)
2. **Configurable**: Can be overridden via `timeoutMs` parameter in executeStep request
3. **Serve responsibility**: The serve MUST implement timeout - Harmony does NOT
4. **Clear distinction**: Step execution timeouts must be clearly distinguished from other timeouts

**Timeout Response Requirements:**
- Set `success: false`
- Include descriptive error message starting with "Step execution timeout:"
- Add `timeoutType: "STEP_EXECUTION_TIMEOUT"` to data field
- Include `timeoutMs` (configured timeout) and `elapsedMs` (actual time) in data
- Return any logs collected before timeout
- Do NOT throw exceptions - handle gracefully

**Types of Timeouts to Distinguish:**
1. **STEP_EXECUTION_TIMEOUT** - Step took too long to execute (30s default)
2. **BUFFER_WAIT_TIMEOUT** - Waiting for buffer read/write timed out
3. **SEMAPHORE_TIMEOUT** - Semaphore wait operation timed out
4. **PROCESS_COMMUNICATION_TIMEOUT** - Inter-process communication failed

### Standard JSON-RPC Errors

1. **Step Not Found**: Return JSON-RPC error with code -32000
2. **Step Execution Failure**: Return error with exception details
3. **Invalid JSON**: Return parse error (code -32700)
4. **Method Not Found**: Return method error (code -32601)

## Performance Considerations

1. **Regex Compilation**: Compile regex patterns once during registration
2. **Memory Management**: Use smart pointers for resource management
3. **String Operations**: Use string_view where possible
4. **JSON Parsing**: Parse once and pass by reference

## Compatibility Requirements

1. **Step Pattern Format**: Must match C# and Python implementations exactly
2. **JSON-RPC Protocol**: Follow same request/response format
3. **Error Codes**: Use same error codes as other implementations
4. **Logging Format**: Match timestamp format and log levels

## Success Criteria

1. All Harmony tests pass with C++ as reader/writer
2. Performance comparable to C# and Python implementations
3. Clean integration with existing build system
4. Maintainable and well-documented code
5. No memory leaks or undefined behavior
6. Proper 30-second timeout enforcement for step execution
7. All exceptions caught and returned as structured responses

## Implementation Status

| Component | C++ | C# | Python | Notes |
|-----------|-----|----|--------|-------|
| JSON-RPC Server | 📝 Design | ✅ Implemented | ✅ Implemented | C++ in design phase |
| Step Registry | 📝 Design | ✅ Implemented | ✅ Implemented | Pattern matching working |
| Test Context | 📝 Design | ✅ Implemented | ✅ Implemented | State management |
| **30s Timeout** | 📝 Design | ❌ TODO | ❌ TODO | **All need implementation** |
| Exception Handling | 📝 Design | ⚠️ Partial | ⚠️ Partial | Need structured responses |
| Dual-Mode Testing | 📝 Design | N/A | N/A | C++ unique feature |

**Action Items**:
1. C# serve needs to add 30-second timeout enforcement in `ExecuteStepAsync`
2. Python serve needs to add 30-second timeout using `asyncio.wait_for`
3. C++ serve must implement timeout from the start using `std::async` with timeout
4. All serves must return structured timeout responses (not exceptions)

## Critical Missing Pieces for C++ Implementation

### 1. **Table Data Handling**
- ⚠️ Need to define how to pass table data to step handlers
- Solution: Pass as `std::vector<std::map<std::string, std::string>>`

### 2. **Process Management in TestContext**
- ⚠️ Need to handle multiple readers/writers per test scenario
- Solution: Use process name as key in maps (e.g., `readers_["reader1"]`)

### 3. **Logging Infrastructure**
- ⚠️ Need dual logging (stderr + in-memory capture)
- Solution: Already designed in DualLogger class

### 4. **Build Dependencies**
- ✅ nlohmann/json - already in project
- ⚠️ jsonrpcpp - need to add via FetchContent or vcpkg
- ✅ Google Test - already in project

### 5. **Exception Types Mapping**
C++ ZeroBuffer has these exceptions that need proper handling:
- ✅ `WriterDeadException` → Return as structured error
- ✅ `ReaderDeadException` → Return as structured error  
- ✅ `BufferFullException` → Return as structured error
- ✅ `MetadataAlreadyWrittenException` → Return as structured error
- ✅ `InvalidFrameSizeException` → Return as structured error

## Feasibility Assessment

**✅ FEASIBLE** - All required components are either:
1. Already implemented in C#/Python (can port logic)
2. Have clear C++ equivalents (std::async, std::regex, etc.)
3. External dependencies are available (jsonrpcpp)

**No Blocking Issues Found** - The design is complete and implementable.

## References

- [jsonrpcpp Documentation](https://github.com/badaix/jsonrpcpp)
- [C# ZeroBuffer.Serve Implementation](../csharp/ZeroBuffer.Serve/)
- [Python zerobuffer_serve Implementation](../python/zerobuffer_serve.py)
- [Harmony Test Framework](../modules/harmony/)